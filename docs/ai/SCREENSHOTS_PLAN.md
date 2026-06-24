# Plan: Automatisierte Screenshots fürs README (mainstream-tauglich)

> Status: **umgesetzt** (2026-06-19). Tooling unter `tools/screenshots/`, Assets unter
> `docs/screenshots/`, README-Abschnitt zwischen `<!-- SCREENSHOTS:START/END -->` generiert.
> Ziel: Screenshots jeder Seite/Maske → ins GitHub-README als Thumbnail-Grid (Klick → groß).
>
> **Re-Run (Voraussetzung: dev-Apps laufen, Bus verbunden, `demo`-User in DB):**
> ```
> cd tools/screenshots
> node shoot.mjs                 # alle Shots + Hero (Desktop alle + Mobile-Subset)
> node shoot.mjs --only=charts   # gezielt einzelne Shots neu
> node gen-readme.mjs            # README-Abschnitt neu generieren
> ```
> Scope: **Desktop** = alle Masken. **Mobile (390px)** nur Monitor-Live, Charts, Statistics
> (`mobile: true` im SHOTS-Katalog). **Tablet (834px) bewusst weggelassen** — App-Header
> überläuft unterhalb des Desktop-Layouts (Breakpoint erst <768px), Inhalte abgeschnitten.
> Neue Mobile-Masken vor Aufnahme auf horizontalen Overflow prüfen.
> Screenshot-Login: User `demo` (BCrypt-Hash direkt in SQLite `Users` eingefügt).
>
> **Update 2026-06-24:** Neue Shots **`graph`** (GA-Netzwerk, Alpha) + **`stats-heatmap`**
> (Aktivität Wochentag×Stunde, 30d-Range). Graph = Canvas → Labels nicht CSS-blurbar; daher
> **Playwright Route-Rewrite** des `/projects/*/locations`-Endpoints: `Building`/`BuildingPart`
> (echter Nachname + Straße) → `Musterhaus` / `Musterstraße 1` (`rewriteLocations: true` im
> SHOTS-Eintrag). Gilt jetzt auch für `topology` (sauberer als der frühere Blur).
>
> Privacy: Projektname/Dateiname (Projects) per CSS-Blur (`blurProject`); Gebäude/Gebäudeteil
> (Graph + Topology) per Route-Rewrite ersetzt — bei neuen Masken mit Standortbezug prüfen.

## Entscheidungen (vom User bestätigt)

- **Theme:** Gallery **nur Light**. **Ausnahme: 1 Dark-Hero-Standbild** oben im README (Dark =
  Brand-Identität des Monitoring-Tools, siehe CLAUDE.md "Dark theme as primary").
- **Hero oben im README:** **animiertes Live-GIF/WebM** (Telegram-Flow) **+** ein **Dark-Standbild**.
- **Browser-Frame/Window-Mockup:** **nein** — plain Viewport-Shots.
- **Auflösung Desktop:** **Full HD 1920×1080, fullscreen** für **alles**.
- **Retina:** **alle** Shots mit `deviceScaleFactor: 2` (scharf auf HiDPI, GitHub skaliert runter).
- **Zusätzlich Tablet + Handy** für die **wichtigsten Masken** (nicht zwingend jede).
- **Live-Ansicht:** **echter Bus** — verbinden und ein paar Minuten laufen lassen, damit der
  Live-Screen mit echten Telegrammen **gefüllt** ist. Zusätzlich Archive-Ansicht (historische Daten).
- **Login:** **temporären Screenshot-User** anlegen (bekanntes Passwort), nach den Shots optional
  wieder entfernen.

### Echter Bus → Begleitmaßnahmen (Risiken abfedern)
Echter Bus ist nicht reproduzierbar und enthält reale Daten. Daher zwingend:
- **Privacy-Maskierung:** reale Gateway-IP, phys. Adressen, ggf. interne GA-Namen im Bild prüfen.
  Wenn das aktive Projekt sensible Klarnamen hat → Demo-Projekt mit neutralen Namen verwenden,
  oder betroffene Bildregionen post-blur (`sharp`). **Vor Commit Sichtprüfung jedes Shots.**
- **Determinismus statt `sleep`:** nicht auf Zeit warten, sondern auf **Row-Count im Grid**
  (`waitForFunction`, z.B. ≥ N Zeilen) → Tabelle garantiert gefüllt, kein leerer State.
- **Fallback Empty-State:** wenn Gateway in der Session nicht erreichbar → Live als dokumentierten
  Empty-/Connecting-State shooten statt abzubrechen.

## Viewports

| Profil | Größe | Scope |
|---|---|---|
| Desktop | **1920×1080** | **alle** Seiten + Masken/Dialoge |
| Tablet | ~**834×1112** (iPad-ähnlich, portrait) | wichtigste Masken |
| Handy | ~**390×844** (iPhone-ähnlich, portrait) | wichtigste Masken + Mobile-Card-Ansicht des Monitors |

„Wichtigste Masken" für Tablet/Handy: **Monitor (Live)**, **Charts**, **Projekte**, **Topologie**,
**Gruppenadressen**, **Settings**, **Login**. (Mobile zeigt unter 768px die Card-Ansicht statt Grid.)

## Zu erfassende Seiten / Masken

Routes (Frontend `app.routes.ts`): `/login`, `/setup` (Initial-Setup), `/monitor`, `/charts`,
`/stats`, `/projects`, `/topology`, `/group-addresses`, `/settings`.

Vollständige Liste inkl. Dialoge/States:
1. **Login** (`/login`)
2. **Initial-Setup** (`/setup`) — nur sichtbar wenn kein User existiert; ggf. auf frischer DB shooten
   oder überspringen.
3. **Monitor – Live** (`/monitor`, Mode Live, **gefüllt** nach Bus-Laufzeit) + Status-Badge/msg-s
4. **Monitor – Archive** (`/monitor`, Mode Archive, Filterleiste sichtbar, Tabelle gefüllt)
5. **Monitor – Telegramm-Detail-Panel** (Klick auf Zeile → Side-Sheet: Wert, Bus-Aktionen,
   Verwendet-von, Chart-Button)
6. **Monitor – Column-Manager** (Spalten ein/aus Popover) *(optional)*
7. **Charts** (`/charts`) — 1–2 GAs gewählt, Kurve sichtbar (Bus-Laufzeit liefert Live-Append)
8. **Statistik** (`/stats`) — Total + msg/s + Telegramme-über-Zeit-Histogramm
9. **Projekte – Liste** (`/projects`) — aktives Projekt, Aktiv/Auto-Connect-Toggles
10. **Projekte – Import-Wizard** (Dialog „Projekt importieren")
11. **Projekte – Detail-Dialog** (Auge-Icon: GAs + Geräte-Tabellen)
12. **Projekte – Keyring-Upload-Dialog**
13. **Topologie** (`/topology`) — Gebäudebaum mit Gerätenamen
14. **Gruppenadressen** (`/group-addresses`) — 3-Ebenen-Baum, Read/Write/Chart-Controls, Gruppennamen
15. **Settings** (`/settings`) — KNX-Config, Recording, Theme/Dichte
16. **Confirm-Dialog** (z.B. Delete-Bestätigung) *(optional)*
17. **Sprachumschalter** EN/DE im Header *(optional, ein Shot je Sprache zeigt i18n)*

## Technischer Ansatz

**Headless Playwright (Chromium)** gegen die laufenden lokalen Apps
(`http://localhost:4200` Frontend, Backend `:8080`).

### Voraussetzungen
- **Laufende Dev-Instanzen erst sauber neu starten:** aktuell laufen Frontend + Backend bereits
  (ng serve + KnxMonitor.Api). Für einen definierten Screenshot-Lauf zuerst beide beenden, dann
  mit Screenshot-Setup frisch starten (richtiges/neu importiertes Projekt aktiv, ggf. eigene
  Screenshot-DB). Backend-Prozess killen:
  `powershell -Command "Get-Process -Name KnxMonitor.Api -ErrorAction SilentlyContinue | Stop-Process -Force"`.
  (ng serve läuft laut House Rules ohnehin parallel — nur bei DB-/Projektwechsel neu laden.)
- Backend (`:8080`) + Frontend (`:4200`) laufen (Dev). Backend mit `ASPNETCORE_ENVIRONMENT=Development`.
- Aktives Projekt importiert (für Topologie/GA-Baum/Charts/Detail-Used-by). **Wichtig:** mit dem
  **neuen Parser** (v0.6.0) neu importiert, sonst falsche Geräteadressen/fehlende Gruppennamen.
- Bus verbunden + ein paar Minuten gelaufen (Live-Buffer gefüllt). Status prüfbar via
  `GET /api/knx/status` (`isConnected: true`).
- Light-Theme aktiv: ThemeService nutzt `localStorage` (Key `knx.theme`/`knx.density` — im Code
  `theme.service.ts` verifizieren). Vor den Shots `localStorage.setItem('<themeKey>','light')`
  setzen (oder über das Settings-UI Light wählen). Sprache: `localStorage` `knx.lang` = `de` oder `en`.

### Login / Screenshot-User
- Auth: Backend nutzt **BCrypt** (+ `Rfc2898`/PBKDF2 an anderer Stelle). `AuthController.cs` +
  `DbInitializer.cs` prüfen: gibt es einen **Register-Endpoint** oder nur Login + Initial-Setup?
  - **Wenn Register-Endpoint** existiert: User darüber anlegen (sauberster Weg).
  - **Sonst** Option A: Initial-Setup auf **frischer DB** nutzen, um den ersten Admin mit bekanntem
    Passwort zu erstellen (separate Screenshot-DB, danach verwerfen).
  - Option B: User direkt in SQLite (`Users`-Tabelle) einfügen mit **BCrypt-Hash** (gleicher
    Work-Factor wie Backend — im Hashing-Code nachsehen) für ein bekanntes Passwort.
- Playwright loggt programmatisch ein (Login-Form ausfüllen) oder setzt das JWT direkt
  (`POST /api/auth/login` → Token → in `localStorage` wie der Frontend-AuthService es ablegt;
  Key/Format in `auth.service.ts`/Interceptor verifizieren).
- **Nach den Shots:** Screenshot-User wieder entfernen (DB-Delete) falls auf der echten DB angelegt.

### Datenstand für aussagekräftige Bilder
- **Charts:** vor dem Shot 1–2 numerische GAs vorselektieren (z.B. „Leistung aktuell"); via
  Query-Param `?ga=<addr>` (Charts liest `ga` schon) oder UI-Klick. Bus-Laufzeit füllt die Kurve.
- **Detail-Panel:** eine Zeile mit guter GA anklicken (mit DPT + Used-by-Daten).
- **Import-Wizard / Keyring / Confirm-Dialoge:** per Klick öffnen, dann Shot (nicht abschicken).

### Capture-Mechanik
- Pro Seite: zur Route navigieren → auf **konkreten Render-Marker** warten (`waitForSelector` /
  `waitForFunction` auf echtes Element bzw. Row-Count) → **kein** `waitForTimeout` → `page.screenshot`.
- **Retina überall:** `deviceScaleFactor: 2` (Desktop, Tablet, Handy).
- **Desktop:** Viewport 1920×1080, **fullscreen-Look** = Viewport-Shot (nicht `fullPage`, damit es
  wie ein echter Bildschirm wirkt) — bei scrollbaren Seiten zusätzlich optional `fullPage` als „lang".
- **Tablet/Handy:** Viewport setzen.
- **Bild-Hygiene (vor jedem Shot per CSS/JS injizieren):**
  - Scrollbars ausblenden (`::-webkit-scrollbar { display:none }`).
  - Cursor verstecken, Hover-/Fokus-Artefakte vermeiden.
  - Caret/Text-Selektion entfernen, evtl. Tooltips schließen.
- Dateinamen: `docs/screenshots/<page>[-<viewport>].webp` (z.B. `monitor-live.webp`,
  `monitor-live-mobile.webp`).

### Bildformat & Gewicht
- **Primär WebP** (Quality ~82) statt PNG → ~70 % kleiner, GitHub rendert es nativ.
  ~17 Seiten × bis 3 Viewports × retina sonst mehrere MB.
- Thumbs ebenfalls WebP.
- Verlustfrei nachoptimieren wo PNG nötig: `oxipng`/`sharp`.

### Hero-Assets (oben im README)
- **Animiertes Live-Hero (GIF + WebM):**
  - Während laufendem Bus N Frames der Monitor-Live-Tabelle capturen (z.B. 1 Frame/300 ms,
    ~6–8 s Loop) → `ffmpeg` → **WebM** (klein, scharf) **+ GIF-Fallback** (für Clients ohne WebM).
  - Im README via `<picture>`/`<video autoplay loop muted playsinline>` mit GIF-`<img>`-Fallback.
  - Loop nahtlos halten (gleicher Start/Endzustand wenn möglich) — sonst Hard-Cut akzeptieren.
- **Dark-Hero-Standbild:** ein 1920×1080-Shot des Monitors im **Dark-Theme** (Light per
  `localStorage` temporär auf `dark` umschalten nur für diesen einen Shot).

### Thumbnails
- Aus jedem großen PNG ein Thumb generieren (Breite ~480–600px) → `docs/screenshots/thumbs/`.
- Tooling: `sharp` (npm) im selben Node-Script, oder Playwright-Resize, oder ImageMagick falls
  vorhanden. `sharp` ist am robustesten/plattformunabhängig.

### README-Einbindung
- **Hero ganz oben** (über/unter Titel): animiertes Live-Hero + Dark-Standbild.
  - `<video autoplay loop muted playsinline poster="hero-dark.webp"><source src="hero.webm"></video>`
    mit GIF-`<img>`-Fallback. GitHub erlaubt `<video>` im README (gehostete Datei im Repo).
- Neuer Abschnitt **„## Screenshots"** (oder „Gallery") nahe oben.
- **Thumbnail-Grid** via HTML-Tabelle: jede Zelle = Thumb **+ Caption darunter**:
  ```html
  <td align="center">
    <a href="docs/screenshots/monitor-live.webp">
      <img src="docs/screenshots/thumbs/monitor-live.webp" width="260" alt="Monitor – Live-Ansicht">
    </a>
    <br><sub><b>Monitor – Live</b><br>Echtzeit-Telegramme vom Bus</sub>
  </td>
  ```
  Markdown-Bilder können nicht verlinkt+größenbeschränkt zugleich → HTML-`<a><img>` nutzen.
  `<sub>` hält die Caption klein/dezent; `<b>` = Titel, optionale 2. Zeile = Kurzbeschreibung.
- **Caption-Text pro Shot in der Script-Konfig** mitführen (`{name, route, ..., caption}`), damit
  Bild + Unterschrift aus einer Quelle kommen und das Grid generierbar bleibt.
- **`alt`-Texte ausfüllen** (a11y/WCAG aus House Rules + SEO) — kann = Caption-Titel sein.
- Gruppieren: Desktop oben, darunter „Mobile/Tablet". Light-Theme.
- Pfade relativ (`docs/screenshots/...`), damit sie auf GitHub rendern.

## Umsetzungsschritte (für die neue Session)

1. Voraussetzungen prüfen/herstellen: Apps laufen, Projekt **neu importiert** (v0.6.0-Parser),
   Bus verbunden (`/api/knx/status` → connected), Light-Theme.
2. Auth klären: `AuthController.cs`/`auth.service.ts` lesen → Register-Endpoint? JWT-localStorage-Key?
   Screenshot-User anlegen, Login-Flow für Playwright festlegen.
3. `npm i -D playwright sharp` (oder eigenes `tools/screenshots/`-Mini-Projekt mit `package.json`,
   damit das Frontend-`package.json` sauber bleibt). `npx playwright install chromium`.
4. Script `tools/screenshots/shoot.mjs`:
   - Konfig: Liste {name, route, viewport(s), prep-Aktionen (Klicks/Selektoren), warte-Selector}.
   - Login einmal, Storage-State wiederverwenden.
   - Theme/Sprache via `localStorage` setzen.
   - Bus-Laufzeit/Settle für Live + Charts.
   - Shots + Thumbs schreiben.
5. **Live füllen:** auf **Row-Count ≥ N im Grid** warten (`waitForFunction`), nicht auf Zeit.
6. **Hero bauen:** Live-Frames capturen → `ffmpeg` → WebM + GIF. Dark-Hero-Standbild (Theme
   temporär `dark`).
7. **Konvertieren/Optimieren:** alle Shots → WebP (q~82) + Thumbs; `oxipng`/`sharp` wo PNG nötig.
8. README einfügen: Hero oben (`<video>` + GIF-Fallback) + Thumbnail-Grid (HTML-Tabelle, `alt`-Texte).
9. **Sichtprüfung + Privacy-Check:** Crops, leere States, Theme korrekt, **keine realen IPs/Klarnamen**
   sichtbar. Nachschießen/maskieren wo nötig.
10. Commit: `docs/screenshots/**` + README. (Repo nicht aufblähen — WebP + Kompression.)

## Offene Punkte / Risiken

- **Bildgewicht:** → WebP (q~82) + Thumbs entschärft das Hauptrisiko. Hero-GIF kann groß werden →
  WebM primär, GIF nur als Fallback klein halten (kurzer Loop, reduzierte Palette/FPS).
- **Privacy (echter Bus):** reale IPs/phys. Adressen/GA-Klarnamen → Sichtprüfung + ggf. Blur/
  Demo-Projekt **vor** Commit. **Blocker bis erledigt.**
- **Hero-Loop:** nahtloser Loop schwer bei Live-Daten → Hard-Cut akzeptieren oder kurzen ruhigen
  Ausschnitt wählen.
- **Setup-Seite** nur auf leerer DB sichtbar → separater Lauf auf frischer DB oder weglassen.
- **Leere/instabile States:** Live braucht echten Bus; wenn Gateway nicht erreichbar →
  Live als Empty-/Connecting-State dokumentieren.
- **Mobile-Abdeckung:** „wichtigste Masken" — finale Auswahl in der Session bestätigen.
- **DptConverter/Detail-Used-by** korrekt nur nach v0.6.0-Reimport (Geräteadressen area.line).
- **Keine Secrets/own-Samples** committen (nur die generierten Bilder + README).

## Optional / später
- **CI-Regen bei Release:** Screenshots im `/release`-Skill oder eigenem Workflow neu generieren,
  damit README nie stale wird (braucht headless Bus-Mock oder Seed — mit echtem Bus in CI schwer).

## Referenzdateien

- Routes: `frontend/src/app/app.routes.ts`
- Theme/Sprache: `frontend/src/app/core/services/theme.service.ts`, `core/i18n/language.service.ts`
  (localStorage-Keys verifizieren)
- Auth: `backend/KnxMonitor.Api/Controllers/AuthController.cs`,
  `backend/KnxMonitor.Infrastructure/Data/DbInitializer.cs`,
  `frontend/src/app/core/services/auth.service.ts` (+ JWT-Interceptor)
- Status: `GET /api/knx/status`
- README-Zielabschnitt: oben, neuer „## Screenshots".
