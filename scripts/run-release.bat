@echo off
REM Run the production build of KNX Monitor

cd /d "%~dp0\.."

if not exist "publish\KnxMonitor.Api.dll" (
    echo Error: No production build found!
    echo.
    echo Please run the build script first:
    echo   Windows PS:   .\scripts\build.ps1
    echo   Windows CMD:  .\scripts\build.bat
    echo   Linux/Mac:    ./scripts/build.sh
    echo.
    exit /b 1
)

echo ========================================
echo KNX Monitor - Running Production Build
echo ========================================
echo.

cd publish
set ASPNETCORE_ENVIRONMENT=Production
dotnet KnxMonitor.Api.dll
