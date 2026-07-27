import { Component, ElementRef, NgZone, OnInit, OnDestroy, ViewChild, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute } from '@angular/router';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { ECharts, EChartsCoreOption } from 'echarts/core';
import { Subscription } from 'rxjs';

import { ChartsService, ChartSeries, extractNumeric } from '../../core/services/charts.service';
import { ProjectService, GroupAddressDto } from '../../core/services/project.service';
import { SignalrService, KnxTelegram } from '../../core/services/signalr.service';
import { LanguageService } from '../../core/i18n/language.service';
import { localeTag } from '../../core/i18n/locale.util';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ThemeService } from '../../core/services/theme.service';
import { OverflowTitleDirective } from '../../shared/grid/overflow-title.directive';
import { RangePreset, resolveRange } from './time-range.util';
import { readSkin, valueAxis, timeAxis, tooltipCfg, dataZoom, styleLineSeries } from './chart-skin';
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
}

@Component({
  selector: 'app-charts',
  imports: [
    CommonModule, FormsModule, MatIconModule, MatSelectModule, MatFormFieldModule,
    MatTooltipModule, NgxEchartsDirective, TranslatePipe, OverflowTitleDirective
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
  @ViewChild('gaSearch') private gaSearch?: ElementRef<HTMLInputElement>;

  // --- Time range ------------------------------------------------------------
  preset: RangePreset = '24h';
  customFrom = '';
  customTo = '';
  readonly presets: RangePreset[] = ['1h', '24h', '7d', '30d'];

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

  private series: LiveSeries[] = [];
  chartOption: EChartsCoreOption = {};

  // --- Display options -------------------------------------------------------
  options: ChartDisplayOptions = loadChartOptions();
  optionsOpen = false;
  readonly curveModes: CurveMode[] = ['line', 'area', 'step'];

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
    this.telegramSub = this.signalr.telegram$.subscribe((t) => this.onLiveTelegram(t));
  }

  ngOnDestroy(): void {
    this.telegramSub?.unsubscribe();
    if (this.flushTimer) clearTimeout(this.flushTimer);
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
    const term = this.gaFilter.trim().toLowerCase();
    if (!term) return this.displayGas;
    const selected = new Set(this.selectedAddresses);
    return this.displayGas.filter(
      (ga) =>
        selected.has(ga.address) ||
        ga.address.toLowerCase().includes(term) ||
        (ga.name ?? '').toLowerCase().includes(term)
    );
  }

  /**
   * Splits a label into matched and unmatched runs so the hit can be emphasised. Returns plain
   * segments rendered as separate spans — no innerHTML, so a group-address name containing
   * markup stays inert text.
   */
  highlightParts(text: string): { text: string; hit: boolean }[] {
    const term = this.gaFilter.trim();
    if (!term || !text) return [{ text, hit: false }];

    const haystack = text.toLowerCase();
    const needle = term.toLowerCase();
    const parts: { text: string; hit: boolean }[] = [];

    let cursor = 0;
    while (cursor < text.length) {
      const hitAt = haystack.indexOf(needle, cursor);
      if (hitAt < 0) {
        parts.push({ text: text.slice(cursor), hit: false });
        break;
      }
      if (hitAt > cursor) parts.push({ text: text.slice(cursor, hitAt), hit: false });
      parts.push({ text: text.slice(hitAt, hitAt + needle.length), hit: true });
      cursor = hitAt + needle.length;
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

  // --- Time range ------------------------------------------------------------
  setPreset(p: RangePreset): void {
    this.preset = p;
    this.load();
  }

  applyCustom(): void {
    this.preset = 'custom';
    this.load();
  }

  toggleLive(): void {
    this.liveAppend = !this.liveAppend;
    // Animation is disabled while live so appended points don't re-animate the whole
    // curve every flush; rebuild so the change takes effect immediately.
    this.rebuildChart();
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
      this.series = [];
      this.chartOption = {};
      this.resetResultState();
      return;
    }
    const range = resolveRange(this.preset, this.customFrom, this.customTo);
    if (!range) return;

    this.rangeSpanMs = new Date(range.to).getTime() - new Date(range.from).getTime();

    this.loading = true;
    this.error = false;
    this.chartsService.getSeries(this.selectedAddresses, range.from, range.to, 2000).subscribe({
      next: (res) => {
        this.downSampled = res.series.some((s) => s.downSampled);
        this.truncated = res.truncated;
        this.totalPoints = res.series.reduce((sum, s) => sum + s.totalPoints, 0);
        this.shownPoints = res.series.reduce((sum, s) => sum + s.points.length, 0);
        this.series = res.series.map((s) => this.toLiveSeries(s));
        this.rebuildChart();
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.error = true;
      }
    });
  }

  private toLiveSeries(s: ChartSeries): LiveSeries {
    const ga = this.availableGas.find((g) => g.address === s.address);
    return {
      address: s.address,
      name: s.name || ga?.name || s.address,
      unit: s.unit,
      isBool: this.isBoolDpt(ga?.datapointType),
      data: s.points.map((p) => [new Date(p.t).getTime(), p.v] as [number, number])
    };
  }

  // --- Live append -----------------------------------------------------------
  private onLiveTelegram(t: KnxTelegram): void {
    if (!this.liveAppend) return;
    const s = this.series.find((x) => x.address === t.destinationAddress);
    if (!s) return;
    const v = extractNumeric(t.valueDecoded);
    if (v === null) return;
    const ts = new Date(t.timestamp).getTime();
    s.data.push([ts, v]);

    // Live means a window that moves with the data, not a range that grows without end. Trimming
    // the data (rather than pinning the axis) keeps the user's dataZoom selection working.
    if (this.rangeSpanMs > 0) {
      const cutoff = ts - this.rangeSpanMs;
      let drop = 0;
      while (drop < s.data.length && s.data[drop][0] < cutoff) drop++;
      if (drop > 0) s.data.splice(0, drop);
    }

    if (s.data.length > this.maxLivePoints) {
      s.data.splice(0, s.data.length - this.maxLivePoints);
    }
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

    // One Y axis per distinct unit (max 3); extra units share the last axis.
    const units: string[] = [];
    for (const s of this.series) {
      if (!units.includes(s.unit)) units.push(s.unit);
    }
    const axisUnits = units.slice(0, 3);
    const unitToAxis = new Map<string, number>();
    units.forEach((u, i) => unitToAxis.set(u, Math.min(i, axisUnits.length - 1)));

    const skin = readSkin();
    const singleSeries = this.series.length === 1;
    const opts = this.options;

    const yAxis = axisUnits.map((u, i) => valueAxis(skin, {
      name: u || undefined,
      position: i === 0 ? 'left' : 'right',
      offset: i >= 2 ? 60 : 0,
      splitLine: { show: i === 0, lineStyle: { color: skin.line } },
      // Pull zero into view without clipping negative readings (outdoor temperatures).
      ...(opts.zeroBased ? { min: (v: { min: number }) => Math.min(0, v.min) } : {})
    }));

    const echartsSeries = this.series.map((s, i) => {
      const color = skin.palette[i % skin.palette.length];
      // 'line' keeps the long-standing behaviour of filling a lone numeric series;
      // 'area' forces the fill for every series, 'step' never fills.
      const withArea = opts.curve === 'area'
        || (opts.curve === 'line' && singleSeries && !s.isBool);
      // Booleans are always stepped — interpolating between on and off is a lie.
      const stepped = opts.curve === 'step' || s.isBool;

      return styleLineSeries({
        name: s.name,
        type: 'line' as const,
        yAxisIndex: unitToAxis.get(s.unit) ?? 0,
        showSymbol: opts.showPoints,
        symbolSize: 4,
        step: stepped ? ('end' as const) : undefined,
        // Thins out dense series for drawing without distorting the curve shape.
        sampling: 'lttb',
        ...(opts.averageLine
          ? {
              markLine: {
                silent: true,
                symbol: 'none',
                data: [{ type: 'average' as const }],
                lineStyle: { color, type: 'dashed' as const, width: 1 },
                label: { color: skin.ink2, fontSize: 10, formatter: '⌀ {c}' }
              }
            }
          : {}),
        data: s.data
      }, color, withArea);
    });

    this.stats = this.series
      .filter((s) => s.data.length > 0)
      .map((s, i) => {
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
      tooltip: tooltipCfg(skin, unitByName, localeTag(this.lang.lang())),
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
      grid: { left: 56, right: axisUnits.length > 1 ? 72 : 24, top: opts.showLegend ? 40 : 16, bottom: 92 },
      xAxis: timeAxis(skin),
      yAxis,
      dataZoom: dataZoom(skin),
      series: echartsSeries
    };
  }

  get hasData(): boolean {
    return this.series.some((s) => s.data.length > 0);
  }

  liveTooltip(): string {
    return this.lang.translate(this.liveAppend ? 'charts.liveOn' : 'charts.liveOff');
  }
}
