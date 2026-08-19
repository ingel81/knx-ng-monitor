import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment.development';
import { LogEntry } from './logs-signalr.service';
import { Availability } from '../models/availability.models';

/**
 * Client for the diagnostics endpoints:
 * GET  /api/diagnostics/logs?level&search&limit  → recent log lines (newest first)
 * GET  /api/diagnostics/availability?from&to     → when did the monitor run, was the link up
 * GET  /api/diagnostics/download                 → diagnostics bundle (zip blob)
 */
@Injectable({ providedIn: 'root' })
export class DiagnosticsService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/diagnostics`;

  getLogs(level: string, search: string, limit = 500): Observable<LogEntry[]> {
    let params = new HttpParams().set('limit', String(limit));
    if (level && level !== 'All') params = params.set('level', level);
    if (search) params = params.set('search', search);
    return this.http.get<LogEntry[]>(`${this.apiUrl}/logs`, { params });
  }

  /**
   * Availability over [from,to] (ISO-8601, UTC). Without a range the backend reports the last
   * seven days. Anything older than the heartbeat retention reads as `MonitorDown`.
   */
  getAvailability(from?: string, to?: string): Observable<Availability> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<Availability>(`${this.apiUrl}/availability`, { params });
  }

  downloadDiagnostics(): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/download`, { responseType: 'blob' });
  }
}
