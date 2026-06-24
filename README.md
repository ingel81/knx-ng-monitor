# KNX-NG-Monitor

A modern, self-contained KNX bus monitoring tool with a web interface that displays, historizes, and presents KNX telegrams in real-time. Ships as a Docker image or as a single self-contained binary for Linux, Windows and macOS (x64 + arm64).

[![Release](https://img.shields.io/github/v/release/ingel81/knx-ng-monitor)](https://github.com/ingel81/knx-ng-monitor/releases)
[![Docker](https://img.shields.io/docker/v/ingel81/knx-ng-monitor?label=docker)](https://hub.docker.com/r/ingel81/knx-ng-monitor)
[![License](https://img.shields.io/github/license/ingel81/knx-ng-monitor)](LICENSE)

<!-- SCREENSHOTS:START -->
## Screenshots

<div align="center">

<a href="docs/screenshots/hero.webm"><img src="docs/screenshots/hero.gif" alt="KNX-NG-Monitor — live bus monitoring" width="900"></a>

</div>

<sub>Click any thumbnail for the full-resolution image.</sub>

### Desktop

<table>
  <tr>
    <td align="center" width="50%">
      <a href="docs/screenshots/login.webp"><img src="docs/screenshots/thumbs/login.webp" alt="Login" width="420"></a><br>
      <sub><b>Login</b><br>JWT auth, dark/light aware</sub>
    </td>
    <td align="center" width="50%">
      <a href="docs/screenshots/monitor-live.webp"><img src="docs/screenshots/thumbs/monitor-live.webp" alt="Monitor — Live" width="420"></a><br>
      <sub><b>Monitor — Live</b><br>Real-time KNX telegrams off the bus</sub>
    </td>
  </tr>
  <tr>
    <td align="center" width="50%">
      <a href="docs/screenshots/monitor-detail.webp"><img src="docs/screenshots/thumbs/monitor-detail.webp" alt="Telegram detail" width="420"></a><br>
      <sub><b>Telegram detail</b><br>Value, bus actions, used-by, chart jump</sub>
    </td>
    <td align="center" width="50%">
      <a href="docs/screenshots/monitor-archive.webp"><img src="docs/screenshots/thumbs/monitor-archive.webp" alt="Monitor — Archive" width="420"></a><br>
      <sub><b>Monitor — Archive</b><br>Historized traffic, structured filters</sub>
    </td>
  </tr>
  <tr>
    <td align="center" width="50%">
      <a href="docs/screenshots/charts.webp"><img src="docs/screenshots/thumbs/charts.webp" alt="Charts — power" width="420"></a><br>
      <sub><b>Charts — power</b><br>Live value trends per group address</sub>
    </td>
    <td align="center" width="50%">
      <a href="docs/screenshots/charts-temp.webp"><img src="docs/screenshots/thumbs/charts-temp.webp" alt="Charts — temperature" width="420"></a><br>
      <sub><b>Charts — temperature</b><br>Sensor curve over time</sub>
    </td>
  </tr>
  <tr>
    <td align="center" width="50%">
      <a href="docs/screenshots/stats.webp"><img src="docs/screenshots/thumbs/stats.webp" alt="Statistics" width="420"></a><br>
      <sub><b>Statistics</b><br>Totals, msg/s, telegrams over time</sub>
    </td>
    <td align="center" width="50%">
      <a href="docs/screenshots/stats-heatmap.webp"><img src="docs/screenshots/thumbs/stats-heatmap.webp" alt="Activity heatmap" width="420"></a><br>
      <sub><b>Activity heatmap</b><br>Telegrams by weekday × hour</sub>
    </td>
  </tr>
  <tr>
    <td align="center" width="50%">
      <a href="docs/screenshots/topology.webp"><img src="docs/screenshots/thumbs/topology.webp" alt="Topology" width="420"></a><br>
      <sub><b>Topology</b><br>Building tree with devices</sub>
    </td>
    <td align="center" width="50%">
      <a href="docs/screenshots/group-addresses.webp"><img src="docs/screenshots/thumbs/group-addresses.webp" alt="Group addresses" width="420"></a><br>
      <sub><b>Group addresses</b><br>3-level tree, read / write / chart</sub>
    </td>
  </tr>
  <tr>
    <td align="center" width="50%">
      <a href="docs/screenshots/projects.webp"><img src="docs/screenshots/thumbs/projects.webp" alt="Projects" width="420"></a><br>
      <sub><b>Projects</b><br>ETS import, active project, auto-connect</sub>
    </td>
    <td align="center" width="50%">
      <a href="docs/screenshots/settings.webp"><img src="docs/screenshots/thumbs/settings.webp" alt="Settings" width="420"></a><br>
      <sub><b>Settings</b><br>KNX interface, recording, theme & density</sub>
    </td>
  </tr>
  <tr>
    <td align="center" width="50%">
      <a href="docs/screenshots/graph.webp"><img src="docs/screenshots/thumbs/graph.webp" alt="GA network — Alpha" width="420"></a><br>
      <sub><b>GA network — Alpha</b><br>Force-directed building map, live activity</sub>
    </td>
    <td align="center" width="50%">
      <a href="docs/screenshots/projects-import.webp"><img src="docs/screenshots/thumbs/projects-import.webp" alt="Import wizard" width="420"></a><br>
      <sub><b>Import wizard</b><br>Two-stage ETS import with password / keyring</sub>
    </td>
  </tr>
  <tr>
    <td align="center" width="50%">
      <a href="docs/screenshots/projects-detail.webp"><img src="docs/screenshots/thumbs/projects-detail.webp" alt="Project detail" width="420"></a><br>
      <sub><b>Project detail</b><br>Group addresses & devices</sub>
    </td>
  </tr>
</table>

### Mobile

<table>
  <tr>
    <td align="center">
      <a href="docs/screenshots/monitor-live-mobile.webp"><img src="docs/screenshots/thumbs/monitor-live-mobile.webp" alt="Monitor — Live mobile" height="420"></a><br>
      <sub><b>Monitor — Live</b></sub>
    </td>
    <td align="center">
      <a href="docs/screenshots/charts-mobile.webp"><img src="docs/screenshots/thumbs/charts-mobile.webp" alt="Charts — power mobile" height="420"></a><br>
      <sub><b>Charts — power</b></sub>
    </td>
    <td align="center">
      <a href="docs/screenshots/stats-mobile.webp"><img src="docs/screenshots/thumbs/stats-mobile.webp" alt="Statistics mobile" height="420"></a><br>
      <sub><b>Statistics</b></sub>
    </td>
  </tr>
</table>

<!-- SCREENSHOTS:END -->

## Features

- **Real-time KNX monitoring** — live view of bus telegrams via SignalR, **always-on**: auto-connects on startup and reconnects after gateway/network/container interruptions
- **DPT decoding** — via the Falcon datapoint catalog: full DPT range, sub-type aware, with units and enumeration names (English labels, locale-invariant numbers)
- **ETS project import** — ETS 4 / 5 / 6 `.knxproj` files, three- / two-level / free addressing
- **KNX Secure** — password-protected projects (incl. ETS6 PBKDF2/AES wrapping) and `.knxkeys` keyring decryption (PBKDF2-HMAC-SHA256 + AES-128-CBC)
- **Two-stage import wizard** — re-detects KNX Secure after the project password is provided and asks for the optional keyring
- **Unified Monitor view** — one view with a **Live / Archive** toggle and a shared filter; custom CDK-virtual-scroll grid with color-coding (Read / Write / Response), value semantics + DPST units, live bus-load badge (msg/s + %), pause (no telegram loss), auto-scroll, free-text + room filter
- **Charts & statistics** — time-series charts for numeric DPTs (separate Y-axes per unit, live append) and a statistics view (total, average msg/s, telegrams over time)
- **Write / read from the UI** — send GroupValueRead or write a DPT-typed value to the live bus from the detail drawer, behind a confirmation dialog
- **KNX Secure runtime** — Data Secure group telegrams decrypted at runtime from the stored keyring; optional opt-in IP-Secure tunnel
- **Topology & communication objects** — Locations (building → floor → room) and com-objects parsed across ETS 4/5/6, with a Topology tree view, room filter and "used by" device list
- **Runtime DE / EN** — full bilingual UI, switchable without a reload
- **JWT auth** — access tokens (15 min) + refresh tokens (7 days), JWT secret auto-generated on first start
- **Data recording** — two-tier, fully self-contained: count-based SQLite hot-tier ring buffer (bounded size) plus an opt-in NDJSON+gzip long-term archive; keyset-paginated query API and server-streamed CSV export
- **Single-port deployment** — backend serves the Angular frontend in production

## Tech Stack

**Backend** — .NET 9 (ASP.NET Core), EF Core 9 + SQLite, SignalR, [Knx.Falcon.Sdk](https://www.nuget.org/packages/Knx.Falcon.Sdk), JWT.
**Frontend** — Angular 20, Angular Material, custom CDK-virtual-scroll grid, RxJS, SignalR client.
**Distribution** — Multi-stage Dockerfile (debian:12-slim, ~120 MB) and self-contained single-file binaries via GitHub Actions.

## Repository layout

```
knx-ng-monitor/
├── backend/
│   ├── KnxMonitor.Api/                 # ASP.NET Core Web API + SignalR Hubs
│   ├── KnxMonitor.Core/                # Domain layer (entities, interfaces, DTOs)
│   ├── KnxMonitor.Infrastructure/      # EF Core, repositories, KNX bus, project import adapter
│   ├── KnxMonitor.ProjectParser/       # Standalone .knxproj parser library (ETS 4/5/6 + Keyring)
│   ├── KnxMonitor.ProjectParser.Tests/ # 192 xUnit tests (~99 % line coverage)
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

### v0.7.0 (latest)

- **GA network graph (Alpha)** — new **Graph** view: a force-directed map of the installation laid out **Building → Floor → Room → group address**. Floors are pinned to angular sectors (no crossing links), rooms bloom organically, and each GA is coloured by **function** (group-range) and clustered with its peers. GA→room is resolved **by name** ("OG Schlafzimmer …" → Schlafzimmer, floor-disambiguated), falling back to device location with central-hub-device exclusion. Live telegrams make the matching GA **glow with its current value** and emit a particle along its edge; click a GA to jump to its chart. Marked **Alpha** in the nav
- **Activity heatmap** — the **Statistics** view gains a **weekday × hour** heatmap of bus activity (timezone-aware backend aggregation), surfacing daily/weekly routines at a glance
- **Mobile-responsive overhaul** — every page reflows on phones: stacked toolbars/filters, tables → cards, a bottom navigation bar, the Monitor's secondary actions collapse into a kebab menu, a filter toggle for the archive, and a **scroll-to-top** button
- **Account menu** — language, theme (Light/Console) and logout moved into a single header **account dropdown**; the header is decluttered, the active nav tab restyled
- **Charts & tooltip polish** — chart tooltip now shows series name, **unit** and a locale-formatted timestamp; selected group addresses sort to the top of the picker; the dataZoom slider is no longer clipped
- **Fix: UTC timestamps** — historized telegrams are now tagged UTC end-to-end, so charts and the archive no longer render an hour offset versus the live view

```bash
docker pull ingel81/knx-ng-monitor:v0.7.0
```

### v0.6.0

- **Unified Monitor view** — the separate *Live View* and *History* pages are merged into one **Monitor** view with a **Live / Archive** toggle (default Live). A single shared quick-search applies in both modes; the live buffer keeps recording in the background while you browse the archive. Archive keeps keyset pagination + infinite scroll
- **Runtime DE / EN language switch** — full English/German UI, switchable **without a reload** (signal-backed translation, ~250 keys); default English, choice persisted in `localStorage`
- **Time-series charts** — new **Charts** view: pick up to 8 numeric group addresses, plot them over **1 h / 24 h / 7 d / 30 d / custom** with **separate Y-axes per unit** (°C vs W vs %), zoom/scroll, DPT-1 rendered as step lines; initial history is loaded and **new telegrams append live** (ECharts, lazy-loaded)
- **Statistics view** — total telegrams, average **msg/s** and a telegrams-over-time histogram for the selected range; plus a live **bus-load badge** (msg/s + % of TP1 bandwidth) in the Monitor header
- **Write / read group values from the UI** — the telegram detail drawer can send a **GroupValueRead** or **write** a DPT-typed value (On/Off toggle for 1-bit, number/text otherwise) to the live bus, gated behind a confirmation dialog. Encoding is verified end-to-end over an in-process KNX/IP loopback bus
- **KNX Secure runtime decryption** — when a project keyring is stored, **Data Secure** group telegrams are decrypted at runtime (Falcon `GroupCommunicationSecurity`); optional **IP-Secure tunnel** (opt-in per configuration). Fully isolated — without a keyring the connection path is unchanged
- **Building topology + communication objects** — the parser now extracts **Locations** (building → floor → room) and **communication objects** across ETS 4/5/6. New **Topology** view (building tree), a **room filter** in the archive, a **"used by"** list of device objects in the detail drawer, and **upload a `.knxkeys` keyring after import** without re-parsing
- **Project lifecycle** — **deactivate** a project (severs the bus link), an **auto-connect toggle** on the active project, a **delete preview** (group-address / device / telegram counts) before deletion, and a faster, transactional **set-based delete**
- **CI** — release workflow actions bumped to the **Node 24** runtime ahead of GitHub's Node 20 removal

```bash
docker pull ingel81/knx-ng-monitor:v0.6.0
```

### v0.5.0

- **Clear history** — the History view gained a "more actions" kebab menu (next to *Export CSV*) with a **Clear history** action that wipes the entire telegram database after a confirmation dialog. The delete is set-based (`ExecuteDelete` + WAL checkpoint), so it is fast even on a full 1 M-row hot-tier; archived NDJSON day-files are left untouched

```bash
docker pull ingel81/knx-ng-monitor:v0.5.0
```

### v0.4.1

- **Fix: DPT decoding returned raw hex in Docker / single-file builds** — Falcon's master-data loader derives its search directories from `Assembly.Location`, which is **empty for assemblies in a single-file bundle**, so `DptFactory` threw and every value fell back to hex (`0x…`). The published binary now self-extracts all content to disk (`IncludeAllContentForSelfExtract`), restoring a valid assembly path. Affects v0.4.0 only; framework-dependent/dev runs were never impacted
- **Decode failures are now logged** instead of silently falling back to hex
- **Fix:** empty-state button icon alignment (the "Import project" upload icon overflowed the button)

```bash
docker pull ingel81/knx-ng-monitor:v0.4.1
```

### v0.4.0

- **DPT decoding via Falcon SDK** — the hand-rolled converter is replaced by Falcon's built-in datapoint catalog: the full DPT range, **sub-type aware** (e.g. 5.001 % vs 5.010 counter pulses), with **units** and **enumeration names** (DPT 20 → `Comfort`, DPT 232 → `#FF8000`). Boolean states now render their spec-correct labels (Switch → On/Off, OpenClose → Open/Close, State → Active/Inactive); labels are pinned to **English** regardless of host locale, numbers stay invariant
- **Always-on recording** — the bus link is now managed by a backend **auto-connect worker**: it connects on startup and **reconnects automatically** after a gateway reboot, network blip or container restart (`KnxConfiguration.AutoConnect`, default on). The manual Connect/Disconnect buttons are gone — the toolbar shows a live **status indicator** (Live / Paused / Connecting / Reconnecting)
- **Live View robustness** — telegram buffer + connection live in a singleton service, so switching **History ↔ Live no longer empties the view**; **Pause** now buffers incoming telegrams instead of dropping them and flushes them on resume
- **English UI** — the entire interface is now English (Live, History, Settings, Projects, import wizard, navigation, grid, detail sheet, cards); time formatting follows the active Angular locale
- **Grid polish** — retuned column widths, **right-aligned** DPT / raw / value columns, and a **tooltip on truncated cells** (only when actually clipped)
- **Non-destructive connection test** — *Test connection* in Settings now probes a throwaway bus and never interrupts the live recording; changing the gateway IP/port **reconnects the live link** to the new settings
- **Licensing** — added [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md): the bundled **KNX Falcon SDK is proprietary** (KNX Tools Software License Agreement, free of fees, redistribution to end-users permitted); the project's MIT license does not cover it

```bash
docker pull ingel81/knx-ng-monitor:v0.4.0
```

### v0.3.0

- **UI redesign ("Instrument")** — coherent **Light + Console (dark)** theme switchable at runtime via CSS design tokens, **IBM Plex** typography, petrol shell with custom SVG icon set; single source of truth in `styles/_design.scss` (mapped 1:1 from the design handoff)
- **Custom virtualized grid** — **AG-Grid replaced** by a lightweight CDK-Virtual-Scroll table (`knx-table`) shared by Live View and History: type pills, value semantics, **DPST units** (°C/W/lx/…), room tags, stable zebra, click-to-detail sheet, column manager, mobile cards (<768px). Bundle ~1 MB smaller
- **History query** — server-side **full-text search** (`q`), **sort direction** (`order`) and **multi-select message types** (`types`) added to `/api/telegrams`
- **Settings** — appearance card with Light/Console + grid-density toggles (persisted, applied live)

```bash
docker pull ingel81/knx-ng-monitor:v0.3.0
```

### v0.2.0

- **Data recording (two-tier)** — telegrams now persist into a count-based SQLite **hot-tier ring buffer** (default 1,000,000) trimmed in-worker (single SQLite writer) with **WAL** enabled; bounded, constant DB size instead of unbounded growth
- **Long-term archive** — opt-in **NDJSON + gzip** daily files under `data/archive/` (`YYYY-MM-DD.ndjson`, gzipped at day rollover, startup self-heal, optional retention); captures every telegram independently of the ring buffer
- **History view** — new `/history` page (AG-Grid infinite scroll) backed by a **keyset-paginated** query API (`/api/telegrams`, `/count`, server-streamed `/export?format=csv`) plus archive day list/download
- **Recording settings** — configure ring-buffer size, archive on/off and retention from the Settings page, applied live without restart (`/api/recording/settings`)
- **Schema** — telegram indices reduced 5 → 2; new `RecordingSettings` table (migration `AddRecordingFeature`)

```bash
docker pull ingel81/knx-ng-monitor:v0.2.0
```

### v0.1.5

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

**Current** — v0.7.0: GA network graph (Alpha) — force-directed Building→Floor→Room→GA map with name-based room attribution and live activity; weekday×hour activity heatmap; mobile-responsive overhaul (cards, bottom-nav, kebab actions, scroll-to-top); account menu (theme/language/logout); charts & tooltip polish; UTC-timestamp fix. Builds on v0.6.0: unified Monitor (Live/Archive), time-series charts + statistics, building topology, group-address tree, runtime DE/EN switch, GA write/read, KNX Secure runtime decryption + optional IP-Secure tunnel, parser extensions, project-lifecycle improvements.
**Next** — Topology/room live dashboard with current values; runtime Secure on-hardware acceptance; notifications (mail/webhook/MQTT); device detail view.

See [`docs/ai/PROJECT_PLAN.md`](docs/ai/PROJECT_PLAN.md) for the full implementation history.

## Contributing

This is a personal project. Bug reports and ideas via [issues](https://github.com/ingel81/knx-ng-monitor/issues) are welcome.

## License

This project's own source code is licensed under the **MIT** license.

It bundles third-party components that remain under their own licenses — most
notably the **KNX Falcon SDK**, which is **proprietary** (KNX Tools Software
License Agreement, free of fees, redistribution to end-users permitted). The MIT
license does **not** cover the Falcon SDK. See
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for the full list and terms.

> "KNX" is a registered trademark of the KNX Association. This project is an
> independent tool, not affiliated with or endorsed by the KNX Association.

## Author

Joerg.
