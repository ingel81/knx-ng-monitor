import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { KnxTelegram } from '../../core/services/signalr.service';
import { messageTypeKind, messageTypeName, unitForDpt } from './knx-grid.util';

/** Mobile Karten-Ansicht (<768px) statt Grid-Zeilen. Tippen -> select. */
@Component({
  selector: 'app-telegram-cards',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="knx-mcards">
      @for (row of rows; track row.id) {
        <button class="knx-mcard" [class]="rowClass(row)" (click)="select.emit(row)">
          <div class="mc-top">
            <span class="mc-name" [class.empty]="!row.groupAddressName">
              {{ row.groupAddressName || '(unbekannt)' }}
            </span>
            <span class="mc-val mono">
              {{ row.valueDecoded || decodedFallback(row) }}<span class="mc-unit">{{ unit(row) }}</span>
            </span>
          </div>
          <div class="mc-meta">
            <span class="mono">{{ formatTime(row.timestamp) }}</span>
            <span class="sep">·</span>
            <span class="mono">{{ row.destinationAddress }}</span>
            <span class="sep">·</span>
            <span class="mc-type" [class]="'knx-type--' + kind(row)">
              <span class="knx-type-dot"></span>{{ typeName(row) }}
            </span>
          </div>
        </button>
      }
    </div>
  `,
  styleUrl: './telegram-cards.component.scss'
})
export class TelegramCardsComponent {
  @Input() rows: KnxTelegram[] = [];
  @Output() select = new EventEmitter<KnxTelegram>();

  kind(row: KnxTelegram): string { return messageTypeKind(row.messageType) || 'write'; }
  typeName(row: KnxTelegram): string { return messageTypeName(row.messageType); }
  rowClass(row: KnxTelegram): string {
    const k = messageTypeKind(row.messageType);
    return k ? `msg-${k}` : '';
  }
  decodedFallback(row: KnxTelegram): string { return (row.value ?? '').toString() || '–'; }

  unit(row: KnxTelegram): string {
    const v = (row.valueDecoded ?? '').toString().trim();
    return /^-?\d[\d.,\s]*$/.test(v) ? unitForDpt((row as { datapointType?: string }).datapointType) : '';
  }

  formatTime(ts: string | number | Date): string {
    const d = new Date(ts);
    if (isNaN(d.getTime())) return '–';
    return d.toLocaleString('de-DE', {
      day: '2-digit', month: '2-digit',
      hour: '2-digit', minute: '2-digit', second: '2-digit'
    });
  }
}
