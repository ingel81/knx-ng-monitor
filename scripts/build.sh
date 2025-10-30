#!/usr/bin/env bash
# Build script for KNX Monitor (Production build for local testing)

set -e

SKIP_FRONTEND=false
SKIP_BACKEND=false
CLEAN=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --skip-frontend)
            SKIP_FRONTEND=true
            shift
            ;;
        --skip-backend)
            SKIP_BACKEND=true
            shift
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        --help)
            echo "Usage: $0 [--skip-frontend] [--skip-backend] [--clean]"
            echo ""
            echo "Options:"
            echo "  --skip-frontend    Skip frontend build"
            echo "  --skip-backend     Skip backend build"
            echo "  --clean            Clean previous builds"
            echo "  --help             Show this help"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--skip-frontend] [--skip-backend] [--clean]"
            exit 1
            ;;
    esac
done

# Change to project root
cd "$(dirname "$0")/.."

echo "========================================"
echo "KNX Monitor - Production Build"
echo "========================================"
echo ""

# Clean previous builds
if [ "$CLEAN" = true ]; then
    echo "[1/4] Cleaning previous builds..."

    if [ -d "publish" ]; then
        rm -rf publish
        echo "  Removed: publish/"
    fi

    if [ -d "backend/KnxMonitor.Api/wwwroot" ]; then
        rm -rf backend/KnxMonitor.Api/wwwroot
        echo "  Removed: backend/KnxMonitor.Api/wwwroot/"
    fi

    if [ -d "frontend/dist" ]; then
        rm -rf frontend/dist
        echo "  Removed: frontend/dist/"
    fi

    echo ""
fi

# Build Frontend
if [ "$SKIP_FRONTEND" = false ]; then
    echo "[2/4] Building Angular frontend..."

    cd frontend
    npm run build -- --configuration production
    cd ..

    echo "  Frontend build completed"
    echo ""

    # Copy frontend to backend wwwroot
    echo "[3/4] Copying frontend to backend..."

    mkdir -p backend/KnxMonitor.Api/wwwroot
    cp -r frontend/dist/frontend/browser/* backend/KnxMonitor.Api/wwwroot/

    echo "  Copied to: backend/KnxMonitor.Api/wwwroot/"
    echo ""
else
    echo "[2/4] Skipping frontend build"
    echo "[3/4] Skipping frontend copy"
    echo ""
fi

# Build Backend
if [ "$SKIP_BACKEND" = false ]; then
    echo "[4/4] Building backend..."

    cd backend
    dotnet publish KnxMonitor.Api/KnxMonitor.Api.csproj \
        -c Release \
        -o ../publish \
        --self-contained false
    cd ..

    echo "  Backend build completed"
    echo ""
else
    echo "[4/4] Skipping backend build"
    echo ""
fi

# Summary
echo "========================================"
echo "Build completed successfully!"
echo "========================================"
echo ""
echo "Output directory: publish/"
echo ""
echo "To run the application:"
echo "  Windows PS:   ./scripts/run-release.ps1"
echo "  Windows CMD:  ./scripts/run-release.bat"
echo "  Linux/Mac:    ./scripts/run-release.sh"
echo ""
