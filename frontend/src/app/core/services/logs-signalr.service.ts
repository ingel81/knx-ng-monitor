import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AuthService } from './auth.service';
import { LoggerService } from '../logging/logger.service';
import { environment } from '../../../environments/environment.development';

/** One application log line, mirrors the backend LogEntryDto. */
export interface LogEntry {
  timestamp: string;            // ISO timestamp
  level: string;                // Information | Warning | Error | Debug | Verbose | Fatal
  message: string;
  source: string | null;
  exception: string | null;
}

/**
 * SignalR client for the diagnostics log hub (`/hubs/logs`). Mirrors
 * SignalrService: JWT via accessTokenFactory, automatic reconnect, and a
 * single reused connection (the retry path stops the old one first).
 */
@Injectable({ providedIn: 'root' })
export class LogsSignalrService {
  private authService = inject(AuthService);
  private logger = inject(LoggerService);

  private hubConnection?: signalR.HubConnection;
  private logSubject = new Subject<LogEntry>();

  public log$ = this.logSubject.asObservable();

  public async startConnection(): Promise<void> {
    // Never leak a previous connection on a retry — stop it before rebuilding.
    if (this.hubConnection) {
      try { await this.hubConnection.stop(); } catch { /* ignore */ }
    }

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.hubUrl}/logs`, {
        accessTokenFactory: () => this.authService.getAccessToken() ?? ''
      })
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('log', (entry: LogEntry) => {
      this.logSubject.next(entry);
    });

    try {
      await this.hubConnection.start();
      this.logger.debug('[LogsSignalR] Connection started');
    } catch (err) {
      this.logger.error('[LogsSignalR] Error starting connection:', err);
      setTimeout(() => this.startConnection(), 5000);
    }
  }

  public async stopConnection(): Promise<void> {
    if (this.hubConnection) {
      await this.hubConnection.stop();
      this.hubConnection = undefined;
      this.logger.debug('[LogsSignalR] Connection stopped');
    }
  }
}
