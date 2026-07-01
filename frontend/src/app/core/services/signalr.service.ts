import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject, Observable, firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';
import { LoggerService } from '../logging/logger.service';
import { environment } from '../../../environments/environment.development';

export interface KnxTelegram {
  id: number;
  timestamp: Date;
  sourceAddress: string;
  destinationAddress: string;
  groupAddressName?: string;
  datapointType?: string;
  messageType: string;
  value: string;          // Hex representation of raw bytes
  valueDecoded: string;   // Human-readable decoded value
  priority: number;
  flags: string;
}

@Injectable({
  providedIn: 'root'
})
export class SignalrService {
  private hubConnection?: signalR.HubConnection;
  private telegramSubject = new Subject<KnxTelegram>();
  // Set by stopConnection so the onclose handler can tell an intentional stop
  // from a dropped connection and not fight a deliberate teardown with a restart.
  private manualStop = false;

  public telegram$ = this.telegramSubject.asObservable();

  constructor(private authService: AuthService, private logger: LoggerService) {}

  public async startConnection(): Promise<void> {
    this.manualStop = false;

    // On a retry, stop the previous connection first so we never leak a stale
    // HubConnection by building a new one on top of it.
    if (this.hubConnection) {
      try { await this.hubConnection.stop(); } catch { /* ignore */ }
    }

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.hubUrl}/telegram`, {
        accessTokenFactory: () => this.resolveAccessToken()
      })
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('NewTelegram', (telegram: KnxTelegram) => {
      this.telegramSubject.next(telegram);
    });

    this.hubConnection.on('Connected', (message: string) => {
      this.logger.debug('[SignalR]', message);
    });

    // withAutomaticReconnect only retries a few times (0/2/10/30s) then gives up.
    // After that — or any other close not triggered by us — restart from scratch
    // so the live view survives a long disconnect / token expiry.
    this.hubConnection.onclose(() => {
      if (this.manualStop) { return; }
      this.logger.error('[SignalR] Connection closed unexpectedly, restarting');
      setTimeout(() => this.startConnection(), 5000);
    });

    try {
      await this.hubConnection.start();
      this.logger.debug('[SignalR] Connection started');
    } catch (err) {
      this.logger.error('[SignalR] Error starting connection:', err);
      setTimeout(() => this.startConnection(), 5000);
    }
  }

  /**
   * Supplies the bearer token for the WebSocket handshake, refreshing it first
   * when the stored access token has expired. Without this the factory would
   * keep handing SignalR a dead token and every (re)connect would fail silently.
   */
  private async resolveAccessToken(): Promise<string> {
    if (!this.authService.isAuthenticated()) {
      try {
        await firstValueFrom(this.authService.refreshToken());
      } catch {
        // Refresh token also gone/expired — hand over whatever we have; the
        // handshake will fail and the HTTP interceptor drives the /login redirect.
      }
    }
    return this.authService.getAccessToken() ?? '';
  }

  public async stopConnection(): Promise<void> {
    this.manualStop = true;
    if (this.hubConnection) {
      await this.hubConnection.stop();
      this.logger.debug('[SignalR] Connection stopped');
    }
  }
}
