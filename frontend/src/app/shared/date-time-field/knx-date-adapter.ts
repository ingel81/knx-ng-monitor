import { Injectable } from '@angular/core';
import { MatDateFormats, NativeDateAdapter } from '@angular/material/core';

export type DateField = 'day' | 'month' | 'year';

/**
 * Order of day / month / year in the locale's numeric date, e.g. de-AT → day, month, year,
 * en-US → month, day, year. Typed input is read in the same order it is displayed.
 */
export function dateFieldOrder(locale: string): DateField[] {
  const parts = new Intl.DateTimeFormat(locale, { day: '2-digit', month: '2-digit', year: 'numeric' })
    .formatToParts(new Date(2026, 8, 24));
  const order = parts
    .map((p) => p.type)
    .filter((t): t is DateField => t === 'day' || t === 'month' || t === 'year');
  return order.length === 3 ? order : ['day', 'month', 'year'];
}

/**
 * Display formats for the date-time field: zero-padded day/month, four-digit year and a
 * 24-hour clock regardless of the locale's own preference.
 */
export const KNX_DATE_FORMATS: MatDateFormats = {
  parse: { dateInput: null, timeInput: null },
  display: {
    dateInput: { day: '2-digit', month: '2-digit', year: 'numeric' },
    timeInput: { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' },
    monthYearLabel: { year: 'numeric', month: 'short' },
    dateA11yLabel: { year: 'numeric', month: 'long', day: 'numeric' },
    monthYearA11yLabel: { year: 'numeric', month: 'long' },
    timeOptionLabel: { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' },
  },
};

/**
 * NativeDateAdapter with strict parsing. The stock adapter hands typed text to `Date.parse`,
 * which guesses (US order, two-digit years) and accepts anything the engine likes; this one
 * reads the locale's field order and rejects everything that is not a real calendar date
 * with a four-digit year (#41: "08.10.202600" was accepted by the native input).
 */
@Injectable()
export class KnxDateAdapter extends NativeDateAdapter {
  override parse(value: unknown, parseFormat?: unknown): Date | null {
    if (typeof value !== 'string') return super.parse(value, parseFormat);
    const text = value.trim();
    if (!text) return null;

    // Any non-digit separates, so "24.09.2026", "24/09/2026", "24-09-2026" and "24,09,2026"
    // (the decimal keypad on phones) all work. A leading four-digit group is ISO order.
    let groups = text.split(/\D+/).filter(Boolean);
    let order = dateFieldOrder(this.locale);
    if (groups.length === 1 && /^\d{8}$/.test(groups[0])) {
      // Separator-less "24092026": cut by the locale order (year is always four digits).
      const g = groups[0];
      let pos = 0;
      groups = order.map((f) => {
        const len = f === 'year' ? 4 : 2;
        const part = g.slice(pos, pos + len);
        pos += len;
        return part;
      });
    }
    if (groups.length !== 3) return this.invalid();
    if (/^\d{4}$/.test(groups[0])) order = ['year', 'month', 'day'];

    const field = (f: DateField) => groups[order.indexOf(f)];
    const y = field('year');
    const m = field('month');
    const d = field('day');
    if (!/^\d{4}$/.test(y) || !/^\d{1,2}$/.test(m) || !/^\d{1,2}$/.test(d)) return this.invalid();

    const year = +y;
    const month = +m - 1;
    const day = +d;
    if (year < 1000 || month < 0 || month > 11 || day < 1) return this.invalid();
    const date = this.createDate(year, month, 1);
    if (day > this.getNumDaysInMonth(date)) return this.invalid();
    return this.createDate(year, month, day);
  }

  override parseTime(userValue: unknown, parseFormat?: unknown): Date | null {
    if (typeof userValue !== 'string') return super.parseTime(userValue, parseFormat);
    const text = userValue.trim();
    if (!text) return null;

    // 24-hour only: "14:30", "14.30", "14,30", "14:30:15" or a bare "1430" / "930".
    const match = text.match(/^(\d{1,2})\D(\d{2})(?:\D(\d{2}))?$/) ?? text.match(/^(\d{1,2})(\d{2})$/);
    if (!match) return this.invalid();
    const hours = +match[1];
    const minutes = +match[2];
    const seconds = match[3] ? +match[3] : 0;
    if (hours > 23 || minutes > 59 || seconds > 59) return this.invalid();
    return this.setTime(this.today(), hours, minutes, seconds);
  }
}
