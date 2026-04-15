$ErrorActionPreference = "Stop"

param(
    [string]$GameDir
)

$releaseRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$stockDll = Join-Path $releaseRoot "CloneHeroV1StockTracker.dll"
$patcherExe = Join-Path $releaseRoot "V1StockAssemblyPatcher.exe"
$desktopOverlayExe = Join-Path $releaseRoot "CloneHeroDesktopOverlay.exe"
$runtimeCheckerExe = Join-Path $releaseRoot "V1RuntimeCompatibilityChecker.exe"

foreach ($requiredPath in @($stockDll, $patcherExe, $desktopOverlayExe, $runtimeCheckerExe)) {
    if (-not (Test-Path $requiredPath)) {
        throw "Missing release file: $requiredPath"
    }
}

function Select-CloneHeroFolder([string]$InitialDirectory)
{
    try {
        Add-Type -AssemblyName System.Windows.Forms | Out-Null
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = "Select the Clone Hero folder that contains 'Clone Hero.exe'"
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
    $managedDir = Join-Path $candidate "Clone Hero_Data\Managed"
    $assemblyPath = Join-Path $managedDir "Assembly-CSharp.dll"

    if (-not (Test-Path $gameExePath)) {
        throw "The selected folder does not contain 'Clone Hero.exe': $candidate"
    }

    if (-not (Test-Path $assemblyPath)) {
        throw "The selected folder does not contain 'Clone Hero_Data\\Managed\\Assembly-CSharp.dll': $candidate"
    }

    return $candidate
}

$GameDir = Resolve-GameDirectory $GameDir
$managedDir = Join-Path $GameDir "Clone Hero_Data\Managed"
$assemblyPath = Join-Path $managedDir "Assembly-CSharp.dll"
$backupAssemblyPath = Join-Path $managedDir "Assembly-CSharp.sectiontracker-backup.dll"
$targetHookDll = Join-Path $managedDir "CloneHeroV1StockTracker.dll"
$targetOverlayExe = Join-Path $managedDir "CloneHeroDesktopOverlay.exe"

if (-not (Test-Path $backupAssemblyPath)) {
    Copy-Item -LiteralPath $assemblyPath -Destination $backupAssemblyPath -Force
}

& $runtimeCheckerExe $stockDll $managedDir
if ($LASTEXITCODE -ne 0) {
    throw "Runtime compatibility check failed for '$GameDir'."
}

Copy-Item -LiteralPath $stockDll -Destination $targetHookDll -Force
Copy-Item -LiteralPath $desktopOverlayExe -Destination $targetOverlayExe -Force
& $patcherExe $assemblyPath $targetHookDll
if ($LASTEXITCODE -ne 0) {
    throw "Failed to patch Assembly-CSharp.dll"
}

Write-Host ""
Write-Host "Clone Hero Section Tracker installed successfully." -ForegroundColor Green
Write-Host "Game folder: $GameDir"
Write-Host "Backup created at: $backupAssemblyPath"
Write-Host ""
Write-Host "Launch 'Clone Hero.exe' from that folder and use Home / Ctrl+O / F8 to open the overlay."
