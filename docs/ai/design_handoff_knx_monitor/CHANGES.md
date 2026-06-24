# Handoff-Nachtrag: Geänderte Details (Iteration Juni 2026)

> Begleitdokument zur `README.md`. Hier stehen **nur die Änderungen** dieser Iteration mit exakten Specs.
> Der Ziel-Stack bleibt unverändert: **Angular 17+ / Angular Material / SCSS-Tokens** (siehe README §2).
> Die Dateien unter `design-source/` sind die **aktualisierten Referenz-Prototypen** — kein Produktionscode.

---

## 0. Was sich strukturell geändert hat

Der Prototyp ist von **4 Bereichen** (Live, History, Projects, Settings) auf den **vollen 7-Bereich-Stand** gebracht worden, plus Login. Navigation (oben auf Desktop/Tablet, Bottom-Nav auf Mobile):

| # | Key | DE | EN |
|---|-----|----|----|
| 1 | `monitor`   | Monitor          | Monitor |
| 2 | `charts`    | Diagramme        | Charts |
| 3 | `statistics`| Statistik        | Statistics |
| 4 | `projects`  | Projekte         | Projects |
| 5 | `topology`  | Topologie        | Topology |
| 6 | `groupaddr` | Gruppenadressen  | Group addresses |
| 7 | `settings`  | Einstellungen    | Settings |

**Monitor** vereint Live-Stream und Archive (historisierte DB) in einem Bereich mit Mode-Switch (`Live` / `Archive`), statt zwei getrennter Menüpunkte.

Neue Quell-Dateien gegenüber der alten Referenz:
- `charts.jsx` — SVG-Chart-Engine (Line, Bar, Brush)
- `monitor.jsx` — Monitor-Seite (Live + Archive)
- `chartspage.jsx` — Charts- und Statistics-Seite
- `tree.jsx` — Topology- und Group-Addresses-Bäume

---

## 1. Chart-Engine (`charts.jsx`) — NEU, höchste Priorität

Eigenständige, abhängigkeitsfreie SVG-Engine. **Im Angular-Stack ist das die Spezifikation für die Chart-Komponente** (z. B. ngx-echarts/ECharts oder eine eigene SVG-Komponente — Mapping unten).

### 1.1 LineChart
- **Padding:** `padL 56 / padR 16 / padT 24 / padB 26` (px, innerhalb der SVG).
- **Y-Achse — „nice scale":** Ticks werden über den `niceNum`/`niceScale`-Algorithmus berechnet (Zielanzahl 7). Nie rohe min/max als Achsengrenzen — immer auf runde Schrittweiten (1/2/5 × 10ⁿ) aufgerundet. → ECharts: `yAxis.scale: true`, Splitnumber 7.
- **Y-Tick-Labels:** Monospace 11px, `--ink-3`, rechtsbündig bei `x = padL − 10`.
- **Einheit** (`W`, `°C`, …): oben **links** bei `x=8 / y=13`, `--font-ui` 600/11px, `--ink-2`. (War vorher kollidierend über dem obersten Tick — jetzt fix.)
- **X-Achse:** ~1 Tick pro 130px. **Mitternacht (`HH===0`) wird als fett gesetzter Tag-Zahl** dargestellt (`--ink`, 700), normale Ticks `HH:MM` (`--ink-3`, 500). → ECharts: `axisLabel.formatter` + `boundaryGap:false`.
- **Linie:** `stroke var(--series)`, `stroke-width 1.7`, `linejoin/linecap round`, kein Fill.
- **Flächen-Verlauf (NEU):** unter der Linie ein vertikaler Gradient `series-grad`: `--series` von **0.20 → 0.0** Opazität (oben → unten). Klasse `.series-fill`. → ECharts: `areaStyle` mit linearem Gradient.
- **Hover-Crosshair:** vertikale gestrichelte Linie (`--ink-3`, `dasharray 3 3`) + Punkt `r=4`, Fill `--surface`, Stroke `--series` (2px). Index wird über die Maus-X-Position auf den nächsten Datenpunkt gerundet.
- **Tooltip:** absolut positioniert, `--surface` / `--line-2` / `--sh-2`, Wert in Monospace 700/15px in `--series`-Farbe + Einheit (11px, `--ink-3`), darunter Zeit `DD/MM HH:MM` (Monospace 11px, `--ink-3`). Clamping: `left ≤ w−150`.

### 1.2 BarChart (Statistics)
- Gleiche Paddings/Achsen-Logik wie LineChart, Y beginnt bei **0**.
- Balken: Breite `bw*0.68`, x-Offset `bw*0.16`, Fill `var(--series)`, Hover `opacity .82`.

### 1.3 Brush / dataZoom (NEU)
- Höhe **56px**, gleiche `padL/padR` wie der Hauptchart (Achsen-Alignment).
- Zeigt die **volle Serie als Mini-Flächenkurve** (`.brush-area`, Fill `--series`, `opacity .16`).
- Zwei Masken (`.brush-mask`, `--paper-sunk`, `opacity .66`) verdunkeln den nicht gewählten Bereich links/rechts.
- **Fenster** (`.brush-window`): transparent, Rahmen `--teal-200`, `cursor: grab` (→ `grabbing` beim Ziehen). Verschiebt den Ausschnitt als Ganzes.
- **Zwei Griffe** (`.brush-handle`, je `10×24` rounded rect, Fill `--brand`, zwei weiße 1px-Striche): ziehen die untere/obere Grenze. Mindestbreite des Fensters 3 % (`0.03`).
- Drag-Logik via `window` mousemove/mouseup; `range` ist ein `[lo, hi]`-Paar normalisiert auf `0..1`, das der Parent als State hält und an Chart **und** Brush gibt.

### 1.4 Mapping auf Angular
| Prototyp | Angular-Empfehlung |
|----------|--------------------|
| LineChart/BarChart | ngx-echarts (`echarts`) ODER standalone `<svg>`-Komponente. Achsen-/Gradient-/Tooltip-Specs oben sind 1:1 umsetzbar. |
| Brush | ECharts `dataZoom: [{type:'slider'}]` — Optik mit den Tokens oben angleichen (Griffe `--brand`, Fenster-Rahmen `--teal-200`). |
| Live-Append | `setInterval`, Datenpunkt alle ~1.6 s anhängen, ältesten entfernen (Fenster konstant). |

---

## 2. Neue / geänderte Design-Tokens

In `tokens.css` (Light) und im `theme-dark`-Block in `app.css` (Console):

```css
/* Chart-Serie — NEU */
--series:       #4F63D6;                 /* Light */
--series-soft:  rgba(79, 99, 214, 0.16); /* Light */

/* im theme-dark Block: */
--series:       #7C8FF5;                 /* Console: helleres Periwinkle, sonst säuft die Linie ab */
--series-soft:  rgba(124,143,245,.18);
```

**Wichtig:** Die Serienfarbe ist jetzt **durchgängig tokenisiert**. Linie, Tooltip-Wert, Legenden-Marker, Crosshair-Punkt und Brush-Vorschau referenzieren alle `var(--series)` — nie ein hartkodierter Hexwert im Markup. Im Console-Theme schaltet das Token auf das hellere Blau um.

Alle übrigen Tokens (Petrol/Teal-Marke, Papier-Hintergründe, Ink-Stufen, Mono-Font, Radien, Schatten) sind **unverändert** — siehe README §9 + `_tokens.scss`.

---

## 3. Filter (`filters.jsx`, `monitor.jsx`)

### 3.1 Live-Toolbar (Monitor → Live)
- Mode-Switch `Live`/`Archive`, dazu `Pause/Resume` und `Auto-scroll`-Toggle (nur Live).
- Status-Pille `Live`/`Paused` mit LED + Zähler-Badge.
- **Rate-Pille** (`.rate-pill`, nur Desktop/Tablet): `X msg/s · Y%`, Monospace, `--surface-3`.
- Freitext-Suche (`SearchField`), Spalten-Manager (nur Desktop), `Clear`, `Export`.

### 3.2 Archive-Filterleiste (`ArchiveFilters`) — strukturiert, gestaged
- **Zwei Zeilen.** Zeile 1: Chip-Gruppen **Time** (`All / Last hour / Today / 7 days`), **Type** (`Write/Read/Response` mit Farbpunkt `--write/--read/--response`), **Topic** (`Temperature/Light/Shading/Power`). Auf Desktop durch `.af-vsep` (1px, `--line-2`) getrennt.
- Zeile 2: Text-Inputs **Dest GA** (`1/2/3`), **Source** (`1.1.5`), zwei `datetime-local` (From/To), **Apply**-Button (primär).
- **Staging-Prinzip:** `draft`-State wird erst bei **Apply** in `applied` übernommen → dann erst filtert das Grid. Das ist bewusst anders als die Live-Suche (sofort). Bei der Angular-Umsetzung: Draft-FormGroup, `applied` separat, Filterung an `applied` gebunden.
- Mobile: `.af-inputs` wird zur Spalte (`flex-direction: column`), Inputs `width:100%`.

### 3.3 Filterlogik (`applyFilters`)
Eine zentrale Funktion filtert sowohl Live (nur `q`) als auch Archive (volles Filterobjekt): Zeit-Span/`today`-Grenze, `types[]`, `topics[]`, `dst`/`src`-Substring, Freitext über alle Felder (case-insensitive). **In Angular als ein gemeinsamer Filter-Service**, von beiden Modi genutzt — vermeidet die heute divergierende Doppel-Implementierung (README §1).

---

## 4. Mobile (vp === 'mobile')

Eigene Layouts statt Skalierung:
- **Bottom-Nav** (`.bottom-nav`), 7 Punkte mit Icon + Label.
- **Monitor-Grid → Karten** (`.knx-mcard`): Name + Wert oben, darunter Meta (Zeit · Dest · Type-Tag). Tabelle wird nicht horizontal gescrollt.
- **Detail → Bottom-Sheet** statt rechtem Slide-over (`sheet-bottom`).
- **Charts/Filter/Trees** stapeln vertikal; Range-Controls und Inputs `width:100%`.
- **Tablet:** reduzierte Spalten (`time, src, dst, name, type, val`), Detail bleibt rechts.

Breakpoint-Logik im Prototyp über `vp`-Prop (`desktop`/`tablet`/`mobile`). In Angular: CDK `BreakpointObserver` oder Container-Queries; die drei Stufen 1:1 übernehmen.

---

## 5. Bäume (`tree.jsx`)

### 5.1 Group addresses (3 Ebenen)
- Top → Mid → Leaf. Leaf-Zeile (`.ga-leaf`): Adresse (Mono, semibold), Name, DPT-Chip, rechts **Read / Value-Input bzw. On-Off-Select / Write / Chart**.
- Filter-Suche blendet nicht-treffende Äste aus und klappt automatisch auf.
- Zähler-Badges pro Knoten (Anzahl GAs).

### 5.2 Topology (Gebäude-Baum)
- Beliebig tief verschachtelt (Building → BuildingPart → Floor → Room/DistributionBoard).
- **Hilfslinien (NEU):** verschachtelte Knoten (`depth > 0`) bekommen `.is-child` → `border-left: 1px solid var(--line)` für sichtbare Hierarchie-Linien.
- Räume/Verteiler nutzen Pin-Icon (`--brand`), Container nutzen Folder-Icon.
- Geräte als `.device-chip` (Mono, umrandet) gelistet.

---

## 6. Grid-Detail (`grid.jsx`) — Sticky-Header

- **Sticky `thead th`** mit `box-shadow: 0 2px 5px -3px rgba(16,32,29,.2)` (NEU) — beim Scrollen trennt sich der fixierte Kopf sichtbar von den durchlaufenden Telegrammen. In `components.css` **und** `app.css` gesetzt (Letzteres überschreibt mit gleicher Regel — beim Angular-Port nur **eine** Regel behalten).
- **Detail-Sheet:** großer Wert, 8 Meta-Felder (Timestamp, Source+Name, Dest GA, DPT, Raw, Type, Priority, Flags), Bus-Aktionen (`Read`/`Chart`), Write-Input, „Used by"-Block (Quell-Gerät).

---

## 7. Login (`app.jsx`)

- Zentrierte Karte, Logo `K`(grün `#2e9b57`)·`NX`(blau `#2f6fd0`)·`-NG-Monitor`(`--ink`).
- Username- und Passwort-Feld (mit Show/Hide-Auge), `Sign in`, Footer `Version 1.0.0`.
- Dark-Theme: Bühne `#0A1413`, Footer `#0E1A18`.

---

## 8. Bewusst NICHT umgesetzt (offene Punkte)

- **Volle DE-Übersetzung** aller Inhalte — EN/DE schaltet derzeit nur die Navigation. (Restliche UI ist EN.)
- **Live-Zähler „seit Session"** statt mitlaufendem Puffer-Count.
- **GA-Baum standardmäßig komplett aufgeklappt**.

Diese drei sind absichtlich offen gelassen und keine Bugs.

---

## 9. Geänderte Dateien dieser Iteration

| Datei | Status |
|-------|--------|
| `charts.jsx` | **neu** — SVG-Engine (Line/Bar/Brush) |
| `monitor.jsx` | **neu** — Live + Archive |
| `chartspage.jsx` | **neu** — Charts + Statistics |
| `tree.jsx` | **neu** — Topology + Group addresses |
| `data.js` | erweitert — GA-Baum, Topologie, Geräte-Map, Chart-Serien, Statistik |
| `icons.jsx` | erweitert — zusätzliche Icons |
| `grid.jsx` | überarbeitet — Spalten, Detail-Sheet, Sticky-Shadow |
| `filters.jsx` | überarbeitet — Live-Suche + Archive-Filter + `applyFilters` |
| `pages.jsx` | überarbeitet — Projects (Import/Detail) + Settings (EN) |
| `app.jsx` | überarbeitet — 7-Nav-Shell, Login, EN/DE, Mobile-Bottom-Nav |
| `tokens.css` | `--series` / `--series-soft` ergänzt |
| `app.css` | Chart-/Tree-/Login-/Mobile-Styles + Dark-`--series` + Sticky-Shadow |
| `components.css` | Sticky-Header-Shadow |
| `KNX Monitor Prototype.html` | alle neuen Scripts eingebunden |
