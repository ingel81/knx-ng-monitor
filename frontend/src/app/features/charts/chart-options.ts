/**
 * User-facing display options for the charts view, persisted across sessions the same way
 * theme and density are — a chart that resets its appearance on every navigation is useless.
 */

export type CurveMode = 'line' | 'area' | 'step';

export interface ChartDisplayOptions {
  curve: CurveMode;
  /** Draw a marker at every data point — tells apart "few readings" from "flat line". */
  showPoints: boolean;
  /** Include zero in the value axis instead of auto-scaling to the data range. */
  zeroBased: boolean;
  /** Horizontal mean line per series. */
  averageLine: boolean;
  showLegend: boolean;
}

export const DEFAULT_CHART_OPTIONS: ChartDisplayOptions = {
  curve: 'line',
  showPoints: false,
  zeroBased: false,
  averageLine: false,
  // Off by default: the summary table below the chart already lists colour and name, and it can
  // toggle series too. Showing both legends only costs vertical space for the chart.
  showLegend: false
};

const STORAGE_KEY = 'knx.chart-options';

export function loadChartOptions(): ChartDisplayOptions {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return { ...DEFAULT_CHART_OPTIONS };
    const parsed = JSON.parse(raw) as Partial<ChartDisplayOptions>;
    // Merge over the defaults so options added in a later version get a sane value
    // instead of `undefined` from an older stored object.
    return { ...DEFAULT_CHART_OPTIONS, ...parsed };
  } catch {
    // Private mode / disabled storage / corrupt JSON — the defaults are always fine.
    return { ...DEFAULT_CHART_OPTIONS };
  }
}

export function saveChartOptions(options: ChartDisplayOptions): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(options));
  } catch {
    // Not being able to remember the setting is not worth surfacing to the user.
  }
}

/**
 * Last group-address selection and time range. Re-picking eight addresses on every visit is
 * the most tedious part of the view, so it is remembered like the display options above.
 */
export interface ChartQueryState {
  addresses: string[];
  preset: string;
  customFrom: string;
  customTo: string;
}

const QUERY_KEY = 'knx.chart-query';

export function loadChartQuery(): ChartQueryState | null {
  try {
    const raw = localStorage.getItem(QUERY_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<ChartQueryState>;
    if (!Array.isArray(parsed.addresses)) return null;
    return {
      addresses: parsed.addresses.filter((a): a is string => typeof a === 'string'),
      preset: typeof parsed.preset === 'string' ? parsed.preset : '24h',
      customFrom: typeof parsed.customFrom === 'string' ? parsed.customFrom : '',
      customTo: typeof parsed.customTo === 'string' ? parsed.customTo : ''
    };
  } catch {
    return null;
  }
}

export function saveChartQuery(state: ChartQueryState): void {
  try {
    localStorage.setItem(QUERY_KEY, JSON.stringify(state));
  } catch {
    // Ignored, same reasoning as above.
  }
}
