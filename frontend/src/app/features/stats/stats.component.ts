import { Component, ElementRef, HostListener, OnInit, ViewChild, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { EChartsCoreOption } from 'echarts/core';

import { ChartsService, StatsResponse, HeatmapResponse } from '../../core/services/charts.service';
import { LanguageService } from '../../core/i18n/language.service';
import { localeTag } from '../../core/i18n/locale.util';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ThemeService } from '../../core/services/theme.service';
import { RangePreset, resolveRange } from '../charts/time-range.util';
import { readSkin, valueAxis, timeAxis, tooltipCfg, dataZoom } from '../charts/chart-skin';

@Component({
  selector: 'app-stats',
  imports: [CommonModule, FormsModule, MatIconModule, NgxEchartsDirective, TranslatePipe],
  templateUrl: './stats.component.html',
  styleUrl: './stats.component.scss'
})
export class StatsComponent implements OnInit {
  private chartsService = inject(ChartsService);
  private lang = inject(LanguageService);
  private theme = inject(ThemeService);

  /** Last bucket data, kept so the chart can be rebuilt on a theme toggle. */
  private lastData: [number, number][] | null = null;
  private lastGrid: number[][] | null = null;

  constructor() {
    effect(() => {
      this.theme.theme();
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
  /** Apply was pressed with an incomplete custom range — marks the empty field. */
  rangeHint = false;
  isMobile = window.innerWidth < 768;
  @ViewChild('fromInput') private fromInput?: ElementRef<HTMLInputElement>;
  @ViewChild('toInput') private toInput?: ElementRef<HTMLInputElement>;

  // --- State -----------------------------------------------------------------
  loading = false;
  error = false;
  total = 0;
  avgRate = 0;
  hasData = false;
  chartOption: EChartsCoreOption = {};
  heatmapOption: EChartsCoreOption = {};
  hasHeatmap = false;

  ngOnInit(): void {
    this.load();
  }

  setPreset(p: RangePreset): void {
    // A preset supersedes the custom range, so a pending "pick both dates" hint is stale.
    this.rangeHint = false;
    this.preset = p;
    this.load();
  }

  /**
   * Validates here instead of disabling the button: on touch a disabled control gives no
   * feedback at all, so a tap now says what is missing and jumps to that field.
   */
  applyCustom(): void {
    if (!this.customFrom || !this.customTo) {
      this.rangeHint = true;
      (this.customFrom ? this.toInput : this.fromInput)?.nativeElement.focus();
      return;
    }
    this.rangeHint = false;
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

    const tz = new Date().getTimezoneOffset();
    this.chartsService.getHeatmap(range.from, range.to, tz).subscribe({
      next: (res) => this.applyHeatmap(res),
      error: () => { this.hasHeatmap = false; },
    });
  }

  private applyHeatmap(res: HeatmapResponse): void {
    this.lastGrid = res.grid;
    this.hasHeatmap = (res.total ?? 0) > 0;
    this.buildHeatmap(res.grid);
  }

  /** weekday(Mon..Sun) × hour heatmap. Backend grid is [0=Sun..6=Sat][hour]. */
  private buildHeatmap(grid: number[][]): void {
    const skin = readSkin();
    const order = [1, 2, 3, 4, 5, 6, 0];                 // Mon..Sun
    const de = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'];
    const en = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    const days = this.lang.lang() === 'de' ? de : en;
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
