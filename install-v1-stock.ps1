$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $root "build-v1-stock.ps1"
$outDir = Join-Path $root "dist-v1-stock"
$stockDll = Join-Path $outDir "CloneHeroV1StockTracker.dll"
$patcherExe = Join-Path $outDir "V1StockAssemblyPatcher.exe"
$desktopOverlayExe = Join-Path $outDir "CloneHeroDesktopOverlay.exe"
$gameDir = "C:\Users\Roxas\Documents\GDBOT\clone-hero-v1-writable"
$managedDir = Join-Path $gameDir "Clone Hero_Data\Managed"
$assemblyPath = Join-Path $managedDir "Assembly-CSharp.dll"
$targetHookDll = Join-Path $managedDir "CloneHeroV1StockTracker.dll"
$targetOverlayExe = Join-Path $managedDir "CloneHeroDesktopOverlay.exe"

if (-not (Test-Path $buildScript)) {
    throw "Missing build script: $buildScript"
}

& powershell -ExecutionPolicy Bypass -File $buildScript

if (-not (Test-Path $assemblyPath)) {
    throw "Missing target assembly: $assemblyPath"
}

Copy-Item -LiteralPath $stockDll -Destination $targetHookDll -Force
Copy-Item -LiteralPath $desktopOverlayExe -Destination $targetOverlayExe -Force
& $patcherExe $assemblyPath $targetHookDll

Write-Host "Installed stock tracker into $gameDir"
