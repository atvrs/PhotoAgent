$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$outputFolder = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
$buildTool = (Get-Command msbuild.exe -ErrorAction Stop).Source
$compiler = Join-Path (Split-Path $buildTool -Parent) 'Roslyn\csc.exe'
$executable = Join-Path $outputFolder 'CameraTests.exe'
$videoLibrary = Join-Path $repository 'packages\AForge.Video.2.2.5\lib\AForge.Video.dll'
$sources = @((Join-Path $PSScriptRoot 'CameraTests.cs'))
foreach ($name in @('CameraPreviewControl', 'CameraShutdown', 'CaptureForm', 'ExtraCaptureForm', 'CaptureWorkflow', 'CaptureState', 'CaptureCommand', 'CaptureResult', 'CommandProcessor', 'PhotoTransfer', 'TransferCache', 'Config', 'SessionManager', 'PhotoSession')) {
    $sources += Join-Path $repository "PhotoAgent\$name.cs"
}
Copy-Item $videoLibrary $outputFolder -Force
Copy-Item (Join-Path $repository 'packages\AForge.2.2.5\lib\AForge.dll') $outputFolder -Force
& $compiler /nologo /codepage:65001 /target:exe "/out:$executable" /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$videoLibrary" $sources
if ($LASTEXITCODE -ne 0) { throw 'Camera regression tests did not compile' }
$stdout = Join-Path $outputFolder 'camera-tests.stdout.log'
$stderr = Join-Path $outputFolder 'camera-tests.stderr.log'
$process = Start-Process $executable -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
# Retain the native handle so Windows PowerShell can read ExitCode after WaitForExit.
$null = $process.Handle
$finished = $process.WaitForExit(60000)
if (-not $finished) { Stop-Process -Id $process.Id -Force }
if ($finished) {
    $process.WaitForExit()
    $process.Refresh()
}
Get-Content $stdout
Get-Content $stderr
if (-not $finished) { throw 'Camera regression tests exceeded 60 seconds' }
if ($process.ExitCode -ne 0) { throw "Camera regression tests failed with exit code $($process.ExitCode)" }
