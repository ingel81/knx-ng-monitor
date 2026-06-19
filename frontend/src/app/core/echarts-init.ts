/**
 * Lazy, tree-shaken ECharts bootstrap. Imported on demand by ngx-echarts'
 * `provideEchartsCore({ echarts: () => import(...) })` so the (sizeable) charting
 * runtime stays out of the eager main bundle and only loads with the charts/stats routes.
 */
import * as echarts from 'echarts/core';
import { LineChart, BarChart } from 'echarts/charts';
import {
  GridComponent,
  TooltipComponent,
  LegendComponent,
  DataZoomComponent,
  MarkLineComponent
} from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';

echarts.use([
  LineChart,
  BarChart,
  GridComponent,
  TooltipComponent,
  LegendComponent,
  DataZoomComponent,
  MarkLineComponent,
  CanvasRenderer
]);

export default echarts;
