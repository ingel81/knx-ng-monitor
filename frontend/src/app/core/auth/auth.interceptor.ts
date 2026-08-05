import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { catchError, switchMap, throwError } from 'rxjs';
import { Router } from '@angular/router';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  // Skip auth header for login, refresh, setup, and needs-setup endpoints
  const skipUrls = ['/auth/login', '/auth/refresh', '/auth/setup', '/auth/needs-setup'];
  if (skipUrls.some(url => req.url.includes(url))) {
    return next(req);
  }

  const token = authService.getAccessToken();

  if (token) {
    req = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    });
  }

  return next(req).pipe(
    catchError(error => {
      if (error.status === 401 && !req.url.includes('/auth/logout')) {
        // Try to refresh token
        return authService.refreshToken().pipe(
          switchMap(() => {
            // Retry request with new token
            const newToken = authService.getAccessToken();
            const retryReq = req.clone({
              setHeaders: {
                Authorization: `Bearer ${newToken}`
              }
            });
            return next(retryReq);
          }),
          catchError(refreshError => {
            // Nur ein echtes Nein des Servers beendet die Sitzung. Bei
            // Netzwerkfehlern (Status 0), 502 oder kurz nicht erreichbarem
            // Server bleiben die Tokens liegen — der Refresh-Token gilt 7 Tage,
            // ein Aussetzer darf ihn nicht wegwerfen.
            const rejected = refreshError?.status === 401 || refreshError?.status === 403;
            if (rejected) {
              authService.clearSession();
              router.navigate(['/login']);
            }
            return throwError(() => refreshError);
          })
        );
      }
      return throwError(() => error);
    })
  );
};
