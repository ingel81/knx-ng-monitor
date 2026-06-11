import { Component, OnInit, OnDestroy, inject, ViewChild, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subscription } from 'rxjs';
import { KnxTelegram } from '../../core/services/signalr.service';
import { LiveBufferService } from '../../core/services/live-buffer.service';
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
  private buffer = inject(LiveBufferService);
  private http = inject(HttpClient);
  private router = inject(Router);
  private detail = inject(TelegramDetailService);
  private density = inject(ThemeService).density;
  private subscription?: Subscription;

  @ViewChild(KnxTableComponent) table?: KnxTableComponent;

  filtered: KnxTelegram[] = [];
  isConnected = false;
  linkState: 'Disconnected' | 'Connecting' | 'Connected' = 'Disconnected';
  autoScroll = true;
  quickFilterText = '';
  isMobile = window.innerWidth < 768;

  hasProject = false;
  hasKnxConfig = false;

  private statusPoll?: ReturnType<typeof setInterval>;

  // Status-Text für den Indikator (Auto-Connect-Mindset: kein manuelles Verbinden).
  get statusLabel(): string {
    if (this.isPaused) return 'Paused';
    if (this.isConnected) return 'Live';
    if (this.linkState === 'Connecting') return 'Connecting…';
    return 'Reconnecting…';
  }

  // Buffer lebt im Singleton-Service -> überlebt Tab-Wechsel.
  get telegrams(): KnxTelegram[] { return this.buffer.telegrams; }
  get isPaused(): boolean { return this.buffer.isPaused; }

  readonly allColumns: KnxColumn[] = [
    { key: 'timestamp', header: 'Time', kind: 'time', width: 140 },
    { key: 'sourceAddress', header: 'Source', kind: 'mono', width: 105 },
    { key: 'destinationAddress', header: 'Dest', kind: 'mono', width: 105 },
    { key: 'groupAddressName', header: 'Name', kind: 'name', grow: 1.4, minWidth: 200 },
    { key: 'datapointType', header: 'DPT', kind: 'muted-mono', width: 120, align: 'right' },
    { key: 'messageType', header: 'Type', kind: 'type', width: 110 },
    { key: 'value', header: 'Raw', kind: 'muted-mono', width: 140, align: 'right' },
    { key: 'valueDecoded', header: 'Value', kind: 'value', grow: 1, minWidth: 150, align: 'right' },
    { key: 'priority', header: 'Priority', kind: 'muted-mono', width: 90, align: 'right' },
    { key: 'flags', header: 'Flags', kind: 'muted-mono', width: 90, align: 'right' }
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
    await this.buffer.start();
    // Buffer kann beim Mount bereits gefüllt sein (Rückkehr von History) -> sofort anzeigen.
    this.applyClientFilter();
    if (this.autoScroll) setTimeout(() => this.table?.scrollToTop(), 0);
    this.subscription = this.buffer.changed$.subscribe(() => this.onBufferChanged());
    this.checkConnectionStatus();
    this.checkSetupStatus();
    // Status periodisch pollen, damit Auto-Reconnect im Indikator sichtbar wird.
    this.statusPoll = setInterval(() => this.checkConnectionStatus(), 5000);
  }

  ngOnDestroy(): void {
    // NUR die View-Subscription lösen — Connection + Buffer leben im Singleton weiter,
    // damit die Live-Ansicht beim Zurückwechseln nicht leer ist.
    this.subscription?.unsubscribe();
    if (this.statusPoll) clearInterval(this.statusPoll);
  }

  private onBufferChanged(): void {
    this.applyClientFilter();
    if (this.autoScroll && !this.isPaused) setTimeout(() => this.table?.scrollToTop(), 0);
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

  togglePause(): void { this.buffer.togglePause(); }

  clearTelegrams(): void { this.buffer.clear(); }

  exportCsv(): void {
    const header = ['Time', 'Source', 'Dest', 'Name', 'DPT', 'Type', 'Raw', 'Value'];
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

  async checkConnectionStatus(): Promise<void> {
    try {
      const status = await this.http
        .get<{ isConnected: boolean; state?: string }>(`${environment.apiUrl}/knx/status`)
        .toPromise();
      this.isConnected = status?.isConnected || false;
      this.linkState = (status?.state as typeof this.linkState) ?? (this.isConnected ? 'Connected' : 'Disconnected');
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
