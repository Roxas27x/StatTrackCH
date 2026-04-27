$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $root "dist-v1-stock"
$stockSrc = Join-Path $root "src\V1StockTracker.cs"
$exportTemplatesSrc = Join-Path $root "src\ExportTemplates.cs"
$patcherSrc = Join-Path $root "src\V1StockAssemblyPatcher.cs"
$desktopOverlaySrc = Join-Path $root "src\DesktopOverlayApp.cs"
$runtimeCheckerSrc = Join-Path $root "src\V1RuntimeCompatibilityChecker.cs"
$stockDll = Join-Path $outDir "StatTrack.dll"
$patcherExe = Join-Path $outDir "V1StockAssemblyPatcher.exe"
$desktopOverlayExe = Join-Path $outDir "StatTrackOverlay.exe"
$runtimeCheckerExe = Join-Path $outDir "V1RuntimeCompatibilityChecker.exe"
$releaseTemplateDir = Join-Path $root "release-v1-stock"
$releaseDir = Join-Path $outDir "release"
$workspaceRoot = Split-Path -Parent $root
$cleanAssemblyPath = Join-Path $workspaceRoot "Assembly-CSharp.dll"
$cleanAssetPath = Join-Path $workspaceRoot "sharedassets1.assets"
$assetBaselineDir = Join-Path $root "asset-baselines"
$shaderPatcherScript = Join-Path $root "tools\patch_animated_menu_shader.py"
$cecilDll = Join-Path $root ".deps\cecil\Mono.Cecil.dll"
$cecilRocksDll = Join-Path $root ".deps\cecil\Mono.Cecil.Rocks.dll"
$modernCsc = Join-Path $root ".deps\nuget\Microsoft.Net.Compilers.Toolset.4.10.0\tasks\net472\csc.exe"
$legacyCsc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$csc = if (Test-Path $modernCsc) { $modernCsc } else { $legacyCsc }

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
        if ((Test-Path (Join-Path $candidateManagedDir "UnityEngine.dll")) -and
            (Test-Path (Join-Path $candidateManagedDir "Assembly-CSharp.dll"))) {
            return $candidateDir
        }
    }

    throw "Unable to resolve a Clone Hero build directory. Set CLONE_HERO_BUILD_DIR or place a Clone Hero install at $(Join-Path (Split-Path $root -Parent) 'Clone Hero')."
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-UnitySerializedVersion {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $length = [Math]::Min([int]$stream.Length, 4096)
        $buffer = New-Object byte[] $length
        [void]$stream.Read($buffer, 0, $length)
    } finally {
        $stream.Dispose()
    }

    $text = [System.Text.Encoding]::ASCII.GetString($buffer)
    if ($text -match "20\d{2}\.\d+\.\d+f\d+") {
        return $Matches[0]
    }

    return "unknown"
}

$gameDir = Resolve-GameDir
$managedDir = Join-Path $gameDir "Clone Hero_Data\Managed"

if (-not (Test-Path $stockSrc)) { throw "Missing source file: $stockSrc" }
if (-not (Test-Path $exportTemplatesSrc)) { throw "Missing source file: $exportTemplatesSrc" }
if (-not (Test-Path $patcherSrc)) { throw "Missing source file: $patcherSrc" }
if (-not (Test-Path $desktopOverlaySrc)) { throw "Missing source file: $desktopOverlaySrc" }
if (-not (Test-Path $runtimeCheckerSrc)) { throw "Missing source file: $runtimeCheckerSrc" }
if (-not (Test-Path (Join-Path $releaseTemplateDir "Install StatTrack.cmd"))) { throw "Missing release template: $(Join-Path $releaseTemplateDir 'Install StatTrack.cmd')" }
if (-not (Test-Path (Join-Path $releaseTemplateDir "Uninstall StatTrack.cmd"))) { throw "Missing release template: $(Join-Path $releaseTemplateDir 'Uninstall StatTrack.cmd')" }
if (-not (Test-Path (Join-Path $releaseTemplateDir "Uninstall StatTrack and Wipe Data.cmd"))) { throw "Missing release template: $(Join-Path $releaseTemplateDir 'Uninstall StatTrack and Wipe Data.cmd')" }
if (-not (Test-Path (Join-Path $releaseTemplateDir "README.txt"))) { throw "Missing release template: $(Join-Path $releaseTemplateDir 'README.txt')" }
if (-not (Test-Path (Join-Path $releaseTemplateDir "version.txt"))) { throw "Missing release template: $(Join-Path $releaseTemplateDir 'version.txt')" }
if (-not (Test-Path (Join-Path $releaseTemplateDir "RELEASE_NOTES.txt"))) { throw "Missing release template: $(Join-Path $releaseTemplateDir 'RELEASE_NOTES.txt')" }
if (-not (Test-Path (Join-Path $releaseTemplateDir "CHANGELOG.txt"))) { throw "Missing release template: $(Join-Path $releaseTemplateDir 'CHANGELOG.txt')" }
if (-not (Test-Path $cleanAssemblyPath)) { throw "Missing bundled clean assembly: $cleanAssemblyPath" }
if (-not (Test-Path $cleanAssetPath)) { throw "Missing bundled clean asset: $cleanAssetPath" }
if (-not (Test-Path $shaderPatcherScript)) { throw "Missing shader patcher script: $shaderPatcherScript" }
if (-not (Test-Path $csc)) { throw "Missing compiler: $csc" }
if (-not (Test-Path $cecilDll)) { throw "Missing Mono.Cecil: $cecilDll" }
if (-not (Test-Path $cecilRocksDll)) { throw "Missing Mono.Cecil.Rocks: $cecilRocksDll" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$stockRefs = @(
    (Join-Path $managedDir "UnityEngine.dll"),
    (Join-Path $managedDir "UnityEngine.CoreModule.dll"),
    (Join-Path $managedDir "UnityEngine.IMGUIModule.dll"),
    (Join-Path $managedDir "UnityEngine.InputLegacyModule.dll"),
    (Join-Path $managedDir "UnityEngine.TextRenderingModule.dll"),
    (Join-Path $managedDir "UnityEngine.UI.dll"),
    (Join-Path $managedDir "UnityEngine.UIModule.dll"),
    (Join-Path $managedDir "Newtonsoft.Json.dll"),
    "System.dll",
    "System.Core.dll"
)

$stockReferenceArgs = $stockRefs | ForEach-Object { "/reference:`"$_`"" }
& $csc /nologo /target:library /langversion:latest /optimize+ /warnaserror+ /out:$stockDll $stockReferenceArgs $stockSrc $exportTemplatesSrc
if ($LASTEXITCODE -ne 0) { throw "Failed to build stock helper." }

& $csc /nologo /target:exe /langversion:latest /optimize+ /warnaserror+ /out:$patcherExe /reference:System.dll /reference:System.Core.dll /reference:$cecilDll /reference:$cecilRocksDll $patcherSrc
if ($LASTEXITCODE -ne 0) { throw "Failed to build stock patcher." }

$desktopOverlayRefs = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Web.Extensions.dll",
    "System.Windows.Forms.dll"
)
$desktopOverlayReferenceArgs = $desktopOverlayRefs | ForEach-Object { "/reference:`"$_`"" }
& $csc /nologo /target:winexe /langversion:latest /optimize+ /warnaserror+ /out:$desktopOverlayExe $desktopOverlayReferenceArgs $desktopOverlaySrc
if ($LASTEXITCODE -ne 0) { throw "Failed to build desktop overlay." }

& $csc /nologo /target:exe /langversion:latest /optimize+ /warnaserror+ /out:$runtimeCheckerExe /reference:System.dll /reference:System.Core.dll /reference:$cecilDll $runtimeCheckerSrc
if ($LASTEXITCODE -ne 0) { throw "Failed to build runtime compatibility checker." }

Copy-Item -LiteralPath $cecilDll -Destination (Join-Path $outDir "Mono.Cecil.dll") -Force
Copy-Item -LiteralPath $cecilRocksDll -Destination (Join-Path $outDir "Mono.Cecil.Rocks.dll") -Force

& $runtimeCheckerExe $stockDll $managedDir
if ($LASTEXITCODE -ne 0) { throw "Runtime compatibility check failed." }

if (Test-Path $releaseDir) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
Copy-Item -LiteralPath $stockDll -Destination (Join-Path $releaseDir "StatTrack.dll") -Force
Copy-Item -LiteralPath $patcherExe -Destination (Join-Path $releaseDir "V1StockAssemblyPatcher.exe") -Force
Copy-Item -LiteralPath $desktopOverlayExe -Destination (Join-Path $releaseDir "StatTrackOverlay.exe") -Force
Copy-Item -LiteralPath $runtimeCheckerExe -Destination (Join-Path $releaseDir "V1RuntimeCompatibilityChecker.exe") -Force
Copy-Item -LiteralPath $cecilDll -Destination (Join-Path $releaseDir "Mono.Cecil.dll") -Force
Copy-Item -LiteralPath $cecilRocksDll -Destination (Join-Path $releaseDir "Mono.Cecil.Rocks.dll") -Force
Copy-Item -LiteralPath (Join-Path $releaseTemplateDir "Install StatTrack.cmd") -Destination (Join-Path $releaseDir "Install StatTrack.cmd") -Force
Copy-Item -LiteralPath (Join-Path $releaseTemplateDir "Uninstall StatTrack.cmd") -Destination (Join-Path $releaseDir "Uninstall StatTrack.cmd") -Force
Copy-Item -LiteralPath (Join-Path $releaseTemplateDir "Uninstall StatTrack and Wipe Data.cmd") -Destination (Join-Path $releaseDir "Uninstall StatTrack and Wipe Data.cmd") -Force
Copy-Item -LiteralPath (Join-Path $releaseTemplateDir "README.txt") -Destination (Join-Path $releaseDir "README.txt") -Force
Copy-Item -LiteralPath (Join-Path $releaseTemplateDir "version.txt") -Destination (Join-Path $releaseDir "version.txt") -Force
Copy-Item -LiteralPath (Join-Path $releaseTemplateDir "RELEASE_NOTES.txt") -Destination (Join-Path $releaseDir "RELEASE_NOTES.txt") -Force
Copy-Item -LiteralPath (Join-Path $releaseTemplateDir "CHANGELOG.txt") -Destination (Join-Path $releaseDir "CHANGELOG.txt") -Force

$releaseCleanDir = Join-Path $releaseDir "clean"
$releasePatchedDir = Join-Path $releaseDir "patched"
New-Item -ItemType Directory -Force -Path $releaseCleanDir | Out-Null
New-Item -ItemType Directory -Force -Path $releasePatchedDir | Out-Null
$releaseCleanAssemblyPath = Join-Path $releaseCleanDir "Assembly-CSharp.dll"
$releaseCleanAssetPath = Join-Path $releaseCleanDir "sharedassets1.assets"
$releasePatchedAssetPath = Join-Path $releasePatchedDir "sharedassets1.assets"
Copy-Item -LiteralPath $cleanAssemblyPath -Destination $releaseCleanAssemblyPath -Force
Copy-Item -LiteralPath $cleanAssetPath -Destination $releaseCleanAssetPath -Force

$sharedAssetBaselinePaths = @($cleanAssetPath)
if (Test-Path $assetBaselineDir) {
    $sharedAssetBaselinePaths += Get-ChildItem -LiteralPath $assetBaselineDir -Filter "sharedassets1-*.assets" -File | ForEach-Object { $_.FullName }
}

$seenSharedAssetHashes = @{}
$manifestLines = @()
$rootCleanAssetHash = Get-Sha256 -Path $cleanAssetPath

foreach ($sharedAssetBaselinePath in $sharedAssetBaselinePaths) {
    $baselineHash = Get-Sha256 -Path $sharedAssetBaselinePath
    if ($seenSharedAssetHashes.ContainsKey($baselineHash)) {
        continue
    }

    $seenSharedAssetHashes[$baselineHash] = $true
    $baselineVersion = Get-UnitySerializedVersion -Path $sharedAssetBaselinePath
    $variantFileName = "sharedassets1.$baselineHash.assets"
    $releaseCleanVariantPath = Join-Path $releaseCleanDir $variantFileName
    $releasePatchedVariantPath = Join-Path $releasePatchedDir $variantFileName

    Copy-Item -LiteralPath $sharedAssetBaselinePath -Destination $releaseCleanVariantPath -Force
    Copy-Item -LiteralPath $sharedAssetBaselinePath -Destination $releasePatchedVariantPath -Force
    & python $shaderPatcherScript $releasePatchedVariantPath $releaseCleanVariantPath
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build patched sharedassets1.assets variant for $baselineVersion ($baselineHash)."
    }

    $variantBackupPath = "$releasePatchedVariantPath.animatedmenu-runtime-tint.bak"
    if (Test-Path $variantBackupPath) {
        Remove-Item -LiteralPath $variantBackupPath -Force
    }
    $fixedBackupPath = Join-Path $releasePatchedDir "sharedassets1.assets.animatedmenu-runtime-tint.bak"
    if (Test-Path $fixedBackupPath) {
        Remove-Item -LiteralPath $fixedBackupPath -Force
    }

    $sourceName = Split-Path -Leaf $sharedAssetBaselinePath
    $manifestLines += "$baselineHash|$baselineVersion|$variantFileName|$sourceName"
    Write-Host "Built sharedassets1.assets patch variant for $baselineVersion ($baselineHash)"
}

$manifestPath = Join-Path $releasePatchedDir "sharedassets1-manifest.txt"
Set-Content -LiteralPath $manifestPath -Value $manifestLines -Encoding ASCII

$rootPatchedVariantPath = Join-Path $releasePatchedDir "sharedassets1.$rootCleanAssetHash.assets"
if (-not (Test-Path $rootPatchedVariantPath)) {
    throw "Missing patched sharedassets1.assets variant for bundled clean asset hash $rootCleanAssetHash."
}
Copy-Item -LiteralPath $rootPatchedVariantPath -Destination $releasePatchedAssetPath -Force

Write-Host "Built stock helper at $stockDll"
Write-Host "Built stock patcher at $patcherExe"
Write-Host "Built desktop overlay at $desktopOverlayExe"
Write-Host "Built runtime checker at $runtimeCheckerExe"
Write-Host "Built player release at $releaseDir"
