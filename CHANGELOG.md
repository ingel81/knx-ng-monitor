# Changelog

All notable changes to KNX-NG-Monitor are documented here.
The format is loosely based on [Keep a Changelog](https://keepachangelog.com/),
and the project follows [Semantic Versioning](https://semver.org/) (pre-1.0, so
minor versions may still introduce breaking changes).

Docker images are published per tag to
[`ingel81/knx-ng-monitor`](https://hub.docker.com/r/ingel81/knx-ng-monitor);
binaries and auto-generated notes are on the
[GitHub Releases](https://github.com/ingel81/knx-ng-monitor/releases) page.

## [Unreleased]

### Added
- **Chart display options** - a settings popover for curve style (line / area /
  steps), data points, including zero on the value axis, an average line and the
  chart's own legend. Persisted across sessions.
- **PNG and CSV export** of the current chart, straight from the toolbar.
- **Per-series summary** under the chart (min / max / average / last / point
  count) which doubles as the interactive legend: clicking a row shows or hides
  that series. The chart's built-in legend is therefore off by default.
- **Search in the group-address picker**, matching address and name, with the
  hit highlighted.
- **Live values on the group-address page** - rows fill from the telegram stream,
  and a read that goes unanswered for 3 s now says so instead of looking
  successful. KNX flags per address are shown as compact badges.
- Chart group-address selection and time range are remembered across visits.

### Fixed
- **Down-sampling dropped peaks** - the series endpoint kept every n-th point, so
  a short spike inside a wide range vanished and left a smooth, trustworthy
  looking curve. It now keeps the minimum and maximum of each bucket, preserving
  the envelope of the signal.
- **The series row cap silently cut off the newest data** - on a large range the
  query kept the *oldest* rows, so the chart stopped short of now without a word.
  It now keeps the newest rows and reports that the range is incomplete.
- **The chart toolbar wrapped onto a second row** once several group addresses
  were selected, because the picker grew with the concatenated labels.
- **Clearing the chart selection did not stick** - the previous selection was
  restored on the next visit, and the summary table kept showing stale rows.
- **Login and setup screens always showed "Version 1.0.0"** - the footer had the
  version hard-coded. Both now read it from `GET /api/version` (anonymous, so it
  works before sign-in) and strip the build metadata the informational version
  carries. The csproj fallback used for local builds was bumped from 0.7.0 to
  0.8.4; CI keeps overriding it from the tag.

## [0.8.4]

### Fixed
- **Portable binary served no frontend when started from another directory** - the
  ASP.NET Core content root defaults to the current working directory, so both
  `appsettings.json` and `wwwroot` were looked up there instead of next to the
  executable: no UI, and configuration silently falling back to defaults. Now
  anchored to the executable like the data directory. Docker was unaffected
  (`WORKDIR /app` happened to match).

### Added
- The release workflow now smoke-tests the artifact it is about to publish, on
  every OS it can execute natively: the app has to start, `/healthz` must answer
  (which exercises EF plus the native SQLite provider), the data directory has to
  sit next to the executable and not in the single-file bundle's extraction
  directory, the frontend has to be served, `/api/version` has to match the tag,
  a log file has to appear, and a restart on the same data directory has to keep
  the database. The image is additionally checked for the bind mount reaching
  `/app/data`, a non-root uid and a healthy `HEALTHCHECK`. The data-loss bug in
  0.8.0 - 0.8.2 existed only in the packaged artifact and passed every unit test.

## [0.8.3]

### Fixed
- **Data loss on update: the data directory was ignored (0.8.0 - 0.8.2)** - the
  data directory was resolved via `AppContext.BaseDirectory`, which for our
  compressed single-file build is not the executable's folder but the bundle's
  *extraction* directory (`/tmp/.net/KnxMonitor.Api/<bundle-hash>/` in the
  container). Database, JWT secret, logs and archive therefore bypassed the
  mounted volume, and because that directory name is a hash over the bundle,
  every new version started from an empty one - hence the setup wizard and an
  empty history after an update. Now anchored to `Environment.ProcessPath`.

  **Recovering data written by 0.8.0 - 0.8.2:** it only exists while the old
  container does, so do this *before* updating:
  ```bash
  docker exec <container> sh -c 'find /tmp/.net -name knxmonitor.db'
  docker cp <container>:<path from above> ./knxmonitor.db
  ```
  Then move that file into the mounted data directory. If the container was
  already recreated, that data is gone; a database from before 0.8.0 is still in
  the mounted directory and is picked up automatically. On startup the app now
  logs a warning when it finds a stranded database.

## [0.8.2]

### Fixed
- **Routing could not be selected** ([#4](https://github.com/ingel81/knx-ng-monitor/issues/4)) -
  the settings page always saved `connectionType: 0`, so the backend's KNXnet/IP
  routing support was unreachable. Settings now has a Tunneling / Routing switch.
- **Routing ignored port and physical address** - telegrams went out under Falcon's
  `0.0.1` default source address; both values are now passed through.
- **Group-address names were rewritten in the table** ([#5](https://github.com/ingel81/knx-ng-monitor/issues/5)) -
  a leading `KG`/`UG`/`EG`/`OG`/`DG` token was turned into a badge and stripped
  from the name, inconsistently (`_` was not an accepted separator) and unlike the
  mobile cards and detail panel. Names are now shown verbatim everywhere.
- **Topology tree was in import order** ([#6](https://github.com/ingel81/knx-ng-monitor/issues/6)) -
  locations are now sorted by name (digit-aware, so `2_` precedes `10_`) and their
  devices and group addresses numerically by address.
- **"Test connection" was green for unreachable routing setups** - a multicast join
  always succeeds locally, so the probe now listens for real bus traffic and warns
  when the group stays silent. Tunneling is unchanged.

### Changed
- The physical address is only shown for routing; a tunneling interface assigns it.

### Note
- Routing needs multicast to reach the process: Docker `--network host`,
  Home Assistant add-on `host_network: true`.

## [0.8.1]

### Fixed
- **Import of current ETS versions** ([#2](https://github.com/ingel81/knx-ng-monitor/issues/2)) -
  the ETS version was matched against a hard-coded list of project schema
  namespaces that ended at schema 21 (ETS 6.0). ETS 6.1 writes schema 22 and
  ETS 6.2 / 6.3 / 6.4 write schema 23, so those projects were detected as
  `Unknown` and the import aborted with *"No loader available for ETS version
  Unknown"*. This hit password-protected projects in particular, where
  `knx_master.xml` is the only version marker readable before decryption. The
  schema is now mapped by numeric range, so future ETS releases keep working.
- **Group-address style ignored** - `GroupAddressStyle` is stored on
  `<ProjectInformation>`, but was read off the document root, so every ETS 5 / 6
  project fell back to three-level addressing. Two-level and free projects
  imported with wrong group addresses (`0/0/1` instead of `0/1` or `1`). ETS 4
  projects were affected additionally because their `P-XXXX/Project.xml` was
  looked up case-sensitively and never found. **Re-import affected projects to
  correct stored addresses.**
- **Missing device names in password-protected projects** - only the `P-XXXX/`
  folder is encrypted, while `knx_master.xml` and the `M-XXXX/` manufacturer
  data sit in the outer archive. The latter were dropped after decryption, so
  devices imported without product name and manufacturer (shown as their bare
  physical address). Affects ETS 4, 5 and 6.
- **Wrong-password diagnostics** - a failed decryption now reports the project
  password as the cause instead of surfacing a raw ZIP error, and both the ETS 6
  (PBKDF2-derived) and the plain ETS 4 / 5 password encoding are attempted when
  the ETS version cannot be determined up front.

## [0.8.0]

### Added
- **In-app log viewer** - a new **Logs** page streams the backend log live over
  SignalR (level filter, pause, copy), backed by an in-memory ring buffer.
- **Diagnostics download** - a one-click button bundles the rolling log files and
  system/version info into a single zip for bug reports.
- **Rolling file logs** - logs are written to `./data/logs/` with a configurable
  level via the `KNX_LOG_LEVEL` environment variable.
- **`/api/version`** endpoint and an embedded build version surfaced in the UI.

### Changed
- **History-preserving re-import** - re-importing a `.knxproj` now matches the
  existing project by its ETS project id and merges group addresses in place, so
  the recorded telegram history is kept instead of being discarded.
- **Data directory anchored to the executable** - the SQLite DB, JWT secret and
  logs now resolve relative to the binary instead of the current working
  directory, so the portable build behaves the same regardless of launch folder.
- **Auth hardening** - refresh tokens are stored hashed with reuse detection,
  rate limiting, automatic pruning and a logout-all option.
- **CI** - multi-arch (amd64 + arm64) Docker image, `SHA256SUMS` for release
  binaries, embedded version stamping.

### Fixed
- **Startup robustness** - `/healthz` now probes the database, startup fails fast
  on a bad data directory, and an upload size limit guards the import endpoint.

## [0.7.0]

### Added
- **GA network graph (Alpha)** — new **Graph** view: a force-directed map of the
  installation laid out **Building → Floor → Room → group address**. Floors are
  pinned to angular sectors (no crossing links), rooms bloom organically, and
  each GA is coloured by **function** (group-range) and clustered with its peers.
  GA→room is resolved **by name** ("OG Schlafzimmer …" → Schlafzimmer,
  floor-disambiguated), falling back to device location with central-hub-device
  exclusion. Live telegrams make the matching GA **glow with its current value**
  and emit a particle along its edge; click a GA to jump to its chart. Marked
  **Alpha** in the nav.
- **Activity heatmap** — the **Statistics** view gains a **weekday × hour**
  heatmap of bus activity (timezone-aware backend aggregation), surfacing
  daily/weekly routines at a glance.
- **Account menu** — language, theme (Light/Console) and logout moved into a
  single header **account dropdown**; the header is decluttered, the active nav
  tab restyled.

### Changed
- **Mobile-responsive overhaul** — every page reflows on phones: stacked
  toolbars/filters, tables → cards, a bottom navigation bar, the Monitor's
  secondary actions collapse into a kebab menu, a filter toggle for the archive,
  and a **scroll-to-top** button.
- **Charts & tooltip polish** — chart tooltip now shows series name, **unit** and
  a locale-formatted timestamp; selected group addresses sort to the top of the
  picker; the dataZoom slider is no longer clipped.

### Fixed
- **UTC timestamps** — historized telegrams are now tagged UTC end-to-end, so
  charts and the archive no longer render an hour offset versus the live view.

## [0.6.0]

### Added
- **Unified Monitor view** — the separate *Live View* and *History* pages are
  merged into one **Monitor** view with a **Live / Archive** toggle (default
  Live). A single shared quick-search applies in both modes; the live buffer
  keeps recording in the background while you browse the archive. Archive keeps
  keyset pagination + infinite scroll.
- **Runtime DE / EN language switch** — full English/German UI, switchable
  **without a reload** (signal-backed translation, ~250 keys); default English,
  choice persisted in `localStorage`.
- **Time-series charts** — new **Charts** view: pick up to 8 numeric group
  addresses, plot them over **1 h / 24 h / 7 d / 30 d / custom** with **separate
  Y-axes per unit** (°C vs W vs %), zoom/scroll, DPT-1 rendered as step lines;
  initial history is loaded and **new telegrams append live** (ECharts,
  lazy-loaded).
- **Statistics view** — total telegrams, average **msg/s** and a
  telegrams-over-time histogram for the selected range; plus a live **bus-load
  badge** (msg/s + % of TP1 bandwidth) in the Monitor header.
- **Write / read group values from the UI** — the telegram detail drawer can
  send a **GroupValueRead** or **write** a DPT-typed value (On/Off toggle for
  1-bit, number/text otherwise) to the live bus, gated behind a confirmation
  dialog. Encoding is verified end-to-end over an in-process KNX/IP loopback bus.
- **KNX Secure runtime decryption** — when a project keyring is stored, **Data
  Secure** group telegrams are decrypted at runtime (Falcon
  `GroupCommunicationSecurity`); optional **IP-Secure tunnel** (opt-in per
  configuration). Fully isolated — without a keyring the connection path is
  unchanged.
- **Building topology + communication objects** — the parser now extracts
  **Locations** (building → floor → room) and **communication objects** across
  ETS 4/5/6. New **Topology** view (building tree), a **room filter** in the
  archive, a **"used by"** list of device objects in the detail drawer, and the
  ability to **upload a `.knxkeys` keyring after import** without re-parsing.
- **Project lifecycle** — **deactivate** a project (severs the bus link), an
  **auto-connect toggle** on the active project, a **delete preview**
  (group-address / device / telegram counts) before deletion, and a faster,
  transactional **set-based delete**.

### Changed
- **CI** — release workflow actions bumped to the **Node 24** runtime ahead of
  GitHub's Node 20 removal.

## [0.5.0]

### Added
- **Clear history** — the History view gained a "more actions" kebab menu (next
  to *Export CSV*) with a **Clear history** action that wipes the entire telegram
  database after a confirmation dialog. The delete is set-based (`ExecuteDelete`
  + WAL checkpoint), so it is fast even on a full 1 M-row hot-tier; archived
  NDJSON day-files are left untouched.

## [0.4.1]

### Fixed
- **DPT decoding returned raw hex in Docker / single-file builds** — Falcon's
  master-data loader derives its search directories from `Assembly.Location`,
  which is **empty for assemblies in a single-file bundle**, so `DptFactory`
  threw and every value fell back to hex (`0x…`). The published binary now
  self-extracts all content to disk (`IncludeAllContentForSelfExtract`),
  restoring a valid assembly path. Affects v0.4.0 only; framework-dependent/dev
  runs were never impacted.
- Empty-state button icon alignment (the "Import project" upload icon overflowed
  the button).

### Changed
- Decode failures are now logged instead of silently falling back to hex.

## [0.4.0]

### Added
- **DPT decoding via Falcon SDK** — the hand-rolled converter is replaced by
  Falcon's built-in datapoint catalog: the full DPT range, **sub-type aware**
  (e.g. 5.001 % vs 5.010 counter pulses), with **units** and **enumeration
  names** (DPT 20 → `Comfort`, DPT 232 → `#FF8000`). Boolean states render their
  spec-correct labels (Switch → On/Off, OpenClose → Open/Close, State →
  Active/Inactive); labels are pinned to **English** regardless of host locale,
  numbers stay invariant.
- **Always-on recording** — the bus link is now managed by a backend
  **auto-connect worker**: it connects on startup and **reconnects
  automatically** after a gateway reboot, network blip or container restart
  (`KnxConfiguration.AutoConnect`, default on). The manual Connect/Disconnect
  buttons are gone — the toolbar shows a live **status indicator** (Live /
  Paused / Connecting / Reconnecting).
- **English UI** — the entire interface is now English (Live, History, Settings,
  Projects, import wizard, navigation, grid, detail sheet, cards); time
  formatting follows the active Angular locale.
- **`THIRD-PARTY-NOTICES.md`** — documents that the bundled **KNX Falcon SDK is
  proprietary** (KNX Tools Software License Agreement, free of fees,
  redistribution to end-users permitted); the project's MIT license does not
  cover it.

### Changed
- **Live View robustness** — telegram buffer + connection live in a singleton
  service, so switching **History ↔ Live no longer empties the view**; **Pause**
  now buffers incoming telegrams instead of dropping them and flushes them on
  resume.
- **Grid polish** — retuned column widths, **right-aligned** DPT / raw / value
  columns, and a **tooltip on truncated cells** (only when actually clipped).
- **Non-destructive connection test** — *Test connection* in Settings now probes
  a throwaway bus and never interrupts the live recording; changing the gateway
  IP/port **reconnects the live link** to the new settings.

## [0.3.0]

### Added
- **UI redesign ("Instrument")** — coherent **Light + Console (dark)** theme
  switchable at runtime via CSS design tokens, **IBM Plex** typography, petrol
  shell with custom SVG icon set; single source of truth in
  `styles/_design.scss` (mapped 1:1 from the design handoff).
- **Custom virtualized grid** — **AG-Grid replaced** by a lightweight
  CDK-Virtual-Scroll table (`knx-table`) shared by Live View and History: type
  pills, value semantics, **DPST units** (°C/W/lx/…), room tags, stable zebra,
  click-to-detail sheet, column manager, mobile cards (<768px). Bundle ~1 MB
  smaller.
- **History query** — server-side **full-text search** (`q`), **sort direction**
  (`order`) and **multi-select message types** (`types`) added to
  `/api/telegrams`.
- **Settings** — appearance card with Light/Console + grid-density toggles
  (persisted, applied live).

## [0.2.0]

### Added
- **Data recording (two-tier)** — telegrams now persist into a count-based
  SQLite **hot-tier ring buffer** (default 1,000,000) trimmed in-worker (single
  SQLite writer) with **WAL** enabled; bounded, constant DB size instead of
  unbounded growth.
- **Long-term archive** — opt-in **NDJSON + gzip** daily files under
  `data/archive/` (`YYYY-MM-DD.ndjson`, gzipped at day rollover, startup
  self-heal, optional retention); captures every telegram independently of the
  ring buffer.
- **History view** — new `/history` page (AG-Grid infinite scroll) backed by a
  **keyset-paginated** query API (`/api/telegrams`, `/count`, server-streamed
  `/export?format=csv`) plus archive day list/download.
- **Recording settings** — configure ring-buffer size, archive on/off and
  retention from the Settings page, applied live without restart
  (`/api/recording/settings`).

### Changed
- **Schema** — telegram indices reduced 5 → 2; new `RecordingSettings` table
  (migration `AddRecordingFeature`).

## [0.1.5]

### Fixed
- **Docker** — single-file bundle now extracts to `/tmp/.net` via
  `DOTNET_BUNDLE_EXTRACT_BASE_DIR`; the non-root `app` user introduced in v0.1.1
  could not write to its `/app` home, so startup failed with `Default extraction
  directory [/app] either doesn't exist or is not accessible for read/write`.
  Affects every container started from `v0.1.1`–`v0.1.4`.

## [0.1.4]

### Fixed
- **CI** — restrict the release job's `download-artifact` to `knx-ng-monitor-*`;
  the bumped `docker/build-push-action@v7` now uploads an extra `.dockerbuild`
  provenance artifact that broke the unfiltered download and aborted release
  creation on v0.1.3.

## [0.1.3]

### Changed
- **CI (docker actions)** — `docker/setup-buildx-action` and
  `docker/login-action` bumped from `@v3` → `@v4`, `docker/build-push-action`
  from `@v5` → `@v7`; finishes the Node 20 → Node 24 migration started in v0.1.2.

### Known issues
- Release job failed mid-publish due to an unrelated `download-artifact`
  incompatibility — fixed in v0.1.4. Docker image
  `ingel81/knx-ng-monitor:v0.1.3` was pushed and is usable; the GitHub Release
  page is empty for this tag.

## [0.1.2]

### Changed
- **CI** — release workflow's `actions/checkout`, `setup-node`, `setup-dotnet`,
  `upload-artifact`, `download-artifact` bumped to `@v5` so the build keeps
  working past the GitHub Actions Node 20 → Node 24 deprecation deadline.

## [0.1.1]

### Changed
- **SignalR + refresh-token hardening** — token factory reads from the auth
  service on every reconnect (no more stale-token loops after a refresh);
  concurrent 401s share a single refresh via `shareReplay` instead of triggering
  a refresh-storm that the backend's revoke-on-rotate then locked out.
- **Atomic refresh-token rotation** — old token is revoked and the new one
  inserted in a single `SaveChanges`, so a crash mid-rotation can no longer lock
  the user out.
- **Telegram persistence pipeline** — bounded `Channel<KnxTelegram>` (10 k,
  drop-oldest) drained by a `BackgroundService` with batched 100-row inserts;
  bus throughput is no longer coupled to DB latency, and a stalled SQLite cannot
  blow memory.
- **Import-job cleanup** — terminal jobs older than 1 h are swept every 5 min;
  `RemoveJob` zeros uploaded buffers and drops password references before
  evicting.
- **Container hardening** — Dockerfile runs as non-root `app` (uid 1000) with
  `--chown` on COPY and a `wget`-based `HEALTHCHECK`; `release.yml` jobs locked
  to `contents: read`.

### Added
- **`/healthz`** — anonymous liveness endpoint for container probes.

## [0.1.0]

### Added
- Standalone `KnxMonitor.ProjectParser` library — ETS 4 / 5 / 6 loaders behind a
  clean interface.
- ETS 6 password-protected archives (PBKDF2-HMAC-SHA256 over UTF-16LE → Base64 +
  AES ZIP).
- KNX Secure keyring decryption (PBKDF2-HMAC-SHA256 + AES-128-CBC) — backbone
  key, tool keys, management password, GA keys.
- Two-stage import wizard with optional keyring + first-project auto-activate.
- `KnxMonitor.ParserTool` CLI (parse / detect, console / json / csv,
  `--keyring`).

## [0.0.1]

### Added
- First production release — self-contained binaries (6 platforms), Docker
  image, auto-generated JWT secret, single-port deployment.

[0.7.0]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.7.0
[0.6.0]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.6.0
[0.5.0]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.5.0
[0.4.1]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.4.1
[0.4.0]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.4.0
[0.3.0]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.3.0
[0.2.0]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.2.0
[0.1.5]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.1.5
[0.1.4]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.1.4
[0.1.3]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.1.3
[0.1.2]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.1.2
[0.1.1]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.1.1
[0.1.0]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.1.0
[0.0.1]: https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.0.1
