$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $root "build-v1-stock.ps1"
$outDir = Join-Path $root "dist-v1-stock"
$stockDll = Join-Path $outDir "StatTrack.dll"
$patcherExe = Join-Path $outDir "V1StockAssemblyPatcher.exe"
$desktopOverlayExe = Join-Path $outDir "StatTrackOverlay.exe"
$canonicalCleanAssemblyPath = "C:\Users\Roxas\Documents\GDBOT\CHCLEANDONOTOVERWRITE\Clone Hero\Clone Hero_Data\Managed\Assembly-CSharp.dll"

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
$cleanBackupAssemblyPath = Join-Path $managedDir "Assembly-CSharp.dll.stocktracker.bak"
$legacyBackupAssemblyPath = Join-Path $managedDir "Assembly-CSharp.sectiontracker-backup.dll"
$targetHookDll = Join-Path $managedDir "StatTrack.dll"
$targetOverlayExe = Join-Path $managedDir "StatTrackOverlay.exe"
$legacyHookDll = Join-Path $managedDir "CloneHeroV1StockTracker.dll"

if (-not (Test-Path $buildScript)) {
    throw "Missing build script: $buildScript"
}

& powershell -ExecutionPolicy Bypass -File $buildScript

if (-not (Test-Path $assemblyPath)) {
    throw "Missing target assembly: $assemblyPath"
}

if (Test-Path $canonicalCleanAssemblyPath) {
    Copy-Item -LiteralPath $canonicalCleanAssemblyPath -Destination $assemblyPath -Force
    Copy-Item -LiteralPath $canonicalCleanAssemblyPath -Destination $cleanBackupAssemblyPath -Force
}
elseif (Test-Path $cleanBackupAssemblyPath) {
    Copy-Item -LiteralPath $cleanBackupAssemblyPath -Destination $assemblyPath -Force
}
elseif (Test-Path $legacyBackupAssemblyPath) {
    Copy-Item -LiteralPath $legacyBackupAssemblyPath -Destination $assemblyPath -Force
}

Copy-Item -LiteralPath $stockDll -Destination $targetHookDll -Force
Copy-Item -LiteralPath $desktopOverlayExe -Destination $targetOverlayExe -Force
& $patcherExe $assemblyPath $targetHookDll
if (Test-Path $legacyHookDll) {
    Remove-Item -LiteralPath $legacyHookDll -Force -ErrorAction SilentlyContinue
}

Write-Host "Installed stock tracker into $gameDir"
