# Session-Handover — KNX-NG-Monitor (Stand 2026-06-28)

Vollständiger Zustand zum Session-Reset. Alles Wichtige hier, damit ohne Chat-Kontext weitergearbeitet werden kann.

---

## 0. TL;DR
- **Pre-Release-Härtung umgesetzt + committet** (`748ac80`). Plan: `docs/ai/PRE_RELEASE_PLAN.md`.
- **Alle README/GitHub-Screenshots neu** aus frischer (recovered) Prod-DB — **uncommitted** im Working-Tree.
- **Intro-Video mit Voice-Over** fertig: neue Stimme, ausführliches DE-Skript, Musik geduckt. Liegt als `docs/screenshots/edit/final.mp4` + Kopie `G:\Meine Ablage\temp\knx-ng-monitor-intro.mp4` (239 s).
- **⚠️ Die gelieferte Prod-DB war korrupt** — recovered. User muss seine echte DB prüfen.

---

## 1. Laufende Services (für Screenshots/Tests; neu starten nach Reset)
- **Backend** (aktueller Code, Dev): `cd backend/KnxMonitor.Api && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://+:8080 dotnet run -c Debug`
  - DB liegt **fix neben der Exe**: `backend/KnxMonitor.Api/bin/Debug/net9.0/data/knxmonitor.db` (durch `AppPaths`, cwd-unabhängig).
  - War mit echtem KNX-Bus verbunden (192.168.10.60, Tunneling) — Live-Telegramme flossen.
- **Frontend**: `cd frontend && npx ng serve --port 4200` (ruft API :8080, CORS in Dev).
- **Login**: `demo` / `demo12345` (manuell in die DB eingefügt; echter User `joerg` Passwort unbekannt).
- Beim Stoppen: `powershell "Get-Process -Name dotnet,KnxMonitor.Api -EA SilentlyContinue | Stop-Process -Force"` (mehrere Zombie-dotnet vermeiden → sonst „attempt to write a readonly database"/Port-Konflikte).

## 2. ⚠️ DB-Korruption + Recovery (wichtig)
- Gelieferte `publish/data/knxmonitor.db` war **malformed** (`PRAGMA integrity_check` → „Tree 12 … Rowid out of order", Index-Rows fehlen). Ursache: vermutlich Live-Kopie ohne Checkpoint (torn image).
- **Recovery**: `sqlite3 publish/data/knxmonitor.db ".dump" > dump.sql` → `sqlite3 recovered.db < dump.sql` (dieses sqlite3 3.44.4 hat **kein** `.recover`, aber `.dump`). Ergebnis: integrity ok, **237.288 / 237.341 Telegramme** gerettet (~53 korrupte verworfen). Recovered DB ist jetzt die Arbeits-DB im bin-Ordner.
- **TODO für User**: echte Prod-DB prüfen (`PRAGMA integrity_check;`); sauberes Backup via App-Stop + Copy **oder** `VACUUM INTO 'backup.db'`.
- Beim DB-Tausch: **immer App stoppen, alte `-wal`/`-shm` löschen**, nur konsistente `.db` kopieren (sonst überschreibt altes WAL die neue DB).

## 3. Intro-Video — Voice-Over-Pipeline
Alles in `docs/screenshots/edit/`.

**Stimme:** ElevenLabs Voice-ID **`dpsgxAAQscpwOkeRSVZr`**, Modell `eleven_multilingual_v2`, settings `stability 0.45, similarity 0.75, style 0`.
- Vorher getestet: Will (`bIHbv24MWmeRgasZH58o`) — verworfen. KNX-Aussprache: **plain** schreiben („KNX-NG", „KNX-Bus") = klingt am besten (nicht „K N X" mit Leerzeichen = zu langsam).
- **API-Key**: `~/.claude/skills/video-use/.env` → `ELEVENLABS_API_KEY` (account-weit, gilt auch für TTS). Tier creator, 131k Zeichen/Monat.

**VO-Generator:** `docs/screenshots/edit/tts_vo.mjs` (Node, UTF-8-safe — nicht via bash-curl wegen Umlaut-Encoding!). Enthält das **17-zeilige DE-Skript** (ausführlich/erklärend, nicht nur Slide-Text). Output: `edit/vo/s00.mp3 … s16.mp3`.
- Skript-Stil-Vorgabe des Users: „ausführlicher und erklärender, nicht nur blankes Ablesen der Slide-Texte".

**Build:** `docs/screenshots/edit/build.py`
- Beat-Dauern **VO-getaktet**: `dur = vo_len + LEAD(0.4) + TRAIL(1.1)`, Floor 4.5.
- Video-Clips (s01 Desktop-Live, s12 Mobile-Live) werden **geloopt** (`-stream_loop -1`), damit sie über lange VO-Beats weiterlaufen (kein Standbild).
- Screenshots **statisch** (0 Shimmer); **Karten** mit sanftem Ken-Burns (4x-Supersample, `SS=4`, `ZOOM=0.030`).
- Audio: VO-Clips per `adelay` auf Timeline → `amix(normalize=0)`; Musik `track03.mp3` (`-stream_loop -1`) `volume=MUSVOL` dann **Sidechain-Ducking** unter VO.
- **Aktuelle Pegel-Settings (User: „Musik war zu laut"):** `MUSVOL=0.13`, `sidechaincompress=threshold=0.04:ratio=14:attack=5:release=400`. Ergebnis Gesamt-Mean ~-31,5 dB, VO vorn.
- `CRF=16`, `T=0.5` xfade. Gesamt aktuell **239 s**.
- Rebuild: `python build.py` (Python = `python`, hat PIL; **nicht** `python3`). Karten/Labels vorher bei Textänderung: `python generate_assets.py`.

**Iteration Musik leiser:** `MUSVOL` senken / Ducking-`threshold` hoch + `ratio` hoch → rebuild → `ffmpeg -i final.mp4 -af volumedetect -f null -` prüfen → nach `G:` kopieren.

**Cards/Labels-Texte:** `generate_assets.py` (label()/close_card()/platform_card()/secure). Regeln: **keine em-dashes (—) → „-"**; „·" ok. Label-BG unten abgedunkelt (`grad_h=460`, alpha-coef 242) für Lesbarkeit.

**Musik:** `track01/02/03.mp3` in edit/ (track03 = aktuell, 180 s). ACE-Step-Prompt für Musik steht im Chat-Verlauf (ambient tech, minimal, ~150 s, instrumental).

## 4. Screenshots-Pipeline (README/GitHub-Galerie)
- **Capture-Script:** `docs/screenshots/edit/gallery.mjs` (Playwright via `scratchpad/shotter/node_modules` pw-core 1.61.1 ↔ Cache chromium-1228; falls weg: `npm i playwright-core@latest` mit `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1`).
  - Desktop 1920×1080@DPR2 = 3840×2160; Mobile 390×844@DPR2 = 780×1688.
  - **Live-Screens** (monitor-live, hero-dark, mobile-live): Bus war ruhig → per `POST /api/knx/read` (GroupValueRead, **passiv**) ein **Read-Burst** über ~40 GAs (`READ_GAS` im Script) ausgelöst → echte Live-Telegramme erscheinen → Screenshot.
  - Interaktiv: monitor-detail (Row-Klick → Drawer), projects-detail (`tbody tr` erste Zeile, `button.knx-btn--ghost`), projects-import (Import-Button → Wizard-Modal), charts (mat-select GA-Pick + Custom-Range).
  - Chart-GAs: charts=`0/2/1,0/2/4,10/0/195,10/0/90` (Temp+Lux), sensor=`10/3/210,10/0/90,10/4/46,20/1/236,6/0/3,10/1/65` (+Booleans). Range `2026-06-24T08:30`–`2026-06-26T12:00`.
- **Promote**: PNG (staging `edit/qa/gen/`) → webp `docs/screenshots/<name>.webp` (`ffmpeg -i x.png -quality 90`); Thumbs `scale=1040:-1 -quality 88` → `thumbs/<name>.webp`.
- **Neu**: `logs.webp` + Thumb + README-Galerie-Zelle (Log-Viewer) ergänzt.

## 5. Code-Stand
**Committet `748ac80`** (Pre-Release-Härtung): AppPaths/Data-Dir-Fix, Serilog File+Buffer+`KNX_LOG_LEVEL`, In-App-Log-Viewer (LogHub/Buffer/DiagnosticsController + Angular logs-Feature + Settings-Download), healthz-DB-Probe + Fail-fast, Login-Rate-Limit, Auth-Härtung (Refresh-Token-Hash, Reuse-Detection, Prune, logout-all), Upload-Limit, `/api/version`, **Re-Import** (`Project.EtsProjectId` + Migration `20260626082558_AddEtsProjectId` + `MergeReimportAsync` = GA-Merge by Address → History bleibt), CI arm64-Docker+SHA256SUMS+Version, README-Split (README/DEVELOPMENT/CHANGELOG), LICENSE.

**Uncommitted (Working-Tree):**
- Alle `docs/screenshots/*.webp` + `thumbs/*.webp` (neu aus recovered DB) + neu `logs.webp`, `thumbs/logs.webp`, `thumbs/hero-dark.webp`
- `README.md` (Logs-Galerie-Zelle)
- `docs/screenshots/edit/build.py` (VO-Pipeline + Pegel), `generate_assets.py` (Archiv-Text „Historie ohne Limit", dunkleres Label-BG)
- Neu (untracked): `edit/gallery.mjs`, `edit/tts_vo.mjs`, `edit/shot.mjs`, `edit/logshot.mjs`, `docs/ai/PRE_RELEASE_PLAN.md`, `docs/ai/SESSION_HANDOVER.md`
- **Noch nichts davon committet** (User entscheidet). `edit/.gitignore` hält Medien (*.mp4/*.mp3/cards/segments/qa/*.png) draußen.

## 6. Verifikation (war grün)
- Backend baut, **206 Parser-Tests grün**, Frontend Prod-Build grün.
- Smoke: `/api/version` (0.7.0+commit), healthz-DB-Probe, Diagnostics-Logs (Live-Buffer), Rate-Limit (10→429), logout-all, `/series` nach Recovery ok.
- **Nicht runtime-getestet**: Re-Import-End-to-End (braucht 2. .knxproj-Version; Projekt 5 hat noch leere `EtsProjectId` → erster Re-Import legt neu an, ab dem zweiten greift Matching). CI (arm64 erst beim nächsten Tag real).

## 7. Offene Punkte / nächste Schritte
- Video-Feinschliff: Musik-Pegel final bestätigen; ggf. Länge straffen (aktuell 4 min) oder einzelne VO-Sätze anpassen.
- Hero-Animation (`hero.gif`/`hero.webm`) noch alt — neue Variante offen (Recording vs. Desktop-Live-Clip).
- Committen der neuen Screenshots + Video-Pipeline-Änderungen (auf User-Zuruf; Hausregel: nie selbst committen).
- Re-Import real testen; echte Prod-DB-Integrität prüfen.
- Optional Auth-Hinweis: bestehende Klartext-Refresh-Tokens werden nach Deploy ungültig (Token-Hashing) → einmal Re-Login.

## 8. Wichtige Pfade
- Video: `docs/screenshots/edit/final.mp4` → Kopie `G:\Meine Ablage\temp\knx-ng-monitor-intro.mp4`
- VO-Clips: `docs/screenshots/edit/vo/s00..s16.mp3`
- Rebuild-Rezept (Screens/Video allgemein): `docs/screenshots/edit/REBUILD.md`
- Session-Log: `docs/screenshots/edit/project.md`
- Plan: `docs/ai/PRE_RELEASE_PLAN.md`
- ElevenLabs-Key: `~/.claude/skills/video-use/.env`
