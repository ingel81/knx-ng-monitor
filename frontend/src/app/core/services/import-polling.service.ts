import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, timer, Subject, BehaviorSubject } from 'rxjs';
import { switchMap, takeUntil, takeWhile, tap, catchError } from 'rxjs/operators';
import { environment } from '../../../environments/environment.development';
import { ImportJob, ImportStatus } from '../../shared/models/import-job.model';

@Injectable({
  providedIn: 'root'
})
export class ImportPollingService {
  private stopPolling$ = new Subject<void>();
  private currentJob$ = new BehaviorSubject<ImportJob | null>(null);

  constructor(private http: HttpClient) {}

  startPolling(jobId: string, intervalMs: number = 500): Observable<ImportJob> {
    this.stopPolling();

    const polling$ = timer(0, intervalMs).pipe(
      switchMap(() => this.getImportStatus(jobId)),
      tap(job => this.currentJob$.next(job)),
      takeWhile(job => this.shouldContinuePolling(job), true),
      takeUntil(this.stopPolling$),
      catchError((error) => {
        console.error('Polling error:', error);
        this.stopPolling();
        throw error;
      })
    );

    return polling$;
  }

  stopPolling(): void {
    this.stopPolling$.next();
  }

  getCurrentJob(): Observable<ImportJob | null> {
    return this.currentJob$.asObservable();
  }

  private getImportStatus(jobId: string): Observable<ImportJob> {
    return this.http.get<ImportJob>(`${environment.apiUrl}/projects/imports/${jobId}`);
  }

  private shouldContinuePolling(job: ImportJob): boolean {
    return job.status !== ImportStatus.Completed &&
           job.status !== ImportStatus.Failed &&
           job.status !== ImportStatus.Cancelled;
  }
}
