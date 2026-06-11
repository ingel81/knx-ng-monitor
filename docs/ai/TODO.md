# TODO

Konsolidierter Stand: siehe [`PROJECT_PLAN.md`](PROJECT_PLAN.md) → "Nächste Schritte"
und das Highlights-Kapitel in [`../../README.md`](../../README.md) → "Project status".

## Stand nach Session 2026-06-11/12

**Design-Folgethemen quasi durch** — erledigt: Grid-Tuning + Tooltip + Rechtsbündig · UI englisch ·
Falcon-Decode inkl. englischer Enum-Labels · Connection-Redesign (Auto-Connect/Watchdog,
Status-only, Live-Buffer-Singleton, Pause ohne Verlust) · non-destruktiver Connection-Test ·
Settings→Live-Reconnect · Lizenz-Notices. Details in den ✅-Abschnitten unten.

**Offen aus den Folgethemen (brauchen Entscheidung):**
1. Sprachumschaltung (UI ist englisch, Multilang-Mechanik fehlt)
2. Live + History zu einer Ansicht mergen (vorher beraten)
3. KNX-Marke im Produktnamen (Disclaimer gesetzt, formale Anfrage = Maintainer-Entscheidung)

**Nächster großer Brocken:** Feature-Backlog Phase 1 (unten).

## Falcon SDK — Decoding & Lizenz

### Decoding auf Falcon umstellen (A) — ✅ ERLEDIGT (Session 2026-06-11)
- [x] `DptConverter` durch Falcons DPT-Katalog ersetzt. API: `DptFactory.Default.Get(main,sub)`
      → `DptBase.Format(groupValue, null, CultureInfo.Invariant)`. **GroupValue vom Bus direkt
      durchgereicht** (1-bit-Shortvalues sonst „Expected 1 bit"-Fehler). EventType jetzt
      typisiertes `GroupEventType`-Switch. Live gegen echtes Gateway verifiziert.
  - Gewinn bestätigt: 5.010 → „128 counter pulses" (≠ %), 20.102 → „Comfort", 14.056 → „42.69 W",
    232.600 → „#FF8000", locale-aware. ~300 Zeilen Handcode entfernt.
  - Frontend-Zeitformat (`knx-table`, `telegram-detail`, `telegram-cards`) nutzt jetzt `LOCALE_ID`.
  - [x] **Enum-Sprache gefixt**: Falcons DPT-Labels (On/Off, Open/Close, Active/Inactive) folgen
    `CurrentUICulture` → auf deutschem OS deutsch. Jetzt prozessweit `en` (`Program.cs`) +
    `CurrentUICulture`-Pin um `Format` (`DptConverter`). Zahlen bleiben invariant (Punkt).
    Live verifiziert: neue Telegramme englisch. (Alt-Zeilen aus Test-Runs bleiben deutsch in DB,
    vernachlässigbar — Re-Decode-Backfill möglich, aber unnötig.)
  - Offen (klein): true per-UI-Locale-Formatierung historischer Werte bräuchte typisierten
    Wert-Storage (statt fertigem String). Aktuell InvariantCulture persistiert.

### Lizenz-Compliance (B/C)
- [x] (B) `THIRD-PARTY-NOTICES.md` angelegt (Falcon KNX-EULA, proprietär, Redistribution-Terms).
      README-Lizenzabschnitt klargestellt (MIT deckt Falcon NICHT ab) + Marken-Disclaimer.
- [ ] (C) Marken-Entscheidung „KNX" im Produktnamen/Logo — Disclaimer in README gesetzt;
      ggf. noch formale Anfrage an KNX Association (offene Entscheidung des Maintainers).

## Design-Überarbeitung — Folge-Themen (v0.3.x)

### Grid (Live + History) — ✅ ERLEDIGT
- [x] Spaltenbreiten getunt (Zeit/Quelle/Ziel breiter, Name grow 2→1.4); Live + History.
- [x] Overflow-Tooltip via `OverflowTitleDirective` (`title` nur bei `scrollWidth>clientWidth`).
- [x] `DPT`/`Rohwert`/`Wert` rechtsbündig (`align`-Feld in `KnxColumn` + CSS).

### i18n / Sprache
- [x] UI auf **Englisch** umgestellt (Live, History, Settings, Projects, Import-Wizard, Nav,
      Grid, Detail-Drawer, Cards, Snackbars).
- [ ] Mehrsprachigkeit + **Sprachumschaltung** noch offen. `@angular/localize` installiert +
      `LOCALE_ID` wird für Zeitformat schon genutzt → Fundament steht. Strings zentralisieren.

### Locale-Formatierung
- [x] Wert-Decode locale-fähig über Falcon (an der Quelle). Frontend-Zeitformat folgt `LOCALE_ID`.
- [ ] Count-Badges (`| number`) folgen Angular-Default-Locale (en-US) — bei Sprachumschaltung
      automatisch korrekt; bis dahin konsistent englisch. Kein separater Task mehr nötig.

### Connection-Lifecycle — Redesign auf Dauerlauf — ✅ GRÖSSTENTEILS ERLEDIGT
- [x] Auto-Connect: `KnxConfiguration.AutoConnect` (Migration, Default true) +
      `KnxAutoConnectWorker` (BackgroundService) — verbindet bei Start/Verbindungsverlust,
      Poll 10s, respektiert `ManualDisconnect`-Latch. Live gegen Gateway verifiziert.
- [x] `Trennen`/`Verbinden`-Buttons raus; UI zeigt nur Status (Live/Paused/Connecting/
      Reconnecting) via `/knx/status` (State-Enum) + 5s-Poll.
- [x] Live-View nicht mehr leer beim Tab-Wechsel: `LiveBufferService` (Root-Singleton) hält
      Buffer + Connection; `stopConnection` NICHT mehr bei Destroy.
- [x] Pause verwirft nicht mehr: stash während Pause, Flush bei Resume (`LiveBufferService`).
- [x] **Connection-Test non-destruktiv**: `POST /api/knx/test-connection` →
      `TestConnectionAsync` öffnet temporären `KnxBus`, prüft, disposed — fasst Live-Verbindung,
      State und `ManualDisconnect`-Latch NICHT an. (Vorher killte Test via connect+disconnect
      den Dauerlauf.) Settings-Test nutzt nur noch diesen Probe.
- [x] **Settings-Änderung → Live-Reconnect**: ändert sich IP/Port/Typ und Link ist aktiv,
      reconnectet `UpdateConfiguration` die laufende Verbindung auf die neue Config
      (non-latching `ConnectAsync`).
- [ ] Live + History zu **einer** Ansicht zusammenführen — bewusst offen (braucht Konzept-
      Entscheidung, vorher beraten).

## Feature-Backlog (priorisiert nach Review v0.3.x)

### Phase 1 — gewünscht
- [ ] **Live-Werte-Dashboard** — letzter Wert je GA kompakt (Karten/Tabelle), statt nur Stream
- [ ] **Zeitreihen-Charts** — Trendgraph numerischer DPTs aus History-Archiv (Klick GA → Kurve)
- [ ] **Bus-Last & Statistik** — Telegramme/s, Top-Talker, Write/Read/Response-Verteilung
- [ ] **Diff zweier Zeitpunkte** — Detail-Drawer selbst ist **bereits umgesetzt**
      (`TelegramDetailService` + `TelegramDetailComponent`, in Live + History verdrahtet).
      Offen nur noch der Vergleich/Diff zweier Telegramme.
- [ ] **Notifications** — Alerts via E-Mail / Webhook / MQTT / Browser-Push
- [ ] **GA schreiben/lesen aus UI** — aktiv Telegramme senden (Write/Read-Request).
      ⚠️ schreibt auf echten Bus → Bestätigung/Guard-Rails nötig.
      (Backend: `KnxController` hat aktuell kein write/read — neuer Endpoint + `KnxBus`-Send nötig.)
- [ ] **KNX Secure Runtime-Decryption** — *muss sein*; Laufzeit-Entschlüsselung mit gespeicherten
      ToolKeys/GA-Keys. → kanonisch erfasst unter „Parser / KNX Secure" (war hier doppelt).
- [ ] **Bus-Scan / Device-Discovery** — Geräte am Bus ohne ETS finden (PhysAddr-Scan)
- [ ] **Multi-Gateway** — mehrere KNX-Interfaces gleichzeitig (Connection-Service ist aktuell
      Single-Instance → Architektur-Eingriff)

### Phase 2 — interessant, später
- [ ] Schwellwert-Regeln (Wert > X → Alert)
- [ ] Offline-Watchdog (GA/Gerät seit X min still → Warnung)
- [ ] Anomalie-Erkennung (auffällige Rate/Werte)
- [ ] MQTT-Bridge (Werte publishen → HA/ioBroker/Node-RED)
- [ ] Prometheus/InfluxDB-Export (Grafana)
- [ ] REST-API für aktuelle Werte (`/api/values`)
- [ ] Unbekannte GAs markieren/sammeln (nicht im aktiven Projekt vorhanden)

## Kurz

### Parser / KNX Secure
- [ ] Telegramm-Decryption zur Laufzeit (KNX Data Secure am Bus, mit den
      gespeicherten ToolKeys / GA-Keys aus dem Keyring)
- [ ] Communication Objects, Topology, Locations, Functions parsen
- [ ] Frontend: Keyring nachträglich pro Projekt hochladen / aktualisieren

### UI / UX
- [ ] Filter-Chips + speicherbare Filter-Presets (Live View) — History hat bereits
      Topic-Chips, Live View nur Plain-Filter.
- [x] ~~Telegramm-Detail-Drawer~~ — umgesetzt (`TelegramDetailComponent`). Vergleich/Diff
      offen → s. „Diff zweier Zeitpunkte" im Feature-Backlog.
- [ ] Mobile-Ansicht systematisch optimieren (Card-View existiert, systematischer Pass offen)
- [ ] Accessibility-Audit (WCAG 2.1 Level AA)

### Betrieb / Qualität
- [ ] Health-Check vertiefen — Basis-Liveness `/healthz` **existiert bereits** (Program.cs).
      Offen: echter Health mit DB-/Bus-/Queue-Status (readiness).
- [ ] Logging-Cleanup
  - Backend: Serilog-Templates vereinheitlichen
  - [x] ~~`Console.WriteLine` aus `ProjectImportService`~~ — erledigt (keine mehr vorhanden)
  - Frontend: `console.log/debug`-Statements aufräumen (~27 über 8 Files, u.a.
    `signalr.service`, `live-view`, `import-wizard`)
- [ ] API-Integration- und E2E-Tests (Cypress / Playwright)
- [ ] Frontend Unit-Test-Coverage ausbauen
