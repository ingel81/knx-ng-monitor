# UI-Redesign — Umsetzungsplan („Instrument"-Designsystem)

Konkrete Angular-Umsetzung des Design-Handoffs (`docs/ai/design_handoff_knx_monitor/`).
Reihenfolge folgt §16 des Handoffs. Status wird hier laufend gepflegt.

## Ausgangslage (Bestand)
- Material **Dark**-Theme global (`styles.scss`, `blue-palette`), Roboto.
- Content-Panels (Live/History) faktisch **hell** (`#fafafa`, `ag-theme-material`), Header dunkel (Blau-Verlauf).
- SCSS-Variablen (`_variables.scss`) hart auf Dark kodiert (weißer Text).
- AG-Grid Community 34.3, Infinite Row Model (History), Client-Grid (Live).

## Zielbild
- **Light-Default** + **Console-Dark** (Runtime-Switch via CSS-Custom-Properties).
- IBM Plex Sans (UI) + IBM Plex Mono (Daten), Teal-Marke `#0F766E`.
- **Ein** geteiltes Grid für Live+History, einheitliche Filterleiste, Detail-Sheet, responsive Karten, Bottom-Nav.

## Bridge-Strategie (minimale Brüche)
Alte SCSS-Variablennamen in `_variables.scss` behalten, **Werte** auf Light-Tokens umstellen → bestehende Komponenten kippen automatisch nach Light, ohne sofortige Per-Komponente-Edits. CSS-Custom-Properties (`_tokens.scss`) treiben den Runtime-Theme-Switch in migrierten Komponenten. Phase für Phase auf `var(--token)` umstellen.

---

## Phase 1 — Foundation  ✅ (in Arbeit)
- [x] IBM Plex Sans + Mono via `@fontsource` installiert.
- [ ] `styles/_tokens.scss` (CSS-Custom-Properties Light + `.theme-dark`).
- [ ] `styles.scss`: Fonts IBM Plex; Material Light-Default + Dark unter `.theme-dark`; Body-Basis (`--paper`/`--ink`/`--font-ui`).
- [ ] `_variables.scss`: Werte → Light-Tokens (Namen bleiben).
- [ ] `core/services/theme.service.ts`: `theme` (light/dark) + `density` (compact/cozy), `localStorage`, Klasse auf `<html>`.
- [ ] Theme-Init im `App`-Root.

## Phase 2 — Shell & Navigation
- Header Petrol-Verlauf `#0F3A35→#0C2B28`, Wortmarke „KNX·NG" + `MONITOR`-Label.
- Top-Nav aktiver Tab: `rgba(24,168,154,.16)` + inset-underline; **nur `color` transitionen**.
- Bottom-Nav (<768px, 4 Tabs).
- `app-main` bg → `--paper`.

## Phase 3 — Gemeinsames Grid + Detail-Sheet
- `shared/knx-grid/` Komponente: Spaltendefs + Renderer (Typ-Pill, Werte-Semantik, Raum-Tag), `getRowClass` Zebra/`is-new`, Sticky-Header, Dichte.
- Detail-Sheet: Side-Panel (Desktop) / Bottom-Sheet (Mobil).
- Live + History auf gemeinsame Komponente (Props: Quelle, `useDateTime`, Sort).
- Mobile-Karten `<768px`.

## Phase 4 — Einheitliche Filterleiste
- `shared/knx-filter-bar/`: globale Suche (debounce 250 ms), Quick-Chips (Zeit/Typ/Themen), Spalten-Manager-Popover.
- Ersetzt Inline-Spaltenfilter (Live) + alte History-Leiste. History serverseitig.

## Phase 5 — Projects & Settings
- Projects: `.proj-table` / `.proj-card` + Import-Modal-Dropzone.
- Settings: 3 Karten (Darstellung mit Theme-/Density-Segmented, KNX Gateway, Datenaufzeichnung).

## Phase 6 — Feinschliff & A11y
- `prefers-reduced-motion`, Fokus-Ringe, Tastatur-Nav, ARIA, Touch ≥44px.
- Eigenes SVG-Icon-Set via `MatIconRegistry`.
- Login/Setup auf Tokens. Finaler Build grün.

---

## Endstand (alle Phasen umgesetzt, Build grün)

| Phase | Status | Ergebnis |
|---|---|---|
| 1 Foundation | ✅ | `_tokens.scss`, IBM Plex, Material M3 Light-Default + `.theme-dark`-Scope, `ThemeService`, `_variables.scss`-Bridge |
| 2 Shell | ✅ | Petrol-Header, Wortmarke, Top-Nav aktiv (inset-underline, color-only), Bottom-Nav <768px, `app-main`→`--paper` |
| 3 Grid + Sheet | ✅ | `styles/_grid.scss`-Mixin + `knx-grid.util.ts` (Typ-Pill, Werte-Semantik), Detail-Sheet (`telegram-detail.*`, responsives Dialog-Panel), Row-Click in Live+History |
| 4 Filter | ✅ | `styles/_filter-bar.scss`, History Quick-Chips (Zeit/Typ) + Reset, Live globale Quick-Suche |
| 5 Projects+Settings | ✅ | Settings Appearance-Karte (Theme+Dichte Segmented, live), Token-Restyle; Projects Teal-Re-Theme |
| 6 Feinschliff | ✅ | Dichte→Grid-Row-Height-Wiring (compact 36/cozy 48), `prefers-reduced-motion` (Grid-Flash), ARIA (Nav/Switch/Sheet), angular.json-Budgets, Build grün |

### Nachgezogen (zweite Runde — alle offenen Design-Punkte)
- ✅ **is-new-Flash** Live-Zeilen (`newIds`-Set + redraw, reduced-motion).
- ✅ **History Sortier-Indikator** — Time-Spalte Klick asc/desc; Backend `order`-Param (Keyset beidseitig).
- ✅ **Detail-Sheet Slide-Animation** (rechts rein / unten hoch, reduced-motion).
- ✅ **Raum-Tag-Pill** im Namen (EG/OG/DG/UG/KG → Pill via `nameCellRenderer`).
- ✅ **Typ-Chips multi-select** — Backend `Types`-CSV (IN-Query); **Themen-Chips** als Volltext-Shortcuts.
- ✅ **Spalten-Manager-Popover** — `ColumnManagerComponent` (beide Grids, localStorage, Name/Wert gesperrt).
- ✅ **Cross-Field-Server-Volltextsuche** (History) — Backend `q` (LIKE-OR über Source/Dest/Name/DPT/Decoded), Filterleisten-Suche debounced.
- ✅ **Mobile-Karten-Ansicht** (<768px, Live + History) — `TelegramCardsComponent`, History mit Cursor-„Mehr laden".
- ✅ **Eigenes SVG-Icon-Set** via `MatIconRegistry` (Namespace `knx`) — Shell-Nav + Settings auf das Set; Feature-Toolbar-Glyphen ohne Set-Äquivalent bleiben Material-Icons.

### Verbleibende Mini-Reste (kein Set-Äquivalent / minor)
- Themen-Chips sind Single-Shortcut (q), nicht Multi-Regex.
- Spalten-Reorder per Drag (nur Ein/Ausblenden umgesetzt).
- Icon-Set deckt nicht alle genutzten Glyphen (z.B. `inbox`, `cable`, `restore`, `info`, `radio_button_unchecked`) → dort weiter Material.
- Login/Setup/Import-Wizard: über Bridge (Light), kein pixelgenaues Restyle.

## Entscheidungen / Notizen
- Material-Primary: nächstgelegene M3-Palette (`cyan`) als Näherung; kritische Brand-Buttons via CSS-Vars/Komponenten-Styles exakt auf `#0F766E`.
- Theme-Switch: Material via `mat.all-component-colors($dark)` unter `.theme-dark`-Scope (M3-Pattern).
- `prefers-color-scheme` ignoriert; Theme ausschließlich aus Settings/`localStorage` (Default Light).
