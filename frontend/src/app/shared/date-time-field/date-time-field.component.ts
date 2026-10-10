import { Component, ElementRef, Input, ViewChild, computed, effect, forwardRef, inject, Output, EventEmitter } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ControlValueAccessor, FormControl, NG_VALUE_ACCESSOR, ReactiveFormsModule } from '@angular/forms';
import { DateAdapter, MAT_DATE_FORMATS } from '@angular/material/core';
import { MatDatepickerIntl, MatDatepickerModule } from '@angular/material/datepicker';
import { MatIconModule } from '@angular/material/icon';
import { MatTimepickerModule } from '@angular/material/timepicker';
import { MatTooltipModule } from '@angular/material/tooltip';
import { merge } from 'rxjs';
import { LanguageService } from '../../core/i18n/language.service';
import { localeTag } from '../../core/i18n/locale.util';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { KNX_DATE_FORMATS, KnxDateAdapter } from './knx-date-adapter';
import { KnxDatepickerIntl } from './knx-datepicker-intl';

const LOCAL_VALUE = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?/;

let nextId = 0;

/**
 * In-app replacement for `<input type="datetime-local">` (#15, #41): a date field with a
 * calendar popup plus a 24-hour time field with a suggestion list.
 *
 * The model value keeps the exact shape the native input produced, a local
 * `YYYY-MM-DDTHH:mm` string (`:ss` only when the seconds are not zero) or `''` while
 * empty, incomplete or invalid, so callers keep turning it into an instant with
 * `new Date(value)` as before.
 */
@Component({
  selector: 'knx-date-time-field',
  standalone: true,
  imports: [ReactiveFormsModule, MatDatepickerModule, MatTimepickerModule, MatIconModule, MatTooltipModule, TranslatePipe],
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => DateTimeFieldComponent), multi: true },
    { provide: DateAdapter, useClass: KnxDateAdapter },
    { provide: MAT_DATE_FORMATS, useValue: KNX_DATE_FORMATS },
    { provide: MatDatepickerIntl, useClass: KnxDatepickerIntl },
  ],
  template: `
    <div class="dtf" [class.is-invalid]="showInvalid" [class.is-disabled]="dateCtrl.disabled">
      <input #dateInput class="dtf-date" type="text" inputmode="decimal" autocomplete="off"
             [attr.maxlength]="dateMaxLength()"
             [formControl]="dateCtrl" [matDatepicker]="picker"
             [placeholder]="datePlaceholder()"
             [attr.aria-label]="fieldLabel('dateTime.date')"
             [attr.aria-invalid]="dateInvalid || invalid || outOfOrder"
             [attr.aria-describedby]="errorKey ? errorId : null"
             [matTooltip]="errorKey ? (errorKey | translate: { format: datePlaceholder() }) : ''"
             [matTooltipDisabled]="!errorKey"
             (blur)="markTouched()" />
      <input class="dtf-time" inputmode="decimal" autocomplete="off" maxlength="8"
             [formControl]="timeCtrl" [matTimepicker]="timepicker"
             [placeholder]="'dateTime.timePlaceholder' | translate"
             [attr.aria-label]="fieldLabel('dateTime.time')"
             [attr.aria-invalid]="timeInvalid || invalid || outOfOrder"
             [attr.aria-describedby]="errorKey ? errorId : null"
             [matTooltip]="'dateTime.timeHint' | translate"
             (blur)="markTouched()" />
      <mat-timepicker #timepicker interval="15m" [aria-label]="fieldLabel('dateTime.time')" />
      <button type="button" class="dtf-toggle" aria-haspopup="dialog"
              [disabled]="dateCtrl.disabled"
              [attr.aria-label]="fieldLabel('dateTime.openCalendar')"
              [matTooltip]="'dateTime.openCalendar' | translate"
              (click)="picker.open()">
        <mat-icon svgIcon="knx:calendar"></mat-icon>
      </button>
      <mat-datepicker #picker [touchUi]="touchUi" />
    </div>
    <span class="dtf-sr" [id]="errorId">{{ errorKey ? (errorKey | translate: { format: datePlaceholder() }) : '' }}</span>
  `,
  styles: `
    :host {
      --dtf-height: 36px;
      display: inline-block;
      width: 200px;
      vertical-align: middle;
    }

    .dtf {
      display: flex;
      align-items: center;
      height: var(--dtf-height);
      width: 100%;
      padding: 0 2px 0 var(--sp-3);
      background: var(--surface);
      border: 1px solid var(--line-strong);
      border-radius: var(--r-sm);
      transition: border-color var(--t-fast), box-shadow var(--t-fast);
    }
    .dtf:hover { border-color: var(--ink-3); }
    .dtf:focus-within { border-color: var(--brand); box-shadow: 0 0 0 3px var(--brand-tint); }
    .dtf.is-invalid { border-color: var(--err); box-shadow: 0 0 0 3px var(--err-tint); }
    .dtf.is-disabled { background: var(--surface-3); }

    input {
      min-width: 0;
      height: 100%;
      padding: 0;
      border: none;
      outline: none;
      background: transparent;
      color: var(--ink);
      font-family: var(--font-mono);
      font-size: var(--fs-sm);
      font-feature-settings: 'tnum' 1;
    }
    input::placeholder { color: var(--ink-3); }
    input:disabled { color: var(--ink-3); cursor: not-allowed; }
    // Ten mono digits plus separators; the time takes what is left, at least hh:mm.
    .dtf-date { flex: 1 1 11ch; }
    .dtf-time { flex: 0 1 6ch; margin-left: var(--sp-2); }

    .dtf-toggle {
      flex: none;
      display: grid;
      place-items: center;
      width: 30px;
      height: 30px;
      margin-left: 2px;
      border: none;
      border-radius: var(--r-sm);
      background: transparent;
      color: var(--ink-3);
      cursor: pointer;
    }
    .dtf-toggle:hover:not(:disabled) { background: var(--surface-3); color: var(--ink); }
    .dtf-toggle:focus-visible { outline: 2px solid var(--brand); outline-offset: 1px; }
    .dtf-toggle:disabled { cursor: not-allowed; }
    .dtf-toggle mat-icon { width: 17px; height: 17px; font-size: 17px; }

    .dtf-sr {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }
  `,
})
export class DateTimeFieldComponent implements ControlValueAccessor {
  private readonly lang = inject(LanguageService);
  private readonly adapter = inject<DateAdapter<Date>>(DateAdapter);

  /** Visible label of the field ("From"), prefixed to the inner controls' aria-labels. */
  @Input() label = '';
  /** Time filled in when a date is picked while the time is still empty. */
  @Input() defaultTime = '00:00';
  /** Flags the field from outside, e.g. as missing when the user applies an empty range. */
  @Input() invalid = false;
  /** Earliest accepted value (same local shape); an earlier value is flagged as out of order. */
  @Input() min = '';

  @ViewChild('dateInput', { static: true }) private dateInput!: ElementRef<HTMLInputElement>;

  readonly dateCtrl = new FormControl<Date | null>(null);
  readonly timeCtrl = new FormControl<Date | null>(null);
  readonly errorId = `knx-dtf-error-${nextId++}`;

  /** Localised hint in the locale's field order, e.g. TT.MM.JJJJ or MM/DD/YYYY. */
  readonly datePlaceholder = computed(() => {
    const t = (key: string) => this.lang.translate(key);
    return new Intl.DateTimeFormat(localeTag(this.lang.lang()), KNX_DATE_FORMATS.display.dateInput)
      .formatToParts(new Date(2026, 8, 24))
      .map((p) =>
        p.type === 'day' ? t('dateTime.dd')
        : p.type === 'month' ? t('dateTime.mm')
        : p.type === 'year' ? t('dateTime.yyyy')
        : p.value)
      .join('');
  });

  /** Length of a formatted date in this locale: 10 for 24.09.2026, 13 for hu-HU's "2026. 09. 24.".
   *  Caps typing there, so a six-digit year cannot even be entered (#41). */
  readonly dateMaxLength = computed(() =>
    new Intl.DateTimeFormat(localeTag(this.lang.lang()), KNX_DATE_FORMATS.display.dateInput)
      .format(new Date(2026, 11, 28)).length);

  private value = '';
  private onChange: (value: string) => void = () => { };
  private onTouched: () => void = () => { };

  constructor() {
    effect(() => this.adapter.setLocale(localeTag(this.lang.lang())));

    // statusChanges as well: an unparsable entry often leaves the value untouched and only
    // flips the parse validator.
    merge(this.dateCtrl.valueChanges, this.dateCtrl.statusChanges)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.sync(true));
    merge(this.timeCtrl.valueChanges, this.timeCtrl.statusChanges)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.sync(false));
  }

  /** The calendar opens as a dialog on phone widths, where the anchored popup does not fit. */
  get touchUi(): boolean {
    return typeof window !== 'undefined' && window.matchMedia?.('(max-width: 767px)').matches;
  }

  get dateInvalid(): boolean { return this.dateCtrl.invalid; }
  get timeInvalid(): boolean { return this.timeCtrl.invalid; }

  get outOfOrder(): boolean {
    if (!this.min || !this.value) return false;
    return new Date(this.value).getTime() < new Date(this.min).getTime();
  }

  get showInvalid(): boolean {
    return this.invalid || this.outOfOrder
      || (this.dateInvalid && this.dateCtrl.touched)
      || (this.timeInvalid && this.timeCtrl.touched);
  }

  /** Translation key of the field's own error, shown once the user has left the control. */
  get errorKey(): string {
    if (this.dateInvalid && this.dateCtrl.touched) return 'dateTime.invalidDate';
    if (this.timeInvalid && this.timeCtrl.touched) return 'dateTime.invalidTime';
    if (this.outOfOrder) return 'dateTime.beforeStart';
    return '';
  }

  fieldLabel(key: string): string {
    const part = this.lang.translate(key);
    return this.label ? `${this.label}: ${part}` : part;
  }

  focus(): void {
    this.dateInput.nativeElement.focus();
  }

  /** True while the typed date or time cannot be read; the bound value is '' then. */
  get hasInvalidInput(): boolean { return this.dateInvalid || this.timeInvalid; }

  /** Only one half filled in (e.g. the time was deleted): the bound value is '' as well. */
  get hasIncompleteInput(): boolean {
    return (this.dateCtrl.value === null) !== (this.timeCtrl.value === null);
  }

  /** Fires when the user changes the field (typing or picking), never for a value set in code. */
  @Output() readonly edited = new EventEmitter<void>();

  /**
   * Empties both inputs, including unreadable text. Setting the model to '' alone does not reach
   * the field when it is '' already (an invalid entry), so the red text would stay on screen.
   */
  clear(): void {
    for (const ctrl of [this.dateCtrl, this.timeCtrl]) {
      ctrl.setValue(null, { emitEvent: false });
      ctrl.markAsUntouched();
      ctrl.updateValueAndValidity({ emitEvent: false });
    }
    if (this.value !== '') {
      this.value = '';
      this.onChange('');
    }
  }

  writeValue(value: string | null): void {
    this.value = value ?? '';
    const m = LOCAL_VALUE.exec(this.value);
    const date = m ? new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], m[6] ? +m[6] : 0) : null;
    const valid = !!date && !isNaN(date.getTime());
    this.dateCtrl.setValue(valid ? date : null, { emitEvent: false });
    this.timeCtrl.setValue(valid ? new Date(date!.getTime()) : null, { emitEvent: false });
  }

  registerOnChange(fn: (value: string) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }

  markTouched(): void { this.onTouched(); }

  setDisabledState(disabled: boolean): void {
    for (const ctrl of [this.dateCtrl, this.timeCtrl]) {
      if (disabled) ctrl.disable({ emitEvent: false });
      else ctrl.enable({ emitEvent: false });
    }
  }

  private sync(fromDate: boolean): void {
    const date = this.dateCtrl.value;
    if (fromDate && this.isValidDate(date) && this.dateCtrl.valid
        && this.timeCtrl.value === null && this.timeCtrl.valid) {
      const [h, m] = this.defaultTime.split(':').map(Number);
      this.timeCtrl.setValue(this.adapter.setTime(date, h || 0, m || 0, 0), { emitEvent: false });
    }
    const next = this.compose();
    if (next === this.value) return;
    this.value = next;
    this.onChange(next);
    this.edited.emit();
  }

  private compose(): string {
    const date = this.dateCtrl.value;
    const time = this.timeCtrl.value;
    if (this.dateCtrl.invalid || this.timeCtrl.invalid) return '';
    if (!this.isValidDate(date) || !this.isValidDate(time)) return '';
    const p = (n: number) => String(n).padStart(2, '0');
    const seconds = time.getSeconds();
    return `${date.getFullYear()}-${p(date.getMonth() + 1)}-${p(date.getDate())}`
      + `T${p(time.getHours())}:${p(time.getMinutes())}${seconds ? `:${p(seconds)}` : ''}`;
  }

  private isValidDate(d: Date | null): d is Date {
    return d instanceof Date && !isNaN(d.getTime());
  }
}
