import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { messageTypeKind, messageTypeName, unitForDpt } from './knx-grid.util';

/** Zeilen-Datensatz (Live + History teilen dieselben Felder). */
export interface TelegramDetailData {
  timestamp?: string | number | Date;
  sourceAddress?: string;
  destinationAddress?: string;
  groupAddressName?: string;
  datapointType?: string;
  messageType?: string | number;
  value?: string;
  valueDecoded?: string;
  priority?: string | number;
  flags?: string;
}

@Component({
  selector: 'app-telegram-detail',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatButtonModule, MatDialogModule],
  template: `
    <div class="knx-sheet">
      <header class="knx-sheet-head">
        <div class="knx-sheet-title">
          <span class="name" [class.empty]="!data.groupAddressName">
            {{ data.groupAddressName || '(unbekannt)' }}
          </span>
          @if (kind) {
            <span class="knx-type" [class]="'knx-type--' + kind">
              <span class="knx-type-dot"></span>{{ typeName }}
            </span>
          }
        </div>
        <button mat-icon-button (click)="close()" aria-label="Schließen">
          <mat-icon>close</mat-icon>
        </button>
      </header>

      <div class="knx-sheet-value">
        <span class="big mono">{{ data.valueDecoded || data.value || '–' }}</span>
        @if (unit) { <span class="big-unit">{{ unit }}</span> }
      </div>

      <dl class="knx-sheet-grid">
        <div class="field"><dt>Zeitpunkt</dt><dd class="mono">{{ formatTime(data.timestamp) }}</dd></div>
        <div class="field"><dt>Quelle</dt><dd class="mono">{{ data.sourceAddress || '–' }}</dd></div>
        <div class="field"><dt>Ziel-GA</dt><dd class="mono">{{ data.destinationAddress || '–' }}</dd></div>
        <div class="field"><dt>DPT</dt><dd class="mono">{{ data.datapointType || '–' }}</dd></div>
        <div class="field"><dt>Rohwert</dt><dd class="mono">{{ data.value || '–' }}</dd></div>
        <div class="field"><dt>Typ</dt><dd>{{ typeName || '–' }}</dd></div>
        @if (data.priority !== undefined && data.priority !== null && data.priority !== '') {
          <div class="field"><dt>Priorität</dt><dd class="mono">{{ data.priority }}</dd></div>
        }
        @if (data.flags) {
          <div class="field"><dt>Flags</dt><dd class="mono">{{ data.flags }}</dd></div>
        }
      </dl>
    </div>
  `,
  styleUrl: './telegram-detail.component.scss'
})
export class TelegramDetailComponent {
  constructor(
    @Inject(MAT_DIALOG_DATA) public data: TelegramDetailData,
    private ref: MatDialogRef<TelegramDetailComponent>
  ) {}

  get kind(): '' | 'write' | 'read' | 'response' {
    return messageTypeKind(this.data.messageType);
  }

  get typeName(): string {
    return messageTypeName(this.data.messageType);
  }

  get unit(): string {
    const v = (this.data.valueDecoded ?? '').toString().trim();
    return /^-?\d[\d.,\s]*$/.test(v) ? unitForDpt(this.data.datapointType) : '';
  }

  close(): void {
    this.ref.close();
  }

  formatTime(ts: string | number | Date | undefined): string {
    if (!ts) return '–';
    const d = new Date(ts);
    if (isNaN(d.getTime())) return '–';
    return d.toLocaleString('de-DE', {
      year: '2-digit', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', second: '2-digit',
      fractionalSecondDigits: 3
    });
  }
}
