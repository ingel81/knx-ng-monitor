# KNX-NG-Monitor — Vorstellungsvideo

## Session 1 — 2026-06-24

**Strategy:** Stills-basiertes Intro-Video aus README-Screenshots (kein Footage/Audio). 1920x1080@30, ~39s, stumm. "Sachlich Demo": 8 Beats (Intro-Card → 6 Feature-Screenshots → Outro-Card), Ken-Burns + Lower-Third-Labels + xfade-Dissolves.

**Decisions:**
- Format 16:9 1080p, stumm, sachliches Pacing (~5.5s/Feature) — per User-Abfrage.
- Cards-bg = App-Nav-Bar-Teal (14,53,49). Bridge zwischen dark hero + light Feature-Screenshots (deren Header genau dieses Teal hat). Mint-Accent (79,224,168).
- **Wichtig:** alle Feature-Screenshots sind LIGHT-Mode (nur hero-dark.webp ist dark). Erst falsch als dark angenommen → Cards entsprechend von near-black auf Nav-Teal umgestellt.
- Ken-Burns-Zoom von 1.10 auf 1.035 reduziert — 1.10 schnitt die Nav-Bar/Chrome ab.
- Labels: PIL-PNG Lower-Third, dunkler Bottom-Gradient + weißer Titel + mint Accent-Bar. Liest sich auf Light-UI als Caption-Strip.

**Build:** `generate_assets.py` (PIL Cards/Labels) → `build.py` (ffmpeg Ken-Burns-Segmente → xfade-Kette 0.4s → fade in/out). Output `final.mp4`.

**Outstanding:** Musik nachrüstbar (User wählte stumm). Optional: README einbinden / als GIF-Loop / vertical 9:16 Variante.

## Session 2 — 2026-06-24

**Strategy:** Langform-Relaunch ~103s, alle Features. Jitter-Fix + echtes App-Recording eingebaut.
**Decisions:**
- **Jitter-Ursache:** zoompan rundet x/y pro Frame auf ganze Pixel (bei s=1920 = mehrere px sichtbares Hüpfen). **Fix:** 3x-Supersample — zoompan @5760x3240, dann Lanczos→1920 (Integer-Step ~0.33px, unsichtbar). Build ~72s.
- **18 Beats:** Intro → Monitor(Live-Recording) → Live/Archiv → Detail/Schreiben&Lesen → Charts → Temp → Statistik → Heatmap → ETS-Import → **KNX-Secure-Card** → Topology → Gruppenadressen → **Mobile-Live(Recording, gecroppt)** → Themes/Sprache → **Platform-Card(Docker/OS/x64+ARM64/Pi/Browser)** → **Graph(EXPERIMENTELL, ans Ende)** → **Beta-Tester-Card** → Outro.
- **Recording** `2026-06-24 11-27-23.mp4` (2560x1440@60, 32s): Desktop-Live 0–21s, Mobile-Live (DevTools Pixel7) ab ~21.5s. Transition per Helligkeitsabfall (237→84) erkannt. Phone-Crop-Box `crop=496:1108:1032:84` (teal/weiß-Detektion).
  - Desktop → s01: scale 2560→1920, -an, +Label.
  - Mobile → s12: gecroppt, auf Teal-bg (`mobile_live_bg.png`) rechts überlagert (h=920), Text links, Mint-Border.
- Neue Cards (PIL @3840 für scharfes Supersampling): secure, platform, beta, mobile(static, ungenutzt), mobile_live_bg. Neue Labels inkl. Graph-Tag „EXPERIMENTELL" (orange Pill).
- Video-Segmente in build.py: `.mp4`-Input → scale+fps statt zoompan.
**Output:** `final.mp4` (1920x1080@30, ~27MB) + Kopie `G:/Meine Ablage/temp/knx-ng-monitor-intro.mp4`.
**Outstanding:** Musik weiter offen. Falls Rest-Shimmer auf Screenshot-Beats → Beats statisch schalten. README-Einbindung optional.

## Session 3 — 2026-06-25

**Strategy:** Korrekturschleife 2 + echte Charts aus Prod-DB + Musik.
**Decisions:**
- **Prod-DB** `publish/data/knxmonitor.db` (138 MB, 144 865 Telegramme, Projekt `Ingelfinger_iob_import`, 843 GAs). User-Account `joerg` drin → zusätzlichen `demo`/`demo12345` per BCrypt-Hash (publish/BCrypt.Net-Next.dll) eingefügt. DB-Backup `knxmonitor.db.bak-premigrate`.
- **Wichtig:** `publish/`-Binary + wwwroot waren **uralt** (kein `/series`-Endpoint, alter `/live-view`-Redirect). Screenshots daher gegen **aktuellen** Code: `dotnet run -c Release` (Backend, Env `ConnectionStrings__DefaultConnection` → Prod-DB) + `ng serve` (Frontend 4200, ruft API 8080). Dev-Backend migriert Prod-DB-Kopie additiv.
- **Screenshots neu** via Playwright (`scratchpad/shotter/`, pw-core 1.61.1 ↔ Cache chromium-1228, viewport 1920×1080 @DPR2 = 3840×2160). Programmatischer Login (localStorage-Token), mat-select-Klicks, Custom-Range 24.06 08:30–25.06 16:00.
  - `charts.webp`: 2 Temps + 2 Helligkeit (°C/Lux, Dual-Y-Achse, glatte Tag/Nacht-Kurven) — ersetzt schlechtes Leistungs-Bild.
  - `charts-temp.webp`: Zahlenwerte (Lux/Temp/Leistungs-Hügel) + Boolean-Step-Lines (Status/Präsenz/Ventil). Originale als `*.webp.orig` gesichert.
- **Textkorrekturen** (generate_assets.py): archive→„umfangreich filterbar · bis 1 Mio. Telegramme (konfigurierbar)"; detail→Sicherheitsabfrage raus; temp→Titel „Mess- & Schaltkurven" / „Alle Zahlenwerte und Schaltvorgänge im Zeitverlauf"; mobile→Bottom-Nav raus, „Live-Feed auch unterwegs / Für Smartphone und Tablet"; **alle em-dashes (—) → normale Bindestriche (-)**.
- **Mobile-Beat-Clip** `clips_mobile_beat.mp4` neu komponiert (Text war eingebrannt): `mobile_live_bg.png` (neu) + `clips_mobile_live.mp4` (scale=-2:920, pad 6px Mint-Border, overlay 1294:74).
- **Musik** `track01.mp3` (102 s) in build.py gemuxt: `volume=0.16`, afade in 1.5 s / out 2 s, AAC 160k. Dezent: mean -31 dB, max -16 dB.
**Output:** `final.mp4` (1920×1080@30, ~34 MB, **mit Ton**) + Kopie auf G:.

## Session 4 — 2026-06-25 (Ultra-Pass)

**Korrekturschleife 3** — Ziel „ultra perfekt".
- **Flackern messbar gefixt:** Shimmer-Metrik (12 Folgeframes, Pixel die Delta-Vorzeichen ≥3× wechseln ∩ bewegt). Vorher @4x-Zoom: charts 13.0%, group-addresses 9.1% oszillierende Pixel. **Lösung:** Screenshots **statisch** rendern (1px-Linien/Kleintext shimmern unter jeder Bewegung), Ken-Burns nur noch auf Karten (kein Feindetail). Nachher: Screenshots **0.00%**, Karten ~1-2.5%. build_segment: `is_card` (dirname==CARDS) → kenburns, sonst statisch.
- **Supersample** 3x→**4x** (SS=4), Zoom 0.045→**0.030**, Transition 0.4→0.5, CRF 18→**16**.
- **Folien länger** (Lesezeit): Beats 6-9s, Gesamt **124.5s** (vorher 102.7s).
- **Beta + Outro → eine Folie:** `close_card()` (Logo + „Beta-Tester gesucht" + URL + „Open Source · MIT · selbst gehostet"); s16_beta/s17_outro ersetzt durch s16_close.
- **Musik:** track01→**track02.mp3**, sehr dezent `volume=0.09` (mean -37 dB), `-stream_loop -1` + atrim auf Länge, Fade 2s/2.5s.
- Statische Beats komprimieren stark → final.mp4 ~16.7 MB.
**Output:** `final.mp4` (124.5s, ~16.7 MB) + Kopie auf G:.
**Outstanding:** Abnahme. Backend (dotnet run :8080) + ng serve (:4200) laufen noch; `demo`/`demo12345`. Falls Karten-Bewegung unerwünscht → cards ebenfalls statisch (1 Zeile).

## Session 5 — 2026-06-28 (Voice-Over + komplette Screen-Neuerstellung)

- **DB-Recovery:** gelieferte Prod-DB war malformed → via `.dump`→rebuild recovered (237.288 Telegramme). Siehe `docs/ai/SESSION_HANDOVER.md`.
- **Alle README/GitHub-Screenshots neu** aus recovered DB via `gallery.mjs` (Playwright). Live-Screens per passivem `POST /api/knx/read`-Burst befüllt. Neu: `logs.webp` + README-Galerie-Zelle. Promote PNG→webp + Thumbs (1040w).
- **Voice-Over** eingebaut: ElevenLabs Stimme `dpsgxAAQscpwOkeRSVZr`, multilingual_v2, ausführliches DE-Skript (`tts_vo.mjs` → `vo/s00..s16.mp3`). build.py VO-Modus: Beat-Dauer aus VO-Länge, Video-Clips geloopt, Musik (track03) Sidechain-geduckt. Pegel nach „zu laut": `MUSVOL=0.13`, ducking threshold 0.04/ratio 14. Gesamt **239 s**.
- Texte: Archiv „Historie ohne Limit"; Label-BG dunkler.
- **Vollständiger Handover:** `docs/ai/SESSION_HANDOVER.md` (für Session-Reset).
- Scratchpad-Scripts ins Repo gesichert: `edit/{gallery,tts_vo,shot,logshot}.mjs`.
- Status: Video auf G: abgelegt. Screenshots + Video-Pipeline-Änderungen **uncommitted**.

## Session 6 — 2026-06-28 (Korrekturschleife: Musik, VO-Text, Datenschutz-Blur)

- **Musik deutlich leiser + langsamer:** build.py `MUSVOL` 0.13→**0.06**, neuer `MUSTEMPO=0.80` (pitch-erhaltendes `atempo` im Musik-Chain, Zeile `[mus]`). Gesamt-Audio mean ~-34.9 dB.
- **„Schaltschranklook" raus:** VO s13 (tts_vo.mjs) umformuliert → „…ein dunkles für abgedunkelte Räume." Nur `s13.mp3` neu via ElevenLabs (one-off Script, gleiche Voice/Settings). Dauer 9.66→8.73 s.
- **Datenschutz-Blur (in-place, betrifft auch README/GitHub):** Name **Ingelfinger** + Adresse **Kilianstraße 7** in 5 webp mosaik-geblurrt (PIL, `scratchpad/blur.py`, Boxen in Original-3840-Koords): `topology` (2 Boxen), `graph` (2 Node-Labels mittig), `projects-import` (NAME+FILE), `projects` (NAME+FILE), `projects-detail` (Dialog-Titel + Hintergrund-Zeile). Thumbs (1040×585) mit-regeneriert. Übrige Screens (monitor/charts/stats/group-addresses/settings) enthielten den Namen nicht. Originale gesichert in `scratchpad/preblur/`.
- Rebuild **238.5 s**, ~25 MB, Kopie auf G:. Alle 3 Video-Beats (topology/graph/import) per Frame-Extract auf Blur geprüft = sauber.
- **Nachtrag (gleiche Session):** Musik-Track gewechselt `track03`→**`track04.mp3`** + `MUSVOL` 0.06→**0.04** (noch leiser). atempo 0.80 + Sidechain bleiben. Dann „zu leise" → `MUSVOL` 0.04→**0.10** (track04 lauter), Gesamt-Audio mean -32.5 dB. Rebuild + G:-Kopie.
