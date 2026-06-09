# Handover: Datenaufzeichnung (Branch `claude/last-commit-review-c5s4jg`)

Dieses Dokument ist für eine **lokale Claude-Code-Session** gedacht, die diese Arbeit fertigstellt
(vor allem: den Backend-Compile verifizieren, den ich in der Cloud-Umgebung **nicht** ausführen konnte).

- Zugehöriger Plan (Begründungen, Schnittstellen-Abgleich, Review-Befunde): `docs/ai/RECORDING_PLAN.md`
- PR: https://github.com/ingel81/knx-ng-monitor/pull/1
- Branch: `claude/last-commit-review-c5s4jg`

---

## 0. Auftrag an die lokale Session (zum direkt Loslegen)

> Lies `docs/ai/RECORDING_HANDOVER.md` und `docs/ai/RECORDING_PLAN.md`. Verifiziere zuerst den
> Backend-Build (`dotnet build backend/KnxMonitor.sln`) und arbeite die **Compile-Risiko-Checkliste
> (§4)** ab. Fixe Compile-Fehler minimal-invasiv, ohne das Design zu ändern. Danach
> `dotnet test backend/KnxMonitor.Infrastructure.Tests`, dann App starten und die History-View +
> Recording-Settings manuell prüfen. Committe kompakt, ohne Footer; pushe auf denselben Branch.

Hausregeln (aus `CLAUDE.md`): Commits kompakt + ohne Claude-Footer; eigene/proprietäre Sample-Files
nie committen; vor `dotnet publish` laufenden `KnxMonitor.Api`-Prozess killen.

---

## 1. Warum dieser Handover

Die Cloud-Umgebung hatte **kein .NET SDK** (SDK-CDN per Netzwerk-Policy geblockt; nur npm/nuget
erreichbar). Folge:
- **Frontend** wurde lokal gebaut → **grün** (`ng build --configuration production`, nur die
  vorbestehenden ag-grid-CSS-Budget-Warnings).
- **Backend** wurde sorgfältig gegen die echten Code-Patterns geschrieben, **aber nie kompiliert**.
  Die EF-Migrationen sind **handgeschrieben** (kein `dotnet ef` verfügbar).
  → **Der erste echte Backend-Compile passiert lokal.** Das ist die Hauptaufgabe hier.

---

## 2. Was implementiert wurde (Feature-Überblick)

Zwei-Tier-Speicher für KNX-Telegramme:
- **Hot-Tier** = SQLite-Ring-Buffer, count-basiert (Default 1.000.000), Retention im Persistenz-Worker
  (Single-Writer), WAL an, Indizes 5→2.
- **Cold-Tier** = NDJSON+gzip Tagesarchiv (`data/archive/YYYY-MM-DD.ndjson[.gz]`), **opt-in (Default aus)**.
- **Query-API** + **History-View** (liest Hot-Tier) + **Settings-UI** mit Live-Apply.

Endpoints (alle `[Authorize]`):
- `GET /api/telegrams` (Keyset-Pagination), `/count`, `/export?format=csv` (gestreamt),
  `/archive/days`, `/archive/{date}`
- `GET|PUT /api/recording/settings`

---

## 3. Geänderte/neue Dateien

### Backend — neu
- `KnxMonitor.Core/Entities/RecordingSettings.cs`
- `KnxMonitor.Core/Interfaces/IRecordingSettingsRepository.cs`
- `KnxMonitor.Core/Interfaces/IRecordingSettingsProvider.cs`  ← **kanonische Schnittstelle** (Strang-Klebstoff)
- `KnxMonitor.Core/DTOs/{TelegramFilter,PagedResult,TelegramDto,RecordingSettingsDto,ArchiveDayDto,TelegramQueryRequest}.cs`
- `KnxMonitor.Infrastructure/Repositories/RecordingSettingsRepository.cs`
- `KnxMonitor.Infrastructure/Services/RecordingSettingsProvider.cs`
- `KnxMonitor.Infrastructure/Services/TelegramArchiveService.cs`
- `KnxMonitor.Infrastructure/Data/SqliteWalConnectionInterceptor.cs`
- `KnxMonitor.Infrastructure/Data/Migrations/20260608120000_ReduceKnxTelegramIndices.cs`
- `KnxMonitor.Infrastructure/Data/Migrations/20260608120100_AddRecordingSettings.cs`
- `KnxMonitor.Api/Controllers/TelegramsController.cs`
- `KnxMonitor.Api/Controllers/RecordingController.cs`

### Backend — geändert
- `KnxMonitor.Core/Interfaces/ITelegramRepository.cs`  (Retention- + Keyset-/Stream-/Count-Methoden)
- `KnxMonitor.Infrastructure/Repositories/TelegramRepository.cs`  (Implementierung + `BuildFilteredQuery`)
- `KnxMonitor.Infrastructure/Services/TelegramPersistenceService.cs`  (Retention im Worker)
- `KnxMonitor.Infrastructure/Data/ApplicationDbContext.cs`  (DbSet + Config; 3 Indizes entfernt)
- `KnxMonitor.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs`  (Hand-Update)
- `KnxMonitor.Api/Program.cs`  (DI + WAL-Interceptor + Provider-Init + Archiv-HostedService)

### Tests — neu
- `KnxMonitor.Infrastructure.Tests/` (csproj, `SqliteTestDb`, `RecordingSettingsTests`,
  `TelegramRepositoryTests`, `WalInterceptorTests`, `TelegramArchiveServiceTests`)
- `backend/KnxMonitor.sln`  (Projekt eingetragen, GUID `CAB208EC-AAD3-49FF-BD9C-A9A9F06EF728`)

### Frontend — neu/geändert (Build bereits grün)
- neu: `core/models/{telegram-history,recording-settings}.models.ts`,
  `core/services/{telegram-history,recording-settings}.service.ts`,
  `features/history/history.component.{ts,html,scss}`
- geändert: `app.routes.ts`, `shared/layout/layout.html`, `features/settings/settings.{ts,html}`

---

## 4. Compile-Risiko-Checkliste (zuerst abarbeiten)

Diese Punkte konnte ich nicht verifizieren. In ungefährer Wahrscheinlichkeits-Reihenfolge:

1. **EF-Migrationen ohne Designer-Dateien.**
   Die zwei Migrationen tragen `[DbContext(typeof(ApplicationDbContext))]` + `[Migration("…")]`
   **am Haupt-Partial** (statt im üblichen `.Designer.cs`). Runtime-`MigrateAsync` braucht nur
   Attribut + `Up()` — sollte reichen.
   **Verifizieren:**
   ```bash
   dotnet ef migrations list --project backend/KnxMonitor.Infrastructure --startup-project backend/KnxMonitor.Api
   ```
   Müssen 5 erscheinen (3 alte + `ReduceKnxTelegramIndices` + `AddRecordingSettings`).
   **Falls EF meckert / Designer erwartet:** beide handgeschriebenen Migrationen **löschen**,
   `ApplicationDbContext`-Änderungen aber behalten, und neu scaffolden:
   ```bash
   # ApplicationDbContext enthält bereits die Index-Reduktion + RecordingSettings-DbSet.
   # Snapshot ggf. zurücksetzen (git checkout der Snapshot-Datei auf master-Stand),
   # dann sauber generieren:
   dotnet ef migrations add ReduceKnxTelegramIndices --project backend/KnxMonitor.Infrastructure --startup-project backend/KnxMonitor.Api --output-dir Data/Migrations
   dotnet ef migrations add AddRecordingSettings     --project backend/KnxMonitor.Infrastructure --startup-project backend/KnxMonitor.Api --output-dir Data/Migrations
   ```

2. **Snapshot ↔ Modell-Konsistenz.** Ich habe `ApplicationDbContextModelSnapshot.cs` von Hand
   editiert (3 Telegram-Indizes raus, `RecordingSettings` rein).
   **Verifizieren** (Dry-Run sollte eine **leere** Migration ergeben):
   ```bash
   dotnet ef migrations add _Verify --project backend/KnxMonitor.Infrastructure --startup-project backend/KnxMonitor.Api --output-dir Data/Migrations
   # Up/Down leer? → gut, danach wieder entfernen:
   dotnet ef migrations remove --project backend/KnxMonitor.Infrastructure --startup-project backend/KnxMonitor.Api
   ```
   Ist die `_Verify`-Migration **nicht** leer, weicht der Snapshot vom Modell ab → den Snapshot
   entsprechend angleichen (oder Snapshot auf master zurücksetzen + beide Migrationen neu scaffolden, s.o.).

3. **`Microsoft.Data.Sqlite` in `TelegramArchiveService`.** Nutzt `SqliteConnectionStringBuilder`
   (für die Archiv-Pfad-Ableitung aus dem Connection-String). Kommt transitiv über
   `Microsoft.EntityFrameworkCore.Sqlite`.
   **Falls** `CS0246 Microsoft.Data.Sqlite not found`: in
   `backend/KnxMonitor.Infrastructure/KnxMonitor.Infrastructure.csproj` ergänzen:
   ```xml
   <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.10" />
   ```

4. **Optionaler ctor-Parameter `Func<DateTime>? clock = null`** in `TelegramArchiveService`.
   Der MS-DI-Container respektiert Default-Werte → sollte als Singleton auflösbar sein (clock = null
   → `DateTime.UtcNow`). **Falls** beim Start `InvalidOperationException: Unable to resolve … Func<DateTime>`:
   den optionalen Parameter aus dem DI-genutzten Konstruktor ziehen und einen separaten
   Test-Konstruktor mit `clock` anlegen (Tests übergeben den clock direkt).

5. **`DbSet<RecordingSettings> RecordingSettings`** — Property-Name == Typname. Standard-EF-Pattern,
   kompiliert normalerweise (Typ-Lookup im generischen Argument). Sollte ok sein.

6. **Kleinkram (geringes Risiko):** `Response.Headers.ContentDisposition = …` (StringValues),
   `File(stream, contentType, fileName)`, `[GeneratedRegex]` am `partial class TelegramsController`.

Wenn 1–3 grün sind, ist der Rest erfahrungsgemäß unkritisch.

---

## 5. Verifikations-Ablauf

```bash
# 1) Build
cd backend
dotnet restore
dotnet build KnxMonitor.sln

# 2) Migrationen prüfen (siehe §4.1 / §4.2)

# 3) Unit-Tests
dotnet test KnxMonitor.Infrastructure.Tests/KnxMonitor.Infrastructure.Tests.csproj
#    Erwartet: RecordingSettings (2), TelegramRepository (4), Wal (1), Archive (4)

# 4) App starten (legt ./data/knxmonitor.db + ./data/archive/ an, wendet Migrationen an)
dotnet run --project KnxMonitor.Api      # http://localhost:8080

# 5) Frontend
cd ../frontend && npm install && ng serve   # http://localhost:4200
```

### Manuelle Smoke-Tests
- Login → **History** in der Nav. Grid lädt (leer, wenn keine Telegramme historisiert sind).
- **Settings → Datenaufzeichnung**: Ring-Größe ändern + speichern → Toast „applied (live)".
  Archiv-Toggle an → `./data/archive/` füllt sich bei Bus-Verkehr; Tagesdatei `YYYY-MM-DD.ndjson`.
- Mit aktiver KNX-Verbindung Telegramme erzeugen → in History sichtbar; CSV-Export lädt eine Datei;
  Archiv-Tagesliste erscheint unten in der History-View.
- WAL prüfen: neben `knxmonitor.db` tauchen `-wal`/`-shm` auf.

---

## 6. Wichtige Design-Entscheidungen (damit nichts „wegoptimiert" wird)

- **Single-Writer-Retention:** Das Trimmen läuft **im selben `using var scope`-Block** des
  Persistenz-Workers direkt nach `AddRangeAsync` (`TelegramPersistenceService.RunAsync`). Niemals in
  einen eigenen Scope/Service verschieben → sonst zweiter SQLite-Connection = Lock-Konkurrenz.
- **Archiv-Tee** hängt an `IKnxConnectionService.TelegramReceived` (wie `TelegramBroadcastService`),
  nicht an der Persistenz-Queue. Hot-Path ist synchron + exception-isoliert (darf den Multicast-
  Broadcast nicht brechen) und zählt Drops statt still zu verlieren.
- **`IRecordingSettingsProvider`** ist die eine kanonische Live-Apply-Schnittstelle: `UpdateAsync`
  macht persist + atomaren Snapshot-Swap. Worker liest `HotBufferMaxCount`, Archiv liest
  `ArchiveEnabled`/`ArchiveRetentionDays` pro Pass/Telegramm vom Snapshot (kein DB-I/O hot-path).
- **Keyset statt OFFSET**, Order `(Timestamp DESC, Id DESC)`, Cursor = `base64url("{Timestamp:O}|{Id}")`,
  deterministisch/replayable → Frontend hält `blockIndex→cursor`-Map für die ganze Session.
- **Index-Reduktion 5→2** ist eine Lese-/Platz-Entscheidung (KNX-Durchsatz ist klein), kein
  Durchsatz-Gewinn. Reine Adress-/Typ-Filter ohne Zeitfenster werden zum Table-Scan (akzeptiert).
- **Frontend AG-Grid = Community** → **Infinite Row Model** (kein Server-Side Model).

---

## 7. Offene Follow-ups (bewusst nicht im PR)

- README/Doku-Abschnitt zur History-View + Recording-Settings.
- Archiv-Query *quer durch* die Tagesdateien (v1 nur Download/List).
- Optionaler dedizierter `(Timestamp, Id)`-Index, falls Bursts groß werden.
- Frontend: nachträgliches Hochladen/Bearbeiten des Keyrings (bestand schon vor diesem Feature).

---

## 8. Was wir gemacht haben (Entscheidungs-Historie)

Damit die lokale Session den vollen Kontext hat — der Weg von der Idee zum Code:

**Ausgangslage.** Telegramme wurden in SQLite geschrieben, aber nie begrenzt (DB-Wachstum
unbegrenzt) und nie zurückgelesen (kein Controller; Live-View ist reiner SignalR-Stream). Ziel:
„beides können" — schneller Live-/Kurzzeit-Zugriff **und** Langzeit-Aufzeichnung über Jahre,
vollständig self-contained.

**Architektur-Entscheidung: Zwei-Tier.** Statt eines einzigen Speichers → **Hot-Tier**
(SQLite-Ring-Buffer, schnell/indiziert, konstante Größe) + **Cold-Tier** (append-only Dateiarchiv
für Langzeit). Begründungen siehe `RECORDING_PLAN.md` §1–§2.

**Zwei diskutierte Knackpunkte (vom User aufgeworfen):**
1. *„Ist das Löschen am Ring-Limit schnell genug?"* → Ja. Schlüssel: **nur ein SQLite-Writer**
   (Retention im Persistenz-Worker), gechunktes `DELETE WHERE Id < maxId−N`, periodisch. Der echte
   Kostenpunkt wäre Insert-Index-Amplifikation — bei KNX-Raten (~zig/s) aber irrelevant. Daraus
   folgte die Index-Reduktion 5→2 (als **Lese-/Platz**-Entscheidung, nicht Durchsatz).
2. *„Eigenes Binärformat vs. JSON+gzip fürs Archiv?"* → Analyse ergab: nach gzip ist der Vorsprung
   eines Eigenformats klein (~1,5–2×), kostet aber Portabilität/Robustheit und macht das Archiv
   **tool-abhängig** (Gegenteil von self-contained). **Entscheidung: NDJSON+gzip** (lesbar mit
   `zcat|jq`, jeder Sprache, für immer). Parquet wäre die Effizienz-Alternative, aber schwerer.

**Gelockte Spec** (User-Entscheidungen): Ring count-basiert Default 1.000.000; Retention im Worker;
WAL an, kein VACUUM; Indizes 5→2; NDJSON Tagesdateien, aktueller Tag plain → bei Rollover gzippen;
Archiv erfasst **alles parallel**, **opt-in (Default aus)**, optionale Datei-Retention;
History-View liest nur Hot-Tier (Archiv-Query quer = später); Settings-UI mit Live-Apply; UTC,
`[Authorize]`.

**Planung — parallel.** Drei Plan-Agents (Storage / API+Archiv / Frontend) haben je ihren Strang
gegen den echten Code recherchiert. Beim Zusammenführen wurden **7 Schnittstellen-Konflikte**
aufgelöst (siehe `RECORDING_PLAN.md` §3), u.a.:
- `IRecordingSettingsProvider` doppelt/verschieden definiert → **eine** kanonische Entity-basierte
  Schnittstelle (`UpdateAsync` = persist + atomarer Snapshot-Swap).
- Keyset ordnet `(Timestamp DESC, Id DESC)`, obwohl Standalone-`Timestamp`-Index gedroppt wird →
  trägt über die führende Spalte des Composite; kein neuer Index.
- Frontend-Sorge „forward-only Cursor + Scroll-up" → Cursor ist deterministisch/replayable,
  `blockIndex→cursor`-Map bleibt die ganze Session erhalten.
- Archiv-Pfad aus dem DB-Connection-String ableiten; DTO-Shape == SignalR-Payload (Model-Reuse).

**Plan-Review.** Der konsolidierte Plan wurde gegen den realen Code verifiziert (Datei:Zeile-Belege)
und um die Befunde **R1–R6** ergänzt (Single-Writer-Präzisierung, Idle-Verhalten,
Handler-Isolation, Overflow-Policy, KNX-Durchsatz-Realität, Grid-`lastRow`). Verdikt: tragfähig,
keine Show-Stopper. → `RECORDING_PLAN.md` §10.

**Umsetzung.** Strang A → B → C → Tests, in vier Commits. Frontend lokal gebaut (grün). Backend
mangels SDK in der Cloud nicht kompiliert → genau deshalb dieses Handover (§4/§5).

---

## 9. Session lokal fortsetzen

**Variante A — diese Web-Session nach lokal holen (`--teleport`).** Holt die komplette
Konversations-Historie dieser Cloud-Session ins lokale CLI. `--resume`/`-c` funktioniert dafür
**nicht** (liest nur lokale Transkripte aus `~/.claude/`); Web-Sessions brauchen `--teleport`:
```bash
git clone https://github.com/ingel81/knx-ng-monitor.git   # oder bestehender Klon
cd knx-ng-monitor
git checkout claude/last-commit-review-c5s4jg              # Branch ist gepusht
claude --teleport session_01VaNFeMceXNbh4KXeoztn9v          # oder: claude --teleport (Liste)
```
Voraussetzungen: selber Claude.ai-User, selbes Repo-Checkout, Branch gepusht (erledigt), keine
uncommitteten Änderungen. Hinweis: nur einbahnig (Web→lokal); zurück geht nicht.

**Variante B — frische lokale Session.** Ohne Teleport: einfach `claude` im Repo starten und
„arbeite `docs/ai/RECORDING_HANDOVER.md` ab" sagen. Plan + Handover liefern den vollen Kontext.
