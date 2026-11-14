"""Prepare verified release assets and publish only a tested commit."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import zipfile


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare(package, output):
    info = (package / "BUILD_INFO.txt").read_text(encoding="utf-8-sig")
    version = re.search(r"^PhotoAgent (\d+\.\d+\.\d+)$", info, re.MULTILINE)[1]
    source = re.search(r"^Source commit: ([0-9a-f]{40})$", info, re.MULTILINE)[1]
    installer = package / f"PhotoAgent_Setup_{version}_Lite.exe"
    if not installer.is_file() or not (0 < installer.stat().st_size < 10 * 1024 * 1024):
        raise RuntimeError("Expected a lightweight installer without the .NET payload")
    sums = (package / "SHA256SUMS.txt").read_text(encoding="ascii").split()
    if sums != [digest(installer), installer.name]:
        raise RuntimeError("Installer checksum does not match the tested build")
    if "Microsoft .NET Framework: not bundled" not in info:
        raise RuntimeError("Build metadata does not describe the Lite variant")
    document = package / "PhotoAgent_1C_Integration_RU.md"
    for required in (document, package / "INSTALL_RU.md", package / "PROTOCOL_RU.md"):
        if not required.is_file() or required.stat().st_size == 0:
            raise RuntimeError(f"Missing release document: {required.name}")

    output.mkdir(parents=True, exist_ok=True)
    archive = output / f"PhotoAgent_{version}_Lite_Setup.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
        for item in sorted(package.iterdir()):
            if not item.is_file():
                raise RuntimeError(f"Unexpected directory in installation package: {item}")
            bundle.write(item, item.name)
    with zipfile.ZipFile(archive) as bundle:
        if bundle.testzip() is not None:
            raise RuntimeError("Release ZIP integrity check failed")

    assets = [archive]
    for item in (installer, document):
        destination = output / item.name
        shutil.copy2(item, destination)
        assets.append(destination)
    checksums = output / "RELEASE_SHA256SUMS.txt"
    checksums.write_text("".join(
        f"{digest(item)}  {item.name}\n" for item in sorted(assets)), encoding="ascii")
    assets.append(checksums)
    print(f"Prepared PhotoAgent {version} from {source}:")
    for asset in assets:
        print(f"  {asset.name}: {asset.stat().st_size} bytes, SHA256 {digest(asset)}")
    return version, source, assets


def gh(*arguments, missing_ok=False):
    result = subprocess.run(["gh", *arguments], text=True, capture_output=True)
    if result.returncode:
        if missing_ok and "(HTTP 404)" in result.stderr:
            return None
        raise RuntimeError(f"GitHub operation failed: {result.stderr.strip()}")
    return result.stdout


def api(path, missing_ok=False, fields=None):
    arguments = ["api", path]
    if fields:
        arguments += ["--method", "POST"]
        for key, value in fields.items():
            arguments += ["-f", f"{key}={value}"]
    result = gh(*arguments, missing_ok=missing_ok)
    return json.loads(result) if result is not None else None


def find_release(prefix, tag):
    # GitHub's "release by tag" endpoint omits unpublished drafts.
    # The authenticated release list includes them.
    for page in range(1, 101):
        releases = api(f"{prefix}/releases?per_page=100&page={page}")
        for release in releases:
            if release["tag_name"] == tag:
                return release
        if len(releases) < 100:
            return None
    raise RuntimeError("Release search exceeded pagination limit")


def tag_target(prefix, tag):
    reference = api(f"{prefix}/git/ref/tags/{tag}")
    target = reference["object"]
    while target["type"] == "tag":
        target = api(f"{prefix}/git/tags/{target['sha']}")["object"]
    if target["type"] != "commit":
        raise RuntimeError("Version tag must resolve to a commit")
    return target["sha"]


def publish(version, source, assets, expected_source=None):
    repository = os.environ["GITHUB_REPOSITORY"]
    commit = expected_source or os.environ["GITHUB_SHA"]
    ref = os.environ["GITHUB_REF"]
    tag = f"v{version}"
    allowed = (f"refs/tags/{tag}", f"refs/heads/release/{tag}")
    if expected_source:
        allowed = (f"refs/heads/publish/{tag}",)
    if ref not in allowed:
        raise RuntimeError(f"Release ref {ref} must match assembly version {tag}")
    if source != commit:
        raise RuntimeError("The tested package was built from a different commit")

    prefix = f"repos/{repository}"
    tag_ref = api(f"{prefix}/git/ref/tags/{tag}", missing_ok=True)
    if tag_ref is None:
        tag_ref = api(f"{prefix}/git/refs", fields={"ref": f"refs/tags/{tag}", "sha": commit})
    target = tag_ref["object"]
    while target["type"] == "tag":
        target = api(f"{prefix}/git/tags/{target['sha']}")["object"]
    if target["type"] != "commit" or target["sha"] != commit:
        raise RuntimeError("Existing version tag points to another commit; it will not be moved")

    release = find_release(prefix, tag)
    if release and not release["draft"]:
        print(f"Published release already exists and remains unchanged: {release['html_url']}")
        return
    if release is None:
        arguments = [
            "release", "create", tag, "--repo", repository, "--draft",
            "--verify-tag", "--target", commit, "--title", f"PhotoAgent {version} Lite",
        ]
        notes = Path("docs/releases") / f"{version}.md"
        if notes.is_file():
            arguments += ["--notes-file", str(notes)]
        else:
            arguments += ["--generate-notes"]
        gh(*arguments)
        release = find_release(prefix, tag)
    if not release["draft"]:
        raise RuntimeError("Assets may only be updated while the release is a draft")

    gh("release", "upload", tag, *map(str, assets), "--repo", repository, "--clobber")
    uploaded = api(f"{prefix}/releases/{release['id']}/assets?per_page=100")
    by_name = {item["name"]: item for item in uploaded}
    if set(by_name) != {item.name for item in assets}:
        raise RuntimeError("Draft release contains an unexpected set of assets")
    for asset in assets:
        remote = by_name[asset.name]
        if remote["size"] != asset.stat().st_size:
            raise RuntimeError(f"Uploaded size mismatch: {asset.name}")
        expected = "sha256:" + digest(asset)
        if remote.get("digest"):
            if remote["digest"] != expected:
                raise RuntimeError(f"Uploaded checksum mismatch: {asset.name}")
        else:
            # Older API responses may omit the digest. Verify the actual bytes.
            with tempfile.TemporaryDirectory() as temporary:
                downloaded = Path(temporary) / asset.name
                with downloaded.open("wb") as stream:
                    result = subprocess.run([
                        "gh", "api", f"{prefix}/releases/assets/{remote['id']}",
                        "-H", "Accept: application/octet-stream",
                    ], stdout=stream, stderr=subprocess.PIPE)
                if result.returncode or digest(downloaded) != digest(asset):
                    raise RuntimeError(f"Uploaded asset verification failed: {asset.name}")
    gh("release", "edit", tag, "--repo", repository, "--draft=false", "--latest")
    published = api(f"{prefix}/releases/tags/{tag}")
    if published["draft"]:
        raise RuntimeError("Release remains a draft")
    print(f"Published verified release: {published['html_url']}")


def resume(output):
    repository = os.environ["GITHUB_REPOSITORY"]
    ref = os.environ["GITHUB_REF"]
    match = re.fullmatch(r"refs/heads/publish/v(\d+\.\d+\.\d+)", ref)
    if not match:
        raise RuntimeError("Resume requires a publish/vX.Y.Z branch")
    version = match[1]
    tag = f"v{version}"
    prefix = f"repos/{repository}"
    release = find_release(prefix, tag)
    if not release:
        raise RuntimeError("There is no existing release draft to resume")
    if not release["draft"]:
        print(f"Published release remains unchanged: {release['html_url']}")
        return
    commit = tag_target(prefix, tag)
    runs = api(f"{prefix}/actions/runs?head_sha={commit}&event=push&per_page=100")
    for run in runs["workflow_runs"]:
        if run["head_sha"] != commit or run["head_branch"] != f"release/{tag}":
            continue
        jobs = api(f"{prefix}/actions/runs/{run['id']}/jobs?per_page=100")
        if not any(job["name"] == "build" and job["conclusion"] == "success"
                   for job in jobs["jobs"]):
            continue
        artifacts = api(f"{prefix}/actions/runs/{run['id']}/artifacts?per_page=100")
        if not any(item["name"] == "PhotoAgent-Lite-Setup" and not item["expired"]
                   for item in artifacts["artifacts"]):
            continue
        with tempfile.TemporaryDirectory() as temporary:
            gh("run", "download", str(run["id"]), "--repo", repository,
               "--name", "PhotoAgent-Lite-Setup", "--dir", temporary)
            actual_version, source, assets = prepare(Path(temporary), output)
            if actual_version != version or source != commit:
                raise RuntimeError("Recovered artifact differs from the immutable version tag")
            print(f"Resuming verified build {run['id']} without changing tag {tag}")
            publish(version, source, assets, expected_source=commit)
        return
    raise RuntimeError("No successful, unexpired build matches the version tag")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", type=Path)
    parser.add_argument("--assets", type=Path, default=Path("release-assets"))
    parser.add_argument("--prepare-only", action="store_true")
    parser.add_argument("--resume", action="store_true")
    arguments = parser.parse_args()
    if arguments.resume:
        resume(arguments.assets)
        return
    if arguments.package is None:
        parser.error("--package is required unless --resume is used")
    version, source, assets = prepare(arguments.package, arguments.assets)
    if not arguments.prepare_only:
        publish(version, source, assets)


if __name__ == "__main__":
    main()
