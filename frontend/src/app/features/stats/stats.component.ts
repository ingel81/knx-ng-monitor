import { Component, HostListener, OnInit, ViewChild, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { EChartsCoreOption } from 'echarts/core';

import { ChartsService, StatsResponse, HeatmapResponse, TopEntry, TopResponse } from '../../core/services/charts.service';
import { LanguageService } from '../../core/i18n/language.service';
import { localeTag } from '../../core/i18n/locale.util';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { DateTimeFieldComponent } from '../../shared/date-time-field/date-time-field.component';
import { DptTitleDirective } from '../../shared/grid/dpt-title.directive';
import { ThemeService } from '../../core/services/theme.service';
import { RangePreset, isRangeOrdered, resolveRange } from '../charts/time-range.util';
import { readSkin, valueAxis, timeAxis, tooltipCfg, dataZoom } from '../charts/chart-skin';

type TopLimit = 10 | 20 | 50;

@Component({
  selector: 'app-stats',
  imports: [
    CommonModule, FormsModule, MatIconModule, MatTooltipModule, NgxEchartsDirective, TranslatePipe,
    DateTimeFieldComponent, DptTitleDirective
  ],
  templateUrl: './stats.component.html',
  styleUrl: './stats.component.scss'
})
export class StatsComponent implements OnInit {
  private chartsService = inject(ChartsService);
  private lang = inject(LanguageService);
  private theme = inject(ThemeService);
  private router = inject(Router);

  /** Last bucket data, kept so the chart can be rebuilt on a theme toggle. */
  private lastData: [number, number][] | null = null;
  private lastGrid: number[][] | null = null;

  constructor() {
    // Rebuild on a theme toggle and on a language switch (day labels and the first weekday).
    effect(() => {
      this.theme.theme();
      this.lang.lang();
      if (this.lastData) this.buildChart(this.lastData);
      if (this.lastGrid) this.buildHeatmap(this.lastGrid);
    });
  }

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
  total = 0;
  avgRate = 0;
  hasData = false;
  chartOption: EChartsCoreOption = {};
  heatmapOption: EChartsCoreOption = {};
  hasHeatmap = false;

  // --- Top group addresses / senders (#38) ------------------------------------
  readonly topLimits: TopLimit[] = [10, 20, 50];
  topLimit: TopLimit = 20;
  topTab: 'ga' | 'sender' = 'ga';
  top: TopResponse | null = null;
  topLoading = false;
  topError = false;
  /** Range the top list was loaded for; a row click opens exactly this window in the archive. */
  private topRange: { from: string; to: string } | null = null;

  get topRows(): TopEntry[] {
    if (!this.top) return [];
    return this.topTab === 'ga' ? this.top.groupAddresses : this.top.senders;
  }

  ngOnInit(): void {
    this.load();
  }

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
    const mobile = window.innerWidth < 768;
    if (mobile === this.isMobile) return;
    this.isMobile = mobile;
    if (this.lastData) this.buildChart(this.lastData);
    if (this.lastGrid) this.buildHeatmap(this.lastGrid);
  }

  load(): void {
    const range = resolveRange(this.preset, this.customFrom, this.customTo);
    if (!range) return;

    this.loading = true;
    this.error = false;
    this.chartsService.getStats(range.from, range.to, 60).subscribe({
      next: (res) => {
        this.applyStats(res, range.from, range.to);
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.error = true;
      }
    });

    this.loadTop(range);

    const tz = new Date().getTimezoneOffset();
    this.chartsService.getHeatmap(range.from, range.to, tz).subscribe({
      next: (res) => this.applyHeatmap(res),
      error: () => { this.hasHeatmap = false; },
    });
  }

  setTopLimit(limit: TopLimit): void {
    if (limit === this.topLimit) return;
    this.topLimit = limit;
    if (this.topRange) this.loadTop(this.topRange);
  }

  private loadTop(range: { from: string; to: string }): void {
    this.topRange = range;
    this.topLoading = true;
    this.topError = false;
    const limit = this.topLimit;
    this.chartsService.getTop(range.from, range.to, limit).subscribe({
      next: (res) => {
        // A slow answer for an older range or limit must not overwrite the current one.
        if (this.topRange !== range || this.topLimit !== limit) return;
        this.top = res;
        this.topLoading = false;
      },
      error: () => {
        if (this.topRange !== range || this.topLimit !== limit) return;
        this.topLoading = false;
        this.topError = true;
      }
    });
  }

  /** Opens the monitor archive filtered to this group address or sender over the same range. */
  openInMonitor(row: TopEntry): void {
    if (!this.topRange) return;
    const filter = this.topTab === 'ga' ? { address: row.address } : { source: row.address };
    void this.router.navigate(['/monitor'], {
      queryParams: { mode: 'archive', ...filter, from: this.topRange.from, to: this.topRange.to }
    });
  }

  formatShare(share: number): string {
    return new Intl.NumberFormat(localeTag(this.lang.lang()), {
      minimumFractionDigits: 1, maximumFractionDigits: 1
    }).format(share);
  }

  formatPerHour(perHour: number): string {
    return new Intl.NumberFormat(localeTag(this.lang.lang()), {
      maximumFractionDigits: perHour >= 10 ? 0 : 1
    }).format(perHour);
  }

  formatCount(count: number): string {
    return new Intl.NumberFormat(localeTag(this.lang.lang())).format(count);
  }

  private applyHeatmap(res: HeatmapResponse): void {
    this.lastGrid = res.grid;
    this.hasHeatmap = (res.total ?? 0) > 0;
    this.buildHeatmap(res.grid);
  }

  /**
   * weekday × hour heatmap. Backend grid is [0=Sun..6=Sat][hour] (the .NET count); the week
   * follows the app language instead (#25): German starts on Monday, English on Sunday.
   */
  private buildHeatmap(grid: number[][]): void {
    const skin = readSkin();
    const de = this.lang.lang() === 'de';
    // Backend day index per row, first row on top.
    const order = de ? [1, 2, 3, 4, 5, 6, 0] : [0, 1, 2, 3, 4, 5, 6];
    const labels = de
      ? ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa']
      : ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
    const days = order.map((d) => labels[d]);
    const hours = Array.from({ length: 24 }, (_, h) => String(h).padStart(2, '0'));

    const data: [number, number, number][] = [];
    let max = 0;
    order.forEach((d, row) => {
      for (let h = 0; h < 24; h++) {
        const v = grid?.[d]?.[h] ?? 0;
        if (v > max) max = v;
        data.push([h, row, v]);
      }
    });

    this.heatmapOption = {
      tooltip: {
        backgroundColor: skin.surface, borderColor: skin.line2, borderWidth: 1, borderRadius: 8,
        padding: [8, 10], textStyle: { color: skin.ink2, fontSize: 11 },
        formatter: (p: { value: [number, number, number] }) =>
          `<b>${days[p.value[1]]} ${hours[p.value[0]]}:00</b><br>${p.value[2]} ${this.lang.translate('stats.telegrams')}`,
      },
      // The legend sits below the plot, so `bottom` has to clear the hour labels *and* the
      // visualMap bar — at 30 they shared one band and overlapped.
      grid: {
        left: this.isMobile ? 30 : 44,
        right: this.isMobile ? 6 : 16,
        top: 10,
        bottom: this.isMobile ? 46 : 44
      },
      xAxis: {
        type: 'category', data: hours, splitArea: { show: true },
        // All 24 hour labels overlap on a narrow screen — show every 4th there.
        axisLabel: {
          color: skin.ink3, fontFamily: skin.mono, fontSize: 10,
          interval: this.isMobile ? 3 : 1
        },
        axisLine: { lineStyle: { color: skin.line2 } }, axisTick: { show: false },
      },
      yAxis: {
        type: 'category', data: days, splitArea: { show: true },
        // A category axis starts at the bottom; inverted, the week reads top to bottom.
        inverse: true,
        axisLabel: { color: skin.ink3, fontFamily: skin.mono, fontSize: 11 },
        axisLine: { lineStyle: { color: skin.line2 } }, axisTick: { show: false },
      },
      visualMap: {
        min: 0, max: Math.max(1, max), calculable: true, orient: 'horizontal', left: 'center', bottom: 0,
        inRange: { color: [skin.surface, skin.series] },
        textStyle: { color: skin.ink3, fontSize: 10 },
        ...(this.isMobile ? { itemWidth: 10, itemHeight: 60 } : {}),
      },
      series: [{
        type: 'heatmap', data, progressive: 0,
        itemStyle: { borderColor: skin.surface, borderWidth: 1, borderRadius: 2 },
        emphasis: { itemStyle: { borderColor: skin.ink2, borderWidth: 1 } },
      }],
    };
  }

  private applyStats(res: StatsResponse, from: string, to: string): void {
    this.total = res.total;
    const rangeSeconds = Math.max(1, (new Date(to).getTime() - new Date(from).getTime()) / 1000);
    this.avgRate = res.total / rangeSeconds;
    this.hasData = res.total > 0;

    const data = res.counts.map((b) => [new Date(b.t).getTime(), b.count] as [number, number]);
    this.lastData = data;
    this.buildChart(data);
  }

  private buildChart(data: [number, number][]): void {
    const skin = readSkin();
    this.chartOption = {
      tooltip: tooltipCfg(skin, {}, localeTag(this.lang.lang())),
      grid: {
        left: this.isMobile ? 44 : 56,
        right: this.isMobile ? 12 : 24,
        top: 24,
        bottom: this.isMobile ? 84 : 92
      },
      xAxis: timeAxis(skin, localeTag(this.lang.lang())),
      // The axis name overlaps the left edge at 360px, so it is dropped on mobile.
      yAxis: valueAxis(skin, {
        min: 0,
        name: this.isMobile ? undefined : this.lang.translate('stats.bucketCount')
      }),
      dataZoom: dataZoom(skin, this.isMobile),
      series: [
        {
          name: this.lang.translate('stats.overTime'),
          type: 'bar',
          barWidth: '70%',
          itemStyle: { color: skin.series },
          data
        }
      ]
    };
  }

  get avgRateLabel(): string {
    return this.avgRate >= 10 ? this.avgRate.toFixed(0) : this.avgRate.toFixed(2);
  }
}
