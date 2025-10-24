import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { Subscription } from 'rxjs';
import { SignalrService, KnxTelegram } from '../../core/services/signalr.service';
import { HttpClient } from '@angular/common/http';

interface KnxConfiguration {
  id: number;
  ipAddress: string;
  port: number;
  physicalAddress: string;
}

@Component({
  selector: 'app-live-view',
  imports: [CommonModule, MatIconModule, MatButtonModule],
  templateUrl: './live-view.component.html',
  styleUrl: './live-view.component.scss'
})
export class LiveViewComponent implements OnInit, OnDestroy {
  private signalrService = inject(SignalrService);
  private http = inject(HttpClient);
  private subscription?: Subscription;

  telegrams: KnxTelegram[] = [];
  isConnected = false;
  isPaused = false;

  async ngOnInit() {
    await this.signalrService.startConnection();

    this.subscription = this.signalrService.telegram$.subscribe(telegram => {
      if (!this.isPaused) {
        this.telegrams.unshift(telegram);
        if (this.telegrams.length > 100) {
          this.telegrams = this.telegrams.slice(0, 100);
        }
      }
    });

    this.checkConnectionStatus();
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
    this.signalrService.stopConnection();
  }

  async connectToKnx() {
    try {
      // Get or create default configuration
      const configs = await this.http.get<KnxConfiguration[]>('http://localhost:5075/api/knx/configurations').toPromise();

      let configId: number;
      if (configs && configs.length > 0) {
        configId = configs[0].id;
      } else {
        // Create default configuration
        const newConfig = await this.http.post<KnxConfiguration>('http://localhost:5075/api/knx/configurations', {
          ipAddress: '192.168.1.100',
          port: 3671,
          physicalAddress: '1.0.58',
          connectionType: 0 // Tunneling
        }).toPromise();
        configId = newConfig!.id;
      }

      await this.http.post('http://localhost:5075/api/knx/connect', configId).toPromise();
      this.isConnected = true;
    } catch (error) {
      console.error('Failed to connect to KNX:', error);
    }
  }

  async disconnectFromKnx() {
    try {
      await this.http.post('http://localhost:5075/api/knx/disconnect', {}).toPromise();
      this.isConnected = false;
    } catch (error) {
      console.error('Failed to disconnect from KNX:', error);
    }
  }

  async checkConnectionStatus() {
    try {
      const status = await this.http.get<{ isConnected: boolean }>('http://localhost:5075/api/knx/status').toPromise();
      this.isConnected = status?.isConnected || false;
    } catch (error) {
      console.error('Failed to check connection status:', error);
    }
  }

  togglePause() {
    this.isPaused = !this.isPaused;
  }

  clearTelegrams() {
    this.telegrams = [];
  }

  getMessageTypeClass(type: string): string {
    switch (type) {
      case 'Write':
      case '0':
        return 'msg-write';
      case 'Read':
      case '1':
        return 'msg-read';
      case 'Response':
      case '2':
        return 'msg-response';
      default:
        return '';
    }
  }

  getMessageTypeName(type: string): string {
    switch (type) {
      case '0': return 'Write';
      case '1': return 'Read';
      case '2': return 'Response';
      default: return type;
    }
  }
}
