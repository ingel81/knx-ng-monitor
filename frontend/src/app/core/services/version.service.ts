import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map, shareReplay } from 'rxjs/operators';
import { environment } from '../../../environments/environment.development';

/**
 * App version as reported by the backend (`GET /api/version`). That endpoint is anonymous on
 * purpose, so the login and initial-setup screens can show it before anyone is authenticated.
 * The value cannot change while the page is open, hence one request per session.
 */
@Injectable({ providedIn: 'root' })
export class VersionService {
  private http = inject(HttpClient);

  readonly version$: Observable<string> = this.http
    .get<{ version: string }>(`${environment.apiUrl}/version`)
    .pipe(
      // The informational version may carry build metadata ("0.8.4+1a2b3c"); drop it for display.
      map(response => (response?.version ?? '').split('+')[0] || '–'),
      // A version label is never worth an error toast on the login screen.
      catchError(() => of('–')),
      shareReplay({ bufferSize: 1, refCount: false })
    );
}
