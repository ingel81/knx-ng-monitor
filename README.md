# KNX-NG-Monitor

A modern, self-contained KNX bus monitoring tool with a web interface that displays, historizes, and presents KNX telegrams in real-time. Ships as a Docker image or as a single self-contained binary for Linux, Windows and macOS (x64 + arm64).

[![Release](https://img.shields.io/github/v/release/ingel81/knx-ng-monitor)](https://github.com/ingel81/knx-ng-monitor/releases)
[![Docker](https://img.shields.io/docker/v/ingel81/knx-ng-monitor?label=docker)](https://hub.docker.com/r/ingel81/knx-ng-monitor)
[![License](https://img.shields.io/github/license/ingel81/knx-ng-monitor)](LICENSE)

## Features

- **Real-time KNX monitoring** — live view of bus telegrams via SignalR
- **ETS project import** — ETS 4 / 5 / 6 `.knxproj` files, three- / two-level / free addressing
- **KNX Secure** — password-protected projects (incl. ETS6 PBKDF2/AES wrapping) and `.knxkeys` keyring decryption (PBKDF2-HMAC-SHA256 + AES-128-CBC)
- **Two-stage import wizard** — re-detects KNX Secure after the project password is provided and asks for the optional keyring
- **Live view UI** — AG-Grid with virtual scrolling, color-coding (Read / Write / Response), pause, auto-scroll, free-text filter
- **JWT auth** — access tokens (15 min) + refresh tokens (7 days), JWT secret auto-generated on first start
- **Persistent history** — embedded SQLite, no extra container needed
- **Single-port deployment** — backend serves the Angular frontend in production

## Tech Stack

**Backend** — .NET 9 (ASP.NET Core), EF Core 9 + SQLite, SignalR, [Knx.Falcon.Sdk](https://www.nuget.org/packages/Knx.Falcon.Sdk), JWT.
**Frontend** — Angular 20, Angular Material, AG-Grid Community, RxJS, SignalR client.
**Distribution** — Multi-stage Dockerfile (debian:12-slim, ~120 MB) and self-contained single-file binaries via GitHub Actions.

## Repository layout

```
knx-ng-monitor/
├── backend/
│   ├── KnxMonitor.Api/                 # ASP.NET Core Web API + SignalR Hubs
│   ├── KnxMonitor.Core/                # Domain layer (entities, interfaces, DTOs)
│   ├── KnxMonitor.Infrastructure/      # EF Core, repositories, KNX bus, project import adapter
│   ├── KnxMonitor.ProjectParser/       # Standalone .knxproj parser library (ETS 4/5/6 + Keyring)
│   ├── KnxMonitor.ProjectParser.Tests/ # 187 xUnit tests (~99 % line coverage)
│   └── KnxMonitor.ParserTool/          # CLI: parse / detect a .knxproj from the shell
├── frontend/                           # Angular application
├── docs/
│   ├── samples/xknxproject/            # Public test fixtures (MIT, from XKNX/xknxproject)
│   ├── samples/own/                    # Your private projects (gitignored)
│   ├── PARSER_LIBRARY_PLAN_*.md        # Library extraction plan (historic)
│   ├── PARSER_LIBRARY_PROGRESS.md      # Implementation status of the parser library
│   ├── SAMPLE_TESTS.md                 # Manual / CLI test recipes per sample
│   └── ai/                             # PROJECT_PLAN, RELEASE_PLAN, DOKUMENTATION, TODO
├── scripts/                            # build.sh / run-release.sh (+ .ps1 / .bat)
├── test-all-samples.sh / .ps1          # Smoke-test the CLI against every sample
├── Dockerfile                          # Multi-stage build
└── docker-compose.yml                  # Optional: dev / prod stack
```

## Quick Start (Production)

### Docker (recommended)

```bash
docker run -d \
  -p 8080:8080 \
  -v ./data:/app/data \
  --name knx-monitor \
  ingel81/knx-ng-monitor:latest
# UI: http://localhost:8080
```

`docker-compose.yml`:

```yaml
services:
  knx-monitor:
    image: ingel81/knx-ng-monitor:latest
    container_name: knx-monitor
    restart: unless-stopped
    ports: ["8080:8080"]
    volumes: ["./data:/app/data"]
```

### Self-contained binary (no .NET required)

Download from [Releases](https://github.com/ingel81/knx-ng-monitor/releases):

```bash
# Linux / macOS
wget https://github.com/ingel81/knx-ng-monitor/releases/latest/download/knx-ng-monitor-linux-x64.tar.gz
tar -xzf knx-ng-monitor-linux-x64.tar.gz
./KnxMonitor.Api          # http://localhost:8080
```

Windows: download `knx-ng-monitor-win-x64.zip`, extract, run `KnxMonitor.Api.exe`.

### First-time setup

1. Open `http://localhost:8080`
2. Initial-setup wizard creates the admin user
3. (optional) Import a `.knxproj` — see [Project import](#project-import) below
4. Configure the KNX IP interface in **Settings**
5. Open **Live View** and start monitoring

## Development

### Prerequisites

- .NET 9 SDK
- Node.js 20+ and npm
- Docker (optional)

### Backend

```bash
cd backend
dotnet restore
dotnet build
dotnet run --project KnxMonitor.Api    # http://localhost:8080 (dev mode also exposes SignalR + scalar/v1)
```

### Frontend (separate dev server, hot-reload)

```bash
cd frontend
npm install
ng serve                                # http://localhost:4200
```

In `Development` the Angular dev server proxies API + SignalR calls to the backend on `http://localhost:8080`. In `Production` the backend serves the Angular bundle directly.

### Tests

```bash
# Parser library tests (the bulk of the suite — 187 tests)
dotnet test backend/KnxMonitor.ProjectParser.Tests/KnxMonitor.ProjectParser.Tests.csproj

# Frontend
cd frontend && ng test
```

`KnxMonitor.ProjectParser.Tests` uses [`Xunit.SkippableFact`](https://github.com/AArnott/Xunit.SkippableFact) for fixtures that may not be present locally — the integration tests against the public xknxproject samples always run; tests that need the proprietary samples in `docs/samples/own/` skip cleanly when those files are absent.

### CLI tool — parse a `.knxproj` from the shell

```bash
# Plain
dotnet run --project backend/KnxMonitor.ParserTool -- parse path/to.knxproj

# Password-protected (works for ETS 4 / 5 / 6)
dotnet run --project backend/KnxMonitor.ParserTool -- parse path/to.knxproj --password '<pw>'

# KNX Secure with keyring
dotnet run --project backend/KnxMonitor.ParserTool -- parse path/to.knxproj \
  --password '<pw>' \
  --keyring path/to.knxkeys --keyring-password '<keyring-pw>'

# Smoke-test every sample at once
bash test-all-samples.sh        # or: ./test-all-samples.ps1
```

### Production build (locally, mirrors what GitHub Actions does)

```bash
bash scripts/build.sh           # builds frontend, copies it into backend/wwwroot, publishes self-contained binary into ./publish
bash scripts/run-release.sh     # runs ./publish/KnxMonitor.Api on http://localhost:8080
```

### Migrations

```bash
dotnet ef migrations add <Name> \
  --project backend/KnxMonitor.Infrastructure \
  --startup-project backend/KnxMonitor.Api

dotnet ef database update \
  --project backend/KnxMonitor.Infrastructure \
  --startup-project backend/KnxMonitor.Api
```

## Project import

The wizard walks through:

1. **File selection** — pick the `.knxproj`
2. **Analyzing** — fast pre-scan: ETS version, password protection, addressing style
3. **Project password** — prompted if the inner `P-XXXX.zip` is encrypted
4. **Re-detect after unlock** — once the password is in, the server decrypts and re-scans the inner XML for KNX Secure indicators (`<Security>` blocks, `DataSecurity="1"` GAs)
5. **Keyring (optional)** — if KNX Secure is detected, the wizard offers a `.knxkeys` upload + keyring password. Skip is always possible; tool keys / backbone key are only needed for runtime telegram decoding, not for parsing
6. **Import** — group addresses, devices, hardware lookup
7. **Auto-activate** — if no other project is active yet, the new project is activated immediately

The same end-to-end flow is exercised by `test-all-samples.sh` and the integration tests in `KnxMonitor.ProjectParser.Tests/Integration/`.

## Configuration

### Environment variables

| Variable | Default | Purpose |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Development` enables CORS for `:4200`, separate dev server, scalar/v1 docs |
| `ASPNETCORE_URLS` | `http://+:8080` | Listen URL — change to use a different port |

JWT secret is auto-generated on first start and persisted as `./data/.jwt-secret` (file mode 600 on Unix). Do not delete or all sessions become invalid.

### Data directory

Everything stateful lives under `./data/`:

- `.jwt-secret` — signing key
- `knxmonitor.db` (+ `-wal` / `-shm`) — SQLite database

Docker mounts that as `/app/data`. Self-contained binary writes alongside the executable.

### KNX connection

Configured in the **Settings** page in the UI:

- IP address of the KNX IP-Interface or router
- Port (default 3671 for KNXnet/IP)
- Connection type: Tunneling or Routing
- Optional physical address

## Database schema (overview)

| Entity | Purpose |
|---|---|
| `Users` / `RefreshTokens` | Auth |
| `Projects` | Imported `.knxproj` files (one is `IsActive`) |
| `GroupAddresses` | Per-project GAs (Address, Name, DPT, DataSecure flag) |
| `Devices` | Per-project devices (PhysicalAddress, Manufacturer, Product) |
| `KnxTelegrams` | Logged bus traffic — main monitoring data |
| `KnxConfigurations` | KNX interface settings |

## API endpoints (overview)

- `POST /api/auth/login` · `POST /api/auth/refresh` · `POST /api/auth/logout`
- `GET /api/projects` · `POST /api/projects/import` · `PUT /api/projects/{id}/activate` · `DELETE /api/projects/{id}` · `POST /api/projects/{id}/provide-input`
- `GET /api/groupaddresses` · `GET /api/groupaddresses/search?query=...`
- `GET /api/telegrams` · `POST /api/telegrams/search` · `GET /api/telegrams/export`
- `GET/PUT /api/knx/config` · `POST /api/knx/test-connection` · `GET /api/knx/status`
- `/hubs/telegram` — SignalR hub (JWT-authenticated)

OpenAPI (Scalar) is exposed in `Development` at `http://localhost:8080/scalar/v1`.

## Releases

Pushing a `vX.Y.Z` tag triggers `.github/workflows/release.yml`, which builds 6 platform binaries, pushes a Docker image to `ingel81/knx-ng-monitor`, and creates a GitHub Release with auto-generated notes. See [`docs/ai/RELEASE_PLAN.md`](docs/ai/RELEASE_PLAN.md) for the full pipeline.

### v0.1.5 (latest)

- **Docker fix** — single-file bundle now extracts to `/tmp/.net` via `DOTNET_BUNDLE_EXTRACT_BASE_DIR`; the non-root `app` user introduced in v0.1.1 could not write to its `/app` home, so startup failed with `Default extraction directory [/app] either doesn't exist or is not accessible for read/write`. Affects every container started from `v0.1.1`–`v0.1.4`.

```bash
docker pull ingel81/knx-ng-monitor:v0.1.5
```

### v0.1.4

- **CI fix** — restrict the release job's `download-artifact` to `knx-ng-monitor-*`; the bumped `docker/build-push-action@v7` now uploads an extra `.dockerbuild` provenance artifact that broke the unfiltered download and aborted release creation on v0.1.3

```bash
docker pull ingel81/knx-ng-monitor:v0.1.4
```

### v0.1.3

- **CI (docker actions)** — `docker/setup-buildx-action` and `docker/login-action` bumped from `@v3` → `@v4`, `docker/build-push-action` from `@v5` → `@v7`; finishes the Node 20 → Node 24 migration started in v0.1.2
- ⚠ Release job failed mid-publish due to unrelated `download-artifact` incompatibility — fixed in v0.1.4. Docker image `ingel81/knx-ng-monitor:v0.1.3` was pushed and is usable; GitHub-Release page is empty for this tag.

```bash
docker pull ingel81/knx-ng-monitor:v0.1.3
```

### v0.1.2

- **CI** — release workflow's `actions/checkout`, `setup-node`, `setup-dotnet`, `upload-artifact`, `download-artifact` bumped to `@v5` so the build keeps working past the GitHub Actions Node 20 → Node 24 deprecation deadline

```bash
docker pull ingel81/knx-ng-monitor:v0.1.2
```

### v0.1.1

- **SignalR + refresh-token hardening** — token factory reads from the auth service on every reconnect (no more stale-token loops after a refresh); concurrent 401s share a single refresh via `shareReplay` instead of triggering a refresh-storm that the backend's revoke-on-rotate then locked out
- **Atomic refresh-token rotation** — old token is revoked and the new one inserted in a single `SaveChanges`, so a crash mid-rotation can no longer lock the user out
- **Telegram persistence pipeline** — bounded `Channel<KnxTelegram>` (10 k, drop-oldest) drained by a `BackgroundService` with batched 100-row inserts; bus throughput is no longer coupled to DB latency, and a stalled SQLite cannot blow memory
- **Import-job cleanup** — terminal jobs older than 1 h are swept every 5 min; `RemoveJob` zeros uploaded buffers and drops password references before evicting
- **`/healthz`** — anonymous liveness endpoint for container probes
- **Container hardening** — Dockerfile runs as non-root `app` (uid 1000) with `--chown` on COPY and a `wget`-based `HEALTHCHECK`; `release.yml` jobs locked to `contents: read`
- **Misc** — `KnxConnectionService` is `IAsyncDisposable` (no more sync `Dispose().Wait()`), `GroupValue.Value` instead of reflecting an internal field, CORS only registered in `Development`, logout stops SignalR before clearing tokens, `Console.WriteLine` removed from `ProjectImportService`

```bash
docker pull ingel81/knx-ng-monitor:v0.1.1
```

### v0.1.0

- Standalone `KnxMonitor.ProjectParser` library — ETS 4 / 5 / 6 loaders behind a clean interface
- ETS 6 password-protected archives (PBKDF2-HMAC-SHA256 over UTF-16LE → Base64 + AES ZIP)
- KNX Secure keyring decryption (PBKDF2-HMAC-SHA256 + AES-128-CBC) — backbone key, tool keys, management password, GA keys
- Two-stage import wizard with optional keyring + first-project auto-activate
- `KnxMonitor.ParserTool` CLI (parse / detect, console / json / csv, `--keyring`)
- 187 xUnit tests, ~99 % line coverage

```bash
docker pull ingel81/knx-ng-monitor:v0.1.0
```

### v0.0.1

- First production release — self-contained binaries (6 platforms), Docker image, auto-generated JWT secret, single-port deployment

## Project status

**Current** — v0.1.5, Docker single-file bundle fixed for non-root container; hardened auth flow + container, batched telegram persistence (v0.1.1).
**Next** — Telegram-time decryption using stored tool keys; communication objects, topology, locations.

See [`docs/ai/PROJECT_PLAN.md`](docs/ai/PROJECT_PLAN.md) for the full implementation history.

## Contributing

This is a personal project. Bug reports and ideas via [issues](https://github.com/ingel81/knx-ng-monitor/issues) are welcome.

## License

MIT.

## Author

Joerg.
