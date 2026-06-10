import { Component, OnInit, OnDestroy, inject, ViewChild, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subscription } from 'rxjs';
import { SignalrService, KnxTelegram } from '../../core/services/signalr.service';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment.development';
import { ThemeService } from '../../core/services/theme.service';
import { TelegramDetailService } from '../../shared/grid/telegram-detail.service';
import { TelegramCardsComponent } from '../../shared/grid/telegram-cards.component';
import { KnxTableComponent, KnxColumn } from '../../shared/grid/knx-table.component';
import { ColumnManagerComponent } from '../../shared/grid/column-manager.component';
import { messageTypeName } from '../../shared/grid/knx-grid.util';

interface KnxConfiguration {
  id: number;
  ipAddress: string;
  port: number;
  physicalAddress: string;
}

@Component({
  selector: 'app-live-view',
  imports: [
    CommonModule, FormsModule, MatIconModule, MatButtonModule, MatTooltipModule,
    TelegramCardsComponent, KnxTableComponent, ColumnManagerComponent
  ],
  templateUrl: './live-view.component.html',
  styleUrl: './live-view.component.scss'
})
export class LiveViewComponent implements OnInit, OnDestroy {
  private signalrService = inject(SignalrService);
  private http = inject(HttpClient);
  private router = inject(Router);
  private detail = inject(TelegramDetailService);
  private density = inject(ThemeService).density;
  private subscription?: Subscription;

  @ViewChild(KnxTableComponent) table?: KnxTableComponent;

  telegrams: KnxTelegram[] = [];
  filtered: KnxTelegram[] = [];
  isConnected = false;
  isPaused = false;
  isConnecting = false;
  autoScroll = true;
  quickFilterText = '';
  isMobile = window.innerWidth < 768;

  hasProject = false;
  hasKnxConfig = false;

  // Live-Telegramme kommen vor dem Persistieren -> id=0. Eindeutige Client-Sequenz
  // vergeben (stabil pro Zeile) -> Zebra + trackBy funktionieren wie in History.
  private clientSeq = 0;

  readonly allColumns: KnxColumn[] = [
    { key: 'timestamp', header: 'Zeit', kind: 'time', width: 120 },
    { key: 'sourceAddress', header: 'Quelle', kind: 'mono', width: 90 },
    { key: 'destinationAddress', header: 'Ziel', kind: 'mono', width: 90 },
    { key: 'groupAddressName', header: 'Name', kind: 'name', grow: 2, minWidth: 180 },
    { key: 'datapointType', header: 'DPT', kind: 'muted-mono', width: 110 },
    { key: 'messageType', header: 'Typ', kind: 'type', width: 110 },
    { key: 'value', header: 'Rohwert', kind: 'muted-mono', width: 120 },
    { key: 'valueDecoded', header: 'Wert', kind: 'value', grow: 1, minWidth: 130 },
    { key: 'priority', header: 'Priorität', kind: 'muted-mono', width: 90 },
    { key: 'flags', header: 'Flags', kind: 'muted-mono', width: 90 }
  ];
  readonly defaultHiddenCols = ['priority', 'flags'];
  columnOptions = this.allColumns.map((c) => ({ key: c.key as string, header: c.header }));
  hiddenCols = new Set<string>();
  readonly lockedCols = ['groupAddressName', 'valueDecoded'];

  get visibleColumns(): KnxColumn[] {
    return this.allColumns.filter((c) => !this.hiddenCols.has(c.key as string));
  }
  get rowHeight(): number { return this.density() === 'cozy' ? 48 : 36; }

  onHiddenChange(hidden: Set<string>): void { this.hiddenCols = hidden; }

  async ngOnInit(): Promise<void> {
    await this.signalrService.startConnection();
    this.subscription = this.signalrService.telegram$.subscribe((t) => this.addTelegram(t));
    this.checkConnectionStatus();
    this.checkSetupStatus();
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
    this.signalrService.stopConnection();
  }

  @HostListener('window:resize')
  onResize(): void { this.isMobile = window.innerWidth < 768; }

  @HostListener('document:keydown', ['$event'])
  handleKeyboardEvent(event: KeyboardEvent): void {
    if (event.code === 'Space' && event.target instanceof HTMLElement) {
      const tag = event.target.tagName.toLowerCase();
      if (tag !== 'input' && tag !== 'textarea' && !event.ctrlKey && !event.shiftKey && !event.altKey && !event.metaKey) {
        event.preventDefault();
        this.togglePause();
      }
    }
  }

  private addTelegram(t: KnxTelegram): void {
    if (this.isPaused) return;
    if (!t.id) t.id = ++this.clientSeq; // eindeutige, stabile id für Zebra/trackBy
    this.telegrams.unshift(t);
    if (this.telegrams.length > 1000) this.telegrams.pop();
    this.applyClientFilter();
    if (this.autoScroll) setTimeout(() => this.table?.scrollToTop(), 0);
  }

  applyClientFilter(): void {
    const q = this.quickFilterText.trim().toLowerCase();
    this.filtered = q
      ? this.telegrams.filter((t) =>
          [t.sourceAddress, t.destinationAddress, t.groupAddressName, t.datapointType, t.value, t.valueDecoded]
            .join(' ').toLowerCase().includes(q))
      : this.telegrams.slice();
  }

  onQuickFilterChanged(): void { this.applyClientFilter(); }

  showDetail(row: KnxTelegram): void { this.detail.open(row); }

  togglePause(): void { this.isPaused = !this.isPaused; }

  clearTelegrams(): void {
    this.telegrams = [];
    this.applyClientFilter();
  }

  exportCsv(): void {
    const header = ['Zeit', 'Quelle', 'Ziel', 'Name', 'DPT', 'Typ', 'Rohwert', 'Wert'];
    const lines = [header.join(',')];
    for (const t of this.filtered) {
      lines.push([
        new Date(t.timestamp).toLocaleString('de-DE'),
        t.sourceAddress, t.destinationAddress, t.groupAddressName ?? '', t.datapointType ?? '',
        messageTypeName(t.messageType), t.value, t.valueDecoded ?? ''
      ].map((v) => this.csv(String(v ?? ''))).join(','));
    }
    const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `knx-live-${new Date().toISOString()}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  private csv(v: string): string {
    return /[",\n\r]/.test(v) ? '"' + v.replace(/"/g, '""') + '"' : v;
  }

  async connectToKnx(): Promise<void> {
    try {
      this.isConnecting = true;
      const configs = await this.http.get<KnxConfiguration[]>(`${environment.apiUrl}/knx/configurations`).toPromise();
      if (!configs || configs.length === 0) {
        alert('No KNX configuration found. Please configure your KNX Gateway in Settings first.');
        return;
      }
      await this.http.post(`${environment.apiUrl}/knx/connect`, configs[0].id).toPromise();
      this.isConnected = true;
    } catch (error) {
      console.error('Failed to connect to KNX:', error);
      alert('Failed to connect to KNX Gateway. Please check your settings and try again.');
    } finally {
      this.isConnecting = false;
    }
  }

  async disconnectFromKnx(): Promise<void> {
    try {
      await this.http.post(`${environment.apiUrl}/knx/disconnect`, {}).toPromise();
      this.isConnected = false;
    } catch (error) {
      console.error('Failed to disconnect from KNX:', error);
    }
  }

  async checkConnectionStatus(): Promise<void> {
    try {
      const status = await this.http.get<{ isConnected: boolean }>(`${environment.apiUrl}/knx/status`).toPromise();
      this.isConnected = status?.isConnected || false;
    } catch (error) {
      console.error('Failed to check connection status:', error);
    }
  }

  async checkSetupStatus(): Promise<void> {
    try {
      const projects = await this.http.get<unknown[]>(`${environment.apiUrl}/projects`).toPromise();
      this.hasProject = (projects && projects.length > 0) || false;
      const configs = await this.http.get<KnxConfiguration[]>(`${environment.apiUrl}/knx/configurations`).toPromise();
      this.hasKnxConfig = (configs && configs.length > 0) || false;
    } catch (error) {
      console.error('Failed to check setup status:', error);
    }
  }

  navigateToSettings(): void { this.router.navigate(['/settings']); }
  navigateToProjects(): void { this.router.navigate(['/projects']); }
}
