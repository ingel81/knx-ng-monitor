import { AfterViewInit, Component, ElementRef, Inject, inject, NgZone, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { messageTypeKind, messageTypeName, unitForDpt } from './knx-grid.util';
import { DptTitleDirective } from './dpt-title.directive';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LanguageService } from '../../core/i18n/language.service';
import { formatKnxDate } from '../../core/i18n/date.util';
import { ConfirmDialogComponent, ConfirmDialogData } from '../confirm-dialog.component';
import { ProjectService, CommunicationObjectDto, DeviceDto } from '../../core/services/project.service';
import { LoggerService } from '../../core/logging/logger.service';
import { environment } from '../../../environments/environment.development';

/** Unterhalb dieser Breite ist das Sheet ein Bottom-Sheet (darüber ein rechts angedocktes Panel).
 *  Hier deklariert, damit Service und Geste dieselbe Schwelle benutzen. */
export const SHEET_MOBILE_BREAKPOINT = 768;

/** Gezogener Anteil der Sheet-Höhe, ab dem Loslassen schließt. */
const DISMISS_RATIO = 0.25;
/** Abwärtsgeschwindigkeit in px/ms, ab der unabhängig von der Strecke geschlossen wird. */
const DISMISS_VELOCITY = 0.5;
/** Ist die letzte Bewegung älter, zählt sie nicht mehr als Schwung (Finger lag still). */
const VELOCITY_MAX_AGE_MS = 100;
/** Spiegelt --t-base (0.2s) — Fallback, weil transitionend beim Schließen ausbleiben kann. */
const SHEET_ANIM_MS = 200;

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
  imports: [CommonModule, FormsModule, MatIconModule, MatButtonModule, MatTooltipModule, MatDialogModule, TranslatePipe, DptTitleDirective],
  template: `
    <div class="knx-sheet">
      <header class="knx-sheet-head" #head>
        <div class="knx-sheet-title">
          <span class="name" [class.empty]="!data.groupAddressName">
            {{ data.groupAddressName || ('detail.unknown' | translate) }}
          </span>
          @if (kind) {
            <span class="knx-type" [class]="'knx-type--' + kind">
              <span class="knx-type-dot"></span>{{ typeName }}
            </span>
          }
        </div>
        <button mat-icon-button (click)="close()" [attr.aria-label]="'detail.close' | translate">
          <mat-icon>close</mat-icon>
        </button>
      </header>

      <div class="knx-sheet-value">
        <div class="value-hero">
          <span class="big mono">{{ data.valueDecoded || data.value || '–' }}</span>
          @if (unit) { <span class="big-unit">{{ unit }}</span> }
        </div>
      </div>

      <dl class="knx-sheet-grid">
        <div class="field field--wide"><dt>{{ 'detail.timestamp' | translate }}</dt><dd class="mono">{{ formatTime(data.timestamp) }}</dd></div>
        <div class="field field--wide"><dt>{{ 'detail.source' | translate }}</dt><dd class="mono">{{ data.sourceAddress || '–' }}@if (sourceDevice?.name) {<span class="dd-extra"> · {{ sourceDevice!.name }}</span>}</dd></div>
        <div class="field"><dt>{{ 'detail.destGa' | translate }}</dt><dd class="mono">{{ data.destinationAddress || '–' }}</dd></div>
        <div class="field"><dt>{{ 'detail.dpt' | translate }}</dt><dd class="mono" [knxDptTitle]="data.datapointType">{{ data.datapointType || '–' }}</dd></div>
        <div class="field"><dt>{{ 'detail.raw' | translate }}</dt><dd class="mono">{{ data.value || '–' }}</dd></div>
        <div class="field"><dt>{{ 'detail.type' | translate }}</dt><dd>{{ typeName || '–' }}</dd></div>
        @if (data.priority !== undefined && data.priority !== null && data.priority !== '') {
          <div class="field"><dt>{{ 'detail.priority' | translate }}</dt><dd class="mono">{{ data.priority }}</dd></div>
        }
        @if (data.flags) {
          <div class="field"><dt>{{ 'detail.flags' | translate }}</dt><dd class="mono">{{ data.flags }}</dd></div>
        }
      </dl>

      <!-- Bus actions: send a read request or write a value to this group address.
           ⚠ Write goes to the real bus and is gated behind a confirmation dialog. -->
      @if (data.destinationAddress) {
        <section class="knx-sheet-actions">
          <div class="actions-head">{{ 'detail.controlsTitle' | translate }}</div>

          <div class="actions-row">
            <button class="knx-btn knx-btn--outline knx-btn--sm" (click)="read()" [disabled]="busy">
              <mat-icon svgIcon="knx:download"></mat-icon>
              {{ 'detail.read' | translate }}
            </button>
            @if (isChartable) {
              <button class="knx-btn knx-btn--ghost knx-btn--sm" (click)="openChart()" [matTooltip]="'ga.openChart' | translate">
                <mat-icon>show_chart</mat-icon>
                {{ 'ga.chart' | translate }}
              </button>
            }
          </div>

          <div class="actions-row write-row">
            @if (isBoolDpt) {
              <select class="knx-input" [(ngModel)]="writeValue">
                <option value="1">{{ 'detail.on' | translate }}</option>
                <option value="0">{{ 'detail.off' | translate }}</option>
              </select>
            } @else {
              <input class="knx-input" type="text" [(ngModel)]="writeValue"
                     [attr.inputmode]="isNumericDpt ? 'decimal' : null"
                     [placeholder]="('detail.writeValue' | translate)">
            }
            <button class="knx-btn knx-btn--primary knx-btn--sm" (click)="write()" [disabled]="busy || writeValue === ''">
              <mat-icon svgIcon="knx:upload"></mat-icon>
              {{ 'detail.write' | translate }}
            </button>
          </div>
          <p class="actions-hint">{{ 'detail.writeHint' | translate }}</p>
        </section>
      }

      <!-- "Used by": the devices (physical address + name) whose communication objects are
           linked to this group address. The internal ETS object number is intentionally hidden
           (it is not an address and only confuses); we show device + what the object does. -->
      @if (usedBy.length > 0) {
        <section class="knx-sheet-usedby">
          <div class="usedby-head">{{ 'detail.usedByTitle' | translate }}</div>
          <ul class="usedby-list">
            @for (co of usedBy; track co.id) {
              <li class="usedby-item">
                <div class="usedby-line">
                  <span class="mono usedby-dev">{{ co.deviceAddress }}</span>
                  @if (co.deviceName) {
                    <span class="usedby-devname">{{ co.deviceName }}</span>
                  }
                  @if (co.flags) {
                    <span class="usedby-flags mono">{{ co.flags }}</span>
                  }
                </div>
                @if (co.functionText || co.name) {
                  <div class="usedby-name">{{ co.functionText || co.name }}</div>
                }
              </li>
            }
          </ul>
        </section>
      }
    </div>
  `,
  styleUrl: './telegram-detail.component.scss'
})
export class TelegramDetailComponent implements OnInit, AfterViewInit, OnDestroy {
  constructor(
    @Inject(MAT_DIALOG_DATA) public data: TelegramDetailData,
    private ref: MatDialogRef<TelegramDetailComponent>
  ) {
    // Seed the write field from the DPT: booleans default to On, numerics to the current value.
    this.writeValue = this.isBoolDpt ? '1' : '';
  }

  @ViewChild('head') private headRef?: ElementRef<HTMLElement>;

  private host: ElementRef<HTMLElement> = inject(ElementRef);
  private zone = inject(NgZone);
  private http = inject(HttpClient);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);
  private lang = inject(LanguageService);
  private projectService = inject(ProjectService);
  private logger = inject(LoggerService);
  private router = inject(Router);

  writeValue = '';
  busy = false;

  /** Device communication objects linked to this telegram's destination GA. */
  commObjects: CommunicationObjectDto[] = [];

  /** Only the meaningful entries: a device + a named/described object. Unnamed objects (pure
   * internal status objects with no label) are dropped — they add no information and confuse. */
  get usedBy(): CommunicationObjectDto[] {
    return this.commObjects.filter(co => !!(co.name || co.functionText));
  }

  /** The sending device resolved from the telegram's source physical address (if in the project). */
  sourceDevice: DeviceDto | null = null;

  ngOnInit(): void {
    void this.loadProjectMeta();
  }

  // Resolve the active project, then enrich: comm objects that reference this GA + the sending device.
  // Best-effort: any failure (no project / request error) leaves the extras empty.
  private async loadProjectMeta(): Promise<void> {
    try {
      const projects = await this.projectService.getAllProjects().toPromise() || [];
      const active = projects.find(p => p.isActive);
      if (!active) return;

      if (this.data.destinationAddress) {
        const objects = await this.projectService.getCommObjects(active.id, this.data.destinationAddress).toPromise();
        this.commObjects = objects ?? [];
      }

      if (this.data.sourceAddress) {
        try {
          this.sourceDevice = await this.projectService.getDeviceByAddress(active.id, this.data.sourceAddress).toPromise() ?? null;
        } catch {
          this.sourceDevice = null; // 404 when the sending device isn't in the project — fine.
        }
      }
    } catch (err) {
      this.logger.error('Failed to load project metadata for telegram detail:', err);
    }
  }

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

  /** Main DPT number (e.g. 1, 9) parsed from any common DPT string form. */
  get dptMain(): number | null {
    const m = (this.data.datapointType ?? '').match(/\d+/);
    return m ? parseInt(m[0], 10) : null;
  }

  get isBoolDpt(): boolean { return this.dptMain === 1; }
  get isNumericDpt(): boolean {
    const m = this.dptMain;
    return m !== null && [5, 6, 7, 8, 9, 12, 13, 14].includes(m);
  }

  /** Chartable: numeric DPTs + DPT1 (boolean step line) — matches the charts view. */
  get isChartable(): boolean {
    const m = this.dptMain;
    return m !== null && [1, 5, 6, 7, 8, 9, 12, 13, 14].includes(m);
  }

  openChart(): void {
    const addr = this.data.destinationAddress;
    if (!addr) return;
    this.ref.close();
    this.router.navigate(['/charts'], { queryParams: { ga: addr } });
  }

  read(): void {
    this.busy = true;
    this.http.post<{ success: boolean }>(`${environment.apiUrl}/knx/read`, { address: this.data.destinationAddress })
      .subscribe({
        next: () => { this.busy = false; this.toast(this.lang.translate('detail.readSent')); },
        error: () => { this.busy = false; this.toast(this.lang.translate('detail.readFailed')); }
      });
  }

  async write(): Promise<void> {
    const address = this.data.destinationAddress;
    if (!address || this.writeValue === '') return;

    const shown = this.isBoolDpt
      ? this.lang.translate(this.writeValue === '1' ? 'detail.on' : 'detail.off')
      : this.writeValue;

    const data: ConfirmDialogData = {
      title: this.lang.translate('detail.writeConfirmTitle'),
      message: this.lang.translate('detail.writeConfirmMsg', { address, value: shown }),
      warning: this.lang.translate('detail.writeConfirmWarning'),
      confirmText: this.lang.translate('detail.write'),
      danger: true
    };

    const confirmed = await this.dialog
      // Ohne maxWidth greift Materials Default 80vw und der Warndialog schrumpft
      // auf 360-px-Geräten auf 288 px zusammen.
      .open(ConfirmDialogComponent, { data, width: 'min(420px, calc(100vw - 32px))', maxWidth: '100vw' })
      .afterClosed()
      .toPromise();

    if (!confirmed) return;

    this.busy = true;
    this.http.post<{ success: boolean }>(`${environment.apiUrl}/knx/write`, {
      address,
      datapointType: this.data.datapointType ?? null,
      value: this.writeValue
    }).subscribe({
      next: () => { this.busy = false; this.toast(this.lang.translate('detail.written', { address, value: shown })); },
      error: () => { this.busy = false; this.toast(this.lang.translate('detail.writeFailed')); }
    });
  }

  private toast(message: string): void {
    this.snackBar.open(message, this.lang.translate('common.close'), {
      duration: 3000, horizontalPosition: 'end', verticalPosition: 'top'
    });
  }

  close(): void {
    this.ref.close();
  }

  formatTime(ts: string | number | Date | undefined): string {
    return formatKnxDate(ts, 'dateTimeMs', this.lang.lang(), '–');
  }

  // ---- Swipe-to-dismiss (nur Bottom-Sheet) ---------------------------------
  // Der Griff im Kopf liest sich als "nach unten wegwischbar"; ohne Geste wirkt er defekt.
  // Bewegt wird die Dialog-Surface, also die sichtbare Karte samt Schatten und Radius —
  // würde nur .knx-sheet wandern, bliebe die Surface als leere Fläche stehen.
  // Ergänzung, kein Ersatz: X, Backdrop und Esc bleiben unverändert.

  private isMobileSheet = window.innerWidth < SHEET_MOBILE_BREAKPOINT;
  private reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  /** Die gezogene Karte. Null heißt: Geste inaktiv, alles verhält sich wie zuvor. */
  private card: HTMLElement | null = null;
  private dragPointerId: number | null = null;
  private dragStartY = 0;
  private dragY = 0;
  private cardHeight = 0;
  private lastMoveY = 0;
  private lastMoveT = 0;
  private velocity = 0;
  private timers: number[] = [];

  ngAfterViewInit(): void {
    if (!this.isMobileSheet) return;

    const head = this.headRef?.nativeElement;
    this.card = this.host.nativeElement.closest<HTMLElement>('.mat-mdc-dialog-surface');
    if (!head || !this.card) return;

    // Außerhalb der Zone: pointermove feuert pro Frame, jede Runde Change Detection wäre
    // reine Verschwendung — die Bewegung landet direkt als Transform auf dem Element.
    this.zone.runOutsideAngular(() => {
      head.addEventListener('pointerdown', this.onPointerDown);
      head.addEventListener('pointermove', this.onPointerMove);
      head.addEventListener('pointerup', this.onPointerUp);
      head.addEventListener('pointercancel', this.onPointerCancel);
    });
  }

  ngOnDestroy(): void {
    for (const id of this.timers) window.clearTimeout(id);
    this.timers = [];

    const head = this.headRef?.nativeElement;
    if (!head) return;
    head.removeEventListener('pointerdown', this.onPointerDown);
    head.removeEventListener('pointermove', this.onPointerMove);
    head.removeEventListener('pointerup', this.onPointerUp);
    head.removeEventListener('pointercancel', this.onPointerCancel);
  }

  private onPointerDown = (ev: PointerEvent): void => {
    const head = this.headRef?.nativeElement;
    if (!this.card || !head || !ev.isPrimary) return;
    if (ev.pointerType === 'mouse' && ev.button !== 0) return;
    // Der Schließen-Button behält seinen Klick: kein Capture, kein Drag.
    if ((ev.target as HTMLElement | null)?.closest('button')) return;

    this.dragPointerId = ev.pointerId;
    this.dragStartY = ev.clientY;
    this.dragY = 0;
    this.cardHeight = this.card.getBoundingClientRect().height;
    this.lastMoveY = ev.clientY;
    this.lastMoveT = ev.timeStamp;
    this.velocity = 0;
    this.card.style.transition = 'none';
    head.setPointerCapture(ev.pointerId);
  };

  private onPointerMove = (ev: PointerEvent): void => {
    if (this.dragPointerId !== ev.pointerId || !this.card) return;

    // Nur nach unten — negative Deltas werden geklemmt, hochziehen geht nicht.
    this.dragY = Math.max(0, ev.clientY - this.dragStartY);

    const dt = ev.timeStamp - this.lastMoveT;
    if (dt > 0) this.velocity = (ev.clientY - this.lastMoveY) / dt;
    this.lastMoveY = ev.clientY;
    this.lastMoveT = ev.timeStamp;

    this.card.style.transform = `translateY(${this.dragY}px)`;
  };

  private onPointerUp = (ev: PointerEvent): void => {
    if (this.dragPointerId !== ev.pointerId) return;

    const stale = ev.timeStamp - this.lastMoveT > VELOCITY_MAX_AGE_MS;
    const flung = !stale && this.velocity > DISMISS_VELOCITY;
    const far = this.cardHeight > 0 && this.dragY > this.cardHeight * DISMISS_RATIO;
    this.endDrag(ev, far || flung);
  };

  private onPointerCancel = (ev: PointerEvent): void => {
    if (this.dragPointerId !== ev.pointerId) return;
    this.endDrag(ev, false);
  };

  private endDrag(ev: PointerEvent, dismiss: boolean): void {
    const head = this.headRef?.nativeElement;
    if (head?.hasPointerCapture(ev.pointerId)) head.releasePointerCapture(ev.pointerId);
    this.dragPointerId = null;

    const card = this.card;
    if (!card) return;

    if (dismiss) {
      if (this.reducedMotion) {
        this.closeFromGesture();
        return;
      }
      card.style.transition = 'transform var(--t-base)';
      card.style.transform = `translateY(${this.cardHeight}px)`;
      this.after(SHEET_ANIM_MS, () => this.closeFromGesture());
      return;
    }

    // Zurückschnappen. Das Inline-Transform fällt weg, damit wieder der CSS-Wert gilt;
    // die Inline-Transition wird danach aufgeräumt, sonst überschriebe sie Materials
    // eigene Schließ-Transition auf derselben Property.
    if (this.reducedMotion) {
      card.style.transform = '';
      this.after(0, () => { card.style.transition = ''; });
      return;
    }
    card.style.transition = 'transform var(--t-base)';
    card.style.transform = '';
    this.after(SHEET_ANIM_MS, () => { card.style.transition = ''; });
  }

  /** Schließt über dieselbe Dialog-Referenz wie X und Esc — kein zweiter Schließpfad. */
  private closeFromGesture(): void {
    this.zone.run(() => this.ref.close());
  }

  /** setTimeout außerhalb der Zone, mit Aufräumen bei Zerstörung. */
  private after(ms: number, fn: () => void): void {
    this.timers.push(window.setTimeout(fn, ms));
  }
}
