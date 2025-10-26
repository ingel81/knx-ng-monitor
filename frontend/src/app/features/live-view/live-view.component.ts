import { Component, OnInit, OnDestroy, inject, ViewChild, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatCardModule } from '@angular/material/card';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { Subscription } from 'rxjs';
import { SignalrService, KnxTelegram } from '../../core/services/signalr.service';
import { HttpClient } from '@angular/common/http';
import { AgGridAngular } from 'ag-grid-angular';
import {
  ColDef,
  GridOptions,
  GridReadyEvent,
  GetRowIdParams,
  RowClassParams,
  ModuleRegistry,
  AllCommunityModule
} from 'ag-grid-community';

// Register AG-Grid modules
ModuleRegistry.registerModules([AllCommunityModule]);

interface KnxConfiguration {
  id: number;
  ipAddress: string;
  port: number;
  physicalAddress: string;
}

@Component({
  selector: 'app-live-view',
  imports: [
    CommonModule,
    FormsModule,
    MatIconModule,
    MatButtonModule,
    MatInputModule,
    MatFormFieldModule,
    MatCardModule,
    MatTooltipModule,
    MatCheckboxModule,
    AgGridAngular
  ],
  templateUrl: './live-view.component.html',
  styleUrl: './live-view.component.scss'
})
export class LiveViewComponent implements OnInit, OnDestroy {
  private signalrService = inject(SignalrService);
  private http = inject(HttpClient);
  private subscription?: Subscription;

  @ViewChild(AgGridAngular) agGrid!: AgGridAngular;

  telegrams: KnxTelegram[] = [];
  isConnected = false;
  isPaused = false;
  isConnecting = false;
  autoScroll = true;
  quickFilterText = '';

  // AG-Grid Configuration
  gridOptions: GridOptions;
  columnDefs: ColDef[];
  defaultColDef: ColDef;

  constructor() {
    // Default column configuration
    this.defaultColDef = {
      sortable: true,
      filter: true,
      resizable: true,
      floatingFilter: true,
      tooltipValueGetter: undefined, // Tooltips deaktiviert
      cellStyle: {
        display: 'flex',
        alignItems: 'center',
        height: '35px',
        lineHeight: 'normal',
        paddingTop: '0',
        paddingBottom: '0',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap'
      }
    };

    // Column definitions with custom configurations
    this.columnDefs = [
      {
        headerName: 'Time',
        field: 'timestamp',
        width: 130,
        minWidth: 100,
        maxWidth: 180,
        filter: 'agDateColumnFilter',
        valueFormatter: (params) => {
          if (!params.value) return '';
          const date = new Date(params.value);
          return date.toLocaleTimeString('de-DE', {
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            fractionalSecondDigits: 3
          });
        }
      },
      {
        headerName: 'Source',
        field: 'sourceAddress',
        width: 110,
        minWidth: 90,
        maxWidth: 150,
        filter: 'agTextColumnFilter'
      },
      {
        headerName: 'Destination',
        field: 'destinationAddress',
        width: 120,
        minWidth: 100,
        maxWidth: 160,
        filter: 'agTextColumnFilter'
      },
      {
        headerName: 'Name',
        field: 'groupAddressName',
        minWidth: 150,
        flex: 2,
        filter: 'agTextColumnFilter',
        cellClass: (params) => params.value ? 'group-name-cell' : 'group-name-cell empty',
        valueFormatter: (params) => params.value || '(unknown)'
      },
      {
        headerName: 'DPT',
        field: 'datapointType',
        width: 100,
        minWidth: 80,
        filter: 'agTextColumnFilter',
        cellClass: (params) => params.value ? 'dpt-cell' : 'dpt-cell empty',
        valueFormatter: (params) => params.value || '-'
      },
      {
        headerName: 'Type',
        field: 'messageType',
        width: 80,
        minWidth: 70,
        filter: 'agTextColumnFilter',
        cellClass: (params) => {
          return this.getMessageTypeClass(params.value);
        },
        valueFormatter: (params) => this.getMessageTypeName(params.value)
      },
      {
        headerName: 'Raw Value',
        field: 'value',
        width: 120,
        minWidth: 100,
        filter: 'agTextColumnFilter',
        cellStyle: { fontFamily: 'monospace', fontSize: '0.9em' }
      },
      {
        headerName: 'Decoded Value',
        field: 'valueDecoded',
        minWidth: 120,
        flex: 1,
        filter: 'agTextColumnFilter',
        cellStyle: { fontWeight: '600', color: '#2e7d32' }
      },
      {
        headerName: 'Priority',
        field: 'priority',
        width: 90,
        minWidth: 70,
        maxWidth: 120,
        filter: 'agNumberColumnFilter',
        hide: true // Hidden by default, can be shown by user
      },
      {
        headerName: 'Flags',
        field: 'flags',
        width: 80,
        minWidth: 60,
        maxWidth: 100,
        filter: 'agTextColumnFilter',
        cellStyle: { fontFamily: 'monospace', fontSize: '0.9em' },
        hide: true // Hidden by default, can be shown by user
      }
    ];

    // Grid options
    this.gridOptions = {
      rowModelType: 'clientSide',
      animateRows: false, // Disable animations to prevent height issues
      enableCellTextSelection: true,
      ensureDomOrder: false, // Let AG-Grid manage DOM order
      suppressCellFocus: false,
      suppressColumnVirtualisation: true, // Render all columns to avoid scroll issues
      getRowId: (params: GetRowIdParams) => {
        // Generate unique ID if not present
        return params.data.id ? params.data.id.toString() : `telegram-${Date.now()}-${Math.random()}`;
      },
      getRowClass: (params: RowClassParams) => {
        return this.getMessageTypeClass(params.data.messageType);
      },
      getRowStyle: () => {
        return {
          height: '35px',
          maxHeight: '35px',
          minHeight: '35px',
          lineHeight: 'normal'
        };
      },
      rowHeight: 35,
      headerHeight: 45,
      floatingFiltersHeight: 35,
      pagination: false,
      suppressPaginationPanel: true,
      suppressScrollOnNewData: false,
      suppressRowTransform: false, // Allow row transforms
      suppressRowVirtualisation: false, // Enable virtualization
      onGridReady: this.onGridReady.bind(this)
    };
  }

  async ngOnInit() {
    await this.signalrService.startConnection();

    this.subscription = this.signalrService.telegram$.subscribe(telegram => {
      if (!this.isPaused) {
        // Add to internal array
        this.telegrams.unshift(telegram);

        // Limit to 1000 telegrams for performance
        if (this.telegrams.length > 1000) {
          this.telegrams.pop();
        }

        // Update grid using transaction API for better performance
        if (this.agGrid?.api) {
          this.agGrid.api.applyTransaction({
            add: [telegram],
            addIndex: 0
          });

          // Auto-scroll to top if enabled
          if (this.autoScroll) {
            setTimeout(() => {
              this.agGrid.api.ensureIndexVisible(0, 'top');
            }, 10);
          }
        }
      }
    });

    this.checkConnectionStatus();
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
    this.signalrService.stopConnection();
  }

  @HostListener('window:resize')
  onWindowResize() {
    // Resize grid when window size changes
    if (this.agGrid?.api) {
      setTimeout(() => {
        this.agGrid.api.sizeColumnsToFit();
      }, 100);
    }
  }

  onGridReady(params: GridReadyEvent) {
    // Grid is ready, auto-size columns initially
    params.api.sizeColumnsToFit();
  }

  async connectToKnx() {
    try {
      this.isConnecting = true;

      const configs = await this.http.get<KnxConfiguration[]>('http://localhost:5075/api/knx/configurations').toPromise();

      if (!configs || configs.length === 0) {
        alert('No KNX configuration found. Please configure your KNX Gateway in Settings first.');
        return;
      }

      const configId = configs[0].id;

      await this.http.post('http://localhost:5075/api/knx/connect', configId).toPromise();
      this.isConnected = true;
    } catch (error) {
      console.error('Failed to connect to KNX:', error);
      alert('Failed to connect to KNX Gateway. Please check your settings and try again.');
    } finally {
      this.isConnecting = false;
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
    if (this.agGrid?.api) {
      this.agGrid.api.setGridOption('rowData', []);
    }
  }

  exportToCsv() {
    if (this.agGrid?.api) {
      this.agGrid.api.exportDataAsCsv({
        fileName: `knx-telegrams-${new Date().toISOString()}.csv`,
        columnKeys: ['timestamp', 'sourceAddress', 'destinationAddress', 'groupAddressName', 'datapointType', 'messageType', 'value', 'valueDecoded']
      });
    }
  }

  autoSizeAll() {
    if (this.agGrid?.api) {
      this.agGrid.api.autoSizeAllColumns(false);
    }
  }

  resetColumns() {
    if (this.agGrid?.api) {
      this.agGrid.api.resetColumnState();
      this.agGrid.api.sizeColumnsToFit();
    }
  }

  onQuickFilterChanged() {
    if (this.agGrid?.api) {
      this.agGrid.api.setGridOption('quickFilterText', this.quickFilterText);
    }
  }

  getMessageTypeClass(type: string | number): string {
    const typeStr = String(type).toLowerCase();
    switch (typeStr) {
      case 'write':
      case '0':
        return 'msg-write';
      case 'read':
      case '1':
        return 'msg-read';
      case 'response':
      case '2':
        return 'msg-response';
      default:
        return '';
    }
  }

  getMessageTypeName(type: string | number): string {
    const typeStr = String(type).toLowerCase();
    switch (typeStr) {
      case 'write':
      case '0':
        return 'Write';
      case 'read':
      case '1':
        return 'Read';
      case 'response':
      case '2':
        return 'Response';
      default:
        return String(type);
    }
  }

  // Action Methods
  async filterByAddress(address: string) {
    if (this.agGrid?.api) {
      const filterInstance = await this.agGrid.api.getColumnFilterInstance<any>('destinationAddress');
      if (filterInstance) {
        filterInstance.setModel({
          type: 'equals',
          filter: address
        });
        this.agGrid.api.onFilterChanged();
      }
    }
  }

  copyValue(value: string) {
    navigator.clipboard.writeText(value || '').then(() => {
      // TODO: Show toast notification
    }).catch(err => {
      console.error('Failed to copy:', err);
    });
  }

  showTelegramDetails(telegram: KnxTelegram) {
    const details = `
KNX Telegram Details
────────────────────────────────
Time:        ${new Date(telegram.timestamp).toLocaleString('de-DE')}
Source:      ${telegram.sourceAddress}
Destination: ${telegram.destinationAddress}
Name:        ${telegram.groupAddressName || '(unknown)'}
DPT:         ${telegram.datapointType || '-'}
Type:        ${this.getMessageTypeName(telegram.messageType)}
Raw Value:   ${telegram.value}
Decoded:     ${telegram.valueDecoded || '-'}
Priority:    ${telegram.priority}
Flags:       ${telegram.flags || '-'}
────────────────────────────────`;

    alert(details);
    // TODO: Replace with Material Dialog
  }

  // Quick actions für häufige Operationen
  showOnlyWrites() {
    this.applyMessageTypeFilter('0');
  }

  showOnlyReads() {
    this.applyMessageTypeFilter('1');
  }

  showOnlyResponses() {
    this.applyMessageTypeFilter('2');
  }

  async applyMessageTypeFilter(type: string) {
    if (this.agGrid?.api) {
      const filterInstance = await this.agGrid.api.getColumnFilterInstance<any>('messageType');
      if (filterInstance) {
        // Try filtering with both the numeric value and text value
        filterInstance.setModel({
          filterType: 'text',
          type: 'contains',
          filter: type
        });
        this.agGrid.api.onFilterChanged();
      }
    }
  }

  clearAllFilters() {
    if (this.agGrid?.api) {
      this.agGrid.api.setFilterModel(null);
      this.quickFilterText = '';
    }
  }
}
