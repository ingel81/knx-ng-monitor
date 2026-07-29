import { Component, EventEmitter, Input, Output, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ScrollingModule, CdkVirtualScrollViewport } from '@angular/cdk/scrolling';
import { KnxTelegram } from '../../core/services/signalr.service';
import { messageTypeKind, messageTypeName, unitForDpt } from './knx-grid.util';
import { OverflowTitleDirective } from './overflow-title.directive';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LanguageService } from '../../core/i18n/language.service';
import { localeTag } from '../../core/i18n/locale.util';
import { formatKnxDateIn } from '../../core/i18n/date.util';

export type ColumnKind = 'time' | 'datetime' | 'mono' | 'muted-mono' | 'name' | 'type' | 'value';

export interface KnxColumn {
  key: keyof KnxTelegram | string;
  header: string;
  kind: ColumnKind;
  width?: number;   // feste Breite (px)
  grow?: number;    // flex-Gewicht (statt width)
  minWidth?: number;
  sortable?: boolean;
  align?: 'left' | 'right';   // Zellen-/Header-Ausrichtung (Default: left)
}

export type SortDir = 'asc' | 'desc';

const BARE_NUMBER_RE = /^-?\d[\d.,\s]*$/;

/**
 * Leichtgewichtige virtualisierte Tabelle (CDK Virtual Scroll + CSS-Grid),
 * voll über Design-Tokens gestylt. Ersetzt AG-Grid für Live + History.
 * - Live: rows = Client-Array, Streaming via scrollToTop().
 * - History: rows = akkumulierte Seiten; (nearEnd) treibt Nachladen; Sort via (sortChange).
 */
@Component({
  selector: 'knx-table',
  standalone: true,
  imports: [CommonModule, ScrollingModule, OverflowTitleDirective, TranslatePipe],
  templateUrl: './knx-table.component.html',
  styleUrl: './knx-table.component.scss'
})
export class KnxTableComponent {
  @Input() rows: KnxTelegram[] = [];
  @Input() columns: KnxColumn[] = [];
  @Input() rowHeight = 36;
  @Input() sortKey?: string;
  @Input() sortDir: SortDir = 'desc';
  @Input() loading = false;
  @Input() emptyText = 'monitor.emptyArchive';

  @Output() rowClick = new EventEmitter<KnxTelegram>();
  @Output() sortChange = new EventEmitter<{ key: string; dir: SortDir }>();
  @Output() nearEnd = new EventEmitter<void>();

  @ViewChild(CdkVirtualScrollViewport) viewport?: CdkVirtualScrollViewport;

  // Zeit-/Datumsformat folgt der aktiven UI-Sprache (Runtime-Switch) — immer 24h.
  private langSvc = inject(LanguageService);
  private get locale(): string { return localeTag(this.langSvc.lang()); }

  get gridCols(): string {
    return this.columns.map((c) =>
      c.grow ? `minmax(${c.minWidth ?? 120}px, ${c.grow}fr)` : `${c.width ?? 110}px`
    ).join(' ');
  }

  trackId = (_: number, row: KnxTelegram) => row.id ?? row;

  scrollToTop(): void { this.viewport?.scrollToIndex(0); }

  onScrolledIndexChange(): void {
    if (this.loading || this.rows.length === 0) return;
    const range = this.viewport?.getRenderedRange();
    if (range && range.end >= this.rows.length - 10) this.nearEnd.emit();
  }

  toggleSort(col: KnxColumn): void {
    if (!col.sortable) return;
    const dir: SortDir = this.sortKey === col.key && this.sortDir === 'desc' ? 'asc' : 'desc';
    this.sortChange.emit({ key: col.key as string, dir });
  }

  rowClass(row: KnxTelegram): string {
    const k = messageTypeKind(row.messageType);
    return `knx-tr${k ? ' msg-' + k : ''}`;
  }

  // Zebra stabil an der Zeilen-ID (nicht an der Position) -> kein Flippen beim Prepend.
  zebra(row: KnxTelegram): boolean {
    return ((Number(row.id) || 0) % 2) === 0;
  }

  val(row: KnxTelegram, key: string): string {
    const v = (row as unknown as Record<string, unknown>)[key];
    return v == null ? '' : String(v);
  }

  // Group-address names are rendered verbatim. An earlier version pulled a leading
  // KG/UG/EG/OG/DG token out into a badge and stripped it from the name, which guessed
  // wrong on other naming schemes (e.g. "OG1_TP_..." kept the prefix while "OG F_..."
  // lost it) and differed from the mobile cards and the detail panel, which never did it.

  // --- Typ -------------------------------------------------------------------
  typeKind(row: KnxTelegram): string { return messageTypeKind(row.messageType) || ''; }
  typeName(row: KnxTelegram): string { return messageTypeName(row.messageType); }

  // --- Wert + Einheit --------------------------------------------------------
  valueText(row: KnxTelegram): string { return this.val(row, 'valueDecoded') || this.val(row, 'value'); }
  valueClass(row: KnxTelegram): string {
    const v = this.valueText(row).trim();
    if (/^(on|an|ein|auf|true|1|ja|yes)$/i.test(v)) return 'val-on';
    if (/^(off|aus|zu|false|0|nein|no)$/i.test(v)) return 'val-off';
    if (BARE_NUMBER_RE.test(v)) return 'val-num';
    return 'val-text';
  }
  valueUnit(row: KnxTelegram): string {
    const v = this.val(row, 'valueDecoded').trim();
    return BARE_NUMBER_RE.test(v) ? unitForDpt(this.val(row, 'datapointType')) : '';
  }

  // --- Zeit ------------------------------------------------------------------
  formatTime(v: unknown): string { return formatKnxDateIn(this.locale, v, 'time'); }
  formatDateTime(v: unknown): string { return formatKnxDateIn(this.locale, v, 'dateTimeMs'); }
}
