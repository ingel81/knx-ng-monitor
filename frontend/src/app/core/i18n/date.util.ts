import { Lang } from './translations';
import { localeTag } from './locale.util';

/**
 * The single set of date/time shapes the UI is allowed to render. Every style is
 * 24-hour; the field order follows the locale (de-DE → 28.07.2026, en-GB → 28/07/2026).
 *
 * | style         | de-DE example            | used by                                  |
 * |---------------|--------------------------|------------------------------------------|
 * | `time`        | `15:04:05.123`           | live table time column                   |
 * | `dayTime`     | `28.07., 15:04:05`       | mobile telegram cards (width-constrained) |
 * | `dateTime`    | `28.07.2026, 15:04:05`   | chart tooltip, GA tooltip, CSV export    |
 * | `dateTimeMs`  | `28.07.2026, 15:04:05.123` | history table, detail dialog, logs      |
 * | `dateTimeMin` | `28.07.2026, 15:04`      | project import date                      |
 * | `hourMinute`  | `15:04`                  | chart time-axis tick                     |
 * | `dayMonth`    | `28.07.`                 | chart time-axis tick at midnight         |
 */
export type KnxDateStyle =
  | 'time' | 'dayTime' | 'dateTime' | 'dateTimeMs' | 'dateTimeMin' | 'hourMinute' | 'dayMonth';

const STYLE_OPTIONS: Record<KnxDateStyle, Intl.DateTimeFormatOptions> = {
  time: {
    hour12: false,
    hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3,
  },
  dayTime: {
    hour12: false,
    day: '2-digit', month: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  },
  dateTime: {
    hour12: false,
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  },
  dateTimeMs: {
    hour12: false,
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3,
  },
  dateTimeMin: {
    hour12: false,
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  },
  hourMinute: {
    hour12: false,
    hour: '2-digit', minute: '2-digit',
  },
  dayMonth: {
    day: '2-digit', month: '2-digit',
  },
};

// Constructing an Intl.DateTimeFormat is expensive relative to formatting with it,
// and the live table formats a few thousand cells per second on a busy bus — so
// keep one instance per locale+style.
const formatters = new Map<string, Intl.DateTimeFormat>();

function formatter(locale: string, style: KnxDateStyle): Intl.DateTimeFormat {
  const key = `${locale}|${style}`;
  let f = formatters.get(key);
  if (!f) {
    f = new Intl.DateTimeFormat(locale, STYLE_OPTIONS[style]);
    formatters.set(key, f);
  }
  return f;
}

/**
 * Formats a timestamp for an already resolved BCP-47 locale tag. Use this where
 * the locale is passed around explicitly (chart configs); components should use
 * {@link formatKnxDate} instead. Returns `fallback` for null/undefined/unparsable input.
 */
export function formatKnxDateIn(
  locale: string, value: unknown, style: KnxDateStyle, fallback = '',
): string {
  if (value === null || value === undefined || value === '') return fallback;
  const d = value instanceof Date ? value : new Date(value as string | number);
  if (isNaN(d.getTime())) return fallback;
  return formatter(locale, style).format(d);
}

/** Formats a timestamp in the app language's locale. */
export function formatKnxDate(
  value: unknown, style: KnxDateStyle, lang: Lang, fallback = '',
): string {
  return formatKnxDateIn(localeTag(lang), value, style, fallback);
}
