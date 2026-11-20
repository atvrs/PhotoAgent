$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$outputFolder = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $outputFolder 'ArchiveTests.exe'
$sources = @(
    (Join-Path $PSScriptRoot 'ArchiveTests.cs')
    (Join-Path $repository 'PhotoAgent\ArchiveManager.cs')
    (Join-Path $repository 'PhotoAgent\Config.cs')
    (Join-Path $repository 'PhotoAgent\ConfigManager.cs')
)
& $compiler /nologo /codepage:65001 /target:exe "/out:$executable" /r:System.Core.dll /r:System.Web.Extensions.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Archive regression tests did not compile' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Archive regression tests failed' }
