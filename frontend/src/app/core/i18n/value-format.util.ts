import { parseDpt } from '../../shared/grid/knx-grid.util';

/**
 * Display formatting of a decoded bus value in the browser locale (#44), the counterpart of
 * the timestamp formatting from #42. The backend decodes invariantly ("847.2 W", "2026-10-10",
 * "Wed 08:11:51"); API, database and CSV keep that form, only the screen gets the local one.
 *
 * Only shapes the DPT is known to produce are touched. Anything that does not match (texts,
 * scenes, hex for an unknown DPT, a weekday Falcon spells differently) is returned unchanged.
 * Numbers keep their decimals and get no grouping, so "31146411 Wh" stays one readable run.
 * Weekdays are short ("Mi.") to keep the value column narrow.
 */
export function formatDecodedValue(decoded: string | null | undefined, dpt: string | null | undefined, locale: string): string {
  const text = decoded ?? '';
  if (!text) return text;
  const p = parseDpt(dpt);
  if (!p) return text;

  if (NUMERIC_MAINS.has(p.main)) return formatNumberPrefix(text, locale);
  if (p.main === 10) return formatTimeOfDay(text, locale) ?? text;
  if (p.main === 11) return formatDate(text, locale) ?? text;
  if (p.main === 19) return formatDateTime(text, locale) ?? text;
  return text;
}

const NUMERIC_MAINS = new Set([5, 6, 7, 8, 9, 12, 13, 14]);

const NUMBER_RE = /^(-?\d+)(?:\.(\d+))?(?![\d.])/;
const TIME_RE = /^(?:(Mon|Tue|Wed|Thu|Fri|Sat|Sun) )?(\d{2}):(\d{2}):(\d{2})$/;
const DATE_RE = /^(\d{4})-(\d{2})-(\d{2})$/;
const DATE_TIME_RE = /^(\d{4})-(\d{2})-(\d{2}) (?:(Mon|Tue|Wed|Thu|Fri|Sat|Sun) )?(\d{2}):(\d{2}):(\d{2})$/;

// 2024-01-01 was a Monday; offsets pick a date with the wanted weekday.
const WEEKDAY_OFFSET: Record<string, number> = { Mon: 0, Tue: 1, Wed: 2, Thu: 3, Fri: 4, Sat: 5, Sun: 6 };

// Formatters are cached per locale and shape: the live table formats many cells per second.
const cache = new Map<string, Intl.NumberFormat | Intl.DateTimeFormat>();

function numberFormat(locale: string, decimals: number): Intl.NumberFormat {
  const key = `n|${locale}|${decimals}`;
  let f = cache.get(key) as Intl.NumberFormat | undefined;
  if (!f) {
    f = new Intl.NumberFormat(locale, { minimumFractionDigits: decimals, maximumFractionDigits: decimals, useGrouping: false });
    cache.set(key, f);
  }
  return f;
}

function dateFormat(locale: string, shape: 'time' | 'dayTime' | 'date' | 'dateTime' | 'dayDateTime'): Intl.DateTimeFormat {
  const key = `d|${locale}|${shape}`;
  let f = cache.get(key) as Intl.DateTimeFormat | undefined;
  if (!f) {
    const time: Intl.DateTimeFormatOptions = { hourCycle: 'h23', hour: '2-digit', minute: '2-digit', second: '2-digit' };
    const date: Intl.DateTimeFormatOptions = { day: '2-digit', month: '2-digit', year: 'numeric' };
    const options: Record<typeof shape, Intl.DateTimeFormatOptions> = {
      time,
      dayTime: { weekday: 'short', ...time },
      date,
      dateTime: { ...date, ...time },
      dayDateTime: { weekday: 'short', ...date, ...time },
    };
    // UTC on both ends: the value is a wall-clock reading from the bus, not an instant.
    f = new Intl.DateTimeFormat(locale, { ...options[shape], timeZone: 'UTC' });
    cache.set(key, f);
  }
  return f;
}

function formatNumberPrefix(text: string, locale: string): string {
  const m = NUMBER_RE.exec(text);
  if (!m) return text;
  const decimals = m[2]?.length ?? 0;
  const value = Number(m[2] !== undefined ? `${m[1]}.${m[2]}` : m[1]);
  if (!Number.isFinite(value)) return text;
  const rest = text.slice(m[0].length);
  // Falcon writes "80.12%"; the locale decides the gap (de "80,12 %", en "80.12%").
  if (rest === '%') return percentFormat(locale, decimals).format(value / 100);
  return numberFormat(locale, decimals).format(value) + rest;
}

function percentFormat(locale: string, decimals: number): Intl.NumberFormat {
  const key = `p|${locale}|${decimals}`;
  let f = cache.get(key) as Intl.NumberFormat | undefined;
  if (!f) {
    f = new Intl.NumberFormat(locale, {
      style: 'percent', minimumFractionDigits: decimals, maximumFractionDigits: decimals, useGrouping: false,
    });
    cache.set(key, f);
  }
  return f;
}

function formatTimeOfDay(text: string, locale: string): string | null {
  const m = TIME_RE.exec(text);
  if (!m) return null;
  const [, day, hh, mm, ss] = m;
  const d = new Date(Date.UTC(2024, 0, 1 + (day ? WEEKDAY_OFFSET[day] : 0), +hh, +mm, +ss));
  if (+hh > 23 || isNaN(d.getTime())) return null;
  return dateFormat(locale, day ? 'dayTime' : 'time').format(d);
}

function formatDate(text: string, locale: string): string | null {
  const m = DATE_RE.exec(text);
  if (!m) return null;
  const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
  if (isNaN(d.getTime()) || d.getUTCDate() !== +m[3]) return null;
  return dateFormat(locale, 'date').format(d);
}

function formatDateTime(text: string, locale: string): string | null {
  const m = DATE_TIME_RE.exec(text);
  if (!m) return null;
  const [, y, mo, da, day, hh, mm, ss] = m;
  const d = new Date(Date.UTC(+y, +mo - 1, +da, +hh, +mm, +ss));
  if (isNaN(d.getTime()) || d.getUTCDate() !== +da || +hh > 23) return null;
  return dateFormat(locale, day ? 'dayDateTime' : 'dateTime').format(d);
}
