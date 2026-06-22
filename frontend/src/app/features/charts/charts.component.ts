import { Component, OnInit, OnDestroy, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute } from '@angular/router';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { EChartsCoreOption } from 'echarts/core';
import { Subscription } from 'rxjs';

import { ChartsService, ChartSeries, extractNumeric } from '../../core/services/charts.service';
import { ProjectService, GroupAddressDto } from '../../core/services/project.service';
import { SignalrService, KnxTelegram } from '../../core/services/signalr.service';
import { LanguageService } from '../../core/i18n/language.service';
import { localeTag } from '../../core/i18n/locale.util';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ThemeService } from '../../core/services/theme.service';
import { RangePreset, resolveRange } from './time-range.util';
import { readSkin, valueAxis, timeAxis, tooltipCfg, dataZoom, styleLineSeries } from './chart-skin';

/** DPT main numbers we treat as chartable (numeric) — plus DPT1 (boolean step lines). */
const NUMERIC_DPT_MAINS = new Set([5, 6, 7, 8, 9, 12, 13, 14]);

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
    MatTooltipModule, NgxEchartsDirective, TranslatePipe
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
  selectedAddresses: string[] = [];
  hasActiveProject = false;

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

  private series: LiveSeries[] = [];
  chartOption: EChartsCoreOption = {};

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
            this.applyPreselect();
          },
          error: () => (this.availableGas = [])
        });
      },
      error: () => (this.hasActiveProject = false)
    });
  }

  /** Preselect the GA from the `ga` query param (once), if it's chartable. */
  private applyPreselect(): void {
    if (!this.preselectGa) return;
    const ga = this.preselectGa;
    this.preselectGa = null;
    if (this.availableGas.some((g) => g.address === ga)) {
      this.selectedAddresses = [ga];
      this.load();
    }
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

  // --- Selection -------------------------------------------------------------
  onSelectionChange(addresses: string[]): void {
    if (addresses.length > this.maxSeries) {
      addresses = addresses.slice(0, this.maxSeries);
    }
    this.selectedAddresses = addresses;
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
  }

  // --- Data load -------------------------------------------------------------
  load(): void {
    if (this.selectedAddresses.length === 0) {
      this.series = [];
      this.chartOption = {};
      return;
    }
    const range = resolveRange(this.preset, this.customFrom, this.customTo);
    if (!range) return;

    this.loading = true;
    this.error = false;
    this.chartsService.getSeries(this.selectedAddresses, range.from, range.to, 2000).subscribe({
      next: (res) => {
        this.downSampled = res.series.some((s) => s.downSampled);
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
  private rebuildChart(): void {
    if (this.series.length === 0) {
      this.chartOption = {};
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

    const yAxis = axisUnits.map((u, i) => valueAxis(skin, {
      name: u || undefined,
      position: i === 0 ? 'left' : 'right',
      offset: i >= 2 ? 60 : 0,
      splitLine: { show: i === 0, lineStyle: { color: skin.line } }
    }));

    const echartsSeries = this.series.map((s, i) => styleLineSeries({
      name: s.name,
      type: 'line' as const,
      yAxisIndex: unitToAxis.get(s.unit) ?? 0,
      showSymbol: false,
      step: s.isBool ? ('end' as const) : undefined,
      data: s.data
    }, skin.palette[i % skin.palette.length], singleSeries && !s.isBool));

    const unitByName: Record<string, string> = {};
    for (const s of this.series) unitByName[s.name] = s.unit;

    this.chartOption = {
      tooltip: tooltipCfg(skin, unitByName, localeTag(this.lang.lang())),
      legend: { type: 'scroll', top: 0, textStyle: { color: skin.ink2 }, data: this.series.map((s) => s.name) },
      grid: { left: 56, right: axisUnits.length > 1 ? 72 : 24, top: 40, bottom: 64 },
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
