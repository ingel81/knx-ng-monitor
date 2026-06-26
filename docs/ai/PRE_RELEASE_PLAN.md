# Pre-Release-Plan — KNX-NG-Monitor (öffentlicher Release)

Stand: 2026-06-26. Ergebnis der Untersuchung (6 parallele Read-only-Agents) über die 9 gesammelten Punkte.
**Status: Untersuchung + Planung. Noch nichts implementiert — wartet auf GO.**

Legende Priorität: 🔴 Release-Blocker · 🟠 Hoch · 🟡 Mittel · ⚪ Niedrig

---

## Entscheidungen (2026-06-26, per AUQ geklärt) — verbindlich

| Thema | Entscheidung |
|---|---|
| **Scope** | **Alles** — Phase 1-6 komplett |
| **Re-Import (P6/7)** | **A+B2 voll**: `EtsProjectId` (P-XXXX) persistieren + Projekt matchen + GAs per Adresse **in-place** updaten → durchgängige History+Namen |
| **Auth (P9)** | **JWT behalten + härten, voller Umfang**: Login-Rate-Limit, Refresh-Token hashen, Reuse-Detection, Prune, Logout-überall. Kein Cookie-Umbau. |
| **Plattform (P4)** | **arm64-Docker JA** (Pi kann pullen). **musl/Alpine NEIN**. |
| **Logging (P2)** | **Voll** + **UI-Log-Viewer** (live via SignalR, Level-/Text-Filter, Suche) + **Diagnose-Zip-Download**. Rolling-File unter data/logs, Env-Log-Level. |
| **Data-Dir (P5)** | **Nur `AppContext.BaseDirectory`** (neben Exe). Docker unverändert `/app/data` (WORKDIR fix). |
| **README (P3)** | **3-fach Split**: `README.md` (Enduser) + `DEVELOPMENT.md` (Entwickler) + `CHANGELOG.md`. **LICENSE-File anlegen.** |
| **Security** | **Startup-Fail-fast + `/healthz`-DB-Probe JA.** HTTPS-Doc nicht explizit gewählt (fließt in README-Security-Hinweis). |
| **DB-Auto-Backup** | **NEIN** (bewusst weggelassen → Migration läuft direkt auf Live-DB; Restrisiko bei fehlerhaftem Upgrade akzeptiert). |
| **Release-Extras** | **Alle drei**: SHA256SUMS + Version-embed (`/api/version`) + Upload-Size-Limit/Job-Cap. |

**Neues Feature aus AUQ:** UI-Log-Viewer (live, filterbar, suchbar) — Ring-Buffer-Sink + SignalR-Push + Viewer-Seite. Über das ursprüngliche Logging-Konzept hinaus.

---

## Umsetzungs-Status (2026-06-26) — implementiert + verifiziert

| Punkt | Status | Verifikation |
|---|---|---|
| LICENSE | ✅ MIT-File angelegt | README-Badge verweist auf ./LICENSE |
| Data-Dir-Fix | ✅ `AppPaths` (BaseDirectory/data), Connection + JWT + Logs verankert | Smoke: fresh DB neben Exe gebootet |
| Logging | ✅ Rolling-File `./data/logs`, `KNX_LOG_LEVEL`, Buffer-Sink | Smoke: /api/diagnostics/logs liefert Live-Buffer |
| UI-Log-Viewer + Download | ✅ LogHub + LogBroadcastWorker + DiagnosticsController + Angular logs-Feature + Settings-Button | Frontend prod build grün; /hubs/logs gemappt |
| healthz-DB-Probe + Fail-fast | ✅ healthz prüft `CanConnectAsync`, Init wirft jetzt | Smoke: healthz 200; Init-Fehler bricht ab |
| Login-Rate-Limit | ✅ FixedWindow 10/min auf /login + /refresh | Smoke: 10×401 dann 429 |
| Auth-Härtung | ✅ Refresh-Token SHA-256-gehasht, Reuse-Detection, Prune-Service, logout-all | Smoke: login/refresh/logout-all 200 |
| Upload-Limit | ✅ 200 MB Body + Multipart-Limit | Build |
| Version-embed | ✅ `<Version>0.7.0</Version>` + `/api/version`, CI `-p:Version` | Smoke: /api/version → 0.7.0+commit |
| Re-Import (A+B2) | ✅ `Project.EtsProjectId` + Migration + `MergeReimportAsync` (GA in-place by Address) + Import-Branch | Build + Migration bootet; **Runtime-Re-Import nicht end-to-end getestet** (braucht 2. .knxproj-Version) |
| Plattform arm64-Docker | ✅ buildx amd64+arm64 + QEMU; **musl=nein** | CI-Edit (nicht ausführbar hier) |
| SHA256SUMS | ✅ Release-Step | CI-Edit |
| README-Split | ✅ README (Enduser) + DEVELOPMENT.md + CHANGELOG.md, Troubleshooting, Beta-CTA, Testzahl 206, todo.md weg | — |
| Frontend-Review-Fixes | ✅ Interceptor clearSession, SignalR-Leak, logout-everywhere | Frontend build grün |
| Tests | ✅ 206 Parser-Tests grün | dotnet test |

**Offen / bewusst nicht gemacht:** DB-Auto-Backup vor Migrate (vom User abgewählt); HTTPS-Doc (nur README-Hinweis); CSV-Formula-Injection (⚪ niedrig, optional). Bestehende Klartext-Refresh-Tokens werden nach Deploy ungültig → Re-Login (durch Token-Hashing). CI-Änderungen erst beim nächsten Tag real getestet (QEMU-arm64-Build läuft emuliert = langsam).

---

## Executive Summary — die wichtigsten Befunde

1. 🔴 **Kein `LICENSE`-File** trotz MIT-Badge + License-Sektion → GitHub-Lizenzerkennung + Badge-Link kaputt. Trivial-Fix.
2. 🔴 **`./data`-Pfad ist cwd-relativ** (DB + `.jwt-secret`). Portable-Binary aus anderem Arbeitsverzeichnis gestartet → neues `data/` woanders → wirkt wie „frischer Setup" / Datenverlust. Das ist Punkt 5 („DB-File stimmig") — realer Bug.
3. 🟠 **arm64-Docker-Manifest fehlt** — Dockerfile kann arm64, aber Pipeline baut nur `linux/amd64`. Raspberry Pi kann das Image **nicht** `docker pull`en (Binär-Tarball für linux-arm64 existiert aber).
4. 🟠 **Re-Import eines aktualisierten ETS-Projekts** verliert die History-Verknüpfung (Punkt 6/7): jeder Import = neue Projekt-ID + neue GA-Rows; alte Telegramme bleiben als Rohdaten erhalten, verlieren aber Name/DPT-Anreicherung. Stabiler Schlüssel (`P-XXXX`) wird geparst, aber nicht gespeichert.
5. 🟠 **Logging nur Console** — kein File-Sink. Endkunde kann heute keine Logs einsammeln (Punkt 2).
6. 🟠 **Kein Brute-Force-Schutz** auf `/login`, **HTTP per Default** (Klartext-Passwörter im LAN).
7. **Auth (JWT):** solide gebaut, aber Tokens in `localStorage` + Refresh-Tokens plaintext in DB. Empfehlung: **JWT behalten + härten**, nicht auf Cookies migrieren (HTTP-LAN macht Secure-Cookies wertlos).

---

## Punkt 1 — Release-Readiness-Check
Sammelpunkt; konkrete Blocker siehe unten. Minimal-Set für „kann raus":
- 🔴 LICENSE-File anlegen (#3-Doku).
- 🔴 `./data`-Pfad fixen (#5).
- 🟠 arm64-Docker (#4).
- 🟠 Brute-Force + HTTPS-Doku (#9/Review).
- 🟠 Endkunden-Logging (#2).
Rest (History-Reimport, Tool-Review-Mediums) kann v0.8 / Folge-Release.

---

## Punkt 2 — Endkunden-Logging (portable + Docker)
**Ist:** Serilog, **nur Console-Sink** (`Program.cs:27-34`). Kein File-Sink-Package. `appsettings`-LogLevel-Blöcke sind toter Config (Serilog liest sie nicht). Portable: stdout im Fenster (weg beim Schließen). Docker: `docker logs`. Nichts auf Platte. Eine Streu-`Console.WriteLine` in `JwtSecretManager.cs:72` (umgeht Serilog). **CLAUDE.md-Notiz „Console.WriteLine in ProjectImportService" ist stale — dort bereits ILogger.**
**Plan (~2 Tage):**
- A) `Serilog.Sinks.File` → Rolling-File unter `./data/logs/knxmonitor-.log` (täglich, 14 Tage, shared). Data-Dir wie `TelegramArchiveService.ResolveArchiveDir` auflösen → liegt auf bestehendem Docker-Volume + neben portable-exe. (~0.5 d)
- B) Log-Level per Env-Var `KNX_LOG_LEVEL` (`LoggingLevelSwitch`). (~0.25 d)
- C) `DiagnosticsController` `GET /api/diagnostics/download` → Zip (Logs + system-info.json), Button auf Settings-Seite. One-Click „Zip schicken". (~1 d)
- D) `JwtSecretManager.cs:72` → `ILogger.LogWarning`. (~15 min)
**Abhängigkeit:** A baut auf #5 (Data-Dir-Auflösung) auf — zusammen machen.

---

## Punkt 3 — README splitten (Enduser / Entwickler)
**Ist:** `README.md` 535 Zeilen, gemischt. 150-Zeilen-Changelog dominiert. Entwickler-Teile (Repo-Layout, Development, DB-Schema, API) verstreut. **Kein `LICENSE`, kein `CONTRIBUTING.md`. Leere `todo.md` (0 B) getrackt. Testzahl widersprüchlich (192 vs 187).**
**Plan (~0.5 Tag):**
- 🔴 `LICENSE` (MIT) anlegen — einziger harter Blocker hier. (~5 min)
- `README.md` = Enduser-Landing: Hero/Screenshots, Intro-Video einbetten, Features, Quick-Start (Docker/Binary), First-Setup, ETS-Import, Config, **NEU Troubleshooting/Logs**, **NEU Beta-CTA (Foren/Issues)**, Status, kurzer „Latest Release"-Blurb.
- `DEVELOPMENT.md` = Dev-Guide: Repo-Layout, Build, Tests, CLI-Tool, Migrations, DB-Schema, API, ausgebautes Contributing, Release-Prozess (Link RELEASE_PLAN).
- `CHANGELOG.md` = Changelog aus README (357-507) ausgelagert.
- Cleanup: `todo.md` löschen, Testzahl vereinheitlichen, `.orig`-Reste weg.

---

## Punkt 4 — Release-Prozess + Versionierung + Plattform-Abdeckung
**Ist:** 6 Binär-RIDs (`linux-x64/arm64`, `win-x64/arm64`, `osx-x64/arm64`) — ARM/Pi-Binary ✅. Docker **nur amd64** (`release.yml:134`). Version **nirgends embedded** (csproj default 1.0.0.0, frontend package.json 0.0.0); Tag nur Label, README ist Versions-Quelle. Kein Checksum, kein Signing. Trimming aus (korrekt wegen Falcon `Assembly.Location`). Release-Notes automatisiert.
**Plan:**
- 🟠 arm64-Docker: `platforms: linux/amd64,linux/arm64` + `setup-qemu-action`. (~1 h) — größter Nutzen.
- 🟡 `SHA256SUMS`-Step + an Release anhängen. (~30 min)
- 🟡 Version embedden: `-p:Version=${VERSION#v}` in publish + Docker; optional `/api/version`. (~1 h)
- ⚪ musl-RIDs (Alpine) `linux-musl-x64/arm64` falls gewünscht. (~2-3 h)
- ⚪ 32-bit `linux-arm` nur falls alte Pis Zielgruppe; sonst „64-bit OS required" dokumentieren.

---

## Punkt 5 — DB-File-Stimmigkeit
**Ist (BUG):** `appsettings.json:11` `Data Source=./data/knxmonitor.db` + `JwtSecretManager` default `"./data"` sind **relativ zum Arbeitsverzeichnis**, nicht zur Exe. Portable-Start aus anderem cwd → DB + JWT-Secret an falscher Stelle → „frischer" Setup, scheinbarer Datenverlust. Auto-Migrate auf User-DB ohne Backup (`DbInitializer.cs:12`).
**Plan (~0.5 Tag):**
- 🔴/🟠 Data-Dir gegen `AppContext.BaseDirectory` auflösen (oder konfigurierbarer Absolutpfad/Env `KNX_DATA_DIR`). Docker unberührt (absolutes `/app/data`).
- 🟡 Vor `MigrateAsync` automatisches Timestamp-Backup der DB. (~1-2 h)
- Doku: wo Daten liegen, wie sichern.

---

## Punkt 6 + 7 — Projektimport-Update ohne ID-/History-Verlust
**Ist:** Telegramme **nicht** direkt an Projekt gebunden — nur `KnxTelegram.GroupAddressId?` (nullable, `OnDelete=SetNull`) → `GroupAddress.ProjectId` (cascade). Jeder Import = **neue Projekt-Row + neue GA-Rows**; kein „Update bestehendes Projekt". Telegramm-Queries filtern nach **Roh-`DestinationAddress`**, nicht nach Projekt → alte Telegramme bleiben sichtbar, **verlieren aber Name/DPT** (alter/gelöschter GA-Pointer nicht im aktiven Cache). Stabiler Schlüssel **`P-XXXX`** existiert: `ProjectFeatures.ProjectId` (`FeatureDetector.cs:83`) — **wird geparst, aber nicht persistiert**.
**Plan (Option A+B2 empfohlen, ~1-2 Tage):**
- A) `Project.EtsProjectId` (string) + Migration; in `ContinueImportAsync` aus `features.ProjectId` füllen. Import: bestehendes Projekt per `EtsProjectId` matchen → **gleiche Projekt-ID wiederverwenden**.
- B2) Eingehende GAs per `Address` auf bestehende GA-Rows matchen → **update-in-place** statt delete+insert → `GroupAddressId` alter Telegramme bleibt gültig → History + Namen durchgängig.
- Alternative B1 (kleiner): nach jedem Import/Activate set-based `ExecuteUpdate` Telegramme↔aktive GAs per `Address` re-linken (heilt nur Namens-Anzeige). 
- Edge: GAs die im Update entfallen — behalten (soft) vs löschen entscheiden.

---

## Punkt 8 — Komplett-Tool-Review (Ergebnis 4+ Agents)
🟠 **Hoch:**
- Kein Brute-Force-Schutz/Lockout auf `/login` + `/refresh` (`AuthController.cs:21,42`) → RateLimiter.
- Startup-DB-Init in `try/catch` verschluckt (`Program.cs:224-228`) + `/healthz` prüft DB nicht (`:259`) → Container „healthy" trotz kaputter DB. Fail-fast **oder** healthz DB-Probe.
- Tokens in `localStorage` (`auth.service.ts:69-94`) — XSS-Exfiltration.
🟡 **Mittel:** kein TLS-Default (Klartext); kein Upload-Size-Limit + unbounded Import-Jobs (`ProjectsController.cs:40`, `ProjectImportService.cs:34`) → Memory-DoS; `./data` cwd-relativ (=#5); Auto-Migrate ohne Backup; Refresh-Tokens plaintext + nie gepruned; SignalR-Retry leakt Connections (`signalr.service.ts:33-55`); Refresh-Fail clært Auth-State nicht (`auth.interceptor.ts:42`); `LoggerService` Dev-Gating totes Code (importiert prod-env).
⚪ **Niedrig:** CSV-Formula-Injection im Export (`TelegramsController.cs:165`); `access_token` in SignalR-Query (Log-Leak); JWT-Secret keine Windows-ACL; `/auth/needs-setup` unauth (Info-Disclosure, gewollt für First-Run).
✅ **OK:** kein Zip-Slip (in-memory), kein SQL-Injection (parametrisiert), CORS nur Dev, Hub `[Authorize]`, KNX-Bus sauber disposed, Docker non-root + healthcheck, prod-API relativ `/api`.

---

## Punkt 9 — JWT vs Cookie-Auth
**Empfehlung: JWT behalten + härten. NICHT auf Cookies migrieren.** Grund: Produkt läuft oft **HTTP im LAN** → `Secure`-Cookies unmöglich, Hauptvorteil (HttpOnly gegen XSS) entfällt; Cookie-Migration trifft Issuance/Validation/SignalR/Interceptor/Guard + Dev-Workflow (cross-origin :4200→:8080). Threat-Model = self-hosted LAN, oft Single-User → XSS ist Hauptrisiko, das auch ohne Cookie-Rewrite mitigierbar.
**Härtungs-Liste (~0.5-1.5 Tage):**
1. Refresh-Tokens **gehasht** speichern (SHA-256). (1-2 h)
2. Refresh-Reuse-Detection → bei revoked-Token ganze Token-Familie killen (`RevokeAllTokensAsync` existiert). (1-2 h)
3. „Logout überall" / Password-Change invalidiert Sessions — `RevokeAllTokensAsync` an Endpoint hängen. (1 h)
4. Expired/revoked Tokens prunen (HostedService wie `ImportJobCleanupService`). (1 h)
5. HTTPS dokumentieren/empfehlen, HSTS wenn HTTPS. (30 min)
6. Login-Rate-Limit/Lockout (= Review-Hoch #1). (1-2 h)
7. Optional/Hybrid (erst wenn HTTPS Default): Access-Token in-memory + Refresh in HttpOnly-Cookie.

---

## Empfohlene Reihenfolge (Vorschlag)

**Phase 1 — Release-Blocker (≈1 Tag):** LICENSE, `./data`-Pfad-Fix + DB-Backup, README-Split-Minimum, Testzahl/todo.md-Cleanup.
**Phase 2 — Plattform + Sicherheit-Hoch (≈1.5 Tage):** arm64-Docker, Checksums, Version-Embed, Login-Rate-Limit, Startup-Fail-fast/healthz-DB-Probe, HTTPS-Doku.
**Phase 3 — Endkunden-Logging (≈2 Tage):** File-Sink + Env-Level + Diagnostics-Download.
**Phase 4 — Auth-Härtung (≈1 Tag):** Refresh-Hash, Reuse-Detection, Prune, Logout-überall.
**Phase 5 — History-erhaltender Re-Import (≈1-2 Tage):** EtsProjectId + Match-in-place (A+B2).
**Phase 6 — Doku-Feinschliff + Mittel/Niedrig-Review-Items.**

Gesamt grob **7-9 Tage** für „ultra perfekt mit allem". Phasen 1-2 = echte Release-Voraussetzung.
