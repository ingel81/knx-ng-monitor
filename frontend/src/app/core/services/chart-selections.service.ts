import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment.development';

/** A saved group-address selection of the charts view. */
export interface ChartSelection {
  id: number;
  name: string;
  addresses: string[];
  updatedAt: string;
}

/**
 * Client for /api/chart-selections. Stored server-side so the same selections show up on every
 * device; saving under an existing name (case-insensitive) overwrites it.
 */
@Injectable({ providedIn: 'root' })
export class ChartSelectionsService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/chart-selections`;

  getAll(): Observable<ChartSelection[]> {
    return this.http.get<ChartSelection[]>(this.apiUrl);
  }

  save(name: string, addresses: string[]): Observable<ChartSelection> {
    return this.http.put<ChartSelection>(this.apiUrl, { name, addresses });
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
