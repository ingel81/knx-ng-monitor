import { Component, OnInit, inject, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatCardModule } from '@angular/material/card';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AgGridAngular } from 'ag-grid-angular';
import {
  ColDef,
  GridApi,
  GridOptions,
  GridReadyEvent,
  IDatasource,
  IGetRowsParams,
  ModuleRegistry,
  AllCommunityModule
} from 'ag-grid-community';
import { TelegramHistoryService } from '../../core/services/telegram-history.service';
import { ArchiveDay, TelegramQueryParams } from '../../core/models/telegram-history.models';

ModuleRegistry.registerModules([AllCommunityModule]);

@Component({
  selector: 'app-history',
  imports: [
    CommonModule,
    FormsModule,
    MatIconModule,
    MatButtonModule,
    MatInputModule,
    MatFormFieldModule,
    MatCardModule,
    MatTooltipModule,
    MatSelectModule,
    MatSnackBarModule,
    AgGridAngular
  ],
  templateUrl: './history.component.html',
  styleUrl: './history.component.scss'
})
export class HistoryComponent implements OnInit {
  private historyService = inject(TelegramHistoryService);
  private snackBar = inject(MatSnackBar);

  @ViewChild(AgGridAngular) agGrid!: AgGridAngular;

  private readonly pageSize = 100;
  private gridApi?: GridApi;

  // Forward-only keyset cursors, kept for the whole session so scroll-up re-fetches work.
  private blockCursors = new Map<number, string>();

  // Filter state
  fromValue = '';   // datetime-local (local time)
  toValue = '';     // datetime-local (local time)
  address = '';
  source = '';
  type = '';        // '', 'Write', 'Read', 'Response'

  totalCount: number | null = null;
  isExporting = false;
  archiveDays: ArchiveDay[] = [];

  gridOptions: GridOptions;
  columnDefs: ColDef[];
  defaultColDef: ColDef;

  constructor() {
    this.defaultColDef = {
      sortable: false,
      filter: false,
      resizable: true,
      cellStyle: {
        display: 'flex',
        alignItems: 'center',
        height: '35px',
        lineHeight: 'normal',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap'
      }
    };

    this.columnDefs = [
      {
        headerName: 'Time',
        field: 'timestamp',
        width: 180,
        minWidth: 150,
        valueFormatter: (params) => {
          if (!params.value) return '';
          const date = new Date(params.value);
          return date.toLocaleString('de-DE', {
            year: '2-digit', month: '2-digit', day: '2-digit',
            hour: '2-digit', minute: '2-digit', second: '2-digit',
            fractionalSecondDigits: 3
          });
        }
      },
      { headerName: 'Source', field: 'sourceAddress', width: 110, minWidth: 90 },
      { headerName: 'Destination', field: 'destinationAddress', width: 120, minWidth: 100 },
      {
        headerName: 'Name',
        field: 'groupAddressName',
        minWidth: 150,
        flex: 2,
        cellClass: (params) => params.value ? 'group-name-cell' : 'group-name-cell empty',
        valueFormatter: (params) => params.value || '(unknown)'
      },
      {
        headerName: 'DPT',
        field: 'datapointType',
        width: 100,
        minWidth: 80,
        cellClass: (params) => params.value ? 'dpt-cell' : 'dpt-cell empty',
        valueFormatter: (params) => params.value || '-'
      },
      {
        headerName: 'Type',
        field: 'messageType',
        width: 90,
        minWidth: 70,
        cellClass: (params) => this.getMessageTypeClass(params.data?.messageType),
        valueGetter: (params) => this.getMessageTypeName(params.data?.messageType)
      },
      {
        headerName: 'Raw Value',
        field: 'value',
        width: 120,
        minWidth: 100,
        cellStyle: { fontFamily: 'monospace', fontSize: '0.9em' }
      },
      {
        headerName: 'Decoded Value',
        field: 'valueDecoded',
        minWidth: 120,
        flex: 1,
        cellStyle: { fontWeight: '600', color: '#2e7d32' }
      }
    ];

    this.gridOptions = {
      rowModelType: 'infinite',
      cacheBlockSize: this.pageSize,
      maxBlocksInCache: 20,
      infiniteInitialRowCount: 1,
      rowHeight: 35,
      headerHeight: 45,
      animateRows: false,
      enableCellTextSelection: true,
      getRowClass: (params) => this.getMessageTypeClass(params.data?.messageType),
      onGridReady: this.onGridReady.bind(this)
    };
  }

  ngOnInit(): void {
    this.loadArchiveDays();
  }

  onGridReady(event: GridReadyEvent): void {
    this.gridApi = event.api;
    this.loadCount();
    this.gridApi.setGridOption('datasource', this.buildDatasource());
  }

  applyFilters(): void {
    this.blockCursors.clear();
    this.loadCount();
    this.gridApi?.setGridOption('datasource', this.buildDatasource());
  }

  resetFilters(): void {
    this.fromValue = '';
    this.toValue = '';
    this.address = '';
    this.source = '';
    this.type = '';
    this.applyFilters();
  }

  private buildDatasource(): IDatasource {
    return {
      getRows: (params: IGetRowsParams) => {
        const blockIndex = Math.floor(params.startRow / this.pageSize);
        const cursor = blockIndex === 0 ? undefined : this.blockCursors.get(blockIndex);

        if (blockIndex !== 0 && cursor === undefined) {
          // No cursor for this block (cannot random-access keyset pages) — fail gracefully.
          params.failCallback();
          return;
        }

        const query: TelegramQueryParams = { ...this.currentFilter(), cursor, pageSize: this.pageSize };
        this.historyService.query(query).subscribe({
          next: (page) => {
            if (page.nextCursor) {
              this.blockCursors.set(blockIndex + 1, page.nextCursor);
            }
            // lastRow unknown (-1) until the end is reached; never seed from /count.
            const lastRow = page.hasMore ? -1 : params.startRow + page.items.length;
            params.successCallback(page.items, lastRow);
          },
          error: () => params.failCallback()
        });
      }
    };
  }

  private currentFilter(): Omit<TelegramQueryParams, 'pageSize' | 'cursor'> {
    return {
      from: this.toIso(this.fromValue),
      to: this.toIso(this.toValue),
      address: this.address.trim() || undefined,
      source: this.source.trim() || undefined,
      type: this.type || undefined
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

  private downloadBlob(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
  }

  private toast(message: string): void {
    this.snackBar.open(message, 'Close', {
      duration: 3000,
      horizontalPosition: 'end',
      verticalPosition: 'top'
    });
  }

  getMessageTypeClass(type: string | number | undefined): string {
    const typeStr = String(type).toLowerCase();
    switch (typeStr) {
      case 'write': case '0': return 'msg-write';
      case 'read': case '1': return 'msg-read';
      case 'response': case '2': return 'msg-response';
      default: return '';
    }
  }

  getMessageTypeName(type: string | number | undefined): string {
    const typeStr = String(type).toLowerCase();
    switch (typeStr) {
      case 'write': case '0': return 'Write';
      case 'read': case '1': return 'Read';
      case 'response': case '2': return 'Response';
      default: return type === undefined ? '' : String(type);
    }
  }
}
