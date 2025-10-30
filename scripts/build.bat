@echo off
REM Build script for KNX Monitor (Production build for local testing)

setlocal enabledelayedexpansion

set SKIP_FRONTEND=0
set SKIP_BACKEND=0
set CLEAN=0

REM Parse arguments
:parse_args
if "%~1"=="" goto start
if /i "%~1"=="--skip-frontend" set SKIP_FRONTEND=1
if /i "%~1"=="--skip-backend" set SKIP_BACKEND=1
if /i "%~1"=="--clean" set CLEAN=1
if /i "%~1"=="/?" goto show_help
if /i "%~1"=="--help" goto show_help
shift
goto parse_args

:show_help
echo Usage: build.bat [--skip-frontend] [--skip-backend] [--clean]
echo.
echo Options:
echo   --skip-frontend    Skip frontend build
echo   --skip-backend     Skip backend build
echo   --clean            Clean previous builds
echo   --help             Show this help
exit /b 0

:start
cd /d "%~dp0\.."

echo ========================================
echo KNX Monitor - Production Build
echo ========================================
echo.

REM Clean previous builds
if %CLEAN%==1 (
    echo [1/4] Cleaning previous builds...

    if exist "publish" (
        rmdir /s /q "publish"
        echo   Removed: publish/
    )

    if exist "backend\KnxMonitor.Api\wwwroot" (
        rmdir /s /q "backend\KnxMonitor.Api\wwwroot"
        echo   Removed: backend\KnxMonitor.Api\wwwroot/
    )

    if exist "frontend\dist" (
        rmdir /s /q "frontend\dist"
        echo   Removed: frontend\dist/
    )

    echo.
)

REM Build Frontend
if %SKIP_FRONTEND%==0 (
    echo [2/4] Building Angular frontend...

    cd frontend
    call npm run build -- --configuration production
    if errorlevel 1 (
        echo Error: Frontend build failed
        exit /b 1
    )
    cd ..

    echo   Frontend build completed
    echo.

    REM Copy frontend to backend wwwroot
    echo [3/4] Copying frontend to backend...

    if not exist "backend\KnxMonitor.Api\wwwroot" mkdir "backend\KnxMonitor.Api\wwwroot"
    xcopy /E /I /Y "frontend\dist\frontend\browser\*" "backend\KnxMonitor.Api\wwwroot\" >nul

    echo   Copied to: backend\KnxMonitor.Api\wwwroot/
    echo.
) else (
    echo [2/4] Skipping frontend build
    echo [3/4] Skipping frontend copy
    echo.
)

REM Build Backend
if %SKIP_BACKEND%==0 (
    echo [4/4] Building backend...

    cd backend
    dotnet publish KnxMonitor.Api\KnxMonitor.Api.csproj -c Release -o ..\publish --self-contained false
    if errorlevel 1 (
        echo Error: Backend build failed
        exit /b 1
    )
    cd ..

    echo   Backend build completed
    echo.
) else (
    echo [4/4] Skipping backend build
    echo.
)

REM Summary
echo ========================================
echo Build completed successfully!
echo ========================================
echo.
echo Output directory: publish/
echo.
echo To run the application:
echo   Windows PS:   .\scripts\run-release.ps1
echo   Windows CMD:  .\scripts\run-release.bat
echo   Linux/Mac:    ./scripts/run-release.sh
echo.
