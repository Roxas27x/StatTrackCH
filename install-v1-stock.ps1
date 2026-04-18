$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $root "build-v1-stock.ps1"
$outDir = Join-Path $root "dist-v1-stock"
$stockDll = Join-Path $outDir "StatTrack.dll"
$patcherExe = Join-Path $outDir "V1StockAssemblyPatcher.exe"
$desktopOverlayExe = Join-Path $outDir "StatTrackOverlay.exe"

function Resolve-GameDir {
    $candidateDirs = @()
    if ($env:CLONE_HERO_BUILD_DIR) {
        $candidateDirs += $env:CLONE_HERO_BUILD_DIR
    }

    $candidateDirs += @(
        (Join-Path (Split-Path $root -Parent) "Clone Hero"),
        "C:\Users\Roxas\Documents\GDBOT\clone-hero-v1-writable"
    )

    foreach ($candidateDir in $candidateDirs | Select-Object -Unique) {
        if ([string]::IsNullOrWhiteSpace($candidateDir)) {
            continue
        }

        $candidateManagedDir = Join-Path $candidateDir "Clone Hero_Data\Managed"
        if (Test-Path (Join-Path $candidateManagedDir "Assembly-CSharp.dll")) {
            return $candidateDir
        }
    }

    throw "Unable to resolve a Clone Hero install directory. Set CLONE_HERO_BUILD_DIR or place a Clone Hero install at $(Join-Path (Split-Path $root -Parent) 'Clone Hero')."
}

$gameDir = Resolve-GameDir
$managedDir = Join-Path $gameDir "Clone Hero_Data\Managed"
$assemblyPath = Join-Path $managedDir "Assembly-CSharp.dll"
$backupAssemblyPath = Join-Path $managedDir "Assembly-CSharp.sectiontracker-backup.dll"
$targetHookDll = Join-Path $managedDir "StatTrack.dll"
$targetOverlayExe = Join-Path $managedDir "StatTrackOverlay.exe"
$legacyHookDll = Join-Path $managedDir "CloneHeroV1StockTracker.dll"
$legacyOverlayExe = Join-Path $managedDir "CloneHeroDesktopOverlay.exe"

if (-not (Test-Path $buildScript)) {
    throw "Missing build script: $buildScript"
}

& powershell -ExecutionPolicy Bypass -File $buildScript

if (-not (Test-Path $assemblyPath)) {
    throw "Missing target assembly: $assemblyPath"
}

if (Test-Path $backupAssemblyPath) {
    Copy-Item -LiteralPath $backupAssemblyPath -Destination $assemblyPath -Force
}

foreach ($legacyPath in @($legacyHookDll, $legacyOverlayExe)) {
    if ($legacyPath -ne $targetHookDll -and $legacyPath -ne $targetOverlayExe -and (Test-Path $legacyPath)) {
        Remove-Item -LiteralPath $legacyPath -Force
    }
}

Copy-Item -LiteralPath $stockDll -Destination $targetHookDll -Force
Copy-Item -LiteralPath $desktopOverlayExe -Destination $targetOverlayExe -Force
& $patcherExe $assemblyPath $targetHookDll

Write-Host "Installed stock tracker into $gameDir"
