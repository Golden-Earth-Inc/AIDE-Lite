param(
    [Parameter(Mandatory=$true)]
    [string]$TargetProject
)

$ErrorActionPreference = "Stop"

Write-Host "=== AIDE Lite - Build & Deploy ===" -ForegroundColor Cyan
Write-Host "Source:  $PSScriptRoot" -ForegroundColor Gray

# Validate target exists
if (!(Test-Path $TargetProject)) {
    Write-Host "Error: Target project not found: $TargetProject" -ForegroundColor Red
    exit 1
}

# ============================================================
# C# Extension Build & Deploy
# ============================================================
Write-Host "`n--- C# Extension ---" -ForegroundColor Cyan

Write-Host "Building C# extension in Release mode..." -ForegroundColor Yellow
$srcDir = Join-Path $PSScriptRoot "src"
dotnet build $srcDir -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "C# build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "C# build succeeded." -ForegroundColor Green

# Determine output directory
$outputDir = Join-Path $srcDir "bin\Release\net8.0-windows"

# Create target extensions directory
$extensionsDir = Join-Path $TargetProject "extensions\AideLite"
if (!(Test-Path $extensionsDir)) {
    New-Item -ItemType Directory -Path $extensionsDir -Force | Out-Null
}

Write-Host "Deploying C# to: $extensionsDir" -ForegroundColor Yellow

# Copy DLL, manifest, and web assets
$filesToCopy = @(
    "AideLite.dll",
    "AideLite.deps.json",
    "manifest.json",
    "System.Security.Cryptography.ProtectedData.dll"
)

foreach ($file in $filesToCopy) {
    $src = Join-Path $outputDir $file
    if (Test-Path $src) {
        Copy-Item $src -Destination $extensionsDir -Force
        Write-Host "  Copied: $file" -ForegroundColor Gray
    }
}

# Copy WebAssets folder
$webAssetsSource = Join-Path $outputDir "WebAssets"
$webAssetsTarget = Join-Path $extensionsDir "WebAssets"
if (Test-Path $webAssetsSource) {
    if (Test-Path $webAssetsTarget) {
        Remove-Item $webAssetsTarget -Recurse -Force
    }
    Copy-Item $webAssetsSource -Destination $webAssetsTarget -Recurse -Force
    Write-Host "  Copied: WebAssets/" -ForegroundColor Gray
}

Write-Host "C# extension deployed." -ForegroundColor Green

# ============================================================
# Summary
# ============================================================
Write-Host "`n=== Deployment Complete ===" -ForegroundColor Green
Write-Host "Target: $TargetProject" -ForegroundColor Gray
Write-Host "  C# extension:  extensions\AideLite\" -ForegroundColor Gray
Write-Host "`nRemember: Studio Pro needs --enable-extension-development flag." -ForegroundColor Cyan
Write-Host "After deploying, restart Studio Pro or use App > Synchronize App Directory (F4)." -ForegroundColor Cyan
