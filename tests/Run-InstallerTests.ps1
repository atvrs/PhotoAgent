# This destructive lifecycle test belongs only on a disposable GitHub runner.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -ne 'true') {
    throw 'Installer lifecycle tests may run only on a disposable GitHub Actions runner.'
}
$repoRoot = Split-Path $PSScriptRoot -Parent
$setups = @(Get-ChildItem (Join-Path $repoRoot 'installer\output\PhotoAgent_Setup_*.exe'))
if ($setups.Count -ne 1) { throw 'Expected exactly one full installer.' }
$setup = $setups[0]
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\PhotoAgent Installer Test'
$installedExe = Join-Path $installDir 'PhotoAgent.exe'
$logs = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $logs | Out-Null
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$dataRoot = 'C:\psa_photos'
$photo = Join-Path $dataRoot 'photos\installer-test\keep.jpg'
$transferPhoto = Join-Path $dataRoot 'transfer\installer-test\keep.jpg'
$configPath = Join-Path $dataRoot 'config.json'
$appProcess = $null
$checks = 0

function Assert-Installer($Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    $script:checks++
    Write-Host "PASS $Message"
}

function Run-Setup([string]$File, [string]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -PassThru
    $null = $process.Handle
    if (!$process.WaitForExit(60000)) {
        $process.Kill()
        throw "Installer process timed out: $File"
    }
    $process.WaitForExit()
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "Installer exit code $($process.ExitCode): $File" }
}

function Assert-DataPreserved {
    Assert-Installer ((Get-FileHash $configPath).Hash -eq $script:configHash) 'Camera configuration preserved byte for byte'
    Assert-Installer ((Get-FileHash $photo).Hash -eq $script:photoHash) 'Existing photo preserved byte for byte'
    Assert-Installer ((Get-FileHash $transferPhoto).Hash -eq $script:photoHash) 'Transfer photo preserved byte for byte'
    foreach ($folder in 'commands', 'responses', 'photos', 'transfer', 'archive', 'logs') {
        Assert-Installer (Test-Path (Join-Path $dataRoot $folder)) "Data folder preserved: $folder"
    }
}

try {
    New-Item -ItemType Directory -Force (Split-Path $photo -Parent) | Out-Null
    New-Item -ItemType Directory -Force (Split-Path $transferPhoto -Parent) | Out-Null
    [IO.File]::WriteAllText($configPath, (@{
        previewWidth = 640; previewHeight = 480; captureTimeoutSec = 120
        logFileMaxMb = 1; logTotalMaxMb = 100
        archiveRetentionDays = 365; archiveMaxGb = 100; alwaysOnTop = $true
        camera1Name = 'test face'; camera2Name = 'test document'
        lomoCameraId = 'installer-test-face'; documentCameraId = 'installer-test-document'
    } | ConvertTo-Json))
    [IO.File]::WriteAllBytes($photo, [byte[]](1, 4, 8, 16, 32))
    Copy-Item $photo $transferPhoto
    $script:configHash = (Get-FileHash $configPath).Hash
    $script:photoHash = (Get-FileHash $photo).Hash
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty $runKey PhotoAgent '"C:\old portable\PhotoAgent.exe"'
    $shell = New-Object -ComObject WScript.Shell
    $oldShortcut = $shell.CreateShortcut((Join-Path $dataRoot 'PhotoAgent.lnk'))
    $oldShortcut.TargetPath = 'C:\old portable\PhotoAgent.exe'
    $oldShortcut.Save()

    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS=desktopicon /DIR="{0}" /LOG="{1}"' -f $installDir, (Join-Path $logs 'installer-first.log')
    Run-Setup $setup.FullName $arguments
    foreach ($file in 'PhotoAgent.exe', 'PhotoAgent.exe.config', 'AForge.dll', 'AForge.Video.dll',
        'AForge.Video.DirectShow.dll', 'photoagent.ico', 'unins000.exe', 'INSTALL_RU.md',
        'LICENSE.txt', 'THIRD_PARTY_NOTICES.txt', 'licenses\lgpl-3.0.txt', 'licenses\gpl-3.0.txt', 'PROTOCOL_RU.md') {
        Assert-Installer (Test-Path (Join-Path $installDir $file)) "Installed file: $file"
    }
    Assert-Installer ([Diagnostics.FileVersionInfo]::GetVersionInfo($installedExe).FileVersion -eq '1.2.0.0') 'Installed executable has expected version'
    $expectedRun = '"' + $installedExe + '"'
    Assert-Installer ((Get-ItemPropertyValue $runKey PhotoAgent) -eq $expectedRun) 'Autostart uses quoted installed path'
    $shortcuts = @(
        (Join-Path $dataRoot 'PhotoAgent.lnk'),
        (Join-Path ([Environment]::GetFolderPath('Desktop')) 'PhotoAgent.lnk'),
        (Join-Path ([Environment]::GetFolderPath('Programs')) 'PhotoAgent\PhotoAgent.lnk')
    )
    foreach ($shortcutPath in $shortcuts) {
        Assert-Installer ((Test-Path $shortcutPath) -and
            $shell.CreateShortcut($shortcutPath).TargetPath -eq $installedExe) "Shortcut targets installed executable: $shortcutPath"
    }
    Assert-DataPreserved

    # Run the real WinForms application from a directory containing spaces.
    # Configured fake device IDs prevent the first-run camera assignment dialog.
    $appProcess = Start-Process $installedExe -PassThru
    Start-Sleep -Seconds 3
    $appProcess.Refresh()
    Assert-Installer (!$appProcess.HasExited) 'Installed application starts and remains running'
    Assert-Installer ((Get-ItemPropertyValue $runKey PhotoAgent) -eq $expectedRun) 'Application retains quoted autostart path'
    Assert-DataPreserved
    $appProcess.Kill()
    $appProcess.WaitForExit()
    $appProcess = $null

    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="{0}"' -f (Join-Path $logs 'installer-upgrade.log')
    Run-Setup $setup.FullName $arguments
    Assert-Installer (Test-Path $installedExe) 'Reinstallation retains the existing installation directory'
    Assert-Installer ((Get-ItemPropertyValue $runKey PhotoAgent) -eq $expectedRun) 'Reinstallation retains correct autostart'
    Assert-DataPreserved

    Run-Setup (Join-Path $installDir 'unins000.exe') ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="{0}"' -f (Join-Path $logs 'installer-uninstall.log'))
    Assert-Installer (!(Test-Path $installedExe)) 'Uninstallation removes application'
    $runValue = Get-ItemProperty -Path $runKey -Name PhotoAgent -ErrorAction SilentlyContinue
    Assert-Installer ($null -eq $runValue) 'Uninstallation removes autostart'
    foreach ($shortcutPath in $shortcuts) {
        Assert-Installer (!(Test-Path $shortcutPath)) "Uninstallation removes shortcut: $shortcutPath"
    }
    Assert-DataPreserved
    Write-Host "$checks installer checks passed."
}
finally {
    if ($null -ne $appProcess -and !$appProcess.HasExited) {
        $appProcess.Kill()
        $appProcess.WaitForExit()
    }
}
