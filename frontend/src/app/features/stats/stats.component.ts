import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { EChartsCoreOption } from 'echarts/core';

import { ChartsService, StatsResponse } from '../../core/services/charts.service';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { RangePreset, resolveRange } from '../charts/time-range.util';

@Component({
  selector: 'app-stats',
  imports: [CommonModule, FormsModule, MatIconModule, NgxEchartsDirective, TranslatePipe],
  templateUrl: './stats.component.html',
  styleUrl: './stats.component.scss'
})
export class StatsComponent implements OnInit {
  private chartsService = inject(ChartsService);
  private lang = inject(LanguageService);

  // --- Time range ------------------------------------------------------------
  preset: RangePreset = '24h';
  customFrom = '';
  customTo = '';
  readonly presets: RangePreset[] = ['1h', '24h', '7d', '30d'];

  // --- State -----------------------------------------------------------------
  loading = false;
  error = false;
  total = 0;
  avgRate = 0;
  hasData = false;
  chartOption: EChartsCoreOption = {};

  ngOnInit(): void {
    this.load();
  }

  setPreset(p: RangePreset): void {
    this.preset = p;
    this.load();
  }

  applyCustom(): void {
    this.preset = 'custom';
    this.load();
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
  }

  private applyStats(res: StatsResponse, from: string, to: string): void {
    this.total = res.total;
    const rangeSeconds = Math.max(1, (new Date(to).getTime() - new Date(from).getTime()) / 1000);
    this.avgRate = res.total / rangeSeconds;
    this.hasData = res.total > 0;

    const data = res.counts.map((b) => [new Date(b.t).getTime(), b.count] as [number, number]);
    this.chartOption = {
      tooltip: { trigger: 'axis' },
      grid: { left: 56, right: 24, top: 24, bottom: 56 },
      xAxis: { type: 'time' },
      yAxis: { type: 'value', name: this.lang.translate('stats.bucketCount') },
      dataZoom: [{ type: 'inside' }, { type: 'slider', bottom: 8 }],
      series: [
        {
          name: this.lang.translate('stats.overTime'),
          type: 'bar',
          barWidth: '70%',
          data
        }
      ]
    };
  }

  get avgRateLabel(): string {
    return this.avgRate >= 10 ? this.avgRate.toFixed(0) : this.avgRate.toFixed(2);
  }
}
