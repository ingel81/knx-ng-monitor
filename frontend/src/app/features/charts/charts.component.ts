import { Component, ElementRef, HostListener, NgZone, OnInit, OnDestroy, ViewChild, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute } from '@angular/router';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { ECharts, EChartsCoreOption } from 'echarts/core';
import { Subscription, firstValueFrom } from 'rxjs';

import { ChartsService, ChartSeries, extractNumeric } from '../../core/services/charts.service';
import { ChartSelectionsService, ChartSelection } from '../../core/services/chart-selections.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/confirm-dialog.component';
import { ProjectService, GroupAddressDto } from '../../core/services/project.service';
import { SignalrService, KnxTelegram } from '../../core/services/signalr.service';
import { LanguageService } from '../../core/i18n/language.service';
import { localeTag } from '../../core/i18n/locale.util';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { DateTimeFieldComponent } from '../../shared/date-time-field/date-time-field.component';
import { ThemeService } from '../../core/services/theme.service';
import { OverflowTitleDirective } from '../../shared/grid/overflow-title.directive';
import { SearchCache, foldForSearch, searchTokens } from '../../shared/search.util';
import { SlashFocusDirective } from '../../shared/slash-focus.directive';
import { RangePreset, isRangeOrdered, resolveRange } from './time-range.util';
import { readSkin, valueAxis, timeAxis, tooltipCfg, dataZoom, styleLineSeries, TooltipRow } from './chart-skin';
import { formatDurationMinutes } from '../../shared/duration.util';
import { A11yModule } from '@angular/cdk/a11y';
import {
  ChartDisplayOptions, CurveMode, DEFAULT_CHART_OPTIONS,
  loadChartOptions, saveChartOptions, loadChartQuery, saveChartQuery
} from './chart-options';

/** Per-series summary shown under the chart; doubles as the interactive legend. */
export interface SeriesStat {
  name: string;
  address: string;
  unit: string;
  color: string;
  min: number;
  max: number;
  avg: number;
  last: number;
  count: number;
}

/** DPT main numbers we treat as chartable (numeric) — plus DPT1 (boolean step lines). */
const NUMERIC_DPT_MAINS = new Set([5, 6, 7, 8, 9, 12, 13, 14]);

/** Quotes a CSV field only when it needs it (comma, quote or newline). */
function csvCell(value: string): string {
  if (!/[",\r\n]/.test(value)) return value;
  return `"${value.replace(/"/g, '""')}"`;
}

interface LiveSeries {
  address: string;
  name: string;
  unit: string;
  isBool: boolean;
  data: [number, number][]; // [epochMs, value]
  /**
   * Last reading before the visible window ([epochMs, value]), carried in so the curve starts at
   * the left edge. Filled from the server's `carry` on load and by points that slide out of the
   * live window. Display only: never part of `data`, the stats or the CSV.
   */
  carry: [number, number] | null;
}

@Component({
  selector: 'app-charts',
  imports: [
    A11yModule,
    CommonModule, FormsModule, MatIconModule, MatSelectModule, MatFormFieldModule,
    MatMenuModule, MatTooltipModule, NgxEchartsDirective, TranslatePipe, OverflowTitleDirective,
    DateTimeFieldComponent, SlashFocusDirective
  ],
  templateUrl: './charts.component.html',
  styleUrl: './charts.component.scss'
})
export class ChartsComponent implements OnInit, OnDestroy {
  private chartsService = inject(ChartsService);
  private projectService = inject(ProjectService);
  private signalr = inject(SignalrService);
  private lang = inject(LanguageService);
  private route = inject(ActivatedRoute);
  private theme = inject(ThemeService);
  private zone = inject(NgZone);
  private selectionsService = inject(ChartSelectionsService);
  private dialog = inject(MatDialog);

  constructor() {
    // Rebuild with re-read tokens whenever the theme toggles (canvas can't use CSS vars).
    effect(() => {
      this.theme.theme();
      if (this.series.length) this.rebuildChart();
    });
  }

  /** GA to preselect once the GA list has loaded (from the `ga` query param). */
  private preselectGa: string | null = null;

  readonly maxSeries = 8;
  readonly maxLivePoints = 5000;

  // --- GA picker -------------------------------------------------------------
  availableGas: GroupAddressDto[] = [];
  /** Dropdown order — selected GAs floated to the top; frozen while the panel is open. */
  displayGas: GroupAddressDto[] = [];
  selectedAddresses: string[] = [];
  hasActiveProject = false;
  /** Free-text filter inside the dropdown panel; reset every time it opens. */
  gaFilter = '';
  /** Folded address + name per GA, built once per GA list instead of on every redraw. */
  private gaSearchCache = new SearchCache<GroupAddressDto>((ga) => `${ga.address} ${ga.name ?? ''}`);
  @ViewChild('gaSearch') private gaSearch?: ElementRef<HTMLInputElement>;
  /** Dropdown panel width in px: wider than the 320px trigger so long GA names stay readable,
   *  but never wider than the viewport minus the page gutters. */
  gaPanelWidth = ChartsComponent.panelWidthFor(window.innerWidth);

  // --- Saved selections ------------------------------------------------------
  savedSelections: ChartSelection[] = [];
  savedOpen = false;
  savedName = '';
  savedBusy = false;
  savedError = false;
  /** The list request is running; the empty state waits for it. */
  savedLoading = false;
  /** At least one list request succeeded, so an empty list really means "none saved". */
  savedLoaded = false;
  private missingById = new Map<number, number>();

  // --- Time range ------------------------------------------------------------
  preset: RangePreset = '24h';
  customFrom = '';
  customTo = '';
  readonly presets: RangePreset[] = ['1h', '24h', '7d', '30d'];

  /** Mobile only: the range controls are collapsed behind a toolbar toggle. */
  rangeOpen = false;
  /**
   * Why Apply was refused: an incomplete custom range (marks the empty field) or an end
   * before the start.
   */
  rangeHint: '' | 'missing' | 'order' = '';
  isMobile = window.innerWidth < 768;
  @ViewChild('fromInput') private fromInput?: DateTimeFieldComponent;
  @ViewChild('toInput') private toInput?: DateTimeFieldComponent;

  // --- State -----------------------------------------------------------------
  loading = false;
  error = false;
  downSampled = false;
  liveAppend = true;

  /** Row cap hit — the loaded range is incomplete at its older end. */
  truncated = false;
  /** Points kept vs. available, for an honest down-sampling note. */
  shownPoints = 0;
  totalPoints = 0;
  /** Per-series min/max/avg/last, rebuilt with the chart. */
  stats: SeriesStat[] = [];

  /** Span of the loaded range in ms — doubles as the live follow window. */
  private rangeSpanMs = 0;
  /** Loaded range bounds (epoch ms); pin the x axis so it shows the picked range, not the data extent. */
  private rangeFromMs = 0;
  private rangeToMs = 0;
  /** Set by a load; the next rebuild starts unzoomed instead of keeping the previous zoom. */
  private freshResult = false;
  /** Moves the live window forward even while the bus is quiet. */
  private followTimer?: ReturnType<typeof setInterval>;

  private series: LiveSeries[] = [];
  chartOption: EChartsCoreOption = {};

  // --- Display options -------------------------------------------------------
  options: ChartDisplayOptions = loadChartOptions();
  optionsOpen = false;
  readonly curveModes: CurveMode[] = ['line', 'smooth', 'area', 'step'];

  /** Set by ngx-echarts once the canvas exists; needed for the PNG export. */
  private chart?: ECharts;

  /** Series switched off via the summary table (or the chart legend). */
  private hiddenSeries = new Set<string>();

  private telegramSub?: Subscription;
  private flushTimer?: ReturnType<typeof setTimeout>;
  private dirty = false;

  ngOnInit(): void {
    this.preselectGa = this.route.snapshot.queryParamMap.get('ga');
    this.loadGroupAddresses();
    this.loadSavedSelections();
    this.telegramSub = this.signalr.telegram$.subscribe((t) => this.onLiveTelegram(t));
  }

  ngOnDestroy(): void {
    this.telegramSub?.unsubscribe();
    if (this.flushTimer) clearTimeout(this.flushTimer);
    this.stopFollow();
  }

  // --- GA loading ------------------------------------------------------------
  private loadGroupAddresses(): void {
    this.projectService.getAllProjects().subscribe({
      next: (projects) => {
        const active = projects.find((p) => p.isActive);
        if (!active) {
          this.hasActiveProject = false;
          return;
        }
        this.hasActiveProject = true;
        this.projectService.getProjectDetails(active.id).subscribe({
          next: (details) => {
            this.availableGas = details.groupAddresses
              .filter((ga) => this.isChartable(ga.datapointType))
              .sort((a, b) => a.address.localeCompare(b.address, undefined, { numeric: true }));
            this.displayGas = this.availableGas;
            this.refreshMissing();
            this.applyPreselect();
          },
          error: () => (this.availableGas = [])
        });
      },
      error: () => (this.hasActiveProject = false)
    });
  }

  /**
   * Restore what to show: an explicit `ga` query param wins, otherwise the selection and range
   * from the last visit. Stored addresses are filtered against the current project — after a
   * project change they may no longer exist.
   */
  private applyPreselect(): void {
    if (this.preselectGa) {
      const ga = this.preselectGa;
      this.preselectGa = null;
      if (this.availableGas.some((g) => g.address === ga)) {
        this.selectedAddresses = [ga];
        this.load();
      }
      return;
    }

    const saved = loadChartQuery();
    if (!saved) return;

    const known = new Set(this.availableGas.map((g) => g.address));
    const addresses = saved.addresses.filter((a) => known.has(a)).slice(0, this.maxSeries);
    if (addresses.length === 0) return;

    this.selectedAddresses = addresses;
    this.preset = saved.preset as RangePreset;
    this.customFrom = saved.customFrom;
    this.customTo = saved.customTo;
    this.load();
  }

  /** Numeric DPT mains (5/6/7/8/9/12/13/14) or DPT1 (boolean) are chartable. */
  private isChartable(dpt?: string): boolean {
    if (!dpt) return false;
    const m = dpt.match(/(\d+)/);
    if (!m) return false;
    const main = Number(m[1]);
    return main === 1 || NUMERIC_DPT_MAINS.has(main);
  }

  private isBoolDpt(dpt?: string): boolean {
    if (!dpt) return false;
    const m = dpt.match(/(\d+)/);
    return !!m && Number(m[1]) === 1;
  }

  // --- Dropdown ordering -----------------------------------------------------
  /** Freeze the option order when the panel opens: selected GAs first (so the
   *  current selection sits at the top), each group keeping its address order.
   *  Frozen while open so toggling a checkbox doesn't make rows jump. */
  onPanelToggle(opened: boolean): void {
    if (opened) {
      this.displayGas = this.orderedGas();
      this.gaFilter = '';
      // The overlay needs a tick to exist before the input can take focus.
      setTimeout(() => this.gaSearch?.nativeElement.focus());
    }
  }

  /**
   * Options matching the search term, plus every selected address regardless of the term.
   * Keeping selected options rendered matters: mat-select tracks its selection through the
   * option components, so filtering one away could drop it from the selection.
   */
  filteredGas(): GroupAddressDto[] {
    const tokens = searchTokens(this.gaFilter);
    if (tokens.length === 0) return this.displayGas;
    const selected = new Set(this.selectedAddresses);
    // Every word has to occur somewhere in address or name, in any order: "küche raum" finds
    // "EG.Küche.Heizung.Raumtemperatur" without typing the path in between.
    return this.displayGas.filter((ga) => selected.has(ga.address) || this.gaSearchCache.matches(ga, tokens));
  }

  /**
   * Splits a label into matched and unmatched runs so the hit can be emphasised. Returns plain
   * segments rendered as separate spans — no innerHTML, so a group-address name containing
   * markup stays inert text.
   */
  highlightParts(text: string): { text: string; hit: boolean }[] {
    const tokens = searchTokens(this.gaFilter);
    if (tokens.length === 0 || !text) return [{ text, hit: false }];

    // foldForSearch keeps one character per character, so indices in the folded string are
    // indices in the original and the hit can be cut out of the original spelling.
    const haystack = foldForSearch(text);
    const hit = new Array<boolean>(text.length).fill(false);
    for (const token of tokens) {
      let from = 0;
      let at: number;
      while ((at = haystack.indexOf(token, from)) >= 0) {
        hit.fill(true, at, at + token.length);
        from = at + token.length;
      }
    }

    const parts: { text: string; hit: boolean }[] = [];
    let start = 0;
    for (let i = 1; i <= text.length; i++) {
      if (i === text.length || hit[i] !== hit[start]) {
        parts.push({ text: text.slice(start, i), hit: hit[start] });
        start = i;
      }
    }
    return parts;
  }

  /**
   * mat-select treats keystrokes as type-ahead and uses space/enter to toggle options, so the
   * search field has to keep its own keys. Escape and the arrows stay with the select for
   * closing and navigating.
   */
  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape' && event.key !== 'ArrowDown' && event.key !== 'ArrowUp') {
      event.stopPropagation();
    }
  }

  private orderedGas(): GroupAddressDto[] {
    const sel = new Set(this.selectedAddresses);
    return [...this.availableGas].sort((a, b) => {
      const da = sel.has(a.address) ? 0 : 1;
      const db = sel.has(b.address) ? 0 : 1;
      return da - db || a.address.localeCompare(b.address, undefined, { numeric: true });
    });
  }

  // --- Selection -------------------------------------------------------------
  /** Label of the first selected address; the rest is summarised by a counter next to it. */
  selectionLabel(): string {
    const first = this.selectedAddresses[0];
    if (!first) return '';
    const ga = this.availableGas.find((g) => g.address === first);
    return ga?.name ? `${first} — ${ga.name}` : first;
  }

  /** Full list as a native tooltip, so nothing is actually hidden. */
  selectionTitle(): string {
    return this.selectedAddresses
      .map((a) => {
        const ga = this.availableGas.find((g) => g.address === a);
        return ga?.name ? `${a} — ${ga.name}` : a;
      })
      .join('\n');
  }

  onSelectionChange(addresses: string[]): void {
    if (addresses.length > this.maxSeries) {
      addresses = addresses.slice(0, this.maxSeries);
    }
    this.selectedAddresses = addresses;
    // A different set of series starts fully visible; stale names would otherwise hide a
    // freshly picked address that happens to carry the same name.
    this.hiddenSeries.clear();
    this.load();
  }

  // --- Saved selections ------------------------------------------------------
  private loadSavedSelections(): void {
    this.savedLoading = true;
    this.selectionsService.getAll().subscribe({
      next: (rows) => {
        this.savedSelections = rows;
        this.savedLoaded = true;
        this.savedLoading = false;
        this.savedError = false;
        this.refreshMissing();
      },
      error: () => {
        this.savedLoading = false;
        this.savedError = true;
      }
    });
  }

  /** Counts per selection the addresses the active project does not know. Done once per load
   *  of either list, not per row on every redraw. */
  private refreshMissing(): void {
    const known = new Set(this.availableGas.map((g) => g.address));
    this.missingById = new Map(
      this.savedSelections.map((s) => [s.id, s.addresses.filter((a) => !known.has(a)).length])
    );
  }

  /** Every address is unknown here (other project, re-import): applying it would only empty
   *  the chart, so the entry is disabled and its meta line says why. */
  isUnusable(sel: ChartSelection): boolean {
    return sel.addresses.length > 0 && this.missingCount(sel) >= sel.addresses.length;
  }

  /** Same rule as the server: case-insensitive including umlauts ("Übersicht" = "übersicht"). */
  private sameName(a: string, b: string): boolean {
    return a.toLowerCase() === b.toLowerCase();
  }

  toggleSaved(): void {
    this.savedOpen = !this.savedOpen;
    if (this.savedOpen) {
      this.savedError = false;
      // Picked up again on every open, so a selection saved on another device shows up.
      this.loadSavedSelections();
    }
  }

  /** Applies a saved selection; addresses unknown to the active project are skipped. */
  applySaved(sel: ChartSelection): void {
    if (this.isUnusable(sel)) return;
    const known = new Set(this.availableGas.map((g) => g.address));
    this.savedOpen = false;
    this.onSelectionChange(sel.addresses.filter((a) => known.has(a)));
  }

  /** Addresses of a saved selection that the active project does not know (renamed, other project). */
  missingCount(sel: ChartSelection): number {
    return this.missingById.get(sel.id) ?? 0;
  }

  async saveSelection(): Promise<void> {
    const name = this.savedName.trim();
    if (!name || this.selectedAddresses.length === 0 || this.savedBusy) return;

    const existing = this.savedSelections.find((s) => this.sameName(s.name, name));
    if (existing) {
      const ok = await this.confirm({
        title: this.lang.translate('charts.saved.overwriteTitle'),
        message: this.lang.translate('charts.saved.overwriteMsg', { name: existing.name }),
        confirmText: this.lang.translate('charts.saved.overwrite')
      });
      if (!ok) return;
    }

    this.savedBusy = true;
    this.selectionsService.save(name, this.selectedAddresses).subscribe({
      next: () => {
        this.savedBusy = false;
        this.savedName = '';
        this.loadSavedSelections();
      },
      error: () => {
        this.savedBusy = false;
        this.savedError = true;
      }
    });
  }

  async deleteSaved(sel: ChartSelection, event: Event): Promise<void> {
    // The row itself applies the selection; the delete button must not trigger that too.
    event.stopPropagation();
    const ok = await this.confirm({
      title: this.lang.translate('charts.saved.deleteTitle'),
      message: this.lang.translate('charts.saved.deleteMsg', { name: sel.name }),
      confirmText: this.lang.translate('common.delete'),
      danger: true
    });
    if (!ok) return;

    const drop = () => (this.savedSelections = this.savedSelections.filter((s) => s.id !== sel.id));
    this.selectionsService.delete(sel.id).subscribe({
      next: drop,
      // Already deleted on another device: the goal is reached, no error to show.
      error: (err: { status?: number }) => {
        if (err?.status === 404) drop(); else this.savedError = true;
      }
    });
  }

  private confirm(data: ConfirmDialogData): Promise<boolean | undefined> {
    return firstValueFrom(
      this.dialog
        .open(ConfirmDialogComponent, { data, width: 'min(420px, calc(100vw - 32px))', maxWidth: '100vw' })
        .afterClosed()
    );
  }

  // --- Time range ------------------------------------------------------------
  setPreset(p: RangePreset): void {
    // A preset supersedes the custom range, so a pending "pick both dates" hint is stale.
    this.rangeHint = '';
    this.preset = p;
    this.load();
  }

  /**
   * Validates here instead of disabling the button: on touch a disabled control gives no
   * feedback at all, so a tap now says what is missing and jumps to that field.
   */
  applyCustom(): void {
    if (!this.customFrom || !this.customTo) {
      this.rangeHint = 'missing';
      (this.customFrom ? this.toInput : this.fromInput)?.focus();
      return;
    }
    if (!isRangeOrdered(this.customFrom, this.customTo)) {
      this.rangeHint = 'order';
      this.toInput?.focus();
      return;
    }
    this.rangeHint = '';
    this.preset = 'custom';
    this.load();
  }

  /** Chart paddings differ per breakpoint; the canvas size itself is handled by [autoResize]. */
  @HostListener('window:resize')
  onResize(): void {
    this.gaPanelWidth = ChartsComponent.panelWidthFor(window.innerWidth);
    const mobile = window.innerWidth < 768;
    if (mobile === this.isMobile) return;
    this.isMobile = mobile;
    if (this.series.length) this.rebuildChart();
  }

  /** At least the 320px trigger, but below 352px the viewport wins so the list never overflows. */
  private static panelWidthFor(viewport: number): number {
    return Math.min(Math.max(320, Math.min(640, viewport - 32)), viewport - 32);
  }

  toggleLive(): void {
    // A custom range never runs live (see onLiveTelegram); the button only explains that.
    if (this.preset === 'custom') return;
    if (this.following) {
      // Switching live off keeps the window that is on screen now. Falling back to the range of
      // the last load would push the newest minutes out on the right.
      const { xMin, xMax } = this.xWindow();
      if (xMin !== undefined && xMax !== undefined) {
        this.rangeFromMs = xMin;
        this.rangeToMs = xMax;
      }
    }
    this.liveAppend = !this.liveAppend;
    if (this.liveAppend) {
      // Telegrams that arrived while live was off were not kept, so the series would extend a
      // stale last value flat up to now. Reload the preset window instead.
      this.load();
      return;
    }
    this.syncFollow();
    // Animation is disabled while live so appended points don't re-animate the whole
    // curve every flush; rebuild so the change takes effect immediately.
    this.rebuildChart();
  }

  /**
   * Live on a relative preset: the x axis always ends at "now" and starts one preset span
   * earlier, so the window keeps moving when no telegram arrives. A custom range is a fixed
   * window and never follows.
   */
  private get following(): boolean {
    return this.liveAppend && this.preset !== 'custom' && this.rangeSpanMs > 0;
  }

  /** (Re)starts or stops the follow tick to match the current live/preset state. */
  private syncFollow(): void {
    this.stopFollow();
    if (!this.following) return;
    // About one pixel of a ~1800px wide chart per tick, clamped to 1 s to 60 s: 1h moves every
    // 2 s, 24h every 48 s, 7d/30d once a minute. More often would just redraw the same picture.
    const tickMs = Math.min(60_000, Math.max(1_000, Math.round(this.rangeSpanMs / 1800)));
    this.followTimer = setInterval(() => this.followTick(), tickMs);
  }

  private stopFollow(): void {
    if (this.followTimer) clearInterval(this.followTimer);
    this.followTimer = undefined;
  }

  /** Drops points that slid out of the live window on the left; true when any did. */
  private trimToWindow(now: number): boolean {
    const cutoff = now - this.rangeSpanMs;
    let dropped = false;
    for (const s of this.series) {
      const before = s.data.length;
      this.dropBefore(s, cutoff);
      dropped ||= s.data.length !== before;
    }
    return dropped;
  }

  /**
   * Removes the points older than `cutoff` (or, with `keep`, all but the newest `keep` points)
   * and keeps the newest removed one as the carry-in value, so the curve still starts at the
   * left edge with the value that was in force there.
   */
  private dropBefore(s: LiveSeries, cutoff: number, keep?: number): void {
    let drop = 0;
    if (keep !== undefined) {
      drop = Math.max(0, s.data.length - keep);
    } else {
      while (drop < s.data.length && s.data[drop][0] < cutoff) drop++;
    }
    if (drop === 0) return;
    s.carry = s.data[drop - 1];
    s.data.splice(0, drop);
  }

  // --- Display options -------------------------------------------------------
  setCurve(mode: CurveMode): void {
    this.options = { ...this.options, curve: mode };
    this.persistAndRedraw();
  }

  toggleOption(key: 'showPoints' | 'zeroBased' | 'averageLine' | 'showLegend'): void {
    this.options = { ...this.options, [key]: !this.options[key] };
    this.persistAndRedraw();
  }

  resetOptions(): void {
    this.options = { ...DEFAULT_CHART_OPTIONS };
    this.persistAndRedraw();
  }

  private persistAndRedraw(): void {
    saveChartOptions(this.options);
    if (this.series.length) this.rebuildChart();
  }

  onChartInit(instance: ECharts): void {
    this.chart = instance;
    // Keep the table in sync when the chart's own legend is switched on and used there.
    // ECharts events fire outside Angular, so re-enter the zone or the table stays stale.
    instance.on('legendselectchanged', (event) => {
      const selected = (event as { selected?: Record<string, boolean> }).selected ?? {};
      this.zone.run(() => {
        this.hiddenSeries = new Set(
          Object.entries(selected).filter(([, visible]) => !visible).map(([name]) => name)
        );
      });
    });
  }

  /**
   * Show/hide a series from the summary table, which is the primary legend. The state is written
   * into `legend.selected` on every rebuild rather than dispatched as an action — live append
   * replaces the whole option twice a second, and a dispatched selection would be lost with it.
   */
  toggleSeries(name: string): void {
    const next = new Set(this.hiddenSeries);
    if (next.has(name)) next.delete(name); else next.add(name);
    this.hiddenSeries = next;
    this.rebuildChart();
  }

  isHidden(name: string): boolean {
    return this.hiddenSeries.has(name);
  }

  /**
   * PNG export via the chart instance's own `getDataURL`. Deliberately not ECharts'
   * `toolbox.feature.saveAsImage`: that would pull the ToolboxComponent into the bundle and
   * render its own icon strip next to our buttons. The explicit background colour matters —
   * without it the export is transparent, which is unreadable on the dark theme.
   */
  exportPng(): void {
    if (!this.chart) return;
    const url = this.chart.getDataURL({
      type: 'png',
      pixelRatio: 2,
      backgroundColor: readSkin().surface
    });
    this.triggerDownload(url, `knx-chart-${this.stamp()}.png`);
  }

  /**
   * CSV of the charted points. Long format (one row per reading) rather than a column per
   * series: the group addresses send at their own times, so a wide table would be almost
   * entirely empty cells.
   */
  exportCsv(): void {
    const rows: string[] = ['Timestamp,Address,Name,Value,Unit'];
    for (const s of this.series) {
      for (const [t, v] of s.data) {
        rows.push([
          new Date(t).toISOString(),
          csvCell(s.address),
          csvCell(s.name),
          String(v),
          csvCell(s.unit)
        ].join(','));
      }
    }
    // BOM so Excel detects UTF-8 and does not mangle unit symbols like °C.
    const blob = new Blob(['﻿' + rows.join('\r\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    this.triggerDownload(url, `knx-chart-${this.stamp()}.csv`);
    URL.revokeObjectURL(url);
  }

  private stamp(): string {
    return new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
  }

  private triggerDownload(url: string, filename: string): void {
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
  }

  /** Locale-aware number for the summary table; trims noise from float arithmetic. */
  formatValue(v: number): string {
    return new Intl.NumberFormat(localeTag(this.lang.lang()), { maximumFractionDigits: 2 }).format(v);
  }

  formatCount(n: number): string {
    return new Intl.NumberFormat(localeTag(this.lang.lang())).format(n);
  }

  // --- Data load -------------------------------------------------------------
  load(): void {
    // Persist first, including an empty selection: saving only on the success path would leave
    // the previous addresses in storage after the user cleared them, and they would come back
    // on the next visit.
    saveChartQuery({
      addresses: this.selectedAddresses,
      preset: this.preset,
      customFrom: this.customFrom,
      customTo: this.customTo
    });

    if (this.selectedAddresses.length === 0) {
      this.stopFollow();
      this.series = [];
      this.chartOption = {};
      this.resetResultState();
      return;
    }
    const range = resolveRange(this.preset, this.customFrom, this.customTo);
    if (!range) return;

    this.rangeFromMs = new Date(range.from).getTime();
    this.rangeToMs = new Date(range.to).getTime();
    this.rangeSpanMs = this.rangeToMs - this.rangeFromMs;
    // Restarted once the data is in; ticking meanwhile would trim the curve of the old result.
    this.stopFollow();

    this.loading = true;
    this.error = false;
    // Quick preset switches overlap requests; only the newest may land, an older 24 h answer
    // arriving late would otherwise replace the 1 h curve while rangeSpanMs is already 1 h.
    const seq = ++this.loadSeq;
    this.chartsService.getSeries(this.selectedAddresses, range.from, range.to, 2000).subscribe({
      next: (res) => {
        if (seq !== this.loadSeq) return;
        this.downSampled = res.series.some((s) => s.downSampled);
        this.truncated = res.truncated;
        this.totalPoints = res.series.reduce((sum, s) => sum + s.totalPoints, 0);
        this.shownPoints = res.series.reduce((sum, s) => sum + s.points.length, 0);
        this.series = res.series.map((s) => this.toLiveSeries(s));
        this.freshResult = true;
        this.loading = false;
        this.rebuildChart();
        this.syncFollow();
      },
      error: () => {
        if (seq !== this.loadSeq) return;
        this.loading = false;
        this.error = true;
      }
    });
  }

  private loadSeq = 0;

  private toLiveSeries(s: ChartSeries): LiveSeries {
    const ga = this.availableGas.find((g) => g.address === s.address);
    const isBool = this.isBoolDpt(ga?.datapointType);
    return {
      address: s.address,
      name: s.name || ga?.name || s.address,
      // For DPT1 the backend's "unit" is the label of the first value ("On", "Inactive"), not a
      // unit; kept, it ended up behind every number ("0.13 Inactive" as the average).
      unit: isBool ? '' : s.unit,
      isBool,
      data: s.points.map((p) => [new Date(p.t).getTime(), p.v] as [number, number]),
      carry: s.carry ? [new Date(s.carry.t).getTime(), s.carry.v] : null
    };
  }

  // --- Live append -----------------------------------------------------------
  private onLiveTelegram(t: KnxTelegram): void {
    // A custom range is a fixed window: an appended reading would lie past its end and trim the
    // curve from the left.
    if (!this.liveAppend || this.preset === 'custom') return;
    const s = this.series.find((x) => x.address === t.destinationAddress);
    if (!s) return;
    const v = extractNumeric(t.valueDecoded);
    if (v === null) return;
    const ts = new Date(t.timestamp).getTime();
    s.data.push([ts, v]);

    // Live means a window that moves with the data, not a range that grows without end. Trimming
    // the data (rather than pinning the axis) keeps the user's dataZoom selection working.
    if (this.rangeSpanMs > 0) this.dropBefore(s, ts - this.rangeSpanMs);
    if (s.data.length > this.maxLivePoints) this.dropBefore(s, 0, this.maxLivePoints);
    this.scheduleFlush();
  }

  // Coalesce rapid live appends into one chart update.
  private scheduleFlush(): void {
    this.dirty = true;
    if (this.flushTimer) return;
    this.flushTimer = setTimeout(() => {
      this.flushTimer = undefined;
      if (this.dirty) {
        this.dirty = false;
        this.rebuildChart();
      }
    }, 500);
  }

  // --- Chart build -----------------------------------------------------------
  /** Clears everything derived from a result set, so nothing from a previous load lingers. */
  private resetResultState(): void {
    this.stats = [];
    this.downSampled = false;
    this.truncated = false;
    this.shownPoints = 0;
    this.totalPoints = 0;
  }

  private rebuildChart(): void {
    if (this.series.length === 0) {
      this.chartOption = {};
      this.resetResultState();
      return;
    }

    // One Y axis per distinct unit, so no series is ever drawn in another unit's scale (#35).
    // Only the first few get labels and space; the rest are invisible but still scale their own
    // series, and tooltip and table give the exact values. All DPT1 series
    // share one fixed 0..1 axis instead: their "unit" from the backend is just the label of the
    // first value ("On", "False", "Start"), so grouping by it gave two switches two axes, and a
    // switch that stayed on for the whole range got an auto-scaled 0.5..1.5 axis; its 1 then sat
    // halfway up next to another switch's 1 at the top.
    const BOOL_AXIS = '\u0000bool';
    const axisKey = (s: LiveSeries) => (s.isBool ? BOOL_AXIS : s.unit);
    const keys: string[] = [];
    for (const s of this.series) {
      const k = axisKey(s);
      if (!keys.includes(k)) keys.push(k);
    }
    const keyToAxis = new Map(keys.map((k, i) => [k, i]));
    // Desktop fits a left axis and two on the right; a phone only one on each side.
    const shownAxes = Math.min(keys.length, this.isMobile ? 2 : 3);

    const skin = readSkin();
    const singleSeries = this.series.length === 1;
    const opts = this.options;

    const yAxis = keys.map((k, i) => valueAxis(skin, {
      show: i < shownAxes,
      position: i === 0 ? 'left' : 'right',
      offset: i >= 2 ? 60 : 0,
      splitLine: { show: i === 0, lineStyle: { color: skin.line } },
      ...(k === BOOL_AXIS
        ? { scale: false, min: 0, max: 1, interval: 1 }
        : {
            name: k || undefined,
            // Pull zero into view without clipping negative readings (outdoor temperatures).
            ...(opts.zeroBased ? { min: (v: { min: number }) => Math.min(0, v.min) } : {})
          })
    }));

    const following = this.following;
    const { now, xMin, xMax } = this.xWindow();

    const echartsSeries = this.series.map((s, i) => {
      const color = skin.palette[i % skin.palette.length];
      // 'line' keeps the long-standing behaviour of filling a lone numeric series;
      // 'area' forces the fill for every series, 'step' never fills.
      const withArea = opts.curve === 'area'
        || ((opts.curve === 'line' || opts.curve === 'smooth') && singleSeries && !s.isBool);
      // Booleans are always stepped — interpolating between on and off is a lie.
      const stepped = opts.curve === 'step' || s.isBool;

      return styleLineSeries({
        name: s.name,
        type: 'line' as const,
        yAxisIndex: keyToAxis.get(axisKey(s)) ?? 0,
        showSymbol: opts.showPoints,
        symbolSize: 4,
        step: stepped ? ('end' as const) : undefined,
        // 'x'-monotone smoothing never bends back in time or overshoots between two readings,
        // so a rounded curve does not invent peaks the sensor never reported.
        ...(opts.curve === 'smooth' && !stepped ? { smooth: 0.4, smoothMonotone: 'x' as const } : {}),
        // Thins out dense series for drawing without distorting the curve shape.
        sampling: 'lttb',
        // The line sits on the table's mean rather than ECharts' own 'average': that one would
        // also count the display-only point at "now" and drift away from the table.
        ...(opts.averageLine && this.seriesAvg(s) !== null
          ? {
              markLine: {
                silent: true,
                symbol: 'none',
                data: [{ yAxis: this.seriesAvg(s)! }],
                lineStyle: { color, type: 'dashed' as const, width: 1 },
                label: { color: skin.ink2, fontSize: 10, formatter: '⌀ {c}' }
              }
            }
          : {}),
        data: this.displayData(s, xMin, following ? now : undefined)
      }, color, withArea);
    });

    this.stats = this.series
      .filter((s) => s.data.length > 0 || s.carry)
      .map((s) => {
        // No reading inside the range, only the value carried in from before: that value held
        // the whole time, so it is min, max, average and last alike, with 0 points.
        if (s.data.length === 0) {
          const v = s.carry![1];
          return {
            name: s.name,
            address: s.address,
            unit: s.unit,
            color: skin.palette[this.series.indexOf(s) % skin.palette.length],
            min: v,
            max: v,
            avg: v,
            last: v,
            count: 0
          };
        }
        let min = s.data[0][1];
        let max = min;
        let sum = 0;
        for (const [, v] of s.data) {
          if (v < min) min = v;
          if (v > max) max = v;
          sum += v;
        }
        return {
          name: s.name,
          address: s.address,
          unit: s.unit,
          color: skin.palette[this.series.indexOf(s) % skin.palette.length],
          min,
          max,
          avg: sum / s.data.length,
          last: s.data[s.data.length - 1][1],
          count: s.data.length
        };
      });

    const unitByName: Record<string, string> = {};
    for (const s of this.series) unitByName[s.name] = s.unit;

    this.chartOption = {
      // Re-animating every curve on each live flush is distracting and costly.
      animation: !this.liveAppend,
      tooltip: tooltipCfg(skin, unitByName, localeTag(this.lang.lang()), (t) => this.tooltipRows(t, skin.palette)),
      legend: {
        // Kept in the option even when hidden: it carries the show/hide state that the
        // summary table drives.
        show: opts.showLegend,
        type: 'scroll',
        top: 0,
        textStyle: { color: skin.ink2 },
        data: this.series.map((s) => s.name),
        selected: Object.fromEntries(this.series.map((s) => [s.name, !this.hiddenSeries.has(s.name)]))
      },
      // Tighter margins on mobile so axes, legend and the dataZoom slider still fit the 46vh box.
      grid: this.isMobile
        // The right margin grows with the axes drawn there; the third one sits 60px further out.
        ? { left: 42, right: shownAxes > 1 ? 46 : 10, top: opts.showLegend ? 34 : 20, bottom: 84 }
        : { left: 56, right: shownAxes > 2 ? 132 : shownAxes > 1 ? 72 : 24, top: opts.showLegend ? 40 : 16, bottom: 92 },
      xAxis: { ...timeAxis(skin, localeTag(this.lang.lang())), min: xMin, max: xMax },
      yAxis,
      dataZoom: this.keepZoom(dataZoom(skin, this.isMobile)),
      series: echartsSeries
    };
  }

  /**
   * ngx-echarts replaces the whole option on every rebuild (live flush, theme, options), which
   * would throw away a zoom the user set with the slider. Carry the current window over.
   */
  private keepZoom(zooms: Array<Record<string, unknown>>): Array<Record<string, unknown>> {
    const current = (this.chart?.getOption() as { dataZoom?: { start?: number; end?: number }[] } | undefined)
      ?.dataZoom?.[0];
    // A fresh result starts unzoomed; the old window would cut into the new range.
    if (this.freshResult) {
      this.freshResult = false;
      return zooms;
    }
    if (!current || current.start == null || current.end == null) return zooms;
    if (current.start <= 0 && current.end >= 100) return zooms;
    return zooms.map((z) => ({ ...z, start: current.start, end: current.end }));
  }

  /**
   * Visible x window: live follows "now", otherwise the axis spans exactly the loaded range.
   * Pinning both ends (instead of letting ECharts fit the data) keeps the chosen period visible
   * even when the readings only cover part of it.
   */
  private xWindow(): { now: number; xMin?: number; xMax?: number } {
    // A server clock slightly ahead of the browser would otherwise push the newest reading past
    // the right edge, exactly the point live mode is about.
    const newest = Math.max(0, ...this.series.map((s) => s.data[s.data.length - 1]?.[0] ?? 0));
    const now = Math.max(Date.now(), newest);
    return this.following
      ? { now, xMin: now - this.rangeSpanMs, xMax: now }
      : { now, xMin: this.rangeFromMs || undefined, xMax: this.rangeToMs || undefined };
  }

  /**
   * One follow tick. Only the x window and the series data change, so they are merged into the
   * running chart instead of rebuilding the option: a rebuild would also reset the zoom.
   */
  private followTick(): void {
    if (!this.following || this.series.length === 0 || this.loading || document.hidden) return;
    // Points falling out on the left change min, max, average and the mean line: rebuild then
    // (rebuildChart keeps the zoom); otherwise only the window moves and a merge is enough.
    if (this.trimToWindow(Date.now()) || !this.chart) {
      this.rebuildChart();
      return;
    }
    const { now, xMin, xMax } = this.xWindow();
    this.chart.setOption({
      xAxis: { min: xMin, max: xMax },
      series: this.series.map((s) => ({ data: this.displayData(s, xMin, now) }))
    });
  }

  /** A tab in the background skips its ticks; catch up at once when it comes back. */
  @HostListener('document:visibilitychange')
  onVisibilityChange(): void {
    if (!document.hidden) this.followTick();
  }

  /** Mean of the readings in range, the same number the summary table shows. Only a carried-in
   *  value and no reading: that value held the whole time. */
  private seriesAvg(s: LiveSeries): number | null {
    if (s.data.length === 0) return s.carry ? s.carry[1] : null;
    let sum = 0;
    for (const [, v] of s.data) sum += v;
    return sum / s.data.length;
  }

  /**
   * A KNX value holds until the next telegram, so the curve is drawn across the whole window:
   * from the left edge with the value carried in from before it (when there is one), and in
   * live mode on to "now" with the last value. Both extra points are display only: they never
   * enter the stats or the CSV, and they get no symbol so they are not mistaken for readings.
   */
  private displayData(s: LiveSeries, start: number | undefined, now: number | undefined): unknown[] {
    const out: unknown[] = [];
    const first = s.data[0];
    if (s.carry && start !== undefined && (!first || first[0] > start)) {
      out.push({ value: [start, s.carry[1]], symbol: 'none' });
      // The carried value held until the first reading, so hold it flat up to there and let the
      // curve jump; a line or smooth curve would otherwise draw a ramp over the whole gap.
      if (first && first[1] !== s.carry[1]) {
        out.push({ value: [first[0], s.carry[1]], symbol: 'none' });
      }
    }
    out.push(...s.data);

    const last = s.data[s.data.length - 1] ?? (s.carry ? [start ?? s.carry[0], s.carry[1]] : undefined);
    if (now !== undefined && last && last[0] < now) {
      out.push({ value: [now, last[1]], symbol: 'none' });
    }
    return out;
  }

  /**
   * Tooltip rows for the time under the pointer: every visible series with the value in force
   * then, i.e. its last reading at or before that time (a KNX value holds until the next
   * telegram), or the carried-in value before the first one. The age tells a fresh reading from
   * a held one. Series without any value yet at that time are left out.
   */
  private tooltipRows(time: number, palette: string[]): TooltipRow[] {
    const rows: TooltipRow[] = [];
    this.series.forEach((s, i) => {
      if (this.hiddenSeries.has(s.name)) return;
      // Last index with timestamp <= time (binary search; data is in time order).
      let lo = 0;
      let hi = s.data.length - 1;
      let at = -1;
      while (lo <= hi) {
        const mid = (lo + hi) >> 1;
        if (s.data[mid][0] <= time) { at = mid; lo = mid + 1; } else hi = mid - 1;
      }
      const point = at >= 0 ? s.data[at] : s.carry;
      if (!point || point[0] > time) return;
      const ageMs = time - point[0];
      rows.push({
        name: s.name,
        color: palette[i % palette.length],
        value: point[1],
        unit: s.unit,
        // Under a second it is the reading itself; no age needed.
        age: ageMs >= 1000
          ? this.lang.translate('charts.tooltipAge', { age: formatDurationMinutes(ageMs / 60_000) })
          : undefined
      });
    });
    return rows;
  }

  get hasData(): boolean {
    return this.series.some((s) => s.data.length > 0 || s.carry);
  }

  liveTooltip(): string {
    if (this.preset === 'custom') return this.lang.translate('charts.liveCustom');
    return this.lang.translate(this.liveAppend ? 'charts.liveOn' : 'charts.liveOff');
  }
}
