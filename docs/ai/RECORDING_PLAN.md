# Implementierungsplan: Datenaufzeichnung (Recording)

Status: Plan, noch nicht umgesetzt. Branch: `claude/last-commit-review-c5s4jg`.

## 1. Ziel & Architektur

Telegramme werden heute zwar in SQLite geschrieben (`TelegramPersistenceService`,
gepufferter Channel, Batch 100, drop-oldest), aber **nie wieder gelesen** (kein
`TelegramsController`, History-loses Live-UI) und **nie begrenzt** (DB wächst
unbegrenzt). Dieses Feature ergänzt Steuerung, Begrenzung und Konsum als
**Zwei-Tier-Speicher**, vollständig self-contained (keine externe DB/Tools/Container).

```
KNX-Bus ─ OnGroupMessageReceived (decodiert: GroupAddressId, ValueDecoded)
            │
            ├─ TelegramReceived event ──┬─ TelegramBroadcastService  → SignalR (Live-View, unverändert)
            │                           └─ TelegramArchiveService    → Cold-Tier (NDJSON+gzip, opt-in)
            │
            └─ ITelegramQueue.TryEnqueue → TelegramPersistenceService
                                              ├─ AddRangeAsync   → SQLite Hot-Tier (Ring-Buffer)
                                              └─ Retention (im selben Worker/Scope = Single-Writer)
                                                   DELETE WHERE Id < maxId-N  +  wal_checkpoint(TRUNCATE)
```

- **Hot-Tier** = SQLite Ring-Buffer, count-basiert, Default 1.000.000, konfigurierbar.
  Schnell & indiziert, deckt Live-/History-Abfragen. Konstante DB-Größe.
- **Cold-Tier** = NDJSON+gzip Tagesdateien unter `data/archive/`. Vollständiger
  Langzeit-Record (Jahre), ~5–10x kleiner, **opt-in (Default aus)**.

## 2. Gelockte Spec (Referenz)

- Ring-Buffer count-basiert, Default 1.000.000.
- Retention **im** Persistenz-Worker (Single SQLite-Writer), gechunktes
  `DELETE WHERE Id < maxId-N`, ~alle 10 s / je M Batches.
- WAL an (`journal_mode=WAL`, `synchronous=NORMAL`), periodischer
  `wal_checkpoint(TRUNCATE)`, **kein VACUUM**.
- Telegram-Indizes von 5 → 2 (behalten: Composite `(Timestamp, DestinationAddress)`
  + FK `GroupAddressId`; droppen: Standalone `Timestamp`, `DestinationAddress`, `MessageType`).
- Cold-Tier NDJSON, kompakte Keys `t,s,d,mt,r,v`; aktueller Tag plain `.ndjson`
  (append-only, crash-safe), bei Tageswechsel gzippen → `.ndjson.gz`, Rohdatei löschen.
- Archiv parallel (Tee auf `TelegramReceived`), erfasst **alles**, nur wenn
  `ArchiveEnabled`. Archiv-Retention optional (`ArchiveRetentionDays`, null=unbegrenzt).
- History-View liest nur Hot-Tier; Archiv-Query *quer durch* die Dateien = später.
- Settings-UI konfigurierbar, **Live-Apply** ohne Neustart.
- Globaler Log, UTC, alle Endpoints `[Authorize]`.

## 3. Schnittstellen-Abgleich (aufgelöste Konflikte zwischen den Strängen)

**3.1 `IRecordingSettingsProvider` — eine kanonische Definition** (Datei gehört Strang A).
Singleton, thread-safe, gecachter Snapshot, hot-path-sicher (kein DB-Hit pro Telegramm):

```csharp
// KnxMonitor.Core/Interfaces/IRecordingSettingsProvider.cs
public interface IRecordingSettingsProvider
{
    RecordingSettings Current { get; }                 // gecachter Entity-Snapshot
    int  HotBufferMaxCount { get; }                    // Convenience: Worker
    bool ArchiveEnabled { get; }                       // Convenience: Archiv
    int? ArchiveRetentionDays { get; }                 // Convenience: Archiv
    Task InitializeAsync(CancellationToken ct = default);                 // Startup-Load
    Task UpdateAsync(RecordingSettings updated, CancellationToken ct = default); // persist + atomar swappen (Live-Apply)
}
```
- `UpdateAsync` macht **persist (Repo) + Snapshot-Swap** in einem Schritt → der
  `RecordingController` bleibt dünn (mappt nur DTO↔Entity). Ersetzt sowohl den
  separaten `Invalidate()` (Strang A) als auch das DTO-`Current`/`GetAsync` (Strang B).
- Worker liest `HotBufferMaxCount`, Archiv liest `ArchiveEnabled`/`ArchiveRetentionDays`
  jeweils pro Pass/Telegramm vom Snapshot → live, ohne Neustart, ohne DB-I/O.

**3.2 Keyset-Index.** API ordnet `(Timestamp DESC, Id DESC)`. Der gedroppte
Standalone-`Timestamp`-Index ist unkritisch: der behaltene Composite
`(Timestamp, DestinationAddress)` bedient Timestamp-Range/-Order über die führende
Spalte; die Tie-Group gleicher Timestamps wird per `Id` klein nachsortiert. **Kein
neuer Index** in v1 (2-Index-Ziel bleibt). Optionales Tuning später:
dedizierter `(Timestamp, Id)`-Index, falls Bursts groß werden.

> **Review-Korrektur (Begründung der Index-Reduktion):** Die ursprüngliche
> Rechtfertigung „weniger Insert-Amplifikation" trägt **nicht** — eine KNX-TP1-Linie
> macht physikalisch ~max. 50 Telegramme/s (real eher einige/s), Insert-Index-Pflege
> ist bei diesen Raten irrelevant (siehe §10/R5). Die Reduktion 5→2 ist daher eine
> **Lese-/Platz-Entscheidung** („keine Indizes pflegen, die niemand abfragt + kleinere
> DB"), kein Durchsatz-Gewinn. Abwägung: dropt man Standalone-`DestinationAddress`/
> `MessageType`, werden History-Filter auf *nur* Adresse oder *nur* Typ **ohne**
> Zeitfenster zum Table-Scan. Wenn solche Filter häufig werden, stattdessen
> `(DestinationAddress, Timestamp)` als Composite behalten (statt `(Timestamp, Dest)`)
> — das opfert aber den reinen Zeitbereichs-Scan. Default bleibt 5→2; bewusst als
> Lese-Trade-off treffen.

**3.3 Source-Filter** ist unterstützt (`TelegramQueryRequest.Source`) → Frontend verdrahtet ihn.

**3.4 Cursor replayable.** Cursor = `base64url("{Timestamp:O}|{Id}")`, deterministisch.
Frontend hält `blockIndex→cursor`-Map über die ganze Session (kein Evicten der
Cursor), `maxBlocksInCache` großzügig → Scroll-up im Infinite-Grid re-fetcht korrekt.

**3.5 Archiv-Pfad** aus dem DB-Connection-String-Verzeichnis ableiten
(`Path.GetDirectoryName(dbPath)/archive`), damit DB und Archiv unter `./data/` liegen.

**3.6 `GetByMessageTypeAsync` verliert seinen Index** (Standalone `MessageType`
gedroppt). Reiner Typ-Filter wird zum Table-Scan; akzeptabel, weil Typ praktisch
immer mit Zeitbereich kombiniert wird (→ Composite greift). Dokumentiert.

**3.7 DTO-Shape.** `TelegramDto` spiegelt **exakt** das SignalR-`NewTelegram`-Payload
(`TelegramBroadcastService`), damit das Frontend den bestehenden `KnxTelegram`-TS-Typ
1:1 weiterverwendet.

## 4. Strang A — Hot-Tier / Storage / Retention

### Neue Dateien
- `KnxMonitor.Core/Entities/RecordingSettings.cs` — Single-Row-Config (`Id`,
  `HotBufferMaxCount=1_000_000`, `ArchiveEnabled=false`, `ArchiveRetentionDays=null`, `UpdatedAt`).
- `KnxMonitor.Core/Interfaces/IRecordingSettingsRepository.cs` — `: IRepository<RecordingSettings>`,
  `GetOrCreateAsync()` (seedet Default-Row lazy).
- `KnxMonitor.Infrastructure/Repositories/RecordingSettingsRepository.cs` — Impl
  (Muster: `KnxConfigurationRepository`).
- `KnxMonitor.Core/Interfaces/IRecordingSettingsProvider.cs` — siehe 3.1 (kanonisch).
- `KnxMonitor.Infrastructure/Services/RecordingSettingsProvider.cs` — Singleton,
  `volatile RecordingSettings _current`, atomarer Reference-Swap; `InitializeAsync`
  via `IServiceScopeFactory`→`GetOrCreateAsync`; `UpdateAsync` persistiert + swappt.
- `KnxMonitor.Infrastructure/Data/SqliteWalConnectionInterceptor.cs` —
  `DbConnectionInterceptor`, `ConnectionOpened(Async)` → `PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;`.

### Geänderte Dateien
- `ApplicationDbContext.cs` — `DbSet<RecordingSettings>`; Entity-Config-Block;
  in `KnxTelegram`-Block die 3 Standalone-`HasIndex` entfernen (Composite + FK behalten).
- `ITelegramRepository.cs` / `TelegramRepository.cs` — neu:
  `GetMaxIdAsync()`, `DeleteOlderThanIdChunkAsync(thresholdId, chunkSize)`
  (Subquery-Form, da SQLite-DELETE kein `LIMIT`), `CheckpointWalAsync()`.
- `TelegramPersistenceService.cs` — ctor-Dep `IRecordingSettingsProvider`; nach
  erfolgreichem Batch-Insert Retention triggern, wenn `batches>=M || elapsed>=10s`:
  `RunRetentionAsync` (maxId holen, threshold=maxId−N, gechunkt löschen
  `ChunkSize=5_000`, `MaxChunksPerPass` cappen, danach `CheckpointWalAsync`). try/catch
  wie der Insert-Pfad, damit Retention nie den Worker killt.
  > **Review-Präzisierung (R1):** „derselbe Scope" heißt **exakt innerhalb** des
  > bestehenden `using var scope`-Blocks (`TelegramPersistenceService.cs:77-80`),
  > direkt nach `AddRangeAsync`, auf demselben `DbContext`/Connection. Ein **eigener**
  > Scope = zweiter SQLite-Connection = genau die Lock-Konkurrenz, die wir vermeiden.
  > **R2:** Der Worker blockiert bei Stille in `ReadAllAsync` (`:65`), d.h. Retention/
  > Checkpoint laufen nur bei **aktivem Verkehr** (geprüft an Batch-Grenzen). Bei Idle
  > kein Wachstum → kein Trim nötig; einzige Folge: ein gesenktes `HotBufferMaxCount`
  > greift erst, wenn wieder Telegramme fließen. `MaxChunksPerPass` ist wichtig, damit
  > ein großer Trim (z.B. nach Senken des Limits) den Channel-Drain nicht aushungert.
- `Program.cs` — `options.AddInterceptors(new SqliteWalConnectionInterceptor())`;
  `AddScoped<IRecordingSettingsRepository,…>`, `AddSingleton<IRecordingSettingsProvider,…>`;
  im Startup-Scope nach `cacheService.InitializeAsync()` →
  `await provider.InitializeAsync()` (Tabelle existiert da bereits via `MigrateAsync`).

### Migrationen (Reihenfolge!)
1. Model-Edit (3 Indizes raus) → `dotnet ef migrations add ReduceKnxTelegramIndices
   --project backend/KnxMonitor.Infrastructure --startup-project backend/KnxMonitor.Api
   --output-dir Data/Migrations`. Erwartet: 3× `DropIndex`. Verifizieren, dass Composite + FK bleiben.
2. Model-Edit (`RecordingSettings` DbSet+Config) → `dotnet ef migrations add AddRecordingSettings …`.
   Erwartet: `CreateTable("RecordingSettings", …)`.

Anwendung automatisch beim Start via `DbInitializer.MigrateAsync`.

## 5. Strang B — Query-API + Cold-Archiv

### Neue Dateien
- `KnxMonitor.Core/DTOs/`: `TelegramDto` (= SignalR-Shape), `TelegramQueryRequest`
  (`From,To,Address,Source,Type,Cursor,PageSize=100` clamp 1..1000),
  `PagedResult<T>` (`Items,NextCursor,HasMore`), `RecordingSettingsDto`,
  `ArchiveDayDto` (`Date,FileName,SizeBytes,Compressed`).
- `KnxMonitor.Infrastructure/Services/TelegramArchiveService.cs` — Hosted Singleton,
  Tee auf `IKnxConnectionService.TelegramReceived` (wie `TelegramBroadcastService`).
  Hot-Path: `ArchiveEnabled`-Gate (Snapshot) → in internen Bound-Channel enqueuen,
  nie auf dem Bus-Thread schreiben. Writer-Loop: `StreamWriter` über
  aktuelle Tagesdatei `data/archive/YYYY-MM-DD.ndjson` (`FileMode.Append`,
  `FileShare.Read`); Zeile = `{"t":ISO-O,"s":src,"d":dst,"mt":enum,"r":hex,"v":decoded}`;
  Flush ~5 s (`FlushToDisk`). Mitternachts-Rollover: alten Tag gzippen → `.gz`,
  Roh erst nach vollständigem gz löschen. Startup-Self-Heal: alte plain `.ndjson`
  (Datum<heute) nachträglich gzippen. Archiv-Retention: bei Rollover/Start Dateien
  älter als `ArchiveRetentionDays` löschen (null=behalten). Clock-Seam (`Func<DateTime>`)
  für Tests. `StopAsync`: unsubscribe, drain, final flush — aktuellen Tag NICHT gzippen.
  > **Review-Präzisierungen:**
  > **R3 (Handler-Isolation):** Archiv und Broadcast hängen am selben Multicast-Event
  > (`KnxConnectionService.cs:178`). Wirft der **synchrone** Teil eines Handlers vor dem
  > ersten `await`, bricht die Invocation ab und nachfolgende Handler laufen nicht. Der
  > Archiv-Hot-Path (`ArchiveEnabled`-Check + Enqueue) **muss** synchron exception-proof
  > sein (eigenes try/catch), sonst kann er den Live-Broadcast killen.
  > **R4 (Overflow-Policy):** Der interne Channel ist **nicht** wie der Hot-Tier
  > drop-oldest „egal" — Vollständigkeit ist der Zweck des Archivs. Großzügig
  > dimensionieren + **Drop-Zähler/Log** (Lücken sichtbar machen), **nicht** `Wait`
  > (würde den Bus-Thread blocken). Positiv: weil das Archiv am *Event* teet (nicht an
  > der SQLite-Queue), unterliegt es **nicht** dem drop-oldest des Hot-Tiers → kann
  > *vollständiger* sein als die DB. Bei KNX-Raten (zig/s) hält der gzip-Append-Writer
  > ohnehin mühelos mit; echte Drops nur bei länger stehender Platte.
- `KnxMonitor.Api/Controllers/TelegramsController.cs` und `RecordingController.cs`.

### Geänderte Dateien
- `ITelegramRepository.cs` / `TelegramRepository.cs` — `TelegramFilter`-Typ +
  `BuildFilteredQuery` (geteilt); `GetPageKeysetAsync` (Take pageSize+1 Sentinel,
  `OrderByDescending(Timestamp).ThenByDescending(Id)`, `Include(GroupAddress)`);
  `CountByFilterAsync`; `StreamByFilterAsync` (`AsAsyncEnumerable().AsNoTracking()`,
  flache Projektion mit JOIN statt `Include`, Timestamp ASC).
- `Program.cs` — `AddSingleton<TelegramArchiveService>` + `AddHostedService(sp=>…)`.

### Endpoints (alle `[Authorize]`)
- `GET /api/telegrams` → `PagedResult<TelegramDto>` (Keyset).
- `GET /api/telegrams/count` → `{ count }`.
- `GET /api/telegrams/export?from=&to=&format=csv` → **gestreamtes** `text/csv`
  direkt auf `Response.Body` (Header + `await foreach` über `StreamByFilterAsync`,
  CSV-Quoting). Kein Buffering.
- `GET /api/telegrams/archive/days` → `IEnumerable<ArchiveDayDto>`.
- `GET /api/telegrams/archive/{date}` → `FileStreamResult` (`application/gzip`),
  `date` strikt `^\d{4}-\d{2}-\d{2}$` (Path-Traversal-Schutz), 404 wenn fehlt.
- `GET /api/recording/settings` → `RecordingSettingsDto` (aus Provider).
- `PUT /api/recording/settings` → validiert (`HotBufferMaxCount>0`,
  `ArchiveRetentionDays==null||>0`), `provider.UpdateAsync(entity)`, gibt aktualisiert zurück.

## 6. Strang C — Frontend

AG-Grid ist **Community** (`ag-grid-community@34`) → **Infinite Row Model**
(im `AllCommunityModule`), kein Server-Side Model.

### Neue Dateien
- `core/models/telegram-history.models.ts` — `TelegramHistoryRow = KnxTelegram`
  (reuse aus `signalr.service.ts`), `TelegramQueryParams`, `TelegramPage`, `ArchiveDay`.
- `core/models/recording-settings.models.ts` — `RecordingSettings`-Interface.
- `core/services/telegram-history.service.ts` — `query`, `count`,
  `exportCsv` (`responseType:'blob'`, Interceptor hängt JWT an),
  `listArchiveDays`, `downloadArchiveDay`. URLs: `${apiUrl}/telegrams[...]`,
  Archiv: `${apiUrl}/telegrams/archive/days` und `/archive/{date}`.
- `core/services/recording-settings.service.ts` — `getSettings`/`updateSettings`
  gegen `${apiUrl}/recording/settings`.
- `features/history/history.component.{ts,html,scss}` — Grid wie Live-View, aber
  `rowModelType:'infinite'` + `IDatasource` (bridged: `blockIndex→cursor`-Map,
  Block 0 ohne Cursor, `successCallback(rows, lastRow)`).
  > **Review-Korrektur (R6):** `lastRow` **nicht** aus `/count` speisen. Solange
  > `nextCursor != null` → `lastRow = -1` (unbekannt). Erst wenn das Ende erreicht ist
  > (`nextCursor == null`) → `lastRow = bisher geladene Zeilen`. Grund: gibt man AG-Grid
  > die exakte Gesamtzahl, erlaubt die Scrollbar einen Sprung zu einem Block, für den wir
  > **keinen** Cursor haben — Keyset ist forward-only. `/count` daher nur als **Anzeige-
  > Zahl** im Header nutzen, nicht als bekannte Zeilenzahl des Grids.
  Floating-Filter aus (Server filtert). Filter-Toolbar: `from`/`to`
  (`datetime-local` → `toISOString()`), `address`, `source`, `type` (`mat-select`),
  Apply/Reset (→ Datasource neu, Cursor-Map reset). CSV-Export via Blob-Download-Helper.
- (empfohlen) `features/live-view/telegram-columns.ts` — `getTelegramColumnDefs()` etc.,
  von Live-View **und** History importiert (sonst Copy-Paste mit Drift).

### Geänderte Dateien
- `app.routes.ts` — Lazy-Child-Route `history` unter dem `Layout`-Shell (authGuard).
- `shared/layout/layout.html` — Nav-Link `/history` (`mat-icon`=`history`), gleiches
  `.nav-link`-Muster (Mobile-Nav bewusst NICHT angefasst).
- `features/settings/settings.{ts,html}` — zweite `mat-card` „Datenaufzeichnung":
  `hotBufferMaxCount` (number ≥1), `archiveEnabled` (slide-toggle),
  `archiveRetentionDays` (number, disabled wenn Archiv aus); Save→PUT, Toast
  „angewendet (live)". Reuse `.config-grid/.settings-card/.button-group`.
- (Lower prio) Archiv-Tageliste mit Download-Buttons (gleicher Blob-Helper).

## 7. Strang-übergreifende Reihenfolge

1. **A** zuerst: Entity, kanonisches `IRecordingSettingsProvider`, Repo, Provider,
   Migrationen, WAL-Interceptor, Retention im Worker, Index-Reduktion.
   (Blockt B, weil Archiv + RecordingController davon abhängen.)
2. **B**: DTOs, Repo-Query-Methoden (Keyset/Stream/Count), `TelegramArchiveService`,
   `TelegramsController`, `RecordingController`, DI.
3. **C**: Models, Services, History-View, Settings-Sektion, Nav, Route. Kann gegen
   den vereinbarten Contract parallel zu B gebaut und nach B integriert werden.

## 8. Teststrategie

Es existiert nur `KnxMonitor.ProjectParser.Tests`. **Neues Projekt
`KnxMonitor.Infrastructure.Tests`** (xUnit) ist fester Bestandteil der Umsetzung
(bestätigt) — zum Solution-File hinzufügen. Abdeckung:
- `RecordingSettingsRepository.GetOrCreateAsync` seedet genau einmal.
- `RecordingSettingsProvider`: Init lädt, `UpdateAsync` persistiert+swappt atomar, Accessoren live.
- Retention (file-backed SQLite): >N Zeilen einfügen, `RunRetentionAsync` → Rest ≈ N,
  `MaxId` erhalten, Chunk-Size respektiert.
- WAL (file-backed, nicht `:memory:`): `PRAGMA journal_mode` == `wal`.
- Keyset-Pagination: gleiche Timestamps → keine Lücken/Dupes über Seiten,
  Cursor-Roundtrip, fehlerhafter Cursor → 400.
- Filter-Komposition (from/to/address/source/type + Kombis).
- CSV-Export: Header + Quoting/Escaping, streamt (`IAsyncEnumerable`).
- `TelegramArchiveService` (Temp-Dir, Clock-Seam): Opt-in-Gate, NDJSON-Zeilenform,
  Flush lesbar, Rollover (Vortag gz + Roh weg, heute plain), Self-Heal, Retention.
- Controller: Routen, `[Authorize]`, Statuscodes (400 bei bad cursor/format/date).
- Frontend (`ng test`, greenfield): Service-URL/Param/Blob, History-Filter→Param-Mapping,
  Datasource success/fail mit gemocktem Service.

## 9. Risiken

- Index-Drop auf großer Tabelle: SQLite `DropIndex` ist Metadaten-Only, billig.
- Migration auf bestehender DB: läuft autom. beim Start; setzt InitialCreate-Schema voraus.
- `dotnet ef` nicht als lokales Manifest vorhanden → globales Tool nötig.
- Retention-Cadence vs. Channel-Drain: `MaxChunksPerPass` cappen, damit Drain unter Burst nicht hungert.
- Streaming-Export: `await foreach` muss im Request abschließen (Response-Buffering aus).
- Crash während gz-Rollover: Roh erst nach vollständigem gz löschen; Start-Self-Heal idempotent.
- Path-Traversal beim Archiv-Download: strikte Datums-Regex.
- Großer CSV-Export: Browser puffert Blob im RAM (Angular kann nicht to-disk streamen) — v1-Limitation.
- `datetime-local` ist lokale Zeit → vor dem Senden konsistent nach UTC/ISO wandeln.
- `environment.development` wird appweit direkt importiert (latenter Prod-Config-Bug) — im Feature nicht mitfixen.

## 10. Review-Befunde (gegen den realen Code verifiziert)

Alle tragenden Annahmen wurden gegen die echten Dateien geprüft. Verdikt:
**tragfähig, keine Show-Stopper.** Die Verfeinerungen R1–R6 sind oben inline an der
jeweiligen Stelle eingearbeitet.

### Verifiziert
| Annahme | Beleg |
|---|---|
| Worker = Single-Writer, Scope **pro Batch** | `TelegramPersistenceService.cs:77` |
| 5 Telegram-Indizes (4 explizit + FK `SetNull`) | `ApplicationDbContext.cs:110-118` |
| Tee `TelegramReceived`, voll decodiert vor `Invoke` | `KnxConnectionService.cs:175-178` |
| Broadcast-Service als Hosted-Vorlage | `TelegramBroadcastService.cs:32,43` |
| SignalR-Payload = DTO-Vorlage, `value`=Hex-String | `TelegramBroadcastService.cs:60-72` ↔ `signalr.service.ts:7-19` |
| AG-Grid **Community** 34.3, kein Enterprise | `frontend/package.json` |
| Kein `OnConfiguring`/Interceptor; `AddDbContext`+SplitQuery | `Program.cs:40-49` |
| Bestehende Repo-Methoden (OFFSET/Count/Range) | `ITelegramRepository.cs:8-13` |

### R1–R6 (inline eingearbeitet)
- **R1** Retention exakt im Batch-`using`-Block (§4) — sonst zweiter Connection.
- **R2** Retention/Checkpoint nur bei aktivem Verkehr (§4) — Idle-Verhalten dokumentiert.
- **R3** Archiv-Handler synchron exception-proof (§5) — sonst Bruch des Multicast-Broadcasts.
- **R4** Archiv-Overflow: Drop-Zähler/Log statt stiller Verlust, kein `Wait` (§5).
- **R5** siehe unten — KNX-Durchsatz entschärft Performance, verschiebt Index-Begründung (§3.2).
- **R6** Infinite-Grid: `lastRow=-1` bis Ende, `/count` nur Anzeige (§6).

### R5 — KNX-Physik als Rahmen
Eine KNX-TP1-Linie macht physikalisch ~max. **50 Telegramme/s** (real eher einige/s im
Mittel, bursty mit langen Idle-Phasen). Damit sind Delete-Speed, Archiv-Durchsatz und
Insert-Index-Amplifikation **allesamt unkritisch**. Konsequenzen:
- Die Index-Reduktion 5→2 ist eine Lese-/Platz-, **keine** Durchsatz-Entscheidung (§3.2).
- Ring-Sizing ist eine Frage der **Aufbewahrungsdauer**, nicht des Durchsatzes:
  1.000.000 Zeilen ≈ Tage–Wochen typischen Verkehrs (bei ~40/s Dauerlast ~7 h, real
  meist viel länger). Wer „immer X Tage" will, nutzt das (unbegrenzte) Archiv.

### Grüne Notizen (kein Handlungszwang)
- SignalR sendet live `Id=0` (`:62`, Id erst nach Persist) — Bestandsverhalten;
  History-DTO hat echte Ids, Modell-Reuse bleibt valide.
- WAL erzeugt `-wal`/`-shm`-Sidecars → „eine DB-Datei" werden drei (Backup/Docker-Volume).
  `synchronous=NORMAL`: sicher gegen App-Crash, kann bei Stromausfall die letzte Txn
  verlieren — für einen Monitor akzeptabel.
- WAL hilft zusätzlich dem Streaming-Export: gleichzeitiger Reader (Export) + Writer
  (Worker) ohne Block.
- SplitQuery ist global, aber `Include(GroupAddress)` ist eine Reference → JOIN (nicht
  gesplittet); Export nutzt flache Projektion. Beides ok.
- `dotnet ef`-Verfügbarkeit vor Umsetzung prüfen, sonst Migrationen handschreiben.
