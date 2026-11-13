$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$outputFolder = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
$buildTool = (Get-Command msbuild.exe -ErrorAction Stop).Source
$compiler = Join-Path (Split-Path $buildTool -Parent) 'Roslyn\csc.exe'
$executable = Join-Path $outputFolder 'CameraTests.exe'
$videoLibrary = Join-Path $repository 'packages\AForge.Video.2.2.5\lib\AForge.Video.dll'
$sources = @((Join-Path $PSScriptRoot 'CameraTests.cs'))
foreach ($name in @('CameraPreviewControl', 'CaptureForm', 'ExtraCaptureForm', 'CaptureWorkflow', 'CaptureState', 'CaptureCommand', 'CaptureResult', 'CommandProcessor', 'Config', 'SessionManager', 'PhotoSession')) {
    $sources += Join-Path $repository "PhotoAgent\$name.cs"
}
Copy-Item $videoLibrary $outputFolder -Force
Copy-Item (Join-Path $repository 'packages\AForge.2.2.5\lib\AForge.dll') $outputFolder -Force
& $compiler /nologo /codepage:65001 /target:exe "/out:$executable" /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$videoLibrary" $sources
if ($LASTEXITCODE -ne 0) { throw 'Camera regression tests did not compile' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Camera regression tests failed' }
