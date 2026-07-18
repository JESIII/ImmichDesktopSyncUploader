# Immich Uploader - install/uninstall helper
# Usage:
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -Install
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -Uninstall
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -Install -SourceDir "C:\path\to\publish"

[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Uninstall,
    [string]$SourceDir = "",
    [switch]$AutoStart
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$InstallDir = Join-Path $env:LOCALAPPDATA 'ImmichUploader'
$StartMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Immich Uploader'
$ExeName = 'ImmichUploader.exe'
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunValueName = 'ImmichUploader'

function Write-Section($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

function Test-Administrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Install-ImmichUploader {
    if (-not $SourceDir) {
        $SourceDir = $ScriptDir
    }
    if (-not (Test-Path (Join-Path $SourceDir $ExeName))) {
        throw "Could not find $ExeName in $SourceDir. Build/publish the app first (dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true) and run this script from the publish output, or pass -SourceDir."
    }
    Write-Section "Installing to $InstallDir"
    if (-not (Test-Path $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }
    Get-ChildItem -Path $SourceDir -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $InstallDir -Force
    }
    Write-Host "Copied $($ExeName) and support files."

    Write-Section "Creating Start Menu shortcut"
    if (-not (Test-Path $StartMenuDir)) {
        New-Item -ItemType Directory -Path $StartMenuDir -Force | Out-Null
    }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path $StartMenuDir 'Immich Uploader.lnk'))
    $shortcut.TargetPath = Join-Path $InstallDir $ExeName
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description = 'Immich Uploader (tray app)'
    $shortcut.Save()
    Write-Host "Shortcut: $StartMenuDir\Immich Uploader.lnk"

    Write-Section "Open log folder shortcut"
    $logShortcut = $shell.CreateShortcut((Join-Path $StartMenuDir 'Immich Uploader Logs.lnk'))
    $logShortcut.TargetPath = Join-Path $InstallDir 'Logs'
    $logShortcut.Description = 'Open the Immich Uploader log folder'
    $logShortcut.Save()

    if ($AutoStart) {
        Write-Section "Enabling autostart on Windows login"
        Set-ItemProperty -Path $RunKey -Name $RunValueName -Value "`"$InstallDir\$ExeName`" --tray"
        Write-Host "Autostart enabled."
    }

    Write-Section "Done"
    Write-Host "Install location: $InstallDir" -ForegroundColor Green
    Write-Host "Launch from Start Menu or run: `"$InstallDir\$ExeName`"" -ForegroundColor Green
    Write-Host "Use -AutoStart on the install command to also register it to launch on login."
}

function Uninstall-ImmichUploader {
    Write-Section "Removing autostart entry"
    if (Get-ItemProperty -Path $RunKey -Name $RunValueName -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $RunKey -Name $RunValueName -ErrorAction SilentlyContinue
        Write-Host "Autostart removed."
    } else {
        Write-Host "Autostart entry not present."
    }

    Write-Section "Removing Start Menu shortcuts"
    if (Test-Path $StartMenuDir) {
        Remove-Item -LiteralPath $StartMenuDir -Recurse -Force
        Write-Host "Removed: $StartMenuDir"
    }

    Write-Section "Removing install folder"
    if (Test-Path $InstallDir) {
        Write-Host "About to delete: $InstallDir"
        $confirm = Read-Host "Type YES to confirm (logs and state.json will be lost)"
        if ($confirm -eq 'YES') {
            Remove-Item -LiteralPath $InstallDir -Recurse -Force
            Write-Host "Removed: $InstallDir"
        } else {
            Write-Host "Skipped."
        }
    }

    Write-Section "Done"
}

if ($Install) { Install-ImmichUploader; exit 0 }
if ($Uninstall) { Uninstall-ImmichUploader; exit 0 }

Write-Host "Usage:"
Write-Host "  powershell -ExecutionPolicy Bypass -File setup.ps1 -Install [-SourceDir PATH] [-AutoStart]"
Write-Host "  powershell -ExecutionPolicy Bypass -File setup.ps1 -Uninstall"
exit 1
