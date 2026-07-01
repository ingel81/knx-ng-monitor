import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject, firstValueFrom } from 'rxjs';
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
  // Distinguishes a deliberate stopConnection() from a dropped link in onclose.
  private manualStop = false;

  public log$ = this.logSubject.asObservable();

  public async startConnection(): Promise<void> {
    this.manualStop = false;

    // Never leak a previous connection on a retry — stop it before rebuilding.
    if (this.hubConnection) {
      try { await this.hubConnection.stop(); } catch { /* ignore */ }
    }

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.hubUrl}/logs`, {
        accessTokenFactory: () => this.resolveAccessToken()
      })
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('log', (entry: LogEntry) => {
      this.logSubject.next(entry);
    });

    // Automatic reconnect eventually gives up; restart from scratch on any close
    // we didn't trigger so the log stream survives a long disconnect / token expiry.
    this.hubConnection.onclose(() => {
      if (this.manualStop) { return; }
      this.logger.error('[LogsSignalR] Connection closed unexpectedly, restarting');
      setTimeout(() => this.startConnection(), 5000);
    });

    try {
      await this.hubConnection.start();
      this.logger.debug('[LogsSignalR] Connection started');
    } catch (err) {
      this.logger.error('[LogsSignalR] Error starting connection:', err);
      setTimeout(() => this.startConnection(), 5000);
    }
  }

  /**
   * Bearer token for the WebSocket handshake, refreshed first when the stored
   * access token has expired — otherwise every (re)connect fails silently.
   */
  private async resolveAccessToken(): Promise<string> {
    if (!this.authService.isAuthenticated()) {
      try {
        await firstValueFrom(this.authService.refreshToken());
      } catch {
        // Refresh token gone too — hand over what we have; the handshake fails
        // and the HTTP interceptor drives the /login redirect.
      }
    }
    return this.authService.getAccessToken() ?? '';
  }

  public async stopConnection(): Promise<void> {
    this.manualStop = true;
    if (this.hubConnection) {
      await this.hubConnection.stop();
      this.hubConnection = undefined;
      this.logger.debug('[LogsSignalR] Connection stopped');
    }
  }
}
