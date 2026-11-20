$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$outputFolder = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $outputFolder 'TransferCacheTests.exe'
$sources = @((Join-Path $PSScriptRoot 'TransferCacheTests.cs'))
foreach ($name in @('TransferCache', 'PhotoTransfer', 'CaptureCommand', 'Config')) {
    $sources += Join-Path $repository "PhotoAgent\$name.cs"
}
& $compiler /nologo /codepage:65001 /target:exe "/out:$executable" /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Transfer cache tests did not compile' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Transfer cache tests failed' }
