$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $root "build-v1-stock.ps1"
$outDir = Join-Path $root "dist-v1-stock"
$stockDll = Join-Path $outDir "StatTrack.dll"
$patcherExe = Join-Path $outDir "V1StockAssemblyPatcher.exe"
$desktopOverlayExe = Join-Path $outDir "StatTrackOverlay.exe"
$workspaceRoot = Split-Path -Parent $root
$cleanAssemblyPath = Join-Path $workspaceRoot "Assembly-CSharp.dll"
$cleanAssetPath = Join-Path $workspaceRoot "sharedassets1.assets"
$shaderPatcherScript = Join-Path $root "tools\patch_animated_menu_shader.py"

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

function Move-ExistingFileAside {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$PreferredBackupPath
    )

    if (-not (Test-Path $Path)) {
        return $null
    }

    $backupPath = $PreferredBackupPath
    if (Test-Path $backupPath) {
        $dir = Split-Path -Parent $Path
        $leaf = Split-Path -Leaf $Path
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupPath = Join-Path $dir "$leaf.pre-stattrack-$stamp.bak"
        $counter = 1
        while (Test-Path $backupPath) {
            $backupPath = Join-Path $dir "$leaf.pre-stattrack-$stamp-$counter.bak"
            $counter++
        }
    }

    Move-Item -LiteralPath $Path -Destination $backupPath
    return $backupPath
}

$gameDir = Resolve-GameDir
$managedDir = Join-Path $gameDir "Clone Hero_Data\Managed"
$dataDir = Join-Path $gameDir "Clone Hero_Data"
$assemblyPath = Join-Path $managedDir "Assembly-CSharp.dll"
$backupAssemblyPath = Join-Path $managedDir "Assembly-CSharp.sectiontracker-backup.dll"
$targetHookDll = Join-Path $managedDir "StatTrack.dll"
$targetOverlayExe = Join-Path $managedDir "StatTrackOverlay.exe"
$legacyHookDll = Join-Path $managedDir "CloneHeroV1StockTracker.dll"
$sharedAssetsPath = Join-Path $dataDir "sharedassets1.assets"
$backupSharedAssetsPath = Join-Path $dataDir "sharedassets1.assets.stattrack-backup"

if (-not (Test-Path $buildScript)) {
    throw "Missing build script: $buildScript"
}

& powershell -ExecutionPolicy Bypass -File $buildScript

if (-not (Test-Path $assemblyPath)) {
    throw "Missing target assembly: $assemblyPath"
}
if (-not (Test-Path $sharedAssetsPath)) {
    throw "Missing target asset: $sharedAssetsPath"
}
if (-not (Test-Path $cleanAssemblyPath)) {
    throw "Missing bundled clean assembly: $cleanAssemblyPath"
}
if (-not (Test-Path $cleanAssetPath)) {
    throw "Missing bundled clean asset: $cleanAssetPath"
}

if ($renamedAssemblyPath = Move-ExistingFileAside -Path $assemblyPath -PreferredBackupPath $backupAssemblyPath) {
    Write-Host "Renamed existing Assembly-CSharp.dll to $renamedAssemblyPath"
}
Copy-Item -LiteralPath $cleanAssemblyPath -Destination $assemblyPath -Force

if ($renamedSharedAssetsPath = Move-ExistingFileAside -Path $sharedAssetsPath -PreferredBackupPath $backupSharedAssetsPath) {
    Write-Host "Renamed existing sharedassets1.assets to $renamedSharedAssetsPath"
}
Copy-Item -LiteralPath $cleanAssetPath -Destination $sharedAssetsPath -Force

Copy-Item -LiteralPath $stockDll -Destination $targetHookDll -Force
Copy-Item -LiteralPath $desktopOverlayExe -Destination $targetOverlayExe -Force
& $patcherExe $assemblyPath $targetHookDll
if (Test-Path $legacyHookDll) {
    Remove-Item -LiteralPath $legacyHookDll -Force -ErrorAction SilentlyContinue
}

if (Test-Path $shaderPatcherScript) {
    python $shaderPatcherScript $sharedAssetsPath $cleanAssetPath
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to patch animated menu wisps."
    }
}

Write-Host "Installed stock tracker into $gameDir"
