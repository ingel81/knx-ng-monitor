# Build Scripts

This directory contains build and run scripts for local production testing of KNX Monitor.

## Available Scripts

### Build Scripts
- `build.ps1` - PowerShell build script (Windows)
- `build.bat` - Batch build script (Windows CMD)
- `build.sh` - Shell build script (Linux/Mac)

### Run Scripts
- `run-release.ps1` - PowerShell run script (Windows)
- `run-release.bat` - Batch run script (Windows CMD)
- `run-release.sh` - Shell run script (Linux/Mac)

## Usage

### Windows (PowerShell)
```powershell
# Build production release
.\scripts\build.ps1

# Build with options
.\scripts\build.ps1 -Clean                # Clean previous builds
.\scripts\build.ps1 -SkipFrontend         # Only build backend
.\scripts\build.ps1 -SkipBackend          # Only build frontend

# Run production build
.\scripts\run-release.ps1
```

### Windows (CMD)
```cmd
REM Build production release
.\scripts\build.bat

REM Build with options
.\scripts\build.bat --clean               # Clean previous builds
.\scripts\build.bat --skip-frontend       # Only build backend
.\scripts\build.bat --skip-backend        # Only build frontend

REM Run production build
.\scripts\run-release.bat
```

### Linux/Mac
```bash
# Build production release
./scripts/build.sh

# Build with options
./scripts/build.sh --clean                # Clean previous builds
./scripts/build.sh --skip-frontend        # Only build backend
./scripts/build.sh --skip-backend         # Only build frontend

# Run production build
./scripts/run-release.sh
```

## What the Scripts Do

### Build Process
1. **Build Frontend** - Compiles Angular app with production configuration
2. **Copy Frontend** - Copies frontend build to `backend/KnxMonitor.Api/wwwroot/`
3. **Build Backend** - Publishes .NET backend to `publish/` directory

### Run Process
- Sets `ASPNETCORE_ENVIRONMENT=Production`
- Runs the compiled application from `publish/` directory
- Application available at `http://localhost:8080`

## Output Directories

- `publish/` - Production build output (ignored by git)
- `backend/KnxMonitor.Api/wwwroot/` - Frontend static files (ignored by git)
- `frontend/dist/` - Frontend build artifacts (ignored by git)

## Notes

- These scripts are for local testing only
- For production deployment, use Docker or GitHub Actions
- All output directories are excluded from version control
