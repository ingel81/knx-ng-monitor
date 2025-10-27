import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject, Observable } from 'rxjs';
import { AuthService } from './auth.service';
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

  public telegram$ = this.telegramSubject.asObservable();

  constructor(private authService: AuthService) {}

  public async startConnection(): Promise<void> {
    const token = this.authService.getAccessToken();

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.hubUrl}/telegram`, {
        accessTokenFactory: () => token || ''
      })
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('NewTelegram', (telegram: KnxTelegram) => {
      this.telegramSubject.next(telegram);
    });

    this.hubConnection.on('Connected', (message: string) => {
      console.debug('[SignalR]', message);
    });

    try {
      await this.hubConnection.start();
      console.debug('[SignalR] Connection started');
    } catch (err) {
      console.error('[SignalR] Error starting connection:', err);
      setTimeout(() => this.startConnection(), 5000);
    }
  }

  public async stopConnection(): Promise<void> {
    if (this.hubConnection) {
      await this.hubConnection.stop();
      console.debug('[SignalR] Connection stopped');
    }
  }
}
