import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { map, catchError, of, Observable } from 'rxjs';

export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  /** Kein gültiger Zugang: je nach Zustand des Servers zur Einrichtung oder zur Anmeldung. */
  const redirectAway = (): Observable<boolean> =>
    authService.needsSetup().pipe(
      map(response => {
        if (response.needsSetup) {
          router.navigate(['/setup']);
          return false;
        }

        router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
        return false;
      }),
      catchError(() => {
        // On error, redirect to login
        router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
        return of(false);
      })
    );

  if (authService.isAuthenticated()) {
    return true;
  }

  // Der Access-Token hält 15 Minuten, der Refresh-Token 7 Tage. Ohne den Versuch
  // hier würde jedes Neuladen nach einer kurzen Pause zur Anmeldung führen,
  // obwohl die Sitzung serverseitig noch tagelang gültig ist — der Interceptor
  // käme nie zum Zug, weil dieser Guard vorher wegnavigiert.
  if (authService.hasRefreshToken()) {
    return authService.refreshToken().pipe(
      map(() => true),
      catchError(() => redirectAway())
    );
  }

  return redirectAway();
};
