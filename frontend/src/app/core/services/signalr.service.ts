import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject, Observable } from 'rxjs';
import { AuthService } from './auth.service';

export interface KnxTelegram {
  id: number;
  timestamp: Date;
  sourceAddress: string;
  destinationAddress: string;
  messageType: string;
  value: string;
  valueDecoded: string;
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
      .withUrl('http://localhost:5075/hubs/telegram', {
        accessTokenFactory: () => token || ''
      })
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('NewTelegram', (telegram: KnxTelegram) => {
      this.telegramSubject.next(telegram);
    });

    this.hubConnection.on('Connected', (message: string) => {
      console.log('[SignalR]', message);
    });

    try {
      await this.hubConnection.start();
      console.log('[SignalR] Connection started');
    } catch (err) {
      console.error('[SignalR] Error starting connection:', err);
      setTimeout(() => this.startConnection(), 5000);
    }
  }

  public async stopConnection(): Promise<void> {
    if (this.hubConnection) {
      await this.hubConnection.stop();
      console.log('[SignalR] Connection stopped');
    }
  }
}
