# Release Automation Plan

## Übersicht

Automatisierte Builds und Releases via GitHub Actions, die bei jedem Git Tag (Pattern `v*`) ausgelöst werden.

**Status:** ✅ Implementiert (v0.0.1+)

## Ziele

1. **Multi-Architecture Binaries**: Self-contained Single-File Executables für alle wichtigen Plattformen
2. **Docker Images**: Multi-Platform Images für linux/amd64 und linux/arm64 auf DockerHub
3. **Automatische Releases**: GitHub Releases mit allen Assets und automatisch generierten Release Notes

## Architektur-Matrix

### .NET Binaries (Self-Contained Single-File)

| Platform | Architecture | Runtime Identifier | Output Format |
|----------|--------------|---------------------|---------------|
| Linux | x64 | linux-x64 | tar.gz |
| Linux | ARM64 | linux-arm64 | tar.gz |
| Windows | x64 | win-x64 | zip |
| Windows | ARM64 | win-arm64 | zip |
| macOS | x64 | osx-x64 | tar.gz |
| macOS | ARM64 | osx-arm64 | tar.gz |

**Build-Parameter:**
- `--self-contained true`
- `-p:PublishSingleFile=true`
- `-p:IncludeNativeLibrariesForSelfExtract=true`
- `-p:EnableCompressionInSingleFile=true`
- `-c Release`

**Erwartete Größe:** ~80-90 MB pro Binary

**Windows Build Fix:** Alle Builds verwenden explizit `shell: bash` (auch auf Windows via Git Bash), um PowerShell-Inkompatibilitäten mit Backslash-Fortsetzung zu vermeiden.

### Docker Images

| Platform | Architecture | Base Image | Build-Strategie |
|----------|--------------|------------|-----------------|
| linux/amd64 | x64 | debian:12-slim (~75 MB) | Self-Contained |

**Image Tags:**
- `ingel81/knx-ng-monitor:latest` (immer neuester Build)
- `ingel81/knx-ng-monitor:<version>` (z.B. v0.0.1)

**Image-Größe:** ~120-130 MB (Self-Contained Binary ~80 MB + Debian Slim ~75 MB + Dependencies)

**Base Image Wahl:**
- **debian:12-slim** statt alpine wegen glibc Kompatibilität
- .NET Self-Contained Binaries sind gegen glibc gelinkt
- Alpine (musl-libc) führt zu "no such file or directory" Fehler beim Exec
- Debian Slim: +30 MB größer, aber native glibc-Kompatibilität

**Plattform-Hinweis:**
- Docker Image nur für **linux/amd64** (native Build, schnell)
- ARM64 wird via QEMU emuliert und ist zu langsam/instabil für CI
- ARM64 **Binaries** sind trotzdem verfügbar (GitHub Actions Matrix)
- ARM64 Docker kann später mit Self-Hosted ARM Runner hinzugefügt werden

**Vorteile Self-Contained Docker:**
- ✅ Konsistente Strategie mit Binaries
- ✅ Kleineres finales Image (~120 MB vs. ~300 MB mit aspnet:9.0)
- ✅ Keine .NET Runtime-Abhängigkeit
- ✅ Native glibc - keine Kompatibilitätsprobleme

## Datei-Struktur

```
knx-ng-monitor/
├── .github/
│   └── workflows/
│       └── release.yml           # GitHub Actions Workflow
├── Dockerfile                     # Multi-Stage Build
├── .dockerignore                  # Docker Build Optimierung
└── RELEASE_PLAN.md               # Dieses Dokument
```

## Workflow-Details

### Trigger
```yaml
on:
  push:
    tags:
      - 'v*'    # Beispiel: v1.0.0, v2.1.3
```

### Job 1: build-binaries (Matrix Build)

**Zweck:** Binaries für alle Plattformen parallel bauen

**Schritte:**
1. Checkout Code
2. Setup Node.js (für Angular Frontend)
3. Setup .NET 9 SDK
4. Restore Dependencies (npm + dotnet)
5. Build Angular Frontend (`ng build --configuration production`)
6. Copy Frontend zu `backend/KnxMonitor.Api/wwwroot/`
7. Publish .NET mit entsprechendem Runtime Identifier
8. Archivieren (zip für Windows, tar.gz für Linux/macOS)
9. Upload als Artifact für späteren Release-Upload

**Matrix:**
```yaml
matrix:
  include:
    - os: ubuntu-latest
      rid: linux-x64
      artifact: knx-ng-monitor-linux-x64.tar.gz
    - os: ubuntu-latest
      rid: linux-arm64
      artifact: knx-ng-monitor-linux-arm64.tar.gz
    - os: windows-latest
      rid: win-x64
      artifact: knx-ng-monitor-win-x64.zip
    - os: windows-latest
      rid: win-arm64
      artifact: knx-ng-monitor-win-arm64.zip
    - os: macos-latest
      rid: osx-x64
      artifact: knx-ng-monitor-osx-x64.tar.gz
    - os: macos-latest
      rid: osx-arm64
      artifact: knx-ng-monitor-osx-arm64.tar.gz
```

### Job 2: build-docker

**Zweck:** Docker Image für amd64 bauen und zu DockerHub pushen

**Schritte:**
1. Checkout Code
2. Setup Docker Buildx
3. Login zu DockerHub (mit Secrets)
4. Extract Version aus Git Tag
5. Build und Push Docker Image
   - Platform: `linux/amd64` (native, schnell)
   - Tags: `latest` und `<version>`
6. Logout von DockerHub

**Hinweis:** ARM64 Docker wurde entfernt wegen QEMU-Instabilität. ARM64 Binaries sind weiterhin verfügbar.

**Benötigte Secrets:**
- `DOCKERHUB_USERNAME`
- `DOCKERHUB_TOKEN`

### Job 3: create-release

**Zweck:** GitHub Release mit allen Assets erstellen

**Schritte:**
1. Download aller Binary-Artifacts aus Job 1
2. Generate Release Notes (automatisch aus Commits seit letztem Tag)
3. Create GitHub Release
   - Title: Version (z.B. "v1.0.0")
   - Body: Automatische Release Notes + Docker Hub Link
   - Assets: Alle 6 Binary-Archive

**Release Notes Format:**
```markdown
## Changes

[Automatisch generierte Commit-Liste seit letztem Tag]

## Docker Image

docker pull username/knx-ng-monitor:v1.0.0

## Downloads

Binary downloads are available below for all supported platforms.
```

## Dockerfile (Multi-Stage Build)

### Stage 1: Build Frontend (Node.js)
```dockerfile
FROM node:20-alpine AS frontend-build
WORKDIR /app/frontend
COPY frontend/package*.json ./
RUN npm ci --silent
COPY frontend/ ./
RUN npm run build -- --configuration production
```

### Stage 2: Build Self-Contained Backend (.NET)
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS backend-build
ARG TARGETARCH

WORKDIR /app
COPY backend/ ./
COPY --from=frontend-build /app/frontend/dist/frontend/browser ./KnxMonitor.Api/wwwroot

# Build self-contained binary for target architecture
# publish führt automatisch restore mit dem korrekten RID aus
RUN if [ "$TARGETARCH" = "arm64" ]; then \
        RID="linux-arm64"; \
    else \
        RID="linux-x64"; \
    fi && \
    dotnet publish KnxMonitor.Api/KnxMonitor.Api.csproj \
        -c Release \
        -r $RID \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -o /app/publish
```

### Stage 3: Debian Slim Runtime (glibc compatible)
```dockerfile
FROM debian:12-slim
WORKDIR /app

# Install runtime dependencies for .NET self-contained apps
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        libicu72 \
        ca-certificates && \
    rm -rf /var/lib/apt/lists/*

COPY --from=backend-build /app/publish .
RUN mkdir -p /app/data && chmod +x /app/KnxMonitor.Api

VOLUME /app/data
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Direkter Binary-Start (kein 'dotnet' Command nötig!)
ENTRYPOINT ["/app/KnxMonitor.Api"]
```

**Wichtige Änderungen zur ursprünglichen Planung:**
1. **Angular Output-Pfad:** `dist/frontend/browser` (Projektname in angular.json)
2. **Self-Contained Build:** Debian Slim Base Image statt aspnet:9.0 Runtime
3. **Base Image Wahl:** debian:12-slim (glibc) statt alpine (musl-libc) für Kompatibilität
4. **Multi-Platform:** Automatische RID-Auswahl via `TARGETARCH` Build-Arg
5. **Restore-Strategie:** `dotnet publish` führt restore automatisch mit korrektem RID aus

## GitHub Secrets Setup

### DockerHub Token erstellen
1. Login zu hub.docker.com
2. Account Settings → Security → Access Tokens
3. "New Access Token" erstellen mit **Read & Write** Berechtigung
4. Description: z.B. "GitHub Actions - knx-ng-monitor"
5. Token kopieren (wird nur einmal angezeigt!)

### GitHub Secrets hinzufügen
1. GitHub Repository → Settings → Secrets and variables → Actions
2. "New repository secret" klicken
3. Secrets hinzufügen:
   - Name: `DOCKERHUB_USERNAME`, Value: `ingel81`
   - Name: `DOCKERHUB_TOKEN`, Value: Der generierte Token (Read & Write)

## Release-Prozess

### Lokales Vorgehen
```bash
# 1. Letzte Änderungen committen
git add .
git commit -m "Prepare release v1.0.0"

# 2. Tag erstellen
git tag v1.0.0

# 3. Tag pushen (startet automatisch Build)
git push origin v1.0.0

# 4. Warten auf GitHub Actions (~15-20 Minuten)
# 5. GitHub Release prüfen
# 6. Docker Image testen: docker pull ingel81/knx-ng-monitor:v0.0.1
```

### Build-Zeiten (geschätzt)
- Binary Builds (parallel): ~8-12 Minuten
- Docker Build: ~5-8 Minuten
- Release Creation: ~1 Minute
- **Gesamt: ~15-20 Minuten**

## Testing

### Binary Testing
```bash
# Linux/macOS
wget https://github.com/user/knx-ng-monitor/releases/download/v1.0.0/knx-ng-monitor-linux-x64.tar.gz
tar -xzf knx-ng-monitor-linux-x64.tar.gz
./KnxMonitor.Api

# Windows
# Download .zip, entpacken, KnxMonitor.Api.exe ausführen
```

### Docker Testing
```bash
# Pull latest
docker pull ingel81/knx-ng-monitor:latest

# Run
docker run -d \
  -p 8080:8080 \
  -v $(pwd)/data:/app/data \
  --name knx-monitor \
  ingel81/knx-ng-monitor:latest

# Logs
docker logs -f knx-monitor

# Open Browser: http://localhost:8080

# Inspect Image Size
docker images ingel81/knx-ng-monitor
# Expected: ~120-130 MB
```

## Production Deployment

### Erstmaliges Setup

Nach dem ersten Start wird automatisch:
1. JWT Secret generiert und in `/app/data/.jwt-secret` (Docker) oder `./data/.jwt-secret` (Binary) gespeichert
2. SQLite Datenbank angelegt in `/app/data/knxmonitor.db` (Docker) oder `./data/knxmonitor.db` (Binary)
3. Initial Setup Wizard im Frontend angezeigt (Admin-User erstellen)

### Docker Deployment (Empfohlen)

**docker-compose.yml Beispiel:**
```yaml
version: '3.8'

services:
  knx-monitor:
    image: ingel81/knx-ng-monitor:latest
    container_name: knx-monitor
    restart: unless-stopped
    ports:
      - "8080:8080"
    volumes:
      - ./data:/app/data
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
    # Optional: Custom Port
    # environment:
    #   - ASPNETCORE_URLS=http://+:5000
    # ports:
    #   - "5000:5000"
```

**Start:**
```bash
docker-compose up -d
```

**Zugriff:**
- Browser: `http://<server-ip>:8080`
- Initial Setup durchführen (Admin-User erstellen)

### Binary Deployment

**Linux/macOS:**
```bash
# Download und entpacken
wget https://github.com/ingel81/knx-ng-monitor/releases/download/v0.0.1/knx-ng-monitor-linux-x64.tar.gz
tar -xzf knx-ng-monitor-linux-x64.tar.gz
cd knx-ng-monitor-linux-x64

# Starten
./KnxMonitor.Api

# Im Browser öffnen
# http://localhost:8080
```

**Windows:**
```powershell
# Download und entpacken
# knx-ng-monitor-win-x64.zip

# Starten
.\KnxMonitor.Api.exe

# Im Browser öffnen
# http://localhost:8080
```

**Systemd Service (Linux):**
```ini
[Unit]
Description=KNX NG Monitor
After=network.target

[Service]
Type=simple
User=knxmonitor
WorkingDirectory=/opt/knx-monitor
ExecStart=/opt/knx-monitor/KnxMonitor.Api
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
```

### Daten-Persistenz

**Wichtige Dateien in `./data/`:**
- `.jwt-secret` - JWT Secret (NICHT löschen, sonst werden alle Sessions ungültig)
- `knxmonitor.db` - SQLite Datenbank (Telegrams, Projects, Users, etc.)

**Backup:**
```bash
# Docker
docker-compose down
cp -r ./data ./data-backup-$(date +%Y%m%d)
docker-compose up -d

# Binary
./KnxMonitor.Api stop
cp -r ./data ./data-backup-$(date +%Y%m%d)
./KnxMonitor.Api
```

### Reverse Proxy (Optional)

**nginx Beispiel:**
```nginx
server {
    listen 80;
    server_name knx.example.com;

    location / {
        proxy_pass http://localhost:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # SignalR WebSocket Support
        proxy_cache_bypass $http_upgrade;
        proxy_read_timeout 86400;
    }
}
```

### Sicherheits-Hinweise

1. **JWT Secret:** Wird automatisch generiert, NICHT manuell ändern oder löschen
2. **Firewall:** Port 8080 nur für vertrauenswürdige Netzwerke freigeben
3. **Reverse Proxy:** HTTPS mit Let's Encrypt für öffentlichen Zugriff empfohlen
4. **Backups:** Regelmäßige Backups von `./data/` erstellen
5. **Updates:** Bei Updates Container/Binary ersetzen, `./data/` Verzeichnis behalten

## Optimierungen

### .dockerignore
```
**/node_modules/
**/bin/
**/obj/
**/.git/
**/.vs/
**/.vscode/
**/data/
```

### Binary Size Optimierung
Bereits implementiert:
- ✅ `-p:EnableCompressionInSingleFile=true` (Kompression aktiv)

Optional (bei Bedarf):
- `-p:PublishTrimmed=true` (Trimming)
- **Achtung:** Kann Reflection-Probleme verursachen, daher aktuell nicht aktiviert

## Wartung

### Neue Architektur hinzufügen
1. Matrix in `.github/workflows/release.yml` erweitern
2. Runtime Identifier auswählen: https://learn.microsoft.com/en-us/dotnet/core/rid-catalog

### Versionsschema
- **Major.Minor.Patch** (z.B. v1.2.3)
- Keine Pre-Release Versionen (keine -beta, -alpha, etc.)

## Troubleshooting

### Build schlägt fehl
- GitHub Actions Logs prüfen
- Lokal mit gleichen Parametern testen

### Docker Push schlägt fehl
- DockerHub Secrets prüfen
- Token-Berechtigung kontrollieren

### Binary funktioniert nicht
- Self-contained Binaries enthalten ALLE Dependencies
- Keine .NET Installation erforderlich
- Bei Fehlern: `ldd` (Linux) oder `otool` (macOS) zur Dependency-Prüfung

### Windows Build schlägt fehl (PowerShell Fehler)
- ✅ **Gelöst:** `shell: bash` explizit gesetzt im Workflow
- PowerShell versteht keine Backslash-Fortsetzung (`\`) in mehrzeiligen Commands

### Docker Build: RID Resolution Error
- ✅ **Gelöst:** Separates `dotnet restore` entfernt
- `dotnet publish` führt restore automatisch mit korrektem RID aus

## Implementierungs-Historie

### v0.0.1 - Initiales Release System
**Implementiert (2025-01):**

1. **Self-Contained Docker Image**
   - Debian Slim Base Image (~75 MB) statt aspnet:9.0 (~220 MB)
   - Finale Image-Größe: ~120-130 MB (vorher ~300+ MB)
   - Konsistente Self-Contained Strategie mit Binaries
   - Native glibc Kompatibilität (Alpine/musl-libc führte zu Exec-Fehler)

2. **Windows Build Fix**
   - Explizites `shell: bash` für Cross-Platform Kompatibilität
   - Vermeidung von PowerShell-Inkompatibilitäten

3. **Angular Build Path Korrektur**
   - Output-Pfad: `dist/frontend/browser` (basierend auf Projektname in angular.json)

4. **Build Optimierungen**
   - `EnableCompressionInSingleFile` aktiviert
   - Automatisches restore mit korrektem RID in `dotnet publish`
   - Docker Build nur für linux/amd64 (ARM64 via QEMU zu instabil)

5. **DockerHub Integration**
   - Username: `ingel81`
   - Automatisches Pushen von `latest` und versionierten Tags
   - Platform: linux/amd64 only (ARM64 Binaries trotzdem verfügbar)

6. **JWT Secret Auto-Generation** 🔐
   - Kryptographisch sicheres Secret (512 bit / 64 Bytes)
   - Automatische Generierung beim ersten Start
   - Persistent gespeichert in `./data/.jwt-secret`
   - Wiederverwendung bei Restarts (Session-Persistenz)
   - Unix-Permissions: 600 (nur Owner read/write)
   - Kein hardcoded Placeholder mehr in Releases
   - Jede Installation hat unique Secret

7. **Database Path Korrektur** 🗄️
   - **Vorher:** `../../data/knxmonitor.db` (relativ, falsch für Docker & Releases)
   - **Nachher:** `./data/knxmonitor.db` (relativ zum Binary)
   - Funktioniert in Docker Container (`/app/data/`)
   - Funktioniert bei Self-Contained Binaries (neben Executable)
   - Volume-Mount in Docker: `/app/data` → Daten bleiben persistent

8. **Environment-Based Configuration** 🌍
   - **Development Mode:**
     - Frontend: `ng serve` auf Port 4200
     - Backend: separat auf Port 5075
     - API URL: `http://localhost:5075/api`
     - SignalR Hub: `http://localhost:5075/hubs`
     - CORS: erlaubt `http://localhost:4200`

   - **Production Mode:**
     - Backend liefert Frontend aus (Static Files aus `wwwroot/`)
     - API URL: `/api` (relativ)
     - SignalR Hub: `/hubs` (relativ)
     - SPA Fallback: `MapFallbackToFile("index.html")` für Angular Routing
     - Single Port: 8080 (Docker) oder konfigurierbar

   - **Frontend Environment Files:**
     - `environment.development.ts`: localhost URLs für Dev
     - `environment.ts`: relative URLs für Production
     - Angular fileReplacements in angular.json

   - **Backend Static File Middleware (Production only):**
     - `UseDefaultFiles()`: Serve index.html als Default
     - `UseStaticFiles()`: Serve alle Static Assets
     - `MapFallbackToFile("index.html")`: Angular Client-Side Routing

**Sicherheitsverbesserungen:**
- ✅ Keine hardcoded Secrets mehr in Releases
- ✅ Unique JWT Secret pro Installation
- ✅ Kryptographisch sichere Secret-Generierung (RNG)
- ✅ File-Permissions für Secret File (Unix: 600)

**Production-Ready Features:**
- ✅ Korrekte Pfade für Docker und Binaries
- ✅ Frontend wird vom Backend ausgeliefert
- ✅ Relative URLs funktionieren in Production
- ✅ Angular Routing funktioniert (SPA Fallback)
- ✅ Dev/Prod Environment vollständig getrennt

**Nächste Schritte:**
- CI/CD Monitoring und Performance-Optimierung
- Evtl. Caching-Strategien für schnellere Builds
- Release Notes Template verfeinern
- Health Check Endpoint hinzufügen
- Monitoring/Metrics Integration (optional)
