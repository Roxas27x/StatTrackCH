$ErrorActionPreference = "Stop"

param(
    [string]$GameDir,
    [switch]$RemoveTrackerData
)

function Select-CloneHeroFolder([string]$InitialDirectory)
{
    try {
        Add-Type -AssemblyName System.Windows.Forms | Out-Null
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = "Select the Clone Hero folder to uninstall the tracker from"
        if ($InitialDirectory -and (Test-Path $InitialDirectory)) {
            $dialog.SelectedPath = (Resolve-Path $InitialDirectory).Path
        }

        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            return $dialog.SelectedPath
        }
    }
    catch {
    }

    while ($true) {
        $inputPath = Read-Host "Enter the folder that contains 'Clone Hero.exe'"
        if ([string]::IsNullOrWhiteSpace($inputPath)) {
            continue
        }

        if (Test-Path $inputPath) {
            return (Resolve-Path $inputPath).Path
        }

        Write-Host "That path does not exist. Please try again." -ForegroundColor Yellow
    }
}

function Resolve-GameDirectory([string]$RequestedDirectory)
{
    $candidate = if ([string]::IsNullOrWhiteSpace($RequestedDirectory)) {
        Select-CloneHeroFolder $null
    }
    elseif (Test-Path $RequestedDirectory) {
        (Resolve-Path $RequestedDirectory).Path
    }
    else {
        Select-CloneHeroFolder $RequestedDirectory
    }

    $gameExePath = Join-Path $candidate "Clone Hero.exe"
    if (-not (Test-Path $gameExePath)) {
        throw "The selected folder does not contain 'Clone Hero.exe': $candidate"
    }

    return $candidate
}

$GameDir = Resolve-GameDirectory $GameDir
$managedDir = Join-Path $GameDir "Clone Hero_Data\Managed"
$assemblyPath = Join-Path $managedDir "Assembly-CSharp.dll"
$backupAssemblyPath = Join-Path $managedDir "Assembly-CSharp.sectiontracker-backup.dll"
$targetHookDll = Join-Path $managedDir "CloneHeroV1StockTracker.dll"
$targetOverlayExe = Join-Path $managedDir "CloneHeroDesktopOverlay.exe"

if (Test-Path $backupAssemblyPath) {
    Copy-Item -LiteralPath $backupAssemblyPath -Destination $assemblyPath -Force
    Remove-Item -LiteralPath $backupAssemblyPath -Force
    Write-Host "Restored original Assembly-CSharp.dll backup." -ForegroundColor Green
}
else {
    Write-Host "No tracker backup assembly was found. The mod files will be removed, but Assembly-CSharp.dll will not be restored automatically." -ForegroundColor Yellow
}

foreach ($path in @($targetHookDll, $targetOverlayExe)) {
    if (Test-Path $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

if ($RemoveTrackerData) {
    $trackerDataDir = Join-Path $env:LOCALAPPDATA "CloneHeroSectionTracker"
    if (Test-Path $trackerDataDir) {
        Remove-Item -LiteralPath $trackerDataDir -Recurse -Force
        Write-Host "Removed tracker data from $trackerDataDir" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Clone Hero Section Tracker uninstall complete." -ForegroundColor Green
Write-Host "Game folder: $GameDir"
