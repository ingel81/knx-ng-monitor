import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment.development';

/** One numeric point of a charted series. `t` is an ISO timestamp, `v` the numeric value. */
export interface ChartPoint {
  t: string;
  v: number;
}

export interface ChartSeries {
  address: string;
  name: string | null;
  unit: string;
  downSampled: boolean;
  points: ChartPoint[];
}

export interface SeriesResponse {
  series: ChartSeries[];
}

export interface StatsBucket {
  t: string;
  count: number;
}

export interface StatsResponse {
  total: number;
  bucketMs: number;
  counts: StatsBucket[];
}

/**
 * Client for the charts / statistics endpoints on the telegram route family.
 * Mirrors GET /api/telegrams/series and /api/telegrams/stats.
 */
@Injectable({ providedIn: 'root' })
export class ChartsService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/telegrams`;

  getSeries(
    addresses: string[],
    from?: string,
    to?: string,
    maxPoints = 2000
  ): Observable<SeriesResponse> {
    let params = new HttpParams()
      .set('addresses', addresses.join(','))
      .set('maxPoints', String(maxPoints));
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<SeriesResponse>(`${this.apiUrl}/series`, { params });
  }

  getStats(from?: string, to?: string, buckets = 60): Observable<StatsResponse> {
    let params = new HttpParams().set('buckets', String(buckets));
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<StatsResponse>(`${this.apiUrl}/stats`, { params });
  }
}

/**
 * Extracts a chartable number from a decoded telegram value — mirrors the backend rules so
 * live-appended points match historical ones: first numeric token (leading minus + decimal
 * allowed, comma or dot), else a known boolean/enum word → 1/0. Returns null when not chartable.
 */
export function extractNumeric(decoded: string | null | undefined): number | null {
  if (!decoded) return null;
  const m = decoded.match(/-?\d+(?:[.,]\d+)?/);
  if (m) {
    const n = Number(m[0].replace(',', '.'));
    if (!Number.isNaN(n)) return n;
  }
  const trimmed = decoded.trim().toLowerCase();
  const truthy = ['on', 'true', 'active', 'open', 'yes', 'up', 'start', 'enable'];
  const falsy = ['off', 'false', 'inactive', 'close', 'closed', 'no', 'down', 'stop', 'disable'];
  if (truthy.includes(trimmed)) return 1;
  if (falsy.includes(trimmed)) return 0;
  return null;
}

/** Strips the leading numeric token to derive a unit string (e.g. "21.5 °C" → "°C"). */
export function extractUnit(decoded: string | null | undefined): string {
  if (!decoded) return '';
  return decoded.replace(/-?\d+(?:[.,]\d+)?/, '').trim();
}
