import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimations } from '@angular/platform-browser/animations';
import { provideEchartsCore } from 'ngx-echarts';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideAnimations(),
    // Lazy + tree-shaken ECharts — keeps the charting runtime off the eager main bundle.
    // ngx-echarts destructures `init` from the loader result, so resolve to the echarts
    // namespace itself (m.default), not the ES module wrapper `{ default: ... }`.
    provideEchartsCore({ echarts: () => import('./core/echarts-init').then(m => m.default) })
  ]
};
