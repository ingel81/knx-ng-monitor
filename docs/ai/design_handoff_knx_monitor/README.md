# Handoff: KNX-NG-Monitor — Vereinheitlichtes Designsystem & UI

> **Sprache:** Dieses Dokument ist auf Deutsch, weil das Produkt und das Team deutschsprachig sind. Code-Bezeichner sind englisch (bestehende Konvention).

---

## 1. Overview / Zweck

Der **KNX-NG-Monitor** ist ein Browser-Tool, das KNX-Bus-Telegramme **live** (Stream) und als **History** (Datenbank-Abfrage) anzeigt. Ziel dieses Handoffs ist die **Vereinheitlichung und Modernisierung** der bestehenden Oberfläche:

- **Ein gemeinsames Grid** für Live View *und* History (heute zwei divergierende Implementierungen mit unterschiedlicher Filter-UX).
- **Eine einheitliche Such-/Filtersprache** (heute: Inline-Spaltenfilter auf Live, separate Filterleiste auf History).
- **Konsistentes, eigenständiges Design** (heute: SCSS definiert ein Dark-Theme, die Oberfläche ist faktisch hell → Widerspruch).
- **Voll responsiv** für Desktop, Tablet (quer + hoch) und Smartphone — gleichwertig.
- **Privat *und* professionell** einsetzbar.
- Zwei Theme-Modi: **Light (Standard)** und **Console (Dark)**.

Das visuelle Konzept heißt intern **„Instrument"**: warmes Papier-Off-White als Bühne, tiefes Petrol/Teal als einzige Markenfarbe, Monospace für alle technischen Daten (Zeit, Adressen, DPT, Rohwerte) — der Look eines präzisen Messgeräts statt eines SaaS-Dashboards.

---

## 2. Über die Design-Dateien (WICHTIG)

Die Dateien unter **`design-source/`** sind **Design-Referenzen, erstellt in HTML/CSS/React** — Prototypen, die *Aussehen und Verhalten* demonstrieren. Sie sind **kein Produktionscode zum 1:1-Kopieren**.

**Die Aufgabe ist, diese Designs im bestehenden Codebase nachzubauen** — siehe Ziel-Stack unten — mit dessen etablierten Mustern und Bibliotheken. Die HTML-Prototypen nutzen React + inline-Babel nur als schnelles Demonstrationsmittel; die Zielumgebung ist **Angular**.

### Ziel-Stack (aus den hochgeladenen Projektdateien erkannt)
- **Framework:** Angular (Standalone-Components, `@if`/`routerLink` Syntax → Angular 17+)
- **UI-Library:** Angular Material (`mat.define-theme`, `theme-type: dark`, `mat.$blue-palette`)
- **Tabelle:** AG-Grid (Tooltip-Fixes in `_mixins.scss` deuten darauf hin)
- **Styling:** SCSS mit zentralen Tokens in `_variables.scss` + Mixins in `_mixins.scss`
- **Fonts:** aktuell Roboto + Material Icons (via `@fontsource`)
- **Layout:** `app-layout` (Header + `router-outlet`), `overflow: hidden` auf allen Ebenen, nur Inhalts-Bereiche scrollen

> Wenn der Codebase abweicht, gilt: **bestehende Patterns gewinnen.** Dieses Doc liefert die Design-Wahrheit (Tokens, Layout, Verhalten); die Umsetzung folgt den Konventionen des realen Repos.

---

## 3. Fidelity

**High-Fidelity (hifi).** Farben, Typografie, Abstände, Radien, Schatten, Interaktionen und Responsive-Verhalten sind final und exakt. Alle Werte stehen als Tokens in `design-source/tokens.css` und unten in §9. Pixel-genau nachbauen mit den vorhandenen Angular/Material-Bausteinen.

**Migrations-Philosophie:** Das Design ersetzt das Material-Default-Look-and-Feel durch ein eigenes Token-Set. Angular Material wird **nicht** rausgeworfen — stattdessen wird Materials Theme auf diese Tokens *gemappt* (siehe §12), und Komponenten werden über die Tokens umgestylt.

---

## 4. Was liegt im Paket

```
design_handoff_knx_monitor/
├── README.md                      ← dieses Dokument (self-sufficient)
├── _tokens.scss                   ← alle Design-Tokens als SCSS, drop-in für euer styles/
├── angular-material-theme.example.scss  ← Beispiel: Material-Theme auf Tokens mappen
├── screenshots/                   ← gerenderte Referenz-Screenshots (Light+Dark, alle Geräte)
│   ├── desktop-light-1..4 …        ← Live · History · Projects · Settings (Light)
│   ├── desktop-dark-1..4 …         ← dieselben Seiten im Console-Dark-Theme
│   └── mobile-*-…                  ← Mobil: Live-Cards (dark), History-Cards, Projects, Settings
└── design-source/                 ← die HTML/React-Referenz-Prototypen
    ├── KNX Design System.html      ← visuelle Doku/Masterplan (im Browser öffnen)
    ├── KNX Monitor Prototype.html  ← interaktiver Prototyp (im Browser öffnen)
    ├── tokens.css                  ← Tokens als CSS-Custom-Properties (Quelle der Wahrheit)
    ├── components.css              ← Komponenten-Klassen (Buttons, Chips, Grid, …)
    ├── app.css                     ← App-Shell, Toolbar, Sheet, Dark-Theme-Overrides, Pages
    ├── doc.css / doc-demos.js      ← nur für die Doku-Seite
    ├── icons.jsx                   ← Inline-SVG-Icon-Set (Pfaddaten 1:1 übernehmbar)
    ├── grid.jsx                    ← das gemeinsame Grid + Detail-Sheet (Referenz-Logik)
    ├── filters.jsx                 ← Filterleiste (Suche, Chips, Spalten-Manager)
    ├── pages.jsx                   ← Projects + Settings + Import-Modal
    ├── app.jsx                     ← Shell, Navigation, Live/History, Theme-/Viewport-State
    └── data.js                     ← Beispiel-Telegramme + Generator (nur Demo-Daten)
```

**Tipp:** Beide HTML-Dateien lokal im Browser öffnen. Im Prototyp oben rechts den **Geräte-Umschalter** (Desktop/Tablet/Mobil) und den **Theme-Toggle** (Mond/Sonne) nutzen, um alle Zustände zu sehen.

---

## 5. Globaler Aufbau / App-Shell

```
┌───────────────────────────────────────────────────────────────┐
│ HEADER (56px, dunkles Petrol, fix)                              │
│  [KNX·NG Monitor]   Live View · History · Projects · Settings   │  ← Top-Nav (Desktop/Tablet)
│                                              joerg  👤  ⎋        │
├───────────────────────────────────────────────────────────────┤
│ MAIN (flex:1, overflow hidden — nur Kinder scrollen)            │
│  ┌─ Seite ────────────────────────────────────────────────┐    │
│  │ TOOLBAR (kontextuell, weiß, fix)                         │    │
│  │ FILTERLEISTE (paper-sunk, fix) — nur Live & History      │    │
│  │ GRID / CONTENT (flex:1, scrollt)                         │    │
│  └──────────────────────────────────────────────────────────┘  │
├───────────────────────────────────────────────────────────────┤
│ BOTTOM-NAV (nur Mobil, 4 Tabs)                                  │
└───────────────────────────────────────────────────────────────┘
```

- **Höhen-Regel (vom Bestand übernehmen):** `app-layout { height:100vh; overflow:hidden }`, Header `flex:none`, Main `flex:1; min-height:0; overflow:hidden`. Nur der Grid-/Content-Bereich scrollt.
- **Header-Hintergrund:** `linear-gradient(180deg, var(--header-bg-2), var(--header-bg))` → `#0F3A35 → #0C2B28`. (Ersetzt den alten Blau-Verlauf `#1e3c72→#2a5298`.)
- **Brand-Wortmarke:** Text „**KNX**·NG" mit `KNX` in `--live` (#18A89A) und `·NG` in Weiß, daneben kleines `MONITOR`-Label in `--header-ink-2`, uppercase, letter-spacing .08em. *(Das offizielle KNX-Logo-SVG ist nicht enthalten — euer bestehendes `KNX_logo.svg` weiterverwenden; die Wortmarke hier ist ein neutraler Platzhalter.)*

### Navigation
- **Top-Nav (≥768px):** horizontale Tab-Leiste. Items: `Live View` (Icon `live`/Auge), `History` (`history`), `Projects` (`folder`), `Settings` (`settings`).
- **Aktiver Tab:** `background: rgba(24,168,154,.16)`, Text weiß, `box-shadow: inset 0 -2px 0 var(--live)` (Unterstrich), `border-radius: 5px 5px 0 0`.
  - ⚠️ **Lektion aus dem Prototyp:** Auf dem Nav-Item **nur `color` transitionen, nicht `all`** — sonst „klebt" der aktive Hintergrund beim Wechsel optisch am alten Item fest.
- **Bottom-Nav (<768px):** 4 gleich breite Tabs, Icon (21px) über 2xs-Label, aktiver Tab in `--brand`. Top-Nav wird ausgeblendet.

---

## 6. Screens / Views — im Detail

### 6.1 Live View

**Zweck:** Echtzeit-Anzeige eintreffender Bus-Telegramme.

**Toolbar (links → rechts):**
| Element | Verhalten |
|---|---|
| **Trennen / Verbinden** | Toggle. Verbunden → roter `--err` „Trennen" mit `disconnect`-Icon. Getrennt → primärer „Verbinden" mit `link`-Icon. |
| **Pause / Fortsetzen** | Outline-Button, `pause`/`play`-Icon. Stoppt den Stream-Append. Disabled wenn getrennt. |
| **Auto-scroll** | Toggle-Switch. Bei aktiv springt die Liste auf die neueste Zeile (oben). |
| *(rechts)* **Live-Status** | `--live`-Punkt mit Puls-Animation; Text „Live" / „Pausiert" / „Getrennt". |
| **Count-Badge** | Anzahl gefilterter Zeilen, Mono, `--brand-tint`. |
| **Dichte-Toggle** | Segmented „Dicht / Komfort" (Zeilenhöhe 36 ↔ 48px). |
| **Clear** | Ghost-Button, leert den lokalen Puffer. |
| **Export** | Outline-Button, `download`-Icon. |

- **Filterleiste:** siehe §7.
- **Grid:** siehe §8. Neueste Zeile oben. Neue Zeilen flashen kurz (`is-new`, 0.5s, `--row-flash`).
- **Stream-Logik (Referenz):** alle ~950 ms 1–2 neue Telegramme vorne anhängen, Puffer auf max. 600 Zeilen kappen. Im echten Backend: WebSocket/SSE-Feed.

### 6.2 History

**Zweck:** Durchsuchbare, sortierbare Historie aller je empfangenen Telegramme.

**Toolbar:** Titel „**History**" (xl, semibold) · Count-Badge mit **Gesamtzahl** (z. B. `14.416`) · bei aktiven Filtern zusätzliches Badge „`<n>` gefiltert" mit `filter`-Icon · rechts Dichte-Toggle + **Export CSV** (Outline).

- **Filterleiste:** identisch zu Live (§7), zusätzlich greifen Zeitbereich-Picker.
- **Grid:** identisch zu Live, aber **Zeit-Spalte zeigt Datum+Zeit** (`09.06.26, 12:56:07,094`) und Header heißt „Zeitpunkt". **Sortierung** ist hier aktiv (Klick auf Spaltenkopf → asc/desc, Pfeil-Indikator).
- **Daten:** echte DB-Abfrage mit Server-seitiger Filterung/Pagination. (Prototyp rendert 800 Zeilen clientseitig als Demo; Gesamtzahl wird separat angezeigt.)

### 6.3 Projects

**Zweck:** ETS-Projekte verwalten; das aktive Projekt liefert die Klartext-Namen für Gruppenadressen.

**Toolbar:** Titel „**Projects**" + Count-Badge · rechts **Projekt importieren** (Primär, `upload`-Icon).

**Tabelle (Desktop/Tablet)** — Klasse `.proj-table`, Karten-Container mit `--r-lg`, Kopf `--paper-sunk`:
| Spalte | Inhalt | Stil |
|---|---|---|
| Name | Projektname | semibold |
| Datei | `*.knxproj` | Mono, `--fs-sm`, `--ink-2` |
| Import-Datum | nur Desktop | Mono, `--fs-sm` |
| Statistik | `<sitemap> 841 GAs` · `<monitor> 94 Geräte` | Zahlen Mono/semibold |
| Aktiv | Toggle-Switch (nur **eines** aktiv) | aktive Zeile bekommt `--brand-tint`-Hintergrund |
| Aktionen | `eye` (GAs ansehen) · `trash` (löschen, `--err`) | Icon-Ghost-Buttons |

**Karten (Mobil)** — `.proj-card`: oben Name+Datei links / Aktiv-Toggle rechts; darunter Meta-Zeile (GAs, Geräte, Datum, Löschen).

**Import-Modal** (`.modal`, max 440px, zentriert):
- Titel „Projekt importieren", Subtext „ETS-Projekt (.knxproj) auswählen…".
- **Dropzone** (gestrichelt): Klick/Drop wählt Datei; gefüllt → solider Rahmen + `--brand-tint` + „N GAs · M Geräte erkannt".
- Fortschrittsbalken beim Import (`.import-progress`).
- Footer: „Abbrechen" (Ghost) · „Import starten" (Primär, disabled bis Datei gewählt).
- Schließen via Backdrop-Klick oder **Esc**.

### 6.4 Settings

**Zweck:** Verbindung, Aufzeichnung und Darstellung konfigurieren. Zentrierte Spalte, max. 880px, scrollt.

Drei Karten (`.set-card`, je mit Icon-Badge-Kopf):

1. **Darstellung** (`sun`-Icon)
   - *Farbschema* → Segmented **Light / Console** (schaltet Theme global, siehe §11).
   - *Grid-Dichte* → Segmented **Dicht / Komfort** (globaler Default für beide Grids).
2. **KNX Gateway** (`wifi`-Icon)
   - Felder (2-spaltig, mobil 1-spaltig) mit Präfix-Icon, Label, Hint:
     - *IP-Adresse* (`globe`) — Hint „IP-Adresse deiner KNX-Schnittstelle"
     - *Port* (`swap`) — Hint „Standard: 3671 (KNXnet/IP)"
     - *Physikalische Adresse* (`plug`) — Hint „Format: Bereich.Linie.Gerät (z. B. `1.0.58`)"
   - Info-Zeile (`--read-tint`, linke Akzentleiste): „**Verbindung:** IP Tunneling (UDP) · **Protokoll:** KNXnet/IP"
   - Aktionen: **Speichern** (Primär) · **Verbindung testen** (Outline → zeigt grünen `--ok`-Status „Erreichbar · 12 ms") · rechts **Zurücksetzen** (Ghost).
3. **Datenaufzeichnung** (`database`-Icon)
   - *Ringpuffer-Größe* (`cpu`) — Hint „Max. Telegramme im Live-SQLite-Puffer"
   - *Archiv-Aufbewahrung (Tage)* (`calendar`) — Hint „Leer = unbegrenzt aufbewahren"
   - Row-Toggle **Langzeit-Archiv (NDJSON + gzip)** — Sub „Erfasst **jedes** Telegramm in Tagesdateien unter `data/archive/`"
   - Aktion: **Speichern**.

---

## 7. Such- & Filtersystem (einheitlich, beide Grids)

**Drei Ebenen — „Tippen → Klicken → Verfeinern":**

1. **Globale Suche** (`.knx-input--search`, `search`-Präfix-Icon, Clear-X): Volltext über **alle** Felder (Zeit, Quelle, Ziel, Name, DPT, Typ, Rohwert, dekodierter Wert, Einheit). Sofort-Filter beim Tippen (im echten Backend: debounce ~250 ms + Server-Query).
2. **Quick-Chips** (`.knx-chip`, scrollbare Reihe):
   - **Zeitbereich:** `Alle` · `Letzte Stunde` · `Heute` · `7 Tage` (Single-Select; `clock`-Icon außer „Alle").
   - **Typ:** `Write` · `Read` · `Response` (Multi-Select, je farbiger Punkt).
   - **Themen/Gewerke:** `Temperatur` · `Leistung` · `Licht` · `Beschattung` (Multi-Select, Regex auf Name/Einheit — im echten System besser über GA-Struktur/Räume).
   - Aktiver Chip: `--brand-tint` Hintergrund, `--teal-200` Rand, `--teal-800` Text.
3. **Spalten-Manager** (Button „Spalten", `columns`-Icon → Popover): Checkbox-Liste zum Ein-/Ausblenden; `Name` und `Wert` sind gesperrt (immer sichtbar); Drag-Handle für Reihenfolge (im Prototyp angedeutet, sollte echtes Reorder werden). Persistenz pro Nutzer empfohlen.

- **Reset:** sobald `aktiveFilter > 0`, erscheint „Zurücksetzen" (Ghost, `close`-Icon).
- **Filterlogik (Referenz):** siehe `applyFilters()` in `app.jsx` — Zeitfenster, Typ-Whitelist, Themen-Regex, Volltext-`includes`.

---

## 8. Das gemeinsame Grid (Kernkomponente)

> Live View und History rendern **dieselbe** Grid-Komponente. Unterschiede nur über Props: Datenquelle (Stream vs. Query), `useDateTime` (Zeit vs. Zeitpunkt), Sortierung aktiv (History) und `is-new`-Flash (Live).

### Spalten
| key | Header | Schrift | Inhalt/Format | Min-Breite | Sortierbar |
|---|---|---|---|---|---|
| `time` | Zeit / Zeitpunkt | Mono `--fs-sm`, `--ink-2` | `14:56:02,259` bzw. `09.06.26, 12:56:07,094` | 96 | ja |
| `src` | Quelle | Mono `--fs-sm` | phys. Adresse `1.1.9` | 70 | ja |
| `dst` | Ziel | Mono `--fs-sm` | Gruppenadresse `10/4/46` | 80 | ja |
| `name` | Name | Sans, `--ink` | Klartext; führendes Raum-Tag (EG/OG/DG/UG/KG) als Pill | 220 (grow) | ja |
| `dpt` | DPT | Mono `--fs-sm`, `--ink-2` | `DPST-14-56` | 90 | ja |
| `type` | Typ | Punkt-Label | Write/Read/Response/GroupRead | 96 | ja |
| `raw` | Rohwert | Mono `--fs-sm`, `--ink-3` | Hex `4023D70A` | 90 | nein |
| `val` | Wert | Mono, semibold, **semantisch** | dekodierter Wert + Einheit | 110 | ja |

### Werte-Semantik (Spalte „Wert")
- **Boolean ON / aktiv** → `--val-on` (#2F7A43 grün): `On`, `Auf`, `Ein`
- **Boolean OFF / inaktiv** → `--val-off` (#97A09B grau): `Off`, `Zu`, `Aus`
- **Numerisch** → `--val-num` (= `--ink`): Tabellenziffern, deutsche Formatierung (`29.364.117`, `660,20`), Einheit gedimmt (`--ink-3`, kleiner).
- **Text/String** → `--val-text` (#0F766E Petrol): in Anführungszeichen `"wechselhaft"`.

### Nachrichtentyp-Indikator (`.knx-type`)
Kleiner quadratischer Punkt (7px, radius 2) + Label, je Typ eingefärbt:
- Write `--write` #2F7A43 · Read `--read` #2563A6 · Response `--response` #B26B07 · GroupRead `--groupread` #6B5BB0.
- **Bewusst dezent** — Farbe nur im Punkt+Label, **keine** eingefärbten Zeilen-Hintergründe (im Gegensatz zur alten `_variables.scss`-Idee mit `$msg-*-bg`).

### Zeilen-Zustände
- Höhe `--row-h-compact` 36px (Standard) / `--row-h-cozy` 48px.
- Zebra: gerade Zeilen `--surface-2`.
- Hover: `--brand-tint`.
- Neu (`is-new`): 0.5s Flash von `--row-flash` (Light: mint `#D2EAE5`, Dark: `rgba(36,194,178,.20)`).
- Klick auf Zeile → **Detail-Sheet**.
- Leerzustand: zentriertes `search`-Icon + „Keine Telegramme für diese Filter."
- Sticky Header (`thead th { position:sticky; top:0 }`), uppercase, `--fs-xs`, `--paper-sunk`.

> **Im echten Codebase mit AG-Grid:** Spaltendefinitionen, `cellRenderer` für Typ-Pill und Werte-Semantik, `getRowClass` für Zebra/`is-new`, Header-Sticky standardmäßig. Detail-Sheet als eigenes Overlay/`MatBottomSheet` (mobil) bzw. seitliches Panel (Desktop), **nicht** AG-Grid-Master/Detail nötig.

### Detail-Sheet (`.sheet`)
Klick auf eine Zeile öffnet ein Detail-Panel mit **allen** Feldern:
- **Mobil:** Sheet von unten (`slideup`, Drag-Handle oben).
- **Desktop/Tablet:** Panel von rechts (`slidein`, volle Höhe, linke Trennlinie).
- Inhalt: Name (mit Raum-Tag) + Typ-Pill · großer dekodierter Wert (`--fs-3xl`, Mono) + Einheit · darunter Feld-Grid (Zeitpunkt, Quelle, Ziel-GA, DPT, Rohwert), je Label (xs, uppercase) + Mono-Wert.
- Schließen: Backdrop-Klick, X-Button, **Esc**.

---

## 9. Responsive-Strategie

| Breakpoint | Layout | Bedienung |
|---|---|---|
| **≥1280 Desktop** | Alle 7–8 Spalten, Toolbar einzeilig, Top-Nav | Maus+Tastatur, Hover, Spalten ordnen, Detail-Panel rechts |
| **768–1279 Tablet** | Reduzierte Spalten (`time, dst, name, type, val`), Toolbar ggf. zweizeilig, Top-Nav | Touch-Targets ≥44px, Detail-Panel rechts |
| **<768 Mobil** | **Karten statt Zeilen** (`.knx-mcard`), Bottom-Nav, Suche immer sichtbar | Tippen öffnet Detail-Sheet von unten |

- **Mobile Telegramm-Karte (`.knx-mcard`):** obere Zeile Name (links) + dekodierter Wert (rechts, `--fs-lg` Mono); Meta-Zeile (Zeit · Ziel-GA · Typ-Punkt), durch kleine Punkte getrennt.
- Bestehender Breakpoint-Satz aus `_variables.scss` ist kompatibel: `xs 480 / sm 768 / md 1024 / lg 1280 / xl 1440`.
- **Touch:** alle interaktiven Elemente ≥44px Hit-Target auf Touch-Viewports.

---

## 10. Interaktionen & Verhalten (Zusammenfassung)

- **Navigation:** Top-/Bottom-Nav setzt aktive Seite; Content + aktiver Indikator folgen derselben State-Variable.
- **Live-Stream:** append vorne, Puffer-Cap, `is-new`-Flash 0.5s; Pause friert ein; Auto-scroll hält neueste Zeile sichtbar.
- **Sortierung (History):** Spaltenkopf-Klick → asc/desc Toggle, Pfeil-Indikator (`arrowUp`/`arrowDown`).
- **Filter:** Sofort-Anwendung; aktive Chips hervorgehoben; Reset-Button bei ≥1 aktivem Filter.
- **Spalten-Popover:** Outside-Click & Esc schließen; `Name`/`Wert` gesperrt.
- **Import:** Datei wählen → erkannte GAs/Geräte anzeigen → „Import starten" → Progress → neue Zeile + Modal schließt.
- **Verbindung testen:** Klick → kurzer Erfolgs-Status (im echten System: echter Roundtrip + Fehlerzustand `--err`).
- **Theme-Toggle:** schaltet `theme-light`/`theme-dark` global (Persistenz empfohlen, siehe §11).
- **Animationen:** alle 0.12–0.22s, `ease`/`cubic-bezier(.2,.8,.2,1)`. **Respektiere `prefers-reduced-motion`** (Live-Puls & Flash deaktivieren).
- **Transition-Fallstrick:** Nav-Items **nicht** `transition: all` — nur `color` (sonst klebt der aktive Hintergrund optisch fest).

---

## 11. Theming (Light + Console-Dark)

- **Mechanik:** Alle Farben sind CSS-Custom-Properties auf `:root`. Das **Console-Dark-Theme überschreibt dieselben Properties** auf einem Scope-Selector (`.knx-app.theme-dark` im Prototyp). In Angular: Theme-Klasse auf den App-Root (oder `<html data-theme>`), Tokens als CSS-Variablen → komplette UI inkl. Material folgt automatisch.
- **Default:** Light. **Console-Dark** für Dauerbetrieb/Leitwarte/Nacht.
- **Persistenz:** Theme + Dichte in `localStorage` (oder User-Settings im Backend) speichern und beim Laden anwenden.
- Dark überschreibt: Oberflächen, Tinte, Linien, Brand (heller: `#19A395`), Live (`#24C2B2`), Nachrichten-/Werte-Farben (aufgehellt für Kontrast), `--row-flash`, Schatten. **Vollständige Dark-Werte:** siehe `app.css` Block `.knx-app.theme-dark` und `_tokens.scss` (Dark-Sektion).
- Eine **dritte Richtung „Studio"** (heller, wohnlicher, mehr Whitespace) ist im Designsystem-Doc als Option skizziert, aber **nicht** implementiert — optional für später.

---

## 12. Design-Tokens (vollständig)

> Quelle der Wahrheit: `design-source/tokens.css`. Drop-in als SCSS: `_tokens.scss` (in diesem Paket). Mapping auf Angular Material: `angular-material-theme.example.scss`.

### Farben — Light
| Token | Hex | Einsatz |
|---|---|---|
| `--paper` | `#F0F1ED` | App-Hintergrund |
| `--paper-sunk` | `#E8EAE4` | Filterleiste, Tabellenkopf-Zone |
| `--surface` | `#FFFFFF` | Karten, Grid-Zeilen |
| `--surface-2` | `#FAFAF7` | Zebra |
| `--surface-3` | `#F4F5F1` | Hover, inaktiver Chip |
| `--ink` | `#16201D` | Primärtext |
| `--ink-2` | `#51605B` | Sekundärtext |
| `--ink-3` | `#8A958F` | gedimmt/Placeholder |
| `--line` | `#E2E5DE` | Hairlines, Zeilentrenner |
| `--line-2` | `#D2D6CD` | stärkere Trenner |
| `--line-strong` | `#B9BFB5` | Input-Rahmen |
| `--brand` / `--teal-700` | `#0F766E` | Markenfarbe (primär) |
| `--brand-hover` / `--teal-800` | `#0B574F` | Hover primär |
| `--brand-tint` / `--teal-50` | `#ECF5F3` | aktive Chips, Hover-Zeile |
| `--teal-100` | `#D2EAE5` | Akzentflächen, Row-Flash (Light) |
| `--teal-200` | `#A9D6CE` | aktive Ränder |
| `--teal-500` | `#15897E` | — |
| `--teal-900` | `#093F39` | dunkelstes Petrol |
| `--header-bg` / `--header-bg-2` | `#0C2B28` / `#0F3A35` | Header-Verlauf |
| `--header-ink` / `--header-ink-2` | `#DCE7E3` / `#8FAAA3` | Header-Text |
| `--live` | `#18A89A` | Live-Signal/Puls |

### Semantik / Status
| Token | Hex (Light) | Hex (Dark) |
|---|---|---|
| `--write` | `#2F7A43` | `#5FCF8A` |
| `--read` | `#2563A6` | `#5AA9E6` |
| `--response` | `#B26B07` | `#E0A94A` |
| `--groupread` | `#6B5BB0` | `#A99BE0` |
| `--ok` | `#2F7A43` | `#5FCF8A` |
| `--warn` | `#B26B07` | `#E0A94A` |
| `--err` | `#BC3B2C` | `#F0726A` |
| `--val-on` | `#2F7A43` | `#5FCF8A` |
| `--val-off` | `#97A09B` | `#6A7D77` |
| `--val-num` | `#16201D` | `#E6EFEC` |
| `--val-text` | `#0F766E` | `#3FD0C0` |

### Farben — Console-Dark (Oberflächen/Tinte)
`--paper #0E1A18` · `--paper-sunk #0A1413` · `--surface #14201E` · `--surface-2 #182523` · `--surface-3 #1E2C29` · `--ink #E6EFEC` · `--ink-2 #9FB3AD` · `--ink-3 #6E837D` · `--line rgba(255,255,255,.07)` · `--line-2 rgba(255,255,255,.11)` · `--line-strong rgba(255,255,255,.20)` · `--brand #19A395` · `--live #24C2B2` · `--row-flash rgba(36,194,178,.20)`.

### Typografie
- **UI-Font:** `'IBM Plex Sans'` (400/500/600/700) — ersetzt Roboto.
- **Daten-Font:** `'IBM Plex Mono'` (400/500/600) — für Zeit, Adressen, DPT, Rohwert, Werte. `font-feature-settings: "tnum" 1` (Tabellenziffern).
- **Scale:** `--fs-2xs 11` · `xs 12` · `sm 13` · `md 14` · `base 15` · `lg 17` · `xl 21` · `2xl 28` · `3xl 40` (px).
- **Gewichte:** normal 400 · medium 500 · semi 600 · bold 700.
- Bezugsquelle Fonts: Google Fonts (`IBM+Plex+Sans`, `IBM+Plex+Mono`) oder `@fontsource/ibm-plex-sans` + `@fontsource/ibm-plex-mono` (passt zum bestehenden `@fontsource`-Setup).

### Abstände (4er-Raster)
`--sp-1 4` · `2 8` · `3 12` · `4 16` · `5 20` · `6 24` · `8 32` · `10 40` · `12 48` (px).

### Radius
`--r-xs 3` · `sm 5` · `md 8` · `lg 12` · `xl 16` · `full 999` (px).

### Schatten (weich, geringe Streuung)
- `--sh-1` `0 1px 2px rgba(16,32,29,.06), 0 1px 1px rgba(16,32,29,.04)`
- `--sh-2` `0 2px 6px rgba(16,32,29,.08), 0 1px 2px rgba(16,32,29,.05)`
- `--sh-3` `0 8px 24px rgba(16,32,29,.12), 0 2px 6px rgba(16,32,29,.06)`
- `--sh-pop` `0 12px 40px rgba(9,40,36,.22)`
- (Dark: dunklere `rgba(0,0,0,…)`-Varianten, siehe `_tokens.scss`.)

### Grid-Metriken
`--row-h-compact 36px` · `--row-h-cozy 48px` · `--row-flash` (s. o.).

### Motion
`--t-fast 0.12s ease` · `--t-base 0.2s ease`.

---

## 13. Komponenten-Inventar (Klassen → Beschreibung)

> CSS in `components.css` (themen-übergreifend) + `app.css` (Shell/Pages). In Angular als Komponenten/Direktiven nachbauen; Material-Pendants über Tokens umstylen.

- **Buttons** `.knx-btn` + Modifier `--primary` / `--ghost` / `--outline` / `--danger` / `--sm` / `--icon`. Höhe 36 (sm 30), Radius `--r-sm`, Icon 17px. Varianten für dunklen Header: `.on-dark`.
- **Chips** `.knx-chip` (+ `.is-active`), 28px, Pille, optional Farbpunkt; Filter- & Quick-Select-Element.
- **Inputs** `.knx-input` (+ `--search` mit Präfix-Icon, `.field-clear`), 38px, Fokus-Ring `0 0 0 3px var(--brand-tint)`. `select.knx-input` mit eigenem Chevron. `.knx-label` (xs, medium).
- **Toggle** `.knx-switch` (38×22 Track, weißer Knopf, aktiv `--brand`).
- **Segmented** `.knx-seg` (Dichte, Theme, etc.).
- **Badges** `.knx-badge` (+ `--count` Mono/`--brand-tint`).
- **Status** `.knx-status` (`--ok` / `--err` / `--live` mit Puls; LED-Punkt + Text).
- **Nachrichtentyp** `.knx-type--write|read|response|groupread`.
- **Card** `.knx-card`.
- **Grid** `.knx-grid` (+ `.is-cozy`); Mobile-Karte `.knx-mcard`.
- **Detail-Sheet** `.sheet` (+ Backdrop, responsives Andocken).
- **Popover** `.popover` (Spalten-Manager).
- **Modal** `.modal` (+ `.dropzone`, `.import-progress`).
- **Settings** `.set-card` / `.set-head` / `.set-grid` / `.set-field` / `.set-row` / `.info-line`.
- **Projects** `.proj-table` / `.proj-card` / `.proj-stat`.
- **Scrollbar** `.knx-scroll` (slim, hell/dunkel adaptiv).

---

## 14. State-Management (Anforderungen)

| State | Scope | Hinweise |
|---|---|---|
| `page` (live/history/projects/settings) | App | Router in Angular (bestehende Routes nutzen). |
| `theme` (light/dark) | App, persistiert | `localStorage`/User-Settings; Klasse auf Root. |
| `density` (compact/cozy) | App, persistiert | globaler Grid-Default. |
| Live: `connected`, `paused`, `autoscroll`, `rows[]`, `newIds` | Live View | rows aus WebSocket/SSE; Cap. |
| History: `rows[]`, `sort{key,dir}`, Pagination | History | Server-seitige Query/Sort/Page. |
| Filter (je Seite): `q`, `timeRange`, `types[]`, `topics[]`, `visibleCols[]` | pro Grid | History-Filter → Server-Query; persistieren optional. |
| Projects: `projects[]`, `activeId`, Import-Status | Projects | aktives Projekt liefert GA-Namen. |
| Settings: Gateway (`ip`,`port`,`pa`), Recording (`buffer`,`retention`,`archive`) | Settings | gegen Backend speichern. |
| `detailRow` | App/Seite | offenes Detail-Sheet. |

Datenmodell eines Telegramms (siehe `data.js`): `{ ts, time, datetime, src, dst, name, dpt, type, raw, value, valKind, unit, room }`.

---

## 15. Assets & Icons

- **Icons:** eigenes Inline-SVG-Stroke-Set (1.75px, runde Enden), Pfaddaten in `icons.jsx` (`KNX_ICONS`). **Ersetzt die Material-Icons-Mischung** für UI-Glyphen (Konsistenz über alle Größen). Vorhandene Glyphen u. a.: live, history, folder, settings, search, filter, download, pause, play, clear, columns, chevron, close, plus, disconnect, link, user, logout, clock, arrowUp/Down, sliders, check, drag, eye, trash, upload, globe, plug, save, cpu, calendar, refresh, wifi, sitemap, monitor, swap, sun, moon, database. An `currentColor` gekoppelt → automatische Einfärbung je Kontext.
  - In Angular: als SVG-Sprite oder eigene `mat-icon`-Registrierung (`MatIconRegistry.addSvgIcon…`) einbinden.
- **Logo:** Wortmarke ist Platzhalter — euer bestehendes `KNX_logo.svg` nutzen. Das offizielle KNX-Logo ist **nicht** Teil dieses Pakets (Markenrecht).
- **Fonts:** IBM Plex Sans + IBM Plex Mono (Google Fonts / `@fontsource`).
- **Keine Bilddateien** im Design — alles CSS/SVG.

---

## 16. Empfohlene Umsetzungs-Reihenfolge (Masterfahrplan)

1. **Foundation:** `_tokens.scss` einziehen, Material-Theme darauf mappen (`angular-material-theme.example.scss`), Fonts umstellen (Roboto → IBM Plex), Light als Default, Console-Dark als Theme-Klasse.
2. **Shell & Navigation:** Header (Petrol), Top-Nav + Bottom-Nav (Mobil), Routing, Theme-/Dichte-Persistenz.
3. **Gemeinsames Grid:** AG-Grid-Spaltendefs + Renderer (Typ-Pill, Werte-Semantik, Raum-Tag), Zebra/Hover/`is-new`, Sticky Header, Dichte; Detail-Sheet (Bottom-Sheet mobil / Side-Panel Desktop). Live & History auf diese eine Komponente umstellen (Props: Quelle, `useDateTime`, Sort).
4. **Filterleiste:** globale Suche + Quick-Chips + Spalten-Manager; ersetzt Inline-Spaltenfilter (Live) und alte Leiste (History). History-Filter server-seitig.
5. **Projects & Settings:** Tabelle/Karten + Import-Modal; Settings-Karten inkl. Theme/Dichte-Steuerung.
6. **Feinschliff:** `prefers-reduced-motion`, A11y (Fokus-Ringe sind via `--brand-tint` vorhanden; Tastatur-Nav, ARIA für Toggles/Tabs/Sheet/Modal), Touch-Targets ≥44px, Persistenz, Export (CSV).

---

## 17. Barrierefreiheit (Pflicht-Checkliste)

- Fokus-Sichtbarkeit: `:focus` Ring `0 0 0 3px var(--brand-tint)` auf Inputs; analoge sichtbare Fokuszustände für Buttons/Chips/Tabs.
- Tastatur: Nav-Tabs (Pfeiltasten), Sheet/Modal mit Esc + Fokus-Falle, Popover schließt mit Esc.
- ARIA: Tabs (`role=tab`/`tablist`), Toggle-Switches (`role=switch`/`aria-checked`), Modal (`role=dialog`/`aria-modal`), Sheet analog, Status-LED mit `aria-live` für Verbindungswechsel.
- Kontrast: Tokens sind auf AA ausgelegt; im Dark-Theme wurden Semantikfarben bewusst aufgehellt.
- `prefers-reduced-motion: reduce` → Live-Puls und `is-new`-Flash deaktivieren.

---

## 18. Bekannte Vereinfachungen im Prototyp (≠ Produktion)

- Daten sind synthetisch (`data.js`); History rendert 800 Zeilen clientseitig, Gesamtzahl wird nur angezeigt → **Produktion: Server-Query + Pagination/Virtual-Scroll.**
- Filter laufen clientseitig; Themen-Chips via Regex → **Produktion: über GA-Struktur/Räume und Server-Filter.**
- „Verbindung testen", Import-Progress, Export sind simuliert → **Produktion: echte Endpunkte.**
- Spalten-Reorder ist nur visuell angedeutet → echtes Drag-Reorder + Persistenz implementieren.
- Der **Geräte-/Theme-Umschalter oben in der Vorschau-Leiste** ist reines Demo-Chrome und **nicht Teil des Produkts** (Theme gehört in Settings; Viewport ist real responsiv).

---

## 19. Referenz: Dateien mit dem Design

Alle unter `design-source/`. Primäre Quellen für Werte/Logik:
- **Tokens:** `tokens.css` → gespiegelt in `_tokens.scss`.
- **Komponenten-Look:** `components.css`.
- **Shell/Pages/Dark:** `app.css`.
- **Grid-Logik:** `grid.jsx`. **Filter-Logik:** `filters.jsx` + `applyFilters()` in `app.jsx`. **Projects/Settings/Import:** `pages.jsx`. **Icons:** `icons.jsx`.
- **Visuelle Gesamtschau:** `KNX Design System.html` (Masterplan) und `KNX Monitor Prototype.html` (interaktiv).

---

## 20. Screenshots (Referenz-Renderings)

Im Ordner `screenshots/` (1280×800 Desktop, 390×800 Mobil). Sie zeigen den Soll-Zustand zum pixelgenauen Abgleich:

| Datei | Zeigt |
|---|---|
| `desktop-light-1-liveview.png` | Live View, Light — Toolbar, Filterleiste, volle Tabelle, Werte-Semantik |
| `desktop-light-2-history.png` | History, Light — „Zeitpunkt"-Spalte, Sortier-Indikator, Quick-Chips, „Export CSV" |
| `desktop-light-3-projects.png` | Projects, Light — Tabelle mit Statistik, Aktiv-Toggle, Aktionen |
| `desktop-light-4-settings.png` | Settings, Light — Darstellung / KNX Gateway / Datenaufzeichnung |
| `desktop-dark-1..4-*.png` | dieselben vier Seiten im **Console-Dark**-Theme |
| `mobile-dark-1-liveview-cards.png` | Mobil Live, Dark — Telegramme als Karten, Bottom-Nav |
| `mobile-light-2-history-cards.png` | Mobil History, Light — Karten mit Datum/Zeit-Meta |
| `mobile-light-3-projects.png` | Mobil Projects, Light — Projekt-Karten |
| `mobile-light-4-settings.png` | Mobil Settings, Light — gestapelte Karten (1-spaltig) |

> Hinweis: Das **Detail-Sheet** (Klick auf eine Zeile/Karte) ist in §8 ausführlich beschrieben; am besten live im Prototyp ansehen (Zeile antippen). Der dunkle Rahmen um die Screens ist nur die Vorschau-Geräteblende, **nicht** Teil des Produkt-UI.
