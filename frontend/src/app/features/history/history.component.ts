import { Component, OnInit, inject, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TelegramHistoryService } from '../../core/services/telegram-history.service';
import { ArchiveDay, TelegramQueryParams } from '../../core/models/telegram-history.models';
import { KnxTelegram } from '../../core/services/signalr.service';
import { TelegramDetailService } from '../../shared/grid/telegram-detail.service';
import { ThemeService } from '../../core/services/theme.service';
import { TelegramCardsComponent } from '../../shared/grid/telegram-cards.component';
import { KnxTableComponent, KnxColumn, SortDir } from '../../shared/grid/knx-table.component';
import { ColumnManagerComponent } from '../../shared/grid/column-manager.component';

@Component({
  selector: 'app-history',
  imports: [
    CommonModule, FormsModule, MatIconModule, MatTooltipModule, MatSnackBarModule,
    KnxTableComponent, TelegramCardsComponent, ColumnManagerComponent
  ],
  templateUrl: './history.component.html',
  styleUrl: './history.component.scss'
})
export class HistoryComponent implements OnInit {
  private historyService = inject(TelegramHistoryService);
  private snackBar = inject(MatSnackBar);
  private detail = inject(TelegramDetailService);
  private density = inject(ThemeService).density;

  private readonly pageSize = 100;

  // Akkumulierte Seiten (Desktop-Tabelle + Mobile-Karten teilen dasselbe Array)
  rows: KnxTelegram[] = [];
  private cursor?: string;
  hasMore = false;
  loading = false;

  // Sort (nur Zeitspalte, beidseitig via Keyset)
  sortKey = 'timestamp';
  sortDir: SortDir = 'desc';

  // Filter
  fromValue = '';
  toValue = '';
  address = '';
  source = '';
  types = new Set<string>();
  search = '';
  timeRange: 'all' | 'hour' | 'today' | '7d' = 'all';
  readonly topics: ReadonlyArray<{ label: string; term: string }> = [
    { label: 'Temperatur', term: 'temp' },
    { label: 'Licht', term: 'licht' },
    { label: 'Beschattung', term: 'jalousie' },
    { label: 'Leistung', term: 'leistung' }
  ];

  totalCount: number | null = null;
  isExporting = false;
  archiveDays: ArchiveDay[] = [];
  isMobile = window.innerWidth < 768;

  readonly allColumns: KnxColumn[] = [
    { key: 'timestamp', header: 'Zeitpunkt', kind: 'datetime', width: 175, sortable: true },
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

  ngOnInit(): void {
    this.loadArchiveDays();
    this.loadCount();
    this.reset();
  }

  @HostListener('window:resize')
  onResize(): void { this.isMobile = window.innerWidth < 768; }

  showDetail(row: KnxTelegram): void { this.detail.open(row); }

  onSort(e: { key: string; dir: SortDir }): void {
    this.sortKey = e.key;
    this.sortDir = e.dir;
    this.reset();
  }

  onNearEnd(): void { this.loadMore(); }

  loadMore(): void {
    if (!this.loading && this.hasMore) this.queryPage();
  }

  private reset(): void {
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
    this.reset();
  }

  resetFilters(): void {
    this.fromValue = '';
    this.toValue = '';
    this.address = '';
    this.source = '';
    this.types.clear();
    this.search = '';
    this.timeRange = 'all';
    this.applyFilters();
  }

  setType(t: 'Write' | 'Read' | 'Response'): void {
    if (this.types.has(t)) this.types.delete(t); else this.types.add(t);
    this.applyFilters();
  }

  setTopic(term: string): void {
    this.search = this.search === term ? '' : term;
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

  onSearchChanged(): void {
    clearTimeout(this.searchDebounce);
    this.searchDebounce = setTimeout(() => this.applyFilters(), 250);
  }
  private searchDebounce?: ReturnType<typeof setTimeout>;

  get hasActiveFilters(): boolean {
    return !!(this.fromValue || this.toValue || this.address || this.source || this.types.size || this.search || this.timeRange !== 'all');
  }

  private currentFilter(): Omit<TelegramQueryParams, 'pageSize' | 'cursor' | 'order'> {
    return {
      from: this.toIso(this.fromValue),
      to: this.toIso(this.toValue),
      address: this.address.trim() || undefined,
      source: this.source.trim() || undefined,
      types: this.types.size ? Array.from(this.types).join(',') : undefined,
      q: this.search.trim() || undefined
    };
  }

  private loadCount(): void {
    this.historyService.count({ ...this.currentFilter(), pageSize: this.pageSize }).subscribe({
      next: (res) => (this.totalCount = res.count),
      error: () => (this.totalCount = null)
    });
  }

  exportCsv(): void {
    this.isExporting = true;
    this.historyService.exportCsv({ ...this.currentFilter(), pageSize: this.pageSize }).subscribe({
      next: (blob) => {
        this.downloadBlob(blob, `knx-history-${new Date().toISOString()}.csv`);
        this.isExporting = false;
      },
      error: () => {
        this.isExporting = false;
        this.toast('✗ Export failed');
      }
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
      error: () => this.toast('✗ Download failed')
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
    this.snackBar.open(message, 'Close', { duration: 3000, horizontalPosition: 'end', verticalPosition: 'top' });
  }
}
