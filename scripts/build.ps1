#!/usr/bin/env pwsh
# Build script for KNX Monitor (Production build for local testing)

param(
    [switch]$SkipFrontend,
    [switch]$SkipBackend,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

# Change to project root
Set-Location $PSScriptRoot\..

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "KNX Monitor - Production Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean previous builds
if ($Clean) {
    Write-Host "[1/4] Cleaning previous builds..." -ForegroundColor Yellow

    if (Test-Path "publish") {
        Remove-Item -Recurse -Force "publish"
        Write-Host "  Removed: publish/" -ForegroundColor Gray
    }

    if (Test-Path "backend/KnxMonitor.Api/wwwroot") {
        Remove-Item -Recurse -Force "backend/KnxMonitor.Api/wwwroot"
        Write-Host "  Removed: backend/KnxMonitor.Api/wwwroot/" -ForegroundColor Gray
    }

    if (Test-Path "frontend/dist") {
        Remove-Item -Recurse -Force "frontend/dist"
        Write-Host "  Removed: frontend/dist/" -ForegroundColor Gray
    }

    Write-Host ""
}

# Build Frontend
if (-not $SkipFrontend) {
    Write-Host "[2/4] Building Angular frontend..." -ForegroundColor Yellow

    Push-Location frontend
    try {
        npm run build -- --configuration production
        if ($LASTEXITCODE -ne 0) { throw "Frontend build failed" }
    }
    finally {
        Pop-Location
    }

    Write-Host "  Frontend build completed" -ForegroundColor Green
    Write-Host ""

    # Copy frontend to backend wwwroot
    Write-Host "[3/4] Copying frontend to backend..." -ForegroundColor Yellow

    if (-not (Test-Path "backend/KnxMonitor.Api/wwwroot")) {
        New-Item -ItemType Directory -Path "backend/KnxMonitor.Api/wwwroot" | Out-Null
    }

    Copy-Item -Path "frontend/dist/frontend/browser/*" -Destination "backend/KnxMonitor.Api/wwwroot/" -Recurse -Force
    Write-Host "  Copied to: backend/KnxMonitor.Api/wwwroot/" -ForegroundColor Green
    Write-Host ""
}
else {
    Write-Host "[2/4] Skipping frontend build" -ForegroundColor Gray
    Write-Host "[3/4] Skipping frontend copy" -ForegroundColor Gray
    Write-Host ""
}

# Build Backend
if (-not $SkipBackend) {
    Write-Host "[4/4] Building backend..." -ForegroundColor Yellow

    Push-Location backend
    try {
        dotnet publish KnxMonitor.Api/KnxMonitor.Api.csproj `
            -c Release `
            -o ../publish `
            --self-contained false

        if ($LASTEXITCODE -ne 0) { throw "Backend build failed" }
    }
    finally {
        Pop-Location
    }

    Write-Host "  Backend build completed" -ForegroundColor Green
    Write-Host ""
}
else {
    Write-Host "[4/4] Skipping backend build" -ForegroundColor Gray
    Write-Host ""
}

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output directory: publish/" -ForegroundColor White
Write-Host ""
Write-Host "To run the application:" -ForegroundColor White
Write-Host "  Windows PS:   .\scripts\run-release.ps1" -ForegroundColor Cyan
Write-Host "  Windows CMD:  .\scripts\run-release.bat" -ForegroundColor Cyan
Write-Host "  Linux/Mac:    ./scripts/run-release.sh" -ForegroundColor Cyan
Write-Host ""
