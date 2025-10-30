#!/usr/bin/env bash
# Run the production build of KNX Monitor

set -e

# Change to project root
cd "$(dirname "$0")/.."

if [ ! -f "publish/KnxMonitor.Api.dll" ]; then
    echo "Error: No production build found!"
    echo ""
    echo "Please run the build script first:"
    echo "  Windows PS:   ./scripts/build.ps1"
    echo "  Windows CMD:  ./scripts/build.bat"
    echo "  Linux/Mac:    ./scripts/build.sh"
    echo ""
    exit 1
fi

echo "========================================"
echo "KNX Monitor - Running Production Build"
echo "========================================"
echo ""

cd publish
ASPNETCORE_ENVIRONMENT=Production dotnet KnxMonitor.Api.dll
