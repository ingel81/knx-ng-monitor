# TODO

Konsolidierter Stand: siehe [`PROJECT_PLAN.md`](PROJECT_PLAN.md) → "Nächste Schritte"
und das Highlights-Kapitel in [`../../README.md`](../../README.md) → "Project status".

---

## Festgezurrte Roadmap (Stand 2026-06-12, AUQ-Runde)

Reihenfolge bewusst entschieden. Abarbeitung von oben nach unten.

### 0. CI Node-Deprecation — ✅ ERLEDIGT (Session 2026-06-12)
- [x] Release-Workflow Actions auf Node-24-Runtime gehoben:
      `softprops/action-gh-release@v1 → @v3` (einziger echter Node-20-Rest; v3.0.0 = Node-24-Runtime,
      API-kompatibel). `setup-node` `node-version: '20' → '22'` (Node 20 LTS EOL erreicht, Angular 20
      unterstützt 22). Verifiziert: checkout/setup-node/setup-dotnet/upload-artifact/download-artifact
      alle @v5 = Node 24; docker login@v4 / setup-buildx@v4 / build-push@v7 = Node 24. **Alle Actions
      Node-24-nativ → kein `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24` nötig.**

### 1. Connection / Projekt-Block — ✅ ERLEDIGT (Session 2026-06-12)
- [x] **Projekt-Löschen beschleunigt + Feedback.** ⚠️ **Korrektur der Root-Cause-Annahme:**
      `IX_KnxTelegrams_GroupAddressId` existiert bereits seit `InitialCreate` (EF FK-Index-
      Konvention) — der Index hat NIE gefehlt. Echte Ursache = EF-Cascade mit Change-Tracking.
      Fix umgesetzt: `ProjectRepository.DeleteProjectFastAsync` macht set-basiert
      `ExecuteUpdateAsync` (Telegramme: GA-Ref auf NULL, indexgestützt) + `ExecuteDeleteAsync`
      für Devices/GAs/Project — kein Tracking, keine geladenen Dependents. Telegramme bleiben
      erhalten (verlieren nur Namenszuordnung). **Synchron + UI-Spinner** (`deletingId`).
      **Bestätigungsdialog** (`ConfirmDialogComponent`) zeigt echte Zahlen via neuem
      `GET /api/projects/{id}/delete-preview` (GA-/Device-/Telegramm-Count). Hinweis: Dialog
      sagt korrekt „Telegramme bleiben erhalten" statt „löschen" (Design preserved Telegramme).
- [x] **Projekt deaktivieren.** `ProjectService.DeactivateProjectAsync` (IsActive=false →
      `cache.RefreshAsync` leert Cache → `knx.DisconnectAsync` mit ManualDisconnect-Latch).
      Endpoint `PUT /api/projects/{id}/deactivate`. Frontend: Aktiv-Toggle nicht mehr disabled,
      `toggleActivation` macht Activate+Deactivate. **Re-Aktivieren reconnectet** (ActivateAsync
      ruft `ConnectAsync` bei AutoConnect → clear Latch). **Live-View Empty-State**: neuer
      Checklist-Schritt „Activate a project" bei `hasProject && !hasActiveProject`.
- [x] **Connection-Test pausiert Live statt 2. Tunnel.** `TestConnectionAsync` merkt sich
      Live-Config, `CleanupBusConnection` (non-latching), probt, `ConnectAsync(liveConfig)` im
      `finally`. Neues `TestInProgress`-Flag hält Worker raus (kein Konkurrenz-Tunnel).
      **Settings-Test warnt + bestätigt** (`ConfirmDialogComponent`) wenn `/knx/status` connected.
- [x] **AutoConnect-Toggle am aktiven Projekt.** `GET`/`PUT /api/knx/autoconnect` auf effektiver
      Config (active/first). **Sofort wirksam**: an → `ConnectAsync` (clear Latch); aus →
      `DisconnectAsync` (Latch). Frontend: Toggle in der Aktiv-Zelle nur am aktiven Projekt.
      Worker überspringt zusätzlich `TestInProgress`.

### 2. Live + History → **eine Ansicht** — ✅ ERLEDIGT (Session 2026-06-12)
- [x] Neue `MonitorComponent` (`features/monitor/`) vereint beide Features. Segmented-Toggle
      Live/Archive im Header, **Default = Live**. Route `monitor`; alte `live-view`/`history`
      redirecten dorthin (Lesezeichen bleiben gültig). Nav (Top + Mobile) auf einen
      „Monitor"-Eintrag reduziert. Alte Komponenten bleiben unreferenziert liegen.
- [x] **Geteiltes Quick-Search** gilt in beiden Modi (live: Client-Filter über Buffer; archive:
      `q`-Query, debounced). Strukturierte Filter (Zeit/Typ/Topic/Adresse) nur im Archive-Mode.
      Mode-Wechsel rendert nur um — `LiveBufferService` (Singleton) läuft durch, keine Aufzeichnungs-
      lücke; Sort-Header nur im Archive (`gridColumns` strippt `sortable` im Live).
- [x] **Archiv: Keyset-Paging (pageSize 100) + Infinite-Scroll** (war in History bereits so;
      übernommen). Verifiziert: `ng build --configuration production` grün.

### 3. i18n — **voll DE/EN umschaltbar** — ✅ ERLEDIGT (Session 2026-06-12)
- [x] **Mechanik-Korrektur:** Statt `$localize` (kann gerendertes DOM nicht ohne Reload neu
      übersetzen — Widerspruch zur No-Reload-Vorgabe) → **signal-basierter `LanguageService`
      + impure `translate`-Pipe** mit In-Repo-Dictionaries (`core/i18n/`). Liest `lang`-Signal,
      schaltet sofort ohne Reload. **Default EN**, persistiert in `localStorage` (`knx.lang`).
- [x] **251 Keys × DE/EN** (502 Einträge), DE orthographisch korrekt (ä/ö/ü/ß). Sprachumschalter
      (EN/DE) im Header. Migriert: Layout/Nav, Monitor, Projects, Import-Wizard, Settings, Login,
      Initial-Setup, Telegram-Detail/Cards, Column-Manager, Confirm-Dialog-Defaults.
- [x] **Scope: nur Frontend-UI** (Backend-Meldungen englisch, wie vorgegeben). Dynamische Daten
      (Projekt-/GA-/Geräte-Namen, Werte) + Topic-Such-*Terms* bewusst untranslated.
      Verifiziert: `ng build --configuration production` grün. (Hinweis: tote `live-view`/`history`-
      Komponenten nicht migriert — sie sind unreferenziert/redirected, kein UI-Effekt.)

### 4. Phase-1-Features
- [x] **Zeitreihen-Charts — ✅ ERLEDIGT (Session 2026-06-12).** `ngx-echarts@20.0.2` + `echarts@6.1.0`
      (echarts lazy-loaded → main-Bundle bleibt unter Budget). Neue Route `charts`. Backend
      `GET /api/telegrams/series` (numerische Extraktion: führender Zahl-Token, sonst On/Off→1/0;
      Down-Sampling, Unit aus dem Decoded-String). Frontend: GA-Multiselect (gefiltert auf
      chartbare DPTs 1/5/6/7/8/9/12/13/14, max 8), Presets 1h/24h/7d/30d + Custom, **getrennte
      Y-Achsen je Einheit**, DPT1 als Step-Line, `dataZoom`, **Live-Append via SignalR** (500ms
      coalesced, 5000 Punkte/Serie). Verifiziert: beide Builds grün.
- [x] **Bus-Last & Statistik — ✅ ERLEDIGT.** **Live-Header-Badge** (msg/s gleitend + Bus-Last%
      relativ ~50 tel/s TP1) im `LiveBufferService` (Sliding-Window, 1s-Tick) → Monitor-Toolbar.
      **DB-Statistik-View** (Route `stats`): Total + Ø msg/s + Telegramme-über-Zeit-Histogramm via
      `GET /api/telegrams/stats`. Top-Talker + Typ-Verteilung bewusst Backlog.
- [x] **KNX Secure Runtime-Decryption — ✅ IMPLEMENTIERT (Code; live-Verifikation hardware-abhängig).**
      ⚠️ Korrektur meiner ersten Annahme: Falcon **hat** die Secure-API — ich hatte die falsche
      Connector-Klasse geprüft. **Data Secure:** `GroupCommunicationSecurity.Load(stream, SecureString)`
      → settable `KnxBus.GroupCommunicationSecurity` (vor `GroupMessageReceived`-Subscribe gesetzt)
      → Falcon ent-/verschlüsselt GA-Telegramme automatisch. **IP Secure Tunnel:** `IpTunneling­Connector­Parameters.LoadSecurityDataAsync(keyring, pw)` (konkret statt der abstrakten
      `IpUnicastConnectorParameters`), opt-in via neuer Spalte `KnxConfiguration.UseSecureTunnel`
      (default false → bestehender Pfad unverändert). **Persistenz:** neue Entity `ProjectKeyringBlob`
      (rohe .knxkeys-Bytes + Passwort) — bei Keyring-Upload + Import gespeichert; Migration
      `AddProjectKeyringBlob`. **Voll defensiv isoliert:** ohne Keyring/Blob exakt der bisherige
      Nicht-Secure-Pfad; jeder Secure-Schritt in try/catch → kann eine normale Verbindung nie brechen.
      Build + Tests grün. **Glue deterministisch verifiziert** (`SecureKeyringLoadTests`): `Load`
      akzeptiert gespeicherte Bytes+Passwort → valides Objekt; falsches Passwort wirft.
      ✅ **DATA-SECURE-LAUFZEIT-ENTSCHLÜSSELUNG END-TO-END VERIFIZIERT** (`SecureLoopbackTests`,
      ohne physisches Gateway): zwei In-Process-`KnxBus` auf localhost-KNX/IP-Routing-Multicast =
      echter Bus. Geteilter Per-GA-Key via `GroupCommunicationSecurityBuilder` (op_Implicit →
      `KnxBus.GroupCommunicationSecurity`, exakt meine Wiring-Pattern) in beide Busse → Sender
      **verschlüsselt automatisch**, übertragen, Empfänger **entschlüsselt automatisch** → Klartext
      „19.5 °C" wieder da. Das ist die tatsächliche Runtime-Decryption, nicht nur Glue/Compile.
      ⚠️ Verbleibend NICHT ohne Hardware prüfbar: nur noch der **IP-Secure-Tunnel-Handshake gegen ein
      spezifisches physisches Secure-Gateway** (Transport-Detail) + das „encrypted"-Badge-Verhalten
      undecryptbarer Telegramme. Der Kern („muss sein": GA-Telegramm-Laufzeit-Entschlüsselung) ist
      verifiziert. `// TODO(secure-hardware)` nur noch für den Tunnel-Handshake.
- [x] **GA schreiben/lesen aus UI — ✅ ERLEDIGT (Plumbing; Write braucht On-Device-Verifikation).**
      Falcon-API verifiziert: `KnxBus.WriteGroupValueAsync` / `RequestGroupValueAsync`,
      `DptBase.ToGroupValue(object)` (Inverse von `Format`). Backend: `DptConverter.Encode`
      (DPT1→bool, 9/14→double, 5/6/7/8/12/13→long, sonst String/Hex), `IKnxConnectionService`
      Write/Read + `POST /api/knx/write|read`. Frontend: **DPT-typisierte Eingabe nur im
      Detail-Drawer** (Toggle On/Off für DPT1, Zahl/Text sonst), **Read-Button**, **Bestätigungs-
      dialog** vor jedem Write. **Jeder eingeloggte User darf** (Login + Dialog als Gate). Builds +
      Tests grün. **DPT-Encoding deterministisch verifiziert** (`DptConverterRoundTripTests`, 16
      Tests): encode→decode-Round-Trip + **exakte Rohbyte-Assertions** gegen Falcons echten Codec
      (5.010→0x80, 7.001→0x1388, 4-Byte-Signed, Float-Toleranz, Raw-Hex, Soft-Fail) — der Bus trägt
      exakt diese Bytes. ✅ **END-TO-END ÜBER ECHTEN BUS VERIFIZIERT** (`SecureLoopbackTests`, ohne
      Gateway): `Encode("9.001","21.5")` → `WriteGroupValueAsync` über In-Process-KNX/IP-Routing-Bus
      → vom zweiten Bus empfangen → `Decode` ergibt „21.5 °C". write→transmit→receive→decode real
      belegt. Verbleibend hardware-abhängig: nur die Übertragung an ein spezifisches physisches
      Endgerät/Gateway.

### 5. Parser-Erweiterungen — ✅ ERLEDIGT (Session 2026-06-12)
- [x] **Keyring nachträglich hochladen.** `POST /api/projects/{id}/keyring` (multipart `file`+`password`)
      → `KeyringReader` entschlüsselt, neue Entity `ProjectKeyringKey` (GA-/Tool-/Backbone-Keys)
      ersetzt-für-Projekt gespeichert, **kein Re-Parse**. Frontend: „Upload keyring"-Button je
      Projektzeile (Dialog: .knxkeys + Passwort), Erfolg-Snackbar mit Counts. Legt Fundament für #4c.
- [x] **Locations + CommObjects parsen — alle ETS 4/5/6.** Library: neue DTOs `Location`/`ComObject`
      in `ParseResult`, `BaseProjectLoader.ParseLocationsAsync/ParseCommunicationObjectsAsync`
      (Id→Adresse-Maps zur Ref-Auflösung). **Gegen echte Fixtures verifiziert**: ETS4 Connectors-Stil
      (`test_project-ets4-no_password` → 5 Locations/5 CommObjects), ETS5/6 `@Links`-Stil + nested
      `Space` (`module-definition-test` → 3/24). **192 Parser-Tests grün** (187+5 neu).
- [x] **Persistenz + API.** Neue Core-Entities `Location`/`CommunicationObject`/`ProjectKeyringKey`
      (Cascade von Project, in `DeleteProjectFastAsync` aufgenommen), Migration
      `AddLocationsCommObjectsAndKeyring`, Adapter-Mapping, `GET /api/projects/{id}/locations` +
      `…/commobjects?address=`.
- [x] **UI:** (a) **Raum-Filter** im Archive-Filterbar (client-seitig über die GA-Links der Location —
      ⚠️ filtert nur die geladene Seite, da Backend-Query Einzeladresse erwartet; Räume ohne GAs
      ausgeblendet — bewusste Limitierung), (b) **Gebäude-Baum als eigene Route `topology`**
      (Stockwerk→Raum→Gerät/GA, Nav-Eintrag), (c) **CommObjects im Detail-Drawer** („Used by"-Liste
      der verknüpften Geräte-Objekte). i18n DE/EN. Beide Builds grün.
- Offene Detail-Notiz: `GroupAddressRef` direkt in Location + voller ComObject-Flag-Satz in keiner
  öffentlichen Fixture vorhanden → best-effort, im Code als unverifiziert markiert.

### 6. Quality-Cleanup — ✅ ERLEDIGT (Session 2026-06-12)
- [x] **Frontend-Logging:** `core/logging/logger.service.ts` (debug/info dev-only, warn/error immer).
      **29 `console.*` über 8 Files ersetzt** (signalr, import-polling, login, settings, monitor,
      live-view, projects, import-wizard). Einzige verbleibende `console.*`: im Logger selbst +
      `main.ts`-Bootstrap (vor DI, außerhalb scope). ⚠️ **ESLint `no-console` NICHT verdrahtet** —
      im Projekt ist kein ESLint konfiguriert; Policy steht als NOTE im Logger statt als Lint-Regel.
- [x] **Serilog-Templates:** bereits einheitlich (einziger Console-Sink in `Program.cs` mit
      `[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}`,
      keine appsettings/File-Sinks) → kein Edit nötig. Beide Builds grün.

---

## Entschieden → aus aktiver Planung raus

- **KNX-Marke im Produktnamen**: Disclaimer in README reicht. Keine formale Anfrage an KNX
  Association eingeplant. (B-Lizenz-Notices bereits erledigt.)

---

## Backlog (erfasst, nicht aktiv eingeplant)

### Features
- [ ] Live-Werte-Dashboard — letzter Wert je GA kompakt (Karten/Tabelle)
- [ ] Statistik-Erweiterung: **Top-Talker** + **Write/Read/Response-Verteilung** (in Phase-1
      bewusst nicht eingeplant; msg/s + Bus-Last% sind drin)
- [ ] Diff zweier Telegramme — Drawer existiert (`TelegramDetailComponent`), nur Vergleich offen
- [ ] Notifications — Alerts via Mail / Webhook / MQTT / Browser-Push
- [ ] Bus-Scan / Device-Discovery — Geräte am Bus ohne ETS finden (PhysAddr-Scan)
- [ ] Multi-Gateway — mehrere KNX-Interfaces gleichzeitig (Connection-Service ist Single-Instance
      → Architektur-Eingriff)

### Parser (Backlog)
- [ ] Topology parsen — Bereich→Linie→Gerät (Bus-Baum)
- [ ] Functions parsen — ETS-Funktionsgruppen

### Phase 2 — interessant, später
- [ ] Schwellwert-Regeln (Wert > X → Alert)
- [ ] Offline-Watchdog (GA/Gerät seit X min still → Warnung)
- [ ] Anomalie-Erkennung (auffällige Rate/Werte)
- [ ] MQTT-Bridge (Werte publishen → HA/ioBroker/Node-RED)
- [ ] Prometheus/InfluxDB-Export (Grafana)
- [ ] REST-API für aktuelle Werte (`/api/values`)
- [ ] Unbekannte GAs markieren/sammeln (nicht im aktiven Projekt vorhanden)

### UI / UX (Backlog)
- [ ] Filter-Chips + speicherbare Filter-Presets (Live View) — History hat bereits Topic-Chips
- [ ] Mobile-Ansicht systematisch optimieren (Card-View existiert, systematischer Pass offen)
- [ ] Accessibility-Audit (WCAG 2.1 Level AA)
- [ ] Live-View: msg/s-Rate-Anzeige neben "Live"-Status (gleitender Durchschnitt)

### Betrieb / Qualität (Backlog)
- [ ] Health-Check vertiefen — Basis-Liveness `/healthz` existiert; offen: readiness mit
      DB-/Bus-/Queue-Status
- [ ] API-Integration- und E2E-Tests (Cypress / Playwright)
- [ ] Frontend Unit-Test-Coverage ausbauen

---

## Erledigt (Referenz)

### Falcon SDK — Decoding (✅ Session 2026-06-11)
- [x] `DptConverter` durch Falcons DPT-Katalog ersetzt (`DptFactory.Default.Get(main,sub)` →
      `DptBase.Format(...)`). GroupValue direkt durchgereicht, typisiertes `GroupEventType`-Switch.
      Live gegen Gateway verifiziert. ~300 Zeilen Handcode entfernt.
- [x] Enum-Sprache gefixt: prozessweit `en` (`Program.cs`) + `CurrentUICulture`-Pin um `Format`.
      Zahlen invariant (Punkt).
- Offen (klein, Backlog): true per-UI-Locale-Formatierung historischer Werte bräuchte
  typisierten Wert-Storage statt fertigem String. Aktuell InvariantCulture persistiert.

### Lizenz-Compliance
- [x] (B) `THIRD-PARTY-NOTICES.md` angelegt (Falcon KNX-EULA). README-Lizenzabschnitt
      klargestellt (MIT deckt Falcon NICHT) + Marken-Disclaimer.
- [x] (C) Marken-Entscheidung → Disclaimer reicht (s.o. "aus aktiver Planung raus").

### Grid (Live + History) (✅)
- [x] Spaltenbreiten getunt; Overflow-Tooltip (`OverflowTitleDirective`); DPT/Rohwert/Wert
      rechtsbündig (`align`-Feld in `KnxColumn`).

### i18n-Fundament (✅)
- [x] UI auf Englisch umgestellt (alle Views). `@angular/localize` + `LOCALE_ID` für Zeitformat.

### Connection-Lifecycle — Dauerlauf (✅ größtenteils)
- [x] Auto-Connect (`KnxConfiguration.AutoConnect` Migration default true + `KnxAutoConnectWorker`,
      Poll 10s, respektiert `ManualDisconnect`-Latch).
- [x] Trennen/Verbinden-Buttons raus; UI zeigt nur Status via `/knx/status` + 5s-Poll.
- [x] `LiveBufferService` (Root-Singleton) hält Buffer + Connection über Tab-Wechsel.
- [x] Pause stash/flush (kein Verlust).
- [x] Connection-Test non-destruktiv (`TestConnectionAsync` temporärer Bus, fasst Live nicht an).
      → verfeinert in Roadmap #1 (kein 2. Tunnel wenn schon verbunden).
- [x] Settings-Änderung → Live-Reconnect (`UpdateConfiguration` non-latching `ConnectAsync`).

### Logging-Cleanup (teilweise)
- [x] `Console.WriteLine` aus `ProjectImportService` entfernt.
