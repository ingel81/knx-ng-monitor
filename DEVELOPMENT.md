# Development Guide

Everything you need to build, test and hack on KNX-NG-Monitor. For the
end-user/install side see the [README](README.md); for version history see the
[CHANGELOG](CHANGELOG.md).

## Tech stack

- **Backend** — .NET 9 (ASP.NET Core Web API), EF Core 9 + SQLite, SignalR,
  [Knx.Falcon.Sdk](https://www.nuget.org/packages/Knx.Falcon.Sdk) for the bus,
  JWT auth (access + refresh tokens), Serilog logging, SharpZipLib for
  AES-encrypted ZIP entries (ETS6 password + KNX Secure).
- **Frontend** — Angular 20 (standalone components), Angular Material, a custom
  CDK-virtual-scroll grid, RxJS, the SignalR client, ECharts (lazy-loaded) for
  charts.
- **Database** — SQLite, a single embedded file under `./data/knxmonitor.db`.
- **Distribution** — multi-stage Dockerfile (debian:12-slim, ~120 MB) and
  self-contained single-file binaries built by GitHub Actions.

## Prerequisites

- .NET 9 SDK
- Node.js 20+ and npm
- Docker (optional, for container builds)

## Repository layout

```
knx-ng-monitor/
├── backend/
│   ├── KnxMonitor.Api/                 # ASP.NET Core Web API + SignalR Hubs + Program.cs (DI)
│   ├── KnxMonitor.Core/                # Domain layer (entities, interfaces, DTOs, enums) — no deps
│   ├── KnxMonitor.Infrastructure/      # EF Core, repositories, KNX bus, project-import adapter
│   ├── KnxMonitor.ProjectParser/       # Standalone .knxproj parser library (ETS 4/5/6 + Keyring)
│   ├── KnxMonitor.ProjectParser.Tests/ # 206 xUnit tests (~99 % line coverage)
│   └── KnxMonitor.ParserTool/          # CLI: parse / detect a .knxproj from the shell
├── frontend/                           # Angular application
│   └── src/app/
│       ├── core/                       # Singleton services (auth, signalr, project)
│       ├── features/                   # Feature modules (login, monitor, charts, projects, settings, …)
│       └── shared/                     # Reusable components, models, layout
├── docs/
│   ├── samples/xknxproject/            # Public test fixtures (MIT, from XKNX/xknxproject)
│   ├── samples/own/                    # Private projects (gitignored)
│   ├── SAMPLE_TESTS.md                 # Manual / CLI test recipes per sample
│   └── ai/                             # PROJECT_PLAN, RELEASE_PLAN, DOKUMENTATION, TODO
├── scripts/                            # build.sh / run-release.sh (+ .ps1 / .bat)
├── test-all-samples.sh / .ps1          # Smoke-test the CLI against every sample
├── Dockerfile                          # Multi-stage build
└── docker-compose.yml                  # Optional: dev / prod stack
```

## Architecture

### Backend — Clean Architecture

- **Core** — entities, interfaces, DTOs. No outward dependencies.
- **ProjectParser** — a pure library with no EF / Infrastructure dependency,
  reusable as a NuGet package. It is the *only* parser implementation.
- **Infrastructure** — EF Core, repositories, the KNX bus connection, and the
  adapter from `IKnxProjectParserService` → the library's `IProjectParser`. Also
  holds `ProjectImportService` (the wizard job state machine, two-stage flow,
  auto-activate).
- **Api** — controllers, SignalR hubs, JWT middleware, and `Program.cs` DI
  registration.
- **Patterns** — repository, dependency injection, async/await throughout.

Key parser internals:

- **`ZipHandler.LoadAsync`** returns a `ProjectFileMap` (an in-memory
  `Dictionary<string, byte[]>` over the inner files). For password-protected
  archives it copies the inner `P-XXXX.zip` into a `MemoryStream` and reads it
  with `SharpZipFile` (AES-capable). For ETS6 the user password is first run
  through `PBKDF2-HMAC-SHA256(UTF-16-LE, salt="21.project.ets.knx.org", 65536,
  32)` → Base64 before being handed to SharpZipLib.
- **Loaders (`Ets4/5/6ProjectLoader`)** consume the `ProjectFileMap`. ETS6 also
  handles the `Line → Segment → DeviceInstance` nesting it introduced.
- **`FeatureDetector`** has both `DetectAsync(stream)` (cheap pre-scan) and
  `DetectAfterUnlockAsync(stream, password)` (re-scans the decrypted inner XML
  for KNX Secure indicators) — this powers the two-stage wizard.
- **`KeyringReader`** parses `.knxkeys` XML and decrypts ToolKey, BackboneKey,
  ManagementPassword, Authentication and per-GA keys with
  `PBKDF2-HMAC-SHA256(UTF-8, salt="1.keyring.ets.knx.org", 65536, 16)` +
  AES-128-CBC. IV is `SHA-256(Created)[:16]`.
- **`ParserOptions`** carries `Password`, `KeyringStream`, `KeyringPassword`.
  The library returns a `ParseResult` with `Features`, `GroupAddresses`,
  `Devices`, `Statistics` and (optionally) `Keyring`.

### Frontend

Standalone components (no NgModules), Angular Material + the custom virtual-scroll
grid, reactive RxJS data flow, smart/dumb component split, route guards, a JWT
HTTP interceptor, and lazy loading per feature.

### Performance notes

- Virtual scrolling everywhere we expect > 1000 rows.
- `GroupAddressCacheService` (singleton) holds a `ConcurrentDictionary` of the
  active project's GAs, refreshed only on project change.
- DB indices on `Timestamp` and `DestinationAddress`.
- Telegram persistence runs through a bounded `Channel<KnxTelegram>` drained by a
  `BackgroundService` with batched inserts, decoupling bus throughput from DB
  latency.
- SignalR throttling on bursts; debounced filter inputs in the UI.

## Build & run from source

### Backend

```bash
cd backend
dotnet restore
dotnet build
dotnet run --project KnxMonitor.Api   # http://localhost:8080 (dev: also exposes scalar/v1 + SignalR)
```

### Frontend (separate dev server, hot-reload)

```bash
cd frontend
npm install
ng serve                              # http://localhost:4200
```

In `Development` the Angular dev server proxies API + SignalR calls to the
backend on `http://localhost:8080`, and CORS for `:4200` is enabled. In
`Production` the backend serves the prebuilt Angular bundle directly (single
port).

The `:8080` default now lives in code and steps aside for anything that
configures an address - a local (gitignored) `Properties/launchSettings.json`
with an `applicationUrl` among it. If yours points somewhere else, `dotnet run`
comes up on that port and the Angular proxy finds nothing: either drop the key,
set it to `http://localhost:8080`, or run with `--no-launch-profile`.

### Local production build (mirrors GitHub Actions)

```bash
bash scripts/build.sh          # builds frontend → backend/wwwroot, publishes self-contained binary → ./publish
bash scripts/run-release.sh    # runs ./publish/KnxMonitor.Api on http://localhost:8080
```

> On Windows, kill any running `KnxMonitor.Api` process before publishing, or
> the DLLs are locked:
> `powershell -Command "Get-Process -Name KnxMonitor.Api -ErrorAction SilentlyContinue | Stop-Process -Force"`

## Tests

```bash
# Parser library tests — the bulk of the suite (206 tests, ~99 % line coverage)
dotnet test backend/KnxMonitor.ProjectParser.Tests/KnxMonitor.ProjectParser.Tests.csproj

# Coverage (cobertura → coverage-tmp/, gitignored)
dotnet test backend/KnxMonitor.ProjectParser.Tests/KnxMonitor.ProjectParser.Tests.csproj \
  --collect:"XPlat Code Coverage" --results-directory ./coverage-tmp

# Frontend
cd frontend && ng test
```

`KnxMonitor.ProjectParser.Tests` uses
[`Xunit.SkippableFact`](https://github.com/AArnott/Xunit.SkippableFact) for
fixtures that may not be present locally — the integration tests against the
public xknxproject samples always run; tests that need the proprietary samples
in `docs/samples/own/` skip cleanly when those files are absent, so CI and fresh
clones still pass.

## CLI parser tool

Parse or inspect a `.knxproj` straight from the shell:

```bash
# Detect ETS version / encryption / addressing style (cheap pre-scan)
dotnet run --project backend/KnxMonitor.ParserTool -- detect path/to.knxproj

# Plain parse
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

## EF Core migrations

Run from the repo root:

```bash
dotnet ef migrations add <Name> \
  --project backend/KnxMonitor.Infrastructure \
  --startup-project backend/KnxMonitor.Api

dotnet ef database update \
  --project backend/KnxMonitor.Infrastructure \
  --startup-project backend/KnxMonitor.Api
```

Migrations live in `backend/KnxMonitor.Infrastructure/Data/Migrations/`. They are
also applied automatically on application startup.

## Database schema (overview)

| Entity | Notes |
|---|---|
| `Users` / `RefreshTokens` | Auth |
| `Projects` | Imported `.knxproj`; exactly one is `IsActive` |
| `GroupAddresses` | Per-project (Address `m/m/s`, Name, Description, DPT, DataSecure flag) |
| `Devices` | Per-project (PhysicalAddress `a.l.d`, Name, Manufacturer, ProductName) |
| `KnxTelegrams` | Logged bus traffic (timestamp, source, dest, type, raw + decoded value) |
| `KnxConfigurations` | KNX interface settings |
| `RecordingSettings` | Ring-buffer size, archive on/off, retention |

## API endpoints (overview)

- `POST /api/auth/login` · `POST /api/auth/refresh` · `POST /api/auth/logout`
- `GET /api/projects` · `POST /api/projects/import` (multipart) ·
  `POST /api/projects/import/{jobId}/provide-input` ·
  `GET /api/projects/import-status/{jobId}` ·
  `PUT /api/projects/{id}/activate` · `DELETE /api/projects/{id}`
- `GET /api/groupaddresses` · `GET /api/groupaddresses/search?query=...`
- `GET /api/telegrams` · `POST /api/telegrams/search` ·
  `GET /api/telegrams/export?format=csv` · `GET /api/telegrams/count`
- `GET/PUT /api/knx/config` · `POST /api/knx/test-connection` ·
  `GET /api/knx/status`
- `GET /api/recording/settings` · `PUT /api/recording/settings`
- `GET /api/diagnostics/logs` · `GET /api/diagnostics/download` ·
  `GET /api/version`
- `GET /healthz` — anonymous liveness probe
- `/hubs/telegram` — SignalR hub for live telegrams (JWT-authenticated)
- `/hubs/logs` — SignalR hub for the live in-app log viewer

OpenAPI (Scalar) is exposed in `Development` at
`http://localhost:8080/scalar/v1`.

## Import wizard flow (`ProjectImportService`)

1. Upload → fast `IProjectFeatureDetector` pre-scan → if a password is needed,
   the job enters `WaitingForInput`.
2. After `ProjectPassword` is supplied, the service calls
   `IFeatureDetector.DetectAfterUnlockAsync` on the original bytes. If KNX Secure
   is detected, it adds `KeyringFile` + `KeyringPassword` as **optional**
   requirements and stays in `WaitingForInput`.
3. The frontend renders requirements with an `IsOptional` flag — optional ones
   get a "Skip" button that submits empty values.
4. Once all non-optional requirements are fulfilled (or the user skipped the
   optional ones), `ContinueImportAsync` runs the real parse via the adapter.
5. If no other project is active yet, the new project is auto-activated and the
   GA cache is refreshed.

## Docker multi-stage build

1. **Node 20** — `ng build --configuration production`.
2. **.NET 9 SDK** — `dotnet publish -c Release -r <RID> --self-contained true
   -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true`.
3. **debian:12-slim** runtime — copies the single-file binary, installs
   `libicu72`, runs as non-root `app` (uid 1000), `ENTRYPOINT
   ["/app/KnxMonitor.Api"]`. Final image ≈ 120 MB, published multi-arch
   (amd64 + arm64).

## Contributing

This is primarily a personal project, but bug reports, ideas and PRs via
[GitHub issues](https://github.com/ingel81/knx-ng-monitor/issues) are welcome.

House rules, distilled from `CLAUDE.md`:

- **Commits** — keep them compact and to the point; no AI/tool footer.
- **Sample fixtures** — never commit your own or otherwise proprietary
  `.knxproj` / `.knxkeys` files. `docs/samples/own/`, `docs/samples/other/` and
  `backend/KnxMonitor.ProjectParser.Tests/TestData/` are gitignored. Public test
  fixtures live in `docs/samples/xknxproject/` (mirrored from XKNX/xknxproject,
  MIT). The test project links samples into its `bin/.../TestData/` via `<None
  Include="..." Link="TestData/..." />` — never drop test files directly into the
  gitignored `TestData/` folder.
- **Tests** — keep the parser suite green; gate anything that needs private
  fixtures behind `[SkippableFact]` + `Skip.IfNot(TestSamples.Exists(...))`.
- **Docs before tags** — update README / docs *before* cutting a release tag,
  since the tag's tree is what ends up in the GitHub Release "Source code" asset.

## Release process

Pushing a `vX.Y.Z` tag triggers `.github/workflows/release.yml`, which builds the
six platform binaries, pushes the multi-arch Docker image to
`ingel81/knx-ng-monitor`, and creates a GitHub Release with auto-generated notes.

There is a `/release` skill (`.claude/skills/release/SKILL.md`) that automates the
steps: version bump, README/CHANGELOG update, commit, tag, push and watch the
workflow. See [`docs/ai/RELEASE_PLAN.md`](docs/ai/RELEASE_PLAN.md) for the full
pipeline.
