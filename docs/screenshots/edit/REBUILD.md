# KNX-NG-Monitor Intro-Video — Rebuild-Rezept

Alles um das Video 1:1 neu zu erzeugen oder mit leichten Anpassungen. Stand: Session 4 (2026-06-25).
Aktuelles Ergebnis: `final.mp4` — 1920×1080@30, **124.5s**, ~16.7 MB, mit Ton.

---

## 0. Schnellster Weg (nur Video neu rendern, Assets/Screenshots existieren)

```bash
cd docs/screenshots/edit
python generate_assets.py     # Cards + Labels (PIL)
python build.py               # Segmente -> xfade-Kette -> final.mp4 (+ Musik)
# Kopie auf G:
cp final.mp4 "/g/Meine Ablage/temp/knx-ng-monitor-intro.mp4"
```
Dauer build.py: ~1m20s (statische Screenshots) bzw. ~2m10s wenn Screenshots wieder bewegt.

Python = `python` (Win, hat PIL 12.1.0). **Nicht** `python3` (fehlt).

---

## 1. Datei-Inventar (alles in `docs/screenshots/edit/`)

| Datei | Zweck |
|---|---|
| `generate_assets.py` | erzeugt `cards/*.png` (3840×2160) + `cards/label_*.png` (1920×1080) via PIL |
| `build.py` | baut 17 Segmente -> xfade -> `final.mp4`, muxt Musik |
| `cards/` | gerenderte Cards + Labels (Output von generate_assets) |
| `segments/` | Zwischenausgabe pro Beat (Output von build) |
| `clips_live_desktop.mp4` | Desktop-Live-Feed aus User-Recording (s01) |
| `clips_mobile_live.mp4` | gecroppter Phone-Live-Feed (Rohmaterial für Beat) |
| `clips_mobile_beat.mp4` | fertiges Mobile-Composite (Phone + Text-bg + Border) (s12) |
| `track01.mp3` / `track02.mp3` | Musik (je 102s). **Aktuell: track02** |
| `final.mp4` | Endergebnis |
| `project.md` | Session-Historie |
| Screenshots: `../charts.webp`, `../charts-temp.webp`, `../monitor-archive.webp`, … | Feature-Stills (im Ordner darüber) |

Quell-Recording (User): `../2026-06-24 11-27-23.mp4` (2560×1440@60, 32s; Desktop 0–21s, Mobile ~21.5–32s).

---

## 2. Screenshots neu schießen (nur wenn UI/Daten sich ändern)

**Wichtig:** `publish/`-Binary war veraltet (kein `/series`-Endpoint). Screenshots gegen **aktuellen Code**:

### 2a. Backend (aktueller Code) gegen Prod-DB

> **Falle:** `ConnectionStrings__DefaultConnection` als Umgebungsvariable **wirkt nicht**.
> `backend/KnxMonitor.Api/Program.cs:93-95` überschreibt die Verbindung bewusst mit einem
> Pfad relativ zur Programmdatei (`AppPaths.DbPath`), damit das portable Binary immer
> dieselbe DB öffnet. Ein `dotnet run` aus `backend/` liest deshalb **immer**
> `backend/KnxMonitor.Api/bin/Debug/net9.0/data/knxmonitor.db` — egal was die Env sagt.
>
> Wer die Prod-Kopie sehen will, kopiert sie an genau diese Stelle (Prozess vorher
> stoppen, `-wal`/`-shm` mit entfernen, vorhandene Dev-DB vorher wegsichern):
> ```bash
> powershell -Command "Get-Process -Name KnxMonitor.Api -EA SilentlyContinue | Stop-Process -Force"
> D=backend/KnxMonitor.Api/bin/Debug/net9.0/data
> mv $D/knxmonitor.db $D/knxmonitor.db.devbak; rm -f $D/knxmonitor.db-wal $D/knxmonitor.db-shm
> cp publish/data/knxmonitor.db $D/knxmonitor.db
> ```
> Kontrolle vor dem Schießen — die Zahl muss zum Datenbestand passen, sonst hängt man
> an der falschen DB und die Diagramme zeigen Lücken:
> ```bash
> curl -s -H "Authorization: Bearer $TOK" \
>   "localhost:8080/api/telegrams/stats?from=2026-07-29T00:00:00Z&to=2026-08-06T00:00:00Z&buckets=56"
> ```
> (Der Parameter heißt `buckets`, nicht `bucketMinutes`. Zeitangaben mit `Z` schicken —
> ohne Zeitzone interpretiert `ResolveRange` sie als Server-Lokalzeit.)
```bash
# alten Prozess killen
powershell -Command "Get-Process -Name KnxMonitor.Api -EA SilentlyContinue | Stop-Process -Force"
# DB-Backup
cp publish/data/knxmonitor.db publish/data/knxmonitor.db.bak-premigrate
# Dev-Backend mit Prod-DB (Env-Override), Port 8080
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://+:8080 \
ConnectionStrings__DefaultConnection="Data Source=d:/Source/knx-ng-monitor/publish/data/knxmonitor.db" \
dotnet run -c Release --project backend/KnxMonitor.Api
```
Prod-DB: 144 865 Telegramme, Projekt `Ingelfinger_iob_import` (843 GAs). Dev-Backend migriert die DB additiv (drum Backup).

### 2b. Frontend (aktueller Code)
```bash
cd frontend && npx ng serve --port 4200    # Dev-Env ruft API direkt auf :8080 (CORS)
```

### 2c. Demo-User (falls keiner / Passwort unbekannt)
Prod-DB hat User `joerg` (Passwort unbekannt). Zusätzlichen `demo` per BCrypt-Hash einfügen:
```powershell
Add-Type -Path "d:\Source\knx-ng-monitor\publish\BCrypt.Net-Next.dll"
[BCrypt.Net.BCrypt]::HashPassword("demo12345")   # -> Hash
```
```bash
sqlite3 publish/data/knxmonitor.db "INSERT INTO Users (Username,PasswordHash,CreatedAt) VALUES ('demo','<HASH>','2026-06-25 00:00:00.0000000');"
```
Login: `demo` / `demo12345`.

### 2c-ter. Ganze Galerie in einem Rutsch

`gallery.mjs` schießt alle 20 Bilder (Desktop, Mobil, Dark-Hero) und bringt
Weichzeichnen (§2c-bis), Füll-Warten und die englische Browsersprache bereits mit:

```bash
cd docs/screenshots/edit
FE_PORT=4321 SHOT_OUT=./qa/gen node gallery.mjs      # Port nur nötig, wenn 4200 belegt ist
```
`gallery.mjs` braucht `playwright-core` — im Repo liegt kein `node_modules`. Einmalig in
einem Scratchpad installieren und das Skript dorthin kopieren (Node löst Importe relativ
zur **Skriptdatei** auf, `NODE_PATH` hilft bei ESM nicht):
```bash
mkdir -p <scratchpad>/shotter && cd <scratchpad>/shotter
echo '{"name":"shotter","private":true,"type":"module"}' > package.json
PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm install playwright-core@1.61.1   # passt zu chromium-1228
cp docs/screenshots/edit/gallery.mjs . && node gallery.mjs
```

Seit 2026-08-17 schießt der Lauf zusätzlich sieben Phone-Ansichten für den Short
(`hero-dark-mobile`, `monitor-archive-mobile`, `monitor-detail-mobile`,
`group-addresses-mobile`, `topology-mobile`, `settings-mobile`, `projects-mobile`).
`SHORT_SHOTS=0` schaltet sie ab, wenn nur die README-Galerie gebraucht wird.

Danach nach webp wandeln (Vollbild + 1040px-Thumb). **Pfad-Falle:** von `qa/gen` aus
sind es *drei* Ebenen zurück nach `docs/screenshots/`, nicht zwei:
```bash
cd qa/gen
for f in *.png; do n="${f%.png}"; ffmpeg -y -i "$f" -q:v 90 "../../../$n.webp"; done
# Thumbs nur für die 20 README-Bilder (die Short-Extras brauchen keine)
for n in login monitor-live monitor-archive monitor-detail hero-dark charts charts-temp \
         stats stats-heatmap topology group-addresses settings graph logs projects \
         projects-detail projects-import monitor-live-mobile charts-mobile stats-mobile; do
  ffmpeg -y -i "$n.png" -vf scale=1040:-2 -q:v 82 "../../../thumbs/$n.webp"
done
```

Drei Dinge, die dabei nicht offensichtlich sind:
- **`--lang=en-GB` beim Browser-Start**, nicht `locale` am Kontext: Der Platzhalter der
  nativen `datetime-local`-Felder hängt an der UI-Sprache des Browsers. Ohne das steht
  `tt.mm.jjjj` in einem sonst englischen Screenshot (siehe Issue #11).
- **Live-Ansichten füllen sich langsam.** `waitFilled()` wartet, bis der Inhalt den
  Viewport überfüllt, statt Zeilen zu zählen — die Tabelle ist virtualisiert und rendert
  immer nur das Sichtbare, die Anzahl bleibt also konstant.
- **Statistik auf 7d** stellen: im 24-h-Standard deckt sie nur den Tagesrand ab und die
  Heatmap zeigt zwei Wochentage.

### 2c-bis. PFLICHT: Personenbezug unkenntlich machen

Die eigene Prod-/Dev-DB enthält echte Namen — Projektname und Gebäude/Adresse aus dem
ETS-Projekt. Betroffen sind **`/topology`** (Gebäudename, Strasse) und **`/projects`**
(Projektname + Dateiname, also auch `projects-detail` und `projects-import`). Alle
anderen Ansichten waren zuletzt sauber — trotzdem jedes Mal neu prüfen, das hängt am
importierten Projekt.

**Weichzeichnen, nicht ersetzen.** So bleibt sichtbar, dass dort ein echter Wert steht,
ohne ihn preiszugeben — und es sieht aus wie in den bisherigen Bildern. `gallery.mjs`
erledigt das über `blurSecrets()`: es sucht Textknoten und `title`/`aria-label` nach
`SECRET_RX` ab und legt `filter: blur(10px)` auf die Trefferelemente. Radius nicht unter
10px setzen — bei 6px waren fette 14px-Labels noch zu erahnen.

Direkt vor dem `screenshot()` aufrufen, sonst rendert Angular neu und der Inline-Stil
ist wieder weg. Das Muster in `SECRET_RX` anpassen, wenn ein anderes Projekt importiert
ist. Zur Kontrolle meldet der Lauf pro Bild, wie viele Elemente unkenntlich gemacht
wurden — steht dort `(0 unkenntlich)`, wo Treffer erwartet waren, stimmt das Muster nicht.

**Der Graph braucht einen anderen Weg.** Er zeichnet seine Beschriftungen auf ein Canvas,
also gibt es keine DOM-Knoten, auf die ein Weichzeichner wirken könnte. `gallery.mjs`
fängt dort stattdessen die API-Antworten ab (`screen(..., redactApi = true)`) und ersetzt
die Treffer schon im JSON durch `•••••`, bevor der Graph sie rendert. Dasselbe Vorgehen
wäre für jede künftige Canvas-Ansicht nötig.

Dabei muss das **ganze Wort** fallen, nicht nur der Treffer: mit einem Muster auf den
Stamm blieb aus „Kilianstraße 7" ein gut lesbares „•••••straße 7" stehen (gefunden am
2026-08-17 in `graph.webp`). Die Ersetzung greift deshalb umgebende Wortzeichen und eine
folgende Hausnummer mit ab. Nach jedem Lauf am Graph-Bild nachsehen — es ist die einzige
Ansicht ohne sichtbare Trefferzählung.

Einzelne Bilder nachschießen, ohne den ganzen Lauf zu wiederholen: `ONLY=graph,topology node gallery.mjs`.

### 2d. Playwright-Screenshot-Script
Ort: `scratchpad/shotter/` (pw-core 1.61.1 ↔ Cache `~/AppData/Local/ms-playwright/chromium-1228`).
Setup einmalig:
```bash
cd <scratchpad>/shotter
echo '{"name":"shotter","private":true}' > package.json
PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm install playwright-core@latest
```
`shot.mjs` Kernpunkte:
- Viewport **1920×1080, deviceScaleFactor 2** -> Screenshot **3840×2160**.
- Login programmatisch: `fetch(API/auth/login)` -> Token in `localStorage` (Keys: `accessToken`,`refreshToken`,`tokenExpiry`,`username`).
- Charts: mat-select öffnen, Optionen per Adress-Text klicken, Custom-Range füllen (`input[type=datetime-local]`), „Apply" klicken.
- Range: **2026-06-24T08:30 bis 2026-06-25T16:00** (volle Datenspanne).
- GA-Auswahl charts.webp (Beat „Charts"): `0/2/1, 0/2/4, 10/0/195, 10/0/90` (2 Temps °C + 2 Helligkeit Lux -> Dual-Y-Achse, glatte Tag/Nacht-Kurven).
- GA-Auswahl charts-temp.webp (Beat „Mess- & Schaltkurven"): `10/3/210, 10/0/90, 10/4/46, 20/1/236, 6/0/3, 10/1/65` (Temp/Lux/Leistung **+ Boolean-Step-Lines** Status/Präsenz/Ventil).

Run: `node shot.mjs` -> schreibt `charts_new.png`,`charts-temp_new.png` nach `docs/screenshots/`.
PNG -> webp (Original ersetzen, Backup `.orig`):
```bash
cd docs/screenshots
cp charts.webp charts.webp.orig; cp charts-temp.webp charts-temp.webp.orig
ffmpeg -y -i charts_new.png      -q:v 90 charts.webp
ffmpeg -y -i charts-temp_new.png -q:v 90 charts-temp.webp
```

---

## 3. Mobile-Beat-Clip neu (nach jeder Neuaufnahme und bei Textänderung)
`clips_mobile_beat.mp4` ist ein fertiges Composite — Text ist eingebrannt, also gibt es
je Sprache eine Datei (`clips_mobile_beat.mp4` / `clips_mobile_beat_en.mp4`; build.py wählt
über `VLANG`):
```bash
cd docs/screenshots/edit
for L in "" "_en"; do
  ffmpeg -y -loop 1 -i "cards${L}/mobile_live_bg.png" -i clips_mobile_live.mp4 \
   -filter_complex "[1:v]scale=-2:920:flags=lanczos[p];[p]pad=iw+12:ih+12:6:6:0x4FE0A8[pb];[0:v][pb]overlay=1294:74,fps=30,setsar=1,format=yuv420p[v]" \
   -map "[v]" -t 12 -r 30 -c:v libx264 -crf 18 -preset medium -pix_fmt yuv420p "clips_mobile_beat${L}.mp4"
done
```
(0x4FE0A8 = Mint-Border. Quelle ist seit 2026-08-17 die Playwright-Aufnahme aus §9 mit
780×1688 — der alte Weg war ein Crop `crop=496:1108:1032:84` aus einer Bildschirmaufnahme.)

---

## 4. build.py — Stellschrauben

```python
FPS, T, CRF, ZOOM = 30, 0.5, "16", 0.030   # T=xfade-Dauer, ZOOM=Ken-Burns-Stärke
SS = 4                                       # Supersample-Faktor (Anti-Jitter)
```
- **SEGMENTS**-Liste: `(id, bild|clip, label|None, dauer_s, zoom_dir)`. Dauer hier ändern für längere/kürzere Folien.
- `OVR`-Dict: pro-Beat Fokus/Zoom-Override (`fx,fy,amt`) — nur für bewegte Beats relevant.
- **Flacker-Logik** (`build_segment`): `is_card` (Datei in `cards/`) -> Ken-Burns; Screenshots -> **statisch** (= 0 Shimmer). Wenn Karten auch statisch sein sollen: im `elif is_card`-Zweig denselben statischen `base` setzen.
- **Musik** (in `main()`): `music = V("track02.mp3")`, `VOL = 0.09` (mean ~-37 dB, „sehr dezent"). `-stream_loop -1` + `atrim` loopt auf Videolänge. Fade in 2s / out 2.5s. Track tauschen = Dateiname ändern.

### Beat-Reihenfolge + aktuelle Dauern
```
s00 intro (card)        6.0   |  s09 secure (card)      9.0
s01 monitor (live mp4)  8.0   |  s10 topology           7.5
s02 archive             9.0   |  s11 group-addresses    7.0
s03 detail              8.5   |  s12 mobile (live mp4)   7.0
s04 charts              8.5   |  s13 settings           7.0
s05 mess-/schaltkurven  7.5   |  s14 platform (card)    9.0
s06 stats               6.5   |  s15 graph (Alpha)      8.5
s07 heatmap             6.5   |  s16 close (card)        9.0
s08 import              8.0   |  Total ~124.5s
```

---

## 5. Texte — alle in `generate_assets.py`
Cards: `intro_card, secure(bullet_card), platform_card, close_card`. Labels: `label(name,title,subtitle,tag=None)` am Ende der Datei.
Regeln: **keine em-dashes (—)**, nur normale Bindestriche (-). „·" (Middot) als Trenner ist ok.
Merge-Abschluss: `close_card()` = Logo + „Beta-Tester gesucht" + URL + „Open Source · MIT · selbst gehostet".

---

## 6. Flacker-QA (objektiv)
Misst oszillierende Pixel (Shimmer) statt Bauchgefühl:
```bash
# 12 Folgeframes mid-beat extrahieren, dann Python:
# Pixel die Delta-Vorzeichen >=3x wechseln UND bewegt sind = Shimmer
```
Skript: `qa/flick.py`. Zielwerte: Screenshots **0.00%**, Karten ~1-2.5% (ok, kein Feindetail).
Beat-Startzeiten (T=0.5): s_i start = Σ(dauer_j - 0.5) für j<i.

---

## 7. Häufige Tweaks — Kurzanleitung
| Wunsch | Wo |
|---|---|
| Folien länger/kürzer | `build.py` SEGMENTS Dauer |
| Musik tauschen / lauter | `build.py` `music=`, `VOL=` |
| Karten auch statisch (kein Shimmer-Rest) | `build.py` `elif is_card` -> statischer base |
| Bewegung auf Screenshots zurück | `build_segment` else-Zweig -> `kenburns(sid,zdir,tot)` |
| Texte ändern | `generate_assets.py` -> `python generate_assets.py` |
| Andere Chart-Kurven | `shot.mjs` GA-Listen + Range, neu schießen, webp ersetzen |
| Übergang weicher/härter | `build.py` `T` |
| Schärfer/kleinere Datei | `build.py` `CRF` (16=hoch, 20=kleiner) |
```

---

## 8. Voice-Over (Stand 2026-06-28)

- **Stimme:** ElevenLabs Voice-ID `dpsgxAAQscpwOkeRSVZr`, Modell `eleven_multilingual_v2`. Key: `~/.claude/skills/video-use/.env`.
- **KNX-Aussprache:** plain schreiben (`KNX-NG`, `KNX-Bus`) — nicht `K N X` (zu langsam).
- **Generator:** `tts_vo.mjs` (Node, UTF-8-safe; enthält das 17-zeilige DE-Skript, ausführlich/erklärend). → `vo/s00.mp3 … s16.mp3`. Aussprache via UTF-8 ok; bash-curl mit Umlauten vermeiden.
- **build.py VO-Modus:** Beat-Dauer = `vo_len + LEAD(0.4) + TRAIL(1.1)`, Floor 4.5. Video-Clips geloopt (`-stream_loop -1`). Audio: VO `adelay`→`amix(normalize=0)`, Musik `track03.mp3` `volume=MUSVOL` + Sidechain-Ducking unter VO.
- **Pegel aktuell:** `MUSVOL=0.13`, `sidechaincompress=threshold=0.04:ratio=14:attack=5:release=400` (User: Musik leiser). Prüfen: `ffmpeg -i final.mp4 -af volumedetect -f null -`.
- **Rebuild VO:** `node tts_vo.mjs` (nur bei Skript/Stimm-Änderung) → `python build.py` → Kopie nach G:.
- Gesamt aktuell **239 s**. Track03 (180 s) loopt unter VO (geduckt, unauffällig).

---

## 9. Bewegte Beats neu aufnehmen (`rec_clips.mjs`, Stand 2026-08-17)

s01 (Desktop-Live) und s12 (Mobile) sind die einzigen Beats mit echter Bewegung. Bis
Session 6 kamen sie aus einer Bildschirmaufnahme vom Juni — nach jedem UI-Umbau zeigen
sie also eine Oberfläche, die es nicht mehr gibt. `rec_clips.mjs` nimmt beide headless neu auf:

```bash
cp docs/screenshots/edit/rec_clips.mjs <scratchpad>/shotter/ && cd <scratchpad>/shotter
FE_PORT=4321 API_PORT=8080 node rec_clips.mjs          # beide Clips
REC_ONLY=phone REC_PHONE=13 node rec_clips.mjs         # nur das Phone, 13 s
```
Ergebnis: `clips_live_desktop.mp4` (1920×1080) und `clips_mobile_live.mp4` (780×1688).
Danach **immer** §3 laufen lassen, sonst bleibt das alte Mobile-Composite stehen.

Drei Punkte, die nicht offensichtlich sind:
- **Aufgenommen wird per CDP-Screencast**, nicht mit Playwrights `recordVideo`. Letzteres
  liefert VP8 mit fester Bitrate; der Tabellentext franst sichtbar aus, während alle
  anderen Beats aus verlustfreien Screenshots stammen. Die JPEG-Frames des Screencasts
  kommen mit ~96 fps herein und werden über ihre Zeitstempel auf konstante 30 fps montiert.
- **`--force-device-scale-factor=2` beim Browserstart ist Pflicht für das Phone.** Ohne
  das Flag rastert der Screencast in CSS-Pixeln, also 390×844 statt 780×1688 — im fertigen
  Video sichtbar weich. `deviceScaleFactor` am Kontext genügt dafür **nicht** (der Wert
  steuert Screenshots, nicht den Screencast). Für den Desktop bleibt das Flag weg, sonst
  entstehen 3840×2160-Frames ohne Gewinn.
- **Der Weichzeichner muss nachgelegt werden.** Ein einmaliger Aufruf reicht nur beim
  Standbild; die Liste rendert während der Aufnahme ständig neu, deshalb legt
  `keepBlurred()` den Filter alle 400 ms wieder auf.

---

## 10. YouTube-Short (9:16, Stand 2026-08-17)

Eigene Pipeline neben dem Langvideo, gleiche Handschrift (Farben, Stimme, Musik):

| Datei | Zweck |
|---|---|
| `generate_short_assets.py` | 1080×1920-Beats (gerendert 2160×3840) → `short/` bzw. `short_en/` |
| `tts_short.mjs` | 8 kurze VO-Sätze → `vo_short/s0..s7.mp3` (`VLANG=en` → `vo_short_en/`) |
| `build_short.py` | Segmente → xfade → `short.mp4` / `short_en.mp4` |

```bash
cd docs/screenshots/edit
python generate_short_assets.py && VLANG=en python generate_short_assets.py
node tts_short.mjs && VLANG=en node tts_short.mjs        # nur bei Textänderung
python build_short.py && VLANG=en python build_short.py
```

Aufbau: Intro-Karte → Live (Phone-Video im Rahmen) → Archiv → Charts → Statistik →
ETS-Import → Desktop+Phone → Beta-Karte. Beat-Dauer = VO + 0.95 s, Übergang 0.35 s.

- **Phone-Ansichten tragen das Format.** Ein 16:9-Screenshot wird im Hochformat zum
  Briefschlitz und seine Schrift am Handy unlesbar. Deshalb kommen sechs von acht Beats
  aus Phone-Screenshots; der Desktop-Beat zeigt nur einen **Ausschnitt** der Tabelle
  (`crop=(0,100,1980,1500)` in 3840×2160-Koordinaten) plus ein dunkles Phone daneben.
- Der Live-Beat lässt in `live_bg.png` ein Loch; `build_short.py` legt das Phone-Video
  per `alphamerge` mit `live_mask.png` hinein, damit die Videoecken dieselbe Rundung
  bekommen wie die Standbilder.
- Musik/Ducking wie im Langvideo, nur etwas straffer (`MUSVOL=0.12`, `atempo=0.85`).
