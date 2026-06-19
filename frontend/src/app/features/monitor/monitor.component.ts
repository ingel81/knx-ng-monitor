import { Component, OnInit, OnDestroy, inject, ViewChild, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { HttpClient } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { KnxTelegram } from '../../core/services/signalr.service';
import { LiveBufferService } from '../../core/services/live-buffer.service';
import { TelegramHistoryService } from '../../core/services/telegram-history.service';
import { ArchiveDay, TelegramQueryParams } from '../../core/models/telegram-history.models';
import { environment } from '../../../environments/environment.development';
import { ThemeService } from '../../core/services/theme.service';
import { TelegramDetailService } from '../../shared/grid/telegram-detail.service';
import { TelegramCardsComponent } from '../../shared/grid/telegram-cards.component';
import { KnxTableComponent, KnxColumn, SortDir } from '../../shared/grid/knx-table.component';
import { ColumnManagerComponent } from '../../shared/grid/column-manager.component';
import { messageTypeName } from '../../shared/grid/knx-grid.util';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LoggerService } from '../../core/logging/logger.service';
import { ProjectService, LocationDto } from '../../core/services/project.service';

interface KnxConfiguration {
  id: number;
  ipAddress: string;
  port: number;
  physicalAddress: string;
}

type MonitorMode = 'live' | 'archive';

/**
 * Vereinte Monitor-Ansicht: Live + Archive in einer Komponente.
 *
 * Der Live-Buffer (Singleton-Service) läuft IMMER im Hintergrund — der Mode
 * steuert nur, welcher Body gerendert wird. Beim Wechsel auf Archive wird die
 * Live-Anzeige also nicht angehalten, nur ausgeblendet; die Aufzeichnung läuft
 * weiter. Beim Zurückwechseln auf Live zeigt der Buffer sofort den aktuellen Stand.
 */
@Component({
  selector: 'app-monitor',
  imports: [
    CommonModule, FormsModule, MatIconModule, MatButtonModule, MatTooltipModule,
    MatMenuModule, MatSnackBarModule,
    TelegramCardsComponent, KnxTableComponent, ColumnManagerComponent, TranslatePipe
  ],
  templateUrl: './monitor.component.html',
  styleUrl: './monitor.component.scss'
})
export class MonitorComponent implements OnInit, OnDestroy {
  private buffer = inject(LiveBufferService);
  private historyService = inject(TelegramHistoryService);
  private http = inject(HttpClient);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);
  private detail = inject(TelegramDetailService);
  private density = inject(ThemeService).density;
  private lang = inject(LanguageService);
  private logger = inject(LoggerService);
  private projectService = inject(ProjectService);
  private subscription?: Subscription;

  @ViewChild(KnxTableComponent) table?: KnxTableComponent;

  mode: MonitorMode = 'live';

  isMobile = window.innerWidth < 768;

  // --- Shared quick-search (gilt in beiden Modi) -----------------------------
  quickFilterText = '';

  // --- Live state ------------------------------------------------------------
  filtered: KnxTelegram[] = [];
  isConnected = false;
  linkState: 'Disconnected' | 'Connecting' | 'Connected' = 'Disconnected';
  autoScroll = true;

  hasProject = false;
  hasActiveProject = false;
  hasKnxConfig = false;

  private statusPoll?: ReturnType<typeof setInterval>;

  // Status-Text für den Indikator (Auto-Connect-Mindset: kein manuelles Verbinden).
  get statusLabel(): string {
    if (this.isPaused) return this.lang.translate('monitor.status.paused');
    if (this.isConnected) return this.lang.translate('monitor.status.live');
    if (this.linkState === 'Connecting') return this.lang.translate('monitor.status.connecting');
    return this.lang.translate('monitor.status.reconnecting');
  }

  // Buffer lebt im Singleton-Service -> überlebt Tab-/Mode-Wechsel.
  get telegrams(): KnxTelegram[] { return this.buffer.telegrams; }
  get isPaused(): boolean { return this.buffer.isPaused; }

  // Live-Rate / Bus-Last (gleitendes Fenster im Buffer-Service berechnet).
  get messagesPerSecond(): number { return this.buffer.messagesPerSecond; }
  get busLoadPercent(): number { return this.buffer.busLoadPercent; }
  private rateSub?: Subscription;

  // --- Archive state ---------------------------------------------------------
  private readonly pageSize = 100;
  rows: KnxTelegram[] = [];
  private cursor?: string;
  hasMore = false;
  loading = false;

  sortKey = 'timestamp';
  sortDir: SortDir = 'desc';

  fromValue = '';
  toValue = '';
  address = '';
  source = '';
  types = new Set<string>();
  timeRange: 'all' | 'hour' | 'today' | '7d' = 'all';
  // Terms match the (often German) group-address names in the imported project,
  // so only the display labels are translated — not the search terms.
  // label = Übersetzungs-Key (Template übersetzt); term = Suchbegriff, bleibt as-is
  // (matcht die oft deutschen GA-Namen im importierten Projekt).
  readonly topics: ReadonlyArray<{ label: string; term: string }> = [
    { label: 'monitor.topic.temperature', term: 'temp' },
    { label: 'monitor.topic.light', term: 'licht' },
    { label: 'monitor.topic.shading', term: 'jalousie' },
    { label: 'monitor.topic.power', term: 'leistung' }
  ];

  totalCount: number | null = null;
  isExporting = false;
  isClearing = false;
  archiveDays: ArchiveDay[] = [];

  // --- Room / location filter (archive only) ---------------------------------
  // Locations of the active project that expose at least one group address. The
  // dropdown selects a room; its GA set client-side-filters the loaded archive rows.
  roomLocations: LocationDto[] = [];
  selectedRoomId: string = '';
  private roomGaSet = new Set<string>();
  private roomsLoaded = false;

  // --- Columns (shared between both modes) -----------------------------------
  // Header-Werte sind Übersetzungs-Keys; knx-table + column-manager rendern sie
  // durch die translate-Pipe.
  readonly allColumns: KnxColumn[] = [
    { key: 'timestamp', header: 'columns.timestamp', kind: 'datetime', width: 220, sortable: true },
    { key: 'sourceAddress', header: 'columns.source', kind: 'mono', width: 105 },
    { key: 'destinationAddress', header: 'columns.dest', kind: 'mono', width: 105 },
    { key: 'groupAddressName', header: 'columns.name', kind: 'name', grow: 1.4, minWidth: 200 },
    { key: 'datapointType', header: 'columns.dpt', kind: 'muted-mono', width: 120, align: 'right' },
    { key: 'messageType', header: 'columns.type', kind: 'type', width: 110 },
    { key: 'value', header: 'columns.raw', kind: 'muted-mono', width: 140, align: 'right' },
    { key: 'valueDecoded', header: 'columns.value', kind: 'value', grow: 1, minWidth: 150, align: 'right' },
    { key: 'priority', header: 'columns.priority', kind: 'muted-mono', width: 90, align: 'right' },
    { key: 'flags', header: 'columns.flags', kind: 'muted-mono', width: 90, align: 'right' }
  ];
  readonly defaultHiddenCols = ['priority', 'flags'];
  columnOptions = this.allColumns.map((c) => ({ key: c.key as string, header: c.header }));
  hiddenCols = new Set<string>();
  readonly lockedCols = ['groupAddressName', 'valueDecoded'];

  get visibleColumns(): KnxColumn[] {
    return this.allColumns.filter((c) => !this.hiddenCols.has(c.key as string));
  }
  // Sort-Header nur im Archiv sinnvoll — im Live-Mode liefert der Buffer newest-first.
  get gridColumns(): KnxColumn[] {
    if (this.mode === 'archive') return this.visibleColumns;
    return this.visibleColumns.map((c) => (c.sortable ? { ...c, sortable: false } : c));
  }
  get rowHeight(): number { return this.density() === 'cozy' ? 48 : 36; }

  // Archive rows after the (client-side) room filter. A room maps to a set of GAs;
  // we keep only rows whose destination GA is in that set. No room selected ⇒ all rows.
  get displayRows(): KnxTelegram[] {
    if (!this.selectedRoomId || this.roomGaSet.size === 0) return this.rows;
    return this.rows.filter(r => r.destinationAddress != null && this.roomGaSet.has(r.destinationAddress));
  }

  onHiddenChange(hidden: Set<string>): void { this.hiddenCols = hidden; }

  async ngOnInit(): Promise<void> {
    // Live-Buffer immer starten — läuft als Singleton im Hintergrund weiter.
    await this.buffer.start();
    this.applyClientFilter();
    if (this.mode === 'live' && this.autoScroll) setTimeout(() => this.table?.scrollToTop(), 0);
    this.subscription = this.buffer.changed$.subscribe(() => this.onBufferChanged());
    // Rate-/Bus-Last-Ticks (1×/s) sichtbar halten, auch ohne neue Telegramme.
    this.rateSub = this.buffer.rate$.subscribe();
    this.checkConnectionStatus();
    this.checkSetupStatus();
    // Status periodisch pollen, damit Auto-Reconnect im Indikator sichtbar wird.
    this.statusPoll = setInterval(() => this.checkConnectionStatus(), 5000);

    // Archiv-Datenquellen für den Archive-Mode vorbereiten.
    this.loadArchiveDays();
    this.loadCount();
    this.resetArchive();
  }

  ngOnDestroy(): void {
    // NUR die View-Subscription lösen — Connection + Buffer leben im Singleton weiter,
    // damit die Live-Ansicht beim Zurückwechseln nicht leer ist.
    this.subscription?.unsubscribe();
    this.rateSub?.unsubscribe();
    if (this.statusPoll) clearInterval(this.statusPoll);
  }

  // --- Mode toggle -----------------------------------------------------------
  setMode(mode: MonitorMode): void {
    if (this.mode === mode) return;
    this.mode = mode;
    if (mode === 'live') {
      // Live-Anzeige wieder aufbauen (Buffer lief durch).
      this.applyClientFilter();
      if (this.autoScroll) setTimeout(() => this.table?.scrollToTop(), 0);
    } else {
      // Geteiltes Quick-Search in den Archiv-Query übernehmen und neu laden.
      void this.loadRoomsIfNeeded();
      this.loadCount();
      this.resetArchive();
    }
  }

  @HostListener('window:resize')
  onResize(): void { this.isMobile = window.innerWidth < 768; }

  @HostListener('document:keydown', ['$event'])
  handleKeyboardEvent(event: KeyboardEvent): void {
    if (this.mode !== 'live') return;
    if (event.code === 'Space' && event.target instanceof HTMLElement) {
      const tag = event.target.tagName.toLowerCase();
      if (tag !== 'input' && tag !== 'textarea' && !event.ctrlKey && !event.shiftKey && !event.altKey && !event.metaKey) {
        event.preventDefault();
        this.togglePause();
      }
    }
  }

  showDetail(row: KnxTelegram): void { this.detail.open(row); }

  // --- Shared quick-search ---------------------------------------------------
  onQuickFilterChanged(): void {
    if (this.mode === 'live') {
      this.applyClientFilter();
    } else {
      clearTimeout(this.searchDebounce);
      this.searchDebounce = setTimeout(() => this.applyFilters(), 250);
    }
  }
  private searchDebounce?: ReturnType<typeof setTimeout>;

  clearQuickFilter(): void {
    this.quickFilterText = '';
    if (this.mode === 'live') this.applyClientFilter(); else this.applyFilters();
  }

  // --- Live mode -------------------------------------------------------------
  private onBufferChanged(): void {
    if (this.mode !== 'live') return;
    this.applyClientFilter();
    if (this.autoScroll && !this.isPaused) setTimeout(() => this.table?.scrollToTop(), 0);
  }

  applyClientFilter(): void {
    const q = this.quickFilterText.trim().toLowerCase();
    this.filtered = q
      ? this.telegrams.filter((t) =>
          [t.sourceAddress, t.destinationAddress, t.groupAddressName, t.datapointType, t.value, t.valueDecoded]
            .join(' ').toLowerCase().includes(q))
      : this.telegrams.slice();
  }

  togglePause(): void { this.buffer.togglePause(); }

  clearTelegrams(): void { this.buffer.clear(); }

  exportLiveCsv(): void {
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
    this.downloadBlob(blob, `knx-live-${new Date().toISOString()}.csv`);
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
      this.logger.error('Failed to check connection status:', error);
    }
  }

  async checkSetupStatus(): Promise<void> {
    try {
      const projects = await this.http.get<{ isActive: boolean }[]>(`${environment.apiUrl}/projects`).toPromise();
      this.hasProject = (projects && projects.length > 0) || false;
      this.hasActiveProject = (projects?.some(p => p.isActive)) || false;
      const configs = await this.http.get<KnxConfiguration[]>(`${environment.apiUrl}/knx/configurations`).toPromise();
      this.hasKnxConfig = (configs && configs.length > 0) || false;
    } catch (error) {
      this.logger.error('Failed to check setup status:', error);
    }
  }

  navigateToSettings(): void { this.router.navigate(['/settings']); }
  navigateToProjects(): void { this.router.navigate(['/projects']); }

  // --- Archive mode ----------------------------------------------------------
  onSort(e: { key: string; dir: SortDir }): void {
    this.sortKey = e.key;
    this.sortDir = e.dir;
    this.resetArchive();
  }

  onNearEnd(): void { this.loadMore(); }

  loadMore(): void {
    if (!this.loading && this.hasMore) this.queryPage();
  }

  private resetArchive(): void {
    this.rows = [];
    this.cursor = undefined;
    this.hasMore = false;
    this.queryPage();
  }

  private queryPage(): void {
    this.loading = true;
    const query: TelegramQueryParams = {
      ...this.currentFilter(), order: this.sortDir, cursor: this.cursor, pageSize: this.pageSize
    };
    this.historyService.query(query).subscribe({
      next: (page) => {
        this.rows = [...this.rows, ...page.items];
        this.cursor = page.nextCursor ?? undefined;
        this.hasMore = page.hasMore;
        this.loading = false;
      },
      error: () => { this.loading = false; }
    });
  }

  applyFilters(): void {
    this.loadCount();
    this.resetArchive();
  }

  resetFilters(): void {
    this.fromValue = '';
    this.toValue = '';
    this.address = '';
    this.source = '';
    this.types.clear();
    this.quickFilterText = '';
    this.timeRange = 'all';
    this.selectedRoomId = '';
    this.roomGaSet.clear();
    this.applyFilters();
  }

  // Load the active project's locations once on first entry into archive mode.
  // Only rooms that expose group addresses are offered (device-only rooms can't be
  // mapped to telegrams from the location DTO alone, so they are omitted).
  private async loadRoomsIfNeeded(): Promise<void> {
    if (this.roomsLoaded) return;
    this.roomsLoaded = true;
    try {
      const projects = await this.projectService.getAllProjects().toPromise() || [];
      const active = projects.find(p => p.isActive);
      if (!active) return;
      const locations = await this.projectService.getLocations(active.id).toPromise() || [];
      this.roomLocations = locations
        .filter(l => l.groupAddresses.length > 0)
        .sort((a, b) => a.name.localeCompare(b.name));
    } catch (err) {
      this.logger.error('Failed to load locations for room filter:', err);
      // Allow a later retry if loading failed.
      this.roomsLoaded = false;
    }
  }

  onRoomChanged(): void {
    const room = this.roomLocations.find(l => String(l.id) === this.selectedRoomId);
    this.roomGaSet = new Set(room ? room.groupAddresses : []);
  }

  setType(t: 'Write' | 'Read' | 'Response'): void {
    if (this.types.has(t)) this.types.delete(t); else this.types.add(t);
    this.applyFilters();
  }

  setTopic(term: string): void {
    this.quickFilterText = this.quickFilterText === term ? '' : term;
    this.applyFilters();
  }

  setTimeRange(range: 'all' | 'hour' | 'today' | '7d'): void {
    this.timeRange = range;
    const now = new Date();
    let from: Date | null = null;
    if (range === 'hour') from = new Date(now.getTime() - 3600_000);
    else if (range === 'today') { from = new Date(now); from.setHours(0, 0, 0, 0); }
    else if (range === '7d') from = new Date(now.getTime() - 7 * 86_400_000);
    this.fromValue = from ? this.toLocalInput(from) : '';
    this.toValue = range === 'all' ? '' : this.toLocalInput(now);
    this.applyFilters();
  }

  get hasActiveFilters(): boolean {
    return !!(this.fromValue || this.toValue || this.address || this.source || this.types.size || this.quickFilterText || this.timeRange !== 'all' || this.selectedRoomId);
  }

  private currentFilter(): Omit<TelegramQueryParams, 'pageSize' | 'cursor' | 'order'> {
    return {
      from: this.toIso(this.fromValue),
      to: this.toIso(this.toValue),
      address: this.address.trim() || undefined,
      source: this.source.trim() || undefined,
      types: this.types.size ? Array.from(this.types).join(',') : undefined,
      q: this.quickFilterText.trim() || undefined
    };
  }

  private loadCount(): void {
    this.historyService.count({ ...this.currentFilter(), pageSize: this.pageSize }).subscribe({
      next: (res) => (this.totalCount = res.count),
      error: () => (this.totalCount = null)
    });
  }

  exportArchiveCsv(): void {
    this.isExporting = true;
    this.historyService.exportCsv({ ...this.currentFilter(), pageSize: this.pageSize }).subscribe({
      next: (blob) => {
        this.downloadBlob(blob, `knx-history-${new Date().toISOString()}.csv`);
        this.isExporting = false;
      },
      error: () => {
        this.isExporting = false;
        this.toast(this.lang.translate('monitor.exportFailed'));
      }
    });
  }

  clearHistory(): void {
    const total = this.totalCount;
    const countText = total != null
      ? this.lang.translate('monitor.confirmClearCount', { count: total.toLocaleString() })
      : this.lang.translate('monitor.confirmClearAll');
    this.dialog.open(ConfirmDialogComponent, {
      autoFocus: false,
      data: {
        title: this.lang.translate('monitor.confirmClearTitle'),
        message: this.lang.translate('monitor.confirmClearMsg', { count: countText }),
        warning: this.lang.translate('monitor.confirmClearWarning'),
        confirmText: this.lang.translate('monitor.confirmClearConfirm'),
        danger: true
      }
    }).afterClosed().subscribe((confirmed: boolean) => {
      if (!confirmed) return;
      this.isClearing = true;
      this.historyService.clearAll().subscribe({
        next: (res) => {
          this.isClearing = false;
          this.toast(this.lang.translate('monitor.cleared', { count: res.deleted.toLocaleString() }));
          this.loadCount();
          this.resetArchive();
        },
        error: () => {
          this.isClearing = false;
          this.toast(this.lang.translate('monitor.clearFailed'));
        }
      });
    });
  }

  loadArchiveDays(): void {
    this.historyService.listArchiveDays().subscribe({
      next: (days) => (this.archiveDays = days),
      error: () => (this.archiveDays = [])
    });
  }

  downloadArchiveDay(day: ArchiveDay): void {
    this.historyService.downloadArchiveDay(day.date).subscribe({
      next: (blob) => this.downloadBlob(blob, day.fileName),
      error: () => this.toast(this.lang.translate('monitor.downloadFailed'))
    });
  }

  formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  private toIso(localValue: string): string | undefined {
    if (!localValue) return undefined;
    const date = new Date(localValue);
    return isNaN(date.getTime()) ? undefined : date.toISOString();
  }

  private toLocalInput(d: Date): string {
    const p = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
  }

  private downloadBlob(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
  }

  private toast(message: string): void {
    this.snackBar.open(message, this.lang.translate('common.close'), { duration: 3000, horizontalPosition: 'end', verticalPosition: 'top' });
  }
}
