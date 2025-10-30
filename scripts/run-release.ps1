#!/usr/bin/env pwsh
# Run the production build of KNX Monitor

$ErrorActionPreference = "Stop"

# Change to project root
Set-Location $PSScriptRoot\..

if (-not (Test-Path "publish/KnxMonitor.Api.dll")) {
    Write-Host "Error: No production build found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please run the build script first:" -ForegroundColor Yellow
    Write-Host "  Windows PS:   .\scripts\build.ps1" -ForegroundColor Cyan
    Write-Host "  Windows CMD:  .\scripts\build.bat" -ForegroundColor Cyan
    Write-Host "  Linux/Mac:    ./scripts/build.sh" -ForegroundColor Cyan
    Write-Host ""
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "KNX Monitor - Running Production Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Push-Location publish
try {
    $env:ASPNETCORE_ENVIRONMENT = "Production"
    dotnet KnxMonitor.Api.dll
}
finally {
    Pop-Location
}
