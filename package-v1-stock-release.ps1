$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $root "build-v1-stock.ps1"
$outDir = Join-Path $root "dist-v1-stock"
$releaseDir = Join-Path $outDir "release"
$releaseTemplateDir = Join-Path $root "release-v1-stock"
$versionPath = Join-Path $releaseTemplateDir "version.txt"

if (-not (Test-Path $buildScript)) {
    throw "Missing build script: $buildScript"
}

if (-not (Test-Path $versionPath)) {
    throw "Missing release version file: $versionPath"
}

$versionLine = Get-Content $versionPath | Where-Object { $_ -like "Release:*" } | Select-Object -First 1
if (-not $versionLine) {
    throw "Could not find release version in $versionPath"
}

if ($versionLine -notmatch "StatTrack v(?<version>\d+\.\d+\.\d+)") {
    throw "Could not parse release version from: $versionLine"
}

$zipPath = Join-Path $outDir ("StatTrack-v{0}.zip" -f $Matches.version)

& powershell -ExecutionPolicy Bypass -File $buildScript

if (-not (Test-Path $releaseDir)) {
    throw "Missing release directory: $releaseDir"
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $releaseDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Packaged player release at $zipPath"
