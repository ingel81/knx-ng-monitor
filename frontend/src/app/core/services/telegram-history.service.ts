import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment.development';
import { ArchiveDay, TelegramPage, TelegramQueryParams } from '../models/telegram-history.models';

@Injectable({ providedIn: 'root' })
export class TelegramHistoryService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/telegrams`;

  query(params: TelegramQueryParams): Observable<TelegramPage> {
    return this.http.get<TelegramPage>(this.apiUrl, { params: this.buildParams(params) });
  }

  count(params: TelegramQueryParams): Observable<{ count: number }> {
    return this.http.get<{ count: number }>(`${this.apiUrl}/count`, { params: this.buildParams(params) });
  }

  exportCsv(params: TelegramQueryParams): Observable<Blob> {
    // The JWT interceptor attaches the bearer token to this normal HttpClient GET.
    let httpParams = this.buildParams(params).set('format', 'csv');
    return this.http.get(`${this.apiUrl}/export`, { params: httpParams, responseType: 'blob' });
  }

  clearAll(): Observable<{ deleted: number }> {
    return this.http.delete<{ deleted: number }>(this.apiUrl);
  }

  listArchiveDays(): Observable<ArchiveDay[]> {
    return this.http.get<ArchiveDay[]>(`${this.apiUrl}/archive/days`);
  }

  downloadArchiveDay(date: string): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/archive/${date}`, { responseType: 'blob' });
  }

  private buildParams(params: TelegramQueryParams): HttpParams {
    let httpParams = new HttpParams().set('pageSize', String(params.pageSize));
    if (params.from) httpParams = httpParams.set('from', params.from);
    if (params.to) httpParams = httpParams.set('to', params.to);
    if (params.address) httpParams = httpParams.set('address', params.address);
    if (params.source) httpParams = httpParams.set('source', params.source);
    if (params.type) httpParams = httpParams.set('type', params.type);
    if (params.types) httpParams = httpParams.set('types', params.types);
    if (params.q) httpParams = httpParams.set('q', params.q);
    if (params.order) httpParams = httpParams.set('order', params.order);
    if (params.cursor) httpParams = httpParams.set('cursor', params.cursor);
    return httpParams;
  }
}
