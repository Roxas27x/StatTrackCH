$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $root "build-v1-stock.ps1"
$outDir = Join-Path $root "dist-v1-stock"
$releaseDir = Join-Path $outDir "release"
$zipPath = Join-Path $outDir "StatTrack-v1.0.2.zip"

if (-not (Test-Path $buildScript)) {
    throw "Missing build script: $buildScript"
}

& powershell -ExecutionPolicy Bypass -File $buildScript

if (-not (Test-Path $releaseDir)) {
    throw "Missing release directory: $releaseDir"
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $releaseDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Packaged player release at $zipPath"
