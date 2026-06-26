# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

KNX-NG-Monitor is a KNX bus monitoring tool with a web UI that displays, historizes and presents KNX telegrams in real time. It ships as a Docker image or as a single self-contained binary. As of v0.1.0 the parser supports ETS 4 / 5 / 6, password-protected projects (incl. ETS6 PBKDF2/AES wrapping) and `.knxkeys` keyring decryption.

## Tech Stack

### Backend
- **.NET 9** (ASP.NET Core Web API) — see `backend/global.json` (none — uses installed SDK)
- **Entity Framework Core 9** with SQLite (`Microsoft.EntityFrameworkCore.Sqlite`)
- **SignalR** for real-time telegram broadcasting
- **[Knx.Falcon.Sdk](https://www.nuget.org/packages/Knx.Falcon.Sdk)** for KNX bus integration
- **JWT auth** (access + refresh tokens, secret auto-generated)
- **SharpZipLib 1.4.2** for AES-encrypted ZIP entries (ETS6-Password + KNX Secure)

### Frontend
- **Angular 20** (standalone components)
- **Angular Material** (dialogs, forms, theming)
- **AG-Grid Community** (live-view, virtual scrolling)
- **RxJS**, SignalR client, Angular CDK virtual scrolling

### Database
- **SQLite** — embedded, single file under `./data/knxmonitor.db`

## Project Structure

```
knx-ng-monitor/
├── backend/
│   ├── KnxMonitor.Api/                   # ASP.NET Core Web API + SignalR Hubs + Program.cs (DI)
│   ├── KnxMonitor.Core/                  # Domain layer (Entities, Interfaces, DTOs, Enums)
│   ├── KnxMonitor.Infrastructure/        # EF Core, repositories, KNX bus, project import, adapter to ProjectParser
│   ├── KnxMonitor.ProjectParser/         # Standalone parser library (ETS 4/5/6 loaders, FeatureDetector, KeyringReader, ZipHandler)
│   ├── KnxMonitor.ProjectParser.Tests/   # xUnit tests (206, ~99 % line cov), uses Xunit.SkippableFact for proprietary fixtures
│   └── KnxMonitor.ParserTool/            # CLI: `parse` / `detect` with --password / --keyring / --keyring-password
├── frontend/
│   └── src/app/
│       ├── core/                         # Singleton services (auth, signalr, project)
│       ├── features/                     # Feature modules (login, live-view, projects/import-wizard, settings)
│       └── shared/                       # Reusable components, models, layout
├── docs/
│   ├── samples/xknxproject/              # Public test fixtures (MIT, mirrored from XKNX/xknxproject)
│   ├── samples/own/                      # Private fixtures (gitignored)
│   ├── PARSER_LIBRARY_PLAN_PART1/2.md    # Historic library-extraction plan
│   ├── PARSER_LIBRARY_PROGRESS.md        # Implementation status of the parser library
│   ├── SAMPLE_TESTS.md                   # Manual / CLI test recipes per sample
│   └── ai/{PROJECT_PLAN,RELEASE_PLAN,DOKUMENTATION,TODO}.md
├── scripts/                              # build.sh / run-release.sh (+ .ps1 / .bat) — local prod-build mirror of CI
├── test-all-samples.sh / .ps1            # CLI smoke-test against every sample (skips own/ when missing)
├── Dockerfile                            # Multi-stage build (Node → SDK → debian:12-slim, ~120 MB)
└── docker-compose.yml
```

## Architecture Principles

### Backend (Clean Architecture)
- **Core** — Entities, Interfaces, DTOs (no dependencies)
- **ProjectParser** — Pure library, no EF / no Infrastructure dependency. Reusable as NuGet.
- **Infrastructure** — EF Core, repositories, KNX bus, adapter from `IKnxProjectParserService` → library `IProjectParser`. Holds `ProjectImportService` (job state machine, two-stage wizard, auto-activate).
- **Api** — Controllers, SignalR Hubs, JWT middleware, Program.cs DI registration.
- **Patterns** — Repository, Dependency Injection, async/await throughout.

### Frontend
- Standalone components (no NgModules), Angular Material + AG-Grid
- Reactive programming with RxJS
- Smart/dumb component split, route guards, JWT HTTP interceptor, lazy loading per feature

## Development Commands

### Backend (.NET)
```bash
cd backend
dotnet restore
dotnet build
dotnet run --project KnxMonitor.Api                                    # http://localhost:8080 (dev: scalar/v1 docs)

# Tests — parser library carries the bulk of the coverage
dotnet test KnxMonitor.ProjectParser.Tests/KnxMonitor.ProjectParser.Tests.csproj

# Coverage (cobertura → coverage-tmp/, ignored by git)
dotnet test KnxMonitor.ProjectParser.Tests/KnxMonitor.ProjectParser.Tests.csproj \
  --collect:"XPlat Code Coverage" --results-directory ../coverage-tmp

# EF migrations (run from repo root)
dotnet ef migrations add <Name> --project backend/KnxMonitor.Infrastructure --startup-project backend/KnxMonitor.Api
dotnet ef database update      --project backend/KnxMonitor.Infrastructure --startup-project backend/KnxMonitor.Api
```

### Frontend (Angular)
```bash
cd frontend
npm install
ng serve                                                                # http://localhost:4200, hot-reload
ng build --configuration production
ng test
```

### CLI parser tool
```bash
dotnet run --project backend/KnxMonitor.ParserTool -- detect <file.knxproj>
dotnet run --project backend/KnxMonitor.ParserTool -- parse  <file.knxproj> --password <pw>
dotnet run --project backend/KnxMonitor.ParserTool -- parse  <file.knxproj> --password <pw> \
  --keyring <file.knxkeys> --keyring-password <kpw>

bash test-all-samples.sh        # full smoke test, public + own samples
```

### Local production build (mirror of CI)
```bash
bash scripts/build.sh           # Angular → wwwroot, dotnet publish → ./publish
bash scripts/run-release.sh     # runs ./publish/KnxMonitor.Api on :8080
```

### Docker
```bash
docker build -t knx-ng-monitor .
docker run -p 8080:8080 -v ./data:/app/data knx-ng-monitor
docker-compose up --build       # dev/prod stack
```

### Release
Pushing a `vX.Y.Z` tag triggers `.github/workflows/release.yml` → 6 platform binaries + Docker image + GitHub Release.
There is a `/release` skill in `.claude/skills/release/SKILL.md` that automates the steps (version bump, README update, tag, push, watch).

## Database Schema

| Entity | Notes |
|---|---|
| `Users` / `RefreshTokens` | Auth |
| `Projects` | Imported `.knxproj`; exactly one is `IsActive` |
| `GroupAddresses` | Per-project (Address `m/m/s`, Name, Description, DPT, …) |
| `Devices` | Per-project (PhysicalAddress `a.l.d`, Name, Manufacturer, ProductName) |
| `KnxTelegrams` | Logged bus traffic (timestamp, source, dest, type, raw + decoded value) |
| `KnxConfigurations` | KNX interface settings |

Migrations live in `backend/KnxMonitor.Infrastructure/Data/Migrations/`.

## API Endpoints

- `/api/auth/*` — login, refresh, logout
- `/api/projects/*` — list, import (multipart), `provide-input`, activate, delete
- `/api/groupaddresses/*` — search/list
- `/api/telegrams/*` — query, search, export
- `/api/knx/*` — config, test-connection, status
- `/hubs/telegram` — SignalR hub (JWT-authenticated)

## Key Implementation Notes

### Project parser library (`KnxMonitor.ProjectParser`)
- **ZipHandler.LoadAsync** returns a `ProjectFileMap` (in-memory `Dictionary<string, byte[]>` over the inner files). For password-protected archives it copies the inner `P-XXXX.zip` into a `MemoryStream` and reads it with `SharpZipFile` (AES-capable). For ETS6 the user password is first run through `PBKDF2-HMAC-SHA256(UTF-16-LE, salt="21.project.ets.knx.org", 65536, 32)` → Base64 before being given to SharpZipLib.
- **Loaders (`Ets4/5/6ProjectLoader`)** consume a `ProjectFileMap` (no `ZipArchive` plumbing). ETS6 also handles the `Line → Segment → DeviceInstance` nesting introduced by ETS 6.
- **FeatureDetector** has both `DetectAsync(stream)` (cheap pre-scan) and `DetectAfterUnlockAsync(stream, password)` (re-scans the decrypted inner XML for KNX Secure indicators).
- **KeyringReader** parses `.knxkeys` XML and decrypts ToolKey, BackboneKey, ManagementPassword, Authentication and per-GA keys with PBKDF2-HMAC-SHA256(UTF-8, salt="1.keyring.ets.knx.org", 65536, 16) + AES-128-CBC. IV is `SHA-256(Created)[:16]`.
- **`ParserOptions`** carries `Password`, `KeyringStream`, `KeyringPassword`. Library returns `ParseResult` with `Features`, `GroupAddresses`, `Devices`, `Statistics` and (optional) `Keyring`.

### Sample fixtures — do not duplicate
- Master location: `docs/samples/`
  - `xknxproject/` — public (committed)
  - `own/` — private (gitignored)
  - `other/` — unclear license (gitignored)
- The test project links these into `bin/Debug/.../TestData/` via `<None Include="..\..\docs\samples\..." Link="TestData\..." />`. **Never put test files directly into `backend/KnxMonitor.ProjectParser.Tests/TestData/`** (that whole folder is gitignored as a build sink).
- Tests that require `own/` use `[SkippableFact]` + `Skip.IfNot(TestSamples.Exists("..."))` so CI / fresh clones still pass.

### Wizard flow (`ProjectImportService`)
1. Upload → fast `IProjectFeatureDetector` scan → if password needed, `WaitingForInput`.
2. After `ProjectPassword` is fulfilled, the service calls `IFeatureDetector.DetectAfterUnlockAsync` on the original bytes. If KNX Secure is detected, it adds `KeyringFile` + `KeyringPassword` as **optional** requirements and stays in `WaitingForInput`.
3. Frontend renders requirements with an `IsOptional` flag — optional ones get a "Skip" button that submits empty values.
4. When all (non-optional) requirements are fulfilled (or user skipped optional), `ContinueImportAsync` runs the real parse via the adapter.
5. If no other project is active yet, the new project is auto-activated and the cache is refreshed.

### DPT conversion
- `KnxMonitor.Infrastructure/KnxConnection/DptConverter.cs` covers the common DPT-1/2/3/5/6/7/8/9/10/11/12/13/14/16/17/18/19/20 types, falls back to hex for unknown.

### Performance
- Virtual scrolling in AG-Grid for live view (1000+ rows)
- `GroupAddressCacheService` (singleton) — `ConcurrentDictionary` of the active project's GAs, refreshed only on project change
- DB indices on `Timestamp`, `DestinationAddress`
- SignalR throttling on bursts; debounced filter inputs in the UI

### SignalR auth
- The `TelegramHub` is decorated with `[Authorize]`; the JWT bearer scheme is also wired into the SignalR pipeline (token via query string for the WebSocket negotiation).

## Docker Multi-Stage Build

1. **Node 20** — `ng build --configuration production`
2. **.NET 9 SDK** — `dotnet publish -c Release -r <RID> --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true`
3. **debian:12-slim** runtime — copies the single-file binary, installs `libicu72`, `ENTRYPOINT ["/app/KnxMonitor.Api"]`. Final image ≈ 120 MB.

## Environment Variables

| Variable | Default | Purpose |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Development` enables CORS for `:4200` and Scalar OpenAPI docs |
| `ASPNETCORE_URLS` | `http://+:8080` | Listen URL |

`./data/.jwt-secret` is auto-generated on first start; never commit it.

## Current Implementation Status

`v0.1.0` is live (Docker + binaries on GitHub Releases, see `README.md`). Parser library + wizard + tests are stable. Open work — see `docs/ai/TODO.md` and the "Next" section of `README.md`:

- Telegram-time decryption with the stored tool keys (KNX Secure runtime)
- Communication objects, topology, locations, functions parsing (currently out of scope)
- Frontend: nachträgliches Bearbeiten / Hochladen des Keyrings am Projekt
- Logging cleanup (Serilog templates, removal of `Console.WriteLine` in `ProjectImportService`)

## Design Guidelines

- **Dark theme** as primary (monitoring environments)
- Responsive layout, desktop-focused but mobile-capable
- Virtual scrolling everywhere we expect > 1000 rows
- Keyboard shortcuts: `Space` = pause, `/` = focus filter
- Tooltips for all icons / buttons; loading indicators for all async actions
- User-friendly error messages, never raw stack traces
- WCAG 2.1 Level AA

## House Rules

- Schreibe Commits generell immer kompakt und immer ohne Claude-Footer.
- Never commit on your own. (Exception: when the user explicitly says so.)
- Parallel läuft immer bereits `ng serve`.
- Sample files: nie eigene/proprietäre Files committen. `docs/samples/own/` und `docs/samples/other/` sind gitignored, ebenso `backend/KnxMonitor.ProjectParser.Tests/TestData/`.
- Vor `dotnet publish`: laufenden `KnxMonitor.Api`-Prozess killen (`powershell -Command "Get-Process -Name KnxMonitor.Api -ErrorAction SilentlyContinue | Stop-Process -Force"`), sonst sind die DLLs gelockt.
- README / Doku **vor** dem Tag updaten — der Tag wird auf einen Commit gesetzt und der Inhalt des Tags ist das, was im "Source code"-Asset des GitHub-Releases liegt.
