param(
    [string]$GameDir = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $root "build-v1-stock.ps1"
$outDir = Join-Path $root "dist-v1-stock"
$stockDll = Join-Path $outDir "StatTrack.dll"
$patcherExe = Join-Path $outDir "V1StockAssemblyPatcher.exe"
$desktopOverlayExe = Join-Path $outDir "StatTrackOverlay.exe"
$releasePatchedDir = Join-Path $outDir "release\patched"
$patchedSharedAssetsManifest = Join-Path $releasePatchedDir "sharedassets1-manifest.txt"
$workspaceRoot = Split-Path -Parent $root
$cleanAssemblyPath = Join-Path $workspaceRoot "Assembly-CSharp.dll"

function Resolve-GameDir {
    $candidateDirs = @()
    if ($GameDir) {
        $candidateDirs += $GameDir
    }

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

function Restore-BaselineFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$BackupPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (Test-Path $BackupPath) {
        Copy-Item -LiteralPath $BackupPath -Destination $Path -Force
        Write-Host "Restored $Label baseline from $BackupPath"
        return
    }

    if (-not (Test-Path $Path)) {
        throw "Missing $Label`: $Path"
    }

    Move-Item -LiteralPath $Path -Destination $BackupPath
    Copy-Item -LiteralPath $BackupPath -Destination $Path -Force
    Write-Host "Renamed existing $Label to $BackupPath"
    Write-Host "Restored $Label baseline for patching."
}

function Get-AssetInfo {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$GameDir
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $count = [Math]::Min($bytes.Length, 4096)
    $text = [System.Text.Encoding]::ASCII.GetString($bytes, 0, $count)
    $assetVersion = "unknown"
    if ($text -match "20\d{2}\.\d+\.\d+f\d+") {
        $assetVersion = $Matches[0]
    }

    $engineVersion = "unknown"
    $unityPlayer = Join-Path $GameDir "UnityPlayer.dll"
    if (Test-Path $unityPlayer) {
        $productVersion = (Get-Item -LiteralPath $unityPlayer).VersionInfo.ProductVersion
        if ($productVersion -match "20\d{2}\.\d+\.\d+f\d+") {
            $engineVersion = $Matches[0]
        }
    }

    [PSCustomObject]@{
        Hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
        AssetVersion = $assetVersion
        UnityPlayerVersion = $engineVersion
    }
}

function Get-VersionMatchedPatchedAsset {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$PatchedDir,
        [Parameter(Mandatory = $true)]$AssetInfo
    )

    $entries = @(Get-Content -LiteralPath $ManifestPath | Where-Object { $_ -and -not $_.StartsWith("#") } | ForEach-Object {
        $parts = $_ -split "\|"
        if ($parts.Count -ge 3) {
            [PSCustomObject]@{
                Hash = $parts[0]
                Version = $parts[1]
                File = $parts[2]
            }
        }
    })

    $preferred = @()
    if ($AssetInfo.UnityPlayerVersion -and $AssetInfo.UnityPlayerVersion -ne "unknown") {
        $preferred += [PSCustomObject]@{ Version = $AssetInfo.UnityPlayerVersion; Reason = "UnityPlayer version" }
    }
    if ($AssetInfo.AssetVersion -and $AssetInfo.AssetVersion -ne "unknown") {
        $preferred += [PSCustomObject]@{ Version = $AssetInfo.AssetVersion; Reason = "asset serialized version" }
    }

    foreach ($candidate in $preferred) {
        $matches = @($entries | Where-Object { $_.Version -eq $candidate.Version })
        if ($matches.Count -eq 1) {
            $path = Join-Path $PatchedDir $matches[0].File
            if (Test-Path $path) {
                return [PSCustomObject]@{ Path = $path; Reason = $candidate.Reason }
            }
        }
    }

    return $null
}

function Install-PatchedSharedAssets {
    param(
        [Parameter(Mandatory = $true)][string]$GameDir,
        [Parameter(Mandatory = $true)][string]$SharedAssetsPath,
        [Parameter(Mandatory = $true)][string]$PatchedDir,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $assetInfo = Get-AssetInfo -Path $SharedAssetsPath -GameDir $GameDir
    Write-Host ""
    Write-Host "Detected sharedassets1.assets baseline:"
    Write-Host "  Asset serialized version: $($assetInfo.AssetVersion)"
    Write-Host "  UnityPlayer version: $($assetInfo.UnityPlayerVersion)"
    Write-Host "  SHA256: $($assetInfo.Hash)"

    $preferUnityPlayerVersion = $assetInfo.UnityPlayerVersion -ne "unknown" -and
        $assetInfo.AssetVersion -ne "unknown" -and
        $assetInfo.UnityPlayerVersion -ne $assetInfo.AssetVersion

    $patchedAsset = $null
    $hashMatchedPath = Join-Path $PatchedDir ("sharedassets1.{0}.assets" -f $assetInfo.Hash)
    if (-not $preferUnityPlayerVersion -and (Test-Path $hashMatchedPath)) {
        $patchedAsset = [PSCustomObject]@{ Path = $hashMatchedPath; Reason = "exact baseline hash" }
    }
    else {
        if ($preferUnityPlayerVersion) {
            Write-Host "  Asset version mismatch detected; selecting the patch for the UnityPlayer version."
        }
        $patchedAsset = Get-VersionMatchedPatchedAsset -ManifestPath $ManifestPath -PatchedDir $PatchedDir -AssetInfo $assetInfo
    }

    if ($patchedAsset -eq $null) {
        Write-Warning "Unsupported sharedassets1.assets baseline. Keeping the restored baseline to avoid black menus or missing textures."
        return
    }

    Copy-Item -LiteralPath $patchedAsset.Path -Destination $SharedAssetsPath -Force
    Write-Host "Installed StatTrack animated menu asset patch using $($patchedAsset.Reason)."
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
if (-not (Test-Path $releasePatchedDir)) {
    throw "Missing release patched asset directory: $releasePatchedDir"
}
if (-not (Test-Path $patchedSharedAssetsManifest)) {
    throw "Missing patched sharedassets1.assets manifest: $patchedSharedAssetsManifest"
}

if ($renamedAssemblyPath = Move-ExistingFileAside -Path $assemblyPath -PreferredBackupPath $backupAssemblyPath) {
    Write-Host "Renamed existing Assembly-CSharp.dll to $renamedAssemblyPath"
}
Copy-Item -LiteralPath $cleanAssemblyPath -Destination $assemblyPath -Force

Restore-BaselineFile -Path $sharedAssetsPath -BackupPath $backupSharedAssetsPath -Label "sharedassets1.assets"

Copy-Item -LiteralPath $stockDll -Destination $targetHookDll -Force
Copy-Item -LiteralPath $desktopOverlayExe -Destination $targetOverlayExe -Force
& $patcherExe $assemblyPath $targetHookDll
if (Test-Path $legacyHookDll) {
    Remove-Item -LiteralPath $legacyHookDll -Force -ErrorAction SilentlyContinue
}

Install-PatchedSharedAssets -GameDir $gameDir -SharedAssetsPath $sharedAssetsPath -PatchedDir $releasePatchedDir -ManifestPath $patchedSharedAssetsManifest

Write-Host "Installed stock tracker into $gameDir"
