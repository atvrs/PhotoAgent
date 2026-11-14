param([switch]$WithoutNet)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$installerRoot = $PSScriptRoot
$repoRoot = Split-Path $installerRoot -Parent
$exe = Join-Path $repoRoot 'PhotoAgent\bin\Release\PhotoAgent.exe'
if (!(Test-Path $exe)) { throw 'Build the Release application before packaging.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
$version = ([Version]$version).ToString(3)

$iscc = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Inno Setup 6\ISCC.exe'
if (!(Test-Path $iscc)) { throw 'Inno Setup 6 is required to build the installer.' }
$suffix = if ($WithoutNet) { '_Lite' } else { '' }
$outputName = if ($WithoutNet) { 'output-lite' } else { 'output' }
$guideName = if ($WithoutNet) { 'INSTALL_LITE_RU.md' } else { 'INSTALL_RU.md' }
$output = Join-Path $installerRoot $outputName
New-Item -ItemType Directory -Force $output | Out-Null
$compilerArguments = @("/DAppVersion=$version")
$runtimeInfo = 'Microsoft .NET Framework: not bundled; version 4.8 or newer is required.'

if (!$WithoutNet) {
    $prerequisites = Join-Path $installerRoot 'prerequisites'
    New-Item -ItemType Directory -Force $prerequisites | Out-Null
    # Official offline runtime, linked from:
    # https://dotnet.microsoft.com/download/dotnet-framework/thank-you/net48-offline-installer
    $runtime = Join-Path $prerequisites 'NDP48-x86-x64-AllOS-ENU.exe'
    if (!(Test-Path $runtime)) {
        Invoke-WebRequest -UseBasicParsing -Uri 'https://go.microsoft.com/fwlink/?linkid=2088631' -OutFile $runtime
    }
    $signature = Get-AuthenticodeSignature $runtime
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') {
        throw 'The .NET offline runtime must have a valid Microsoft Authenticode signature.'
    }
    Write-Host "Verified Microsoft .NET offline runtime ($((Get-Item $runtime).Length) bytes)."
    $runtimeHash = (Get-FileHash $runtime -Algorithm SHA256).Hash.ToLowerInvariant()
    $runtimeInfo = "Microsoft .NET Framework 4.8 offline runtime SHA256: $runtimeHash" +
        [Environment]::NewLine + "Runtime signature: $($signature.Status), $($signature.SignerCertificate.Subject)"
} else {
    $compilerArguments += '/DWithoutNet'
    Write-Host '.NET download and bundling are disabled for the Lite package.'
}

$compilerOutput = & $iscc @compilerArguments (Join-Path $installerRoot 'PhotoAgent.iss')
$compilerExitCode = $LASTEXITCODE
$compilerOutput | ForEach-Object { Write-Host $_ }
if ($compilerExitCode -ne 0) { throw "Inno Setup failed with exit code $compilerExitCode" }
$compilerVersion = ($compilerOutput | Select-String '^Compiler engine version: (.+)$' |
    Select-Object -First 1).Matches[0].Groups[1].Value
$setup = Join-Path $output "PhotoAgent_Setup_$version$suffix.exe"
Copy-Item (Join-Path $installerRoot $guideName) (Join-Path $output 'INSTALL_RU.md') -Force
Copy-Item (Join-Path $repoRoot 'README.MD') (Join-Path $output 'PROTOCOL_RU.md') -Force
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($setup))" | Set-Content (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
$sourceCommit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (& git -C $repoRoot rev-parse HEAD) }
@"
PhotoAgent $version
Source commit: $sourceCommit
Built in UTC: $([DateTime]::UtcNow.ToString('o'))
Compiler: $compilerVersion
$runtimeInfo
Setup SHA256: $hash
"@ | Set-Content (Join-Path $output 'BUILD_INFO.txt') -Encoding UTF8
Write-Host "Created $setup"
