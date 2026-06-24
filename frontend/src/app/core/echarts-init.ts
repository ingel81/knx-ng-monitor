/**
 * Lazy, tree-shaken ECharts bootstrap. Imported on demand by ngx-echarts'
 * `provideEchartsCore({ echarts: () => import(...) })` so the (sizeable) charting
 * runtime stays out of the eager main bundle and only loads with the charts/stats routes.
 */
import * as echarts from 'echarts/core';
import { LineChart, BarChart, HeatmapChart } from 'echarts/charts';
import {
  GridComponent,
  TooltipComponent,
  LegendComponent,
  DataZoomComponent,
  MarkLineComponent,
  VisualMapComponent
} from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';

echarts.use([
  LineChart,
  BarChart,
  HeatmapChart,
  GridComponent,
  TooltipComponent,
  LegendComponent,
  DataZoomComponent,
  MarkLineComponent,
  VisualMapComponent,
  CanvasRenderer
]);

export default echarts;
