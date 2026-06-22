/**
 * Theme-aware ECharts styling derived from the design tokens (CHANGES.md §1/§2).
 * ECharts renders to canvas, so CSS `var(--token)` cannot be used directly — the
 * token values are read from the document root here and fed into the chart option.
 * Re-read (and the chart rebuilt) whenever the app theme toggles.
 */

export interface ChartSkin {
  series: string;
  ink: string;
  ink2: string;
  ink3: string;
  line: string;
  line2: string;
  surface: string;
  brand: string;
  teal200: string;
  mono: string;
  palette: string[];
}

function token(name: string, fallback = ''): string {
  if (typeof getComputedStyle === 'undefined') return fallback;
  const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return v || fallback;
}

/** #RRGGBB → rgba(r,g,b,a). Passes through non-hex (already rgba) unchanged. */
function withAlpha(color: string, alpha: number): string {
  const m = /^#?([0-9a-f]{6})$/i.exec(color.trim());
  if (!m) return color;
  const n = parseInt(m[1], 16);
  return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${alpha})`;
}

export function readSkin(): ChartSkin {
  const series = token('--series', '#4F63D6');
  return {
    series,
    ink: token('--ink', '#16201D'),
    ink2: token('--ink-2', '#51605B'),
    ink3: token('--ink-3', '#8A958F'),
    line: token('--line', '#E2E5DE'),
    line2: token('--line-2', '#D2D6CD'),
    surface: token('--surface', '#FFFFFF'),
    brand: token('--brand', '#0F766E'),
    teal200: token('--teal-200', '#A9D6CE'),
    mono: token('--font-mono', 'monospace'),
    // First series = the tokenized --series; extra series reuse semantic accents.
    palette: [
      series,
      token('--teal-700', '#0F766E'),
      token('--response', '#B26B07'),
      token('--read', '#2563A6'),
      token('--groupread', '#6B5BB0'),
      token('--err', '#BC3B2C'),
      token('--ok', '#2F7A43'),
      token('--warn', '#B26B07'),
    ],
  };
}

const pad = (n: number) => String(n).padStart(2, '0');

/** "nice scale" value axis: rounded steps, ~7 ticks, mono tick labels (§1.1). */
export function valueAxis(skin: ChartSkin, extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    type: 'value',
    scale: true,
    splitNumber: 7,
    nameTextStyle: { color: skin.ink2, fontFamily: skin.mono, fontSize: 11, align: 'left' },
    nameGap: 14,
    axisLabel: { color: skin.ink3, fontFamily: skin.mono, fontSize: 11 },
    axisLine: { show: false },
    axisTick: { show: false },
    splitLine: { lineStyle: { color: skin.line } },
    ...extra,
  };
}

/** Time axis: no boundary gap, midnight rendered as a bold day number (§1.1). */
export function timeAxis(skin: ChartSkin): Record<string, unknown> {
  return {
    type: 'time',
    boundaryGap: false,
    axisLine: { lineStyle: { color: skin.line2 } },
    axisTick: { lineStyle: { color: skin.line2 } },
    axisLabel: {
      fontFamily: skin.mono,
      fontSize: 11,
      formatter: (val: number) => {
        const d = new Date(val);
        if (d.getHours() === 0 && d.getMinutes() === 0) return `{day|${d.getDate()}}`;
        return `{t|${pad(d.getHours())}:${pad(d.getMinutes())}}`;
      },
      rich: {
        day: { fontWeight: 700, color: skin.ink, fontFamily: skin.mono, fontSize: 11 },
        t: { fontWeight: 500, color: skin.ink3, fontFamily: skin.mono, fontSize: 11 },
      },
    },
  };
}

/**
 * Axis tooltip with dashed crosshair. Each row shows a colour marker, the series
 * name, and the mono value with its unit (§1.1). `units` maps seriesName → unit.
 */
export function tooltipCfg(
  skin: ChartSkin, units: Record<string, string> = {}, locale?: string,
): Record<string, unknown> {
  return {
    trigger: 'axis',
    backgroundColor: skin.surface,
    borderColor: skin.line2,
    borderWidth: 1,
    borderRadius: 8,
    padding: [10, 12],
    extraCssText: 'box-shadow:0 6px 18px -6px rgba(16,32,29,.28);',
    textStyle: { color: skin.ink2, fontSize: 11 },
    axisPointer: {
      type: 'line',
      lineStyle: { color: skin.ink3, type: 'dashed', width: 1 },
    },
    formatter: (params: unknown) => {
      const arr = (Array.isArray(params) ? params : [params]) as Array<{
        value: [number, number]; color: string; seriesName: string;
      }>;
      if (!arr.length) return '';
      const t = new Date(arr[0].value[0]);
      const stamp = t.toLocaleString(locale, {
        day: '2-digit', month: '2-digit', year: 'numeric',
        hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
      });
      const head =
        `<div style="font-family:${skin.mono};font-size:11px;color:${skin.ink3};` +
        `margin-bottom:6px;letter-spacing:.02em">${stamp}</div>`;
      const rows = arr.map((p) => {
        const unit = units[p.seriesName] || '';
        const v = typeof p.value[1] === 'number'
          ? p.value[1].toLocaleString(undefined, { maximumFractionDigits: 2 })
          : p.value[1];
        const unitHtml = unit
          ? ` <span style="color:${skin.ink3};font-weight:500;font-size:11px">${unit}</span>`
          : '';
        return (
          `<div style="display:flex;align-items:center;justify-content:space-between;gap:16px;margin:3px 0">` +
            `<span style="display:inline-flex;align-items:center;gap:7px;color:${skin.ink2};font-size:12px">` +
              `<span style="width:8px;height:8px;border-radius:2px;background:${p.color};display:inline-block"></span>` +
              `${p.seriesName}</span>` +
            `<span style="font-family:${skin.mono};font-weight:700;font-size:13px;color:${skin.ink}">${v}${unitHtml}</span>` +
          `</div>`
        );
      }).join('');
      return head + rows;
    },
  };
}

/** Inside + slider dataZoom, slider styled with brand handles / teal frame (§1.3). */
export function dataZoom(skin: ChartSkin): Array<Record<string, unknown>> {
  return [
    { type: 'inside' },
    {
      type: 'slider',
      bottom: 40,
      height: 28,
      borderColor: skin.teal200,
      fillerColor: withAlpha(skin.series, 0.16),
      dataBackground: { lineStyle: { color: skin.line2 }, areaStyle: { color: withAlpha(skin.series, 0.16) } },
      selectedDataBackground: { lineStyle: { color: skin.series }, areaStyle: { color: withAlpha(skin.series, 0.28) } },
      handleStyle: { color: skin.brand, borderColor: skin.brand },
      moveHandleStyle: { color: skin.brand },
      textStyle: { color: skin.ink3, fontFamily: skin.mono, fontSize: 10 },
    },
  ];
}

/** Enrich a line-series with the tokenized colour, round 1.7px stroke + area fill. */
export function styleLineSeries(
  s: Record<string, unknown>, color: string, withArea: boolean,
): Record<string, unknown> {
  return {
    ...s,
    itemStyle: { color },
    lineStyle: { width: 1.7, cap: 'round', join: 'round', color },
    ...(withArea
      ? {
          areaStyle: {
            color: {
              type: 'linear', x: 0, y: 0, x2: 0, y2: 1,
              colorStops: [
                { offset: 0, color: withAlpha(color, 0.2) },
                { offset: 1, color: withAlpha(color, 0) },
              ],
            },
          },
        }
      : {}),
  };
}
