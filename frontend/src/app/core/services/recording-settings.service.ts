import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment.development';
import { RecordingSettings } from '../models/recording-settings.models';

@Injectable({ providedIn: 'root' })
export class RecordingSettingsService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/recording/settings`;

  getSettings(): Observable<RecordingSettings> {
    return this.http.get<RecordingSettings>(this.apiUrl);
  }

  updateSettings(settings: RecordingSettings): Observable<RecordingSettings> {
    return this.http.put<RecordingSettings>(this.apiUrl, settings);
  }
}
