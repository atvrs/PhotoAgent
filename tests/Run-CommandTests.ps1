$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$outputFolder = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $outputFolder 'CommandTests.exe'
$sources = @(
    (Join-Path $PSScriptRoot 'CommandTests.cs')
    (Join-Path $repository 'PhotoAgent\CommandWatcher.cs')
    (Join-Path $repository 'PhotoAgent\CommandProcessor.cs')
    (Join-Path $repository 'PhotoAgent\CaptureCommand.cs')
    (Join-Path $repository 'PhotoAgent\CaptureResult.cs')
    (Join-Path $repository 'PhotoAgent\CaptureState.cs')
    (Join-Path $repository 'PhotoAgent\PhotoTransfer.cs')
)
& $compiler /nologo /codepage:65001 /target:exe "/out:$executable" /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Command regression tests did not compile' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Command regression tests failed' }
