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
$prerequisites = Join-Path $installerRoot 'prerequisites'
$output = Join-Path $installerRoot 'output'
New-Item -ItemType Directory -Force $prerequisites, $output | Out-Null

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

& $iscc "/DAppVersion=$version" (Join-Path $installerRoot 'PhotoAgent.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
$setup = Join-Path $output "PhotoAgent_Setup_$version.exe"
Copy-Item (Join-Path $installerRoot 'INSTALL_RU.md') $output -Force
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($setup))" | Set-Content (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
$sourceCommit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (& git -C $repoRoot rev-parse HEAD) }
$runtimeHash = (Get-FileHash $runtime -Algorithm SHA256).Hash.ToLowerInvariant()
@"
PhotoAgent $version
Source commit: $sourceCommit
Built in UTC: $([DateTime]::UtcNow.ToString('o'))
Compiler: $([Diagnostics.FileVersionInfo]::GetVersionInfo($iscc).FileVersion)
Microsoft .NET Framework 4.8 offline runtime SHA256: $runtimeHash
Runtime signature: $($signature.Status), $($signature.SignerCertificate.Subject)
Setup SHA256: $hash
"@ | Set-Content (Join-Path $output 'BUILD_INFO.txt') -Encoding UTF8
Write-Host "Created $setup"
