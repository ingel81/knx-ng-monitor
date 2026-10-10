import { Lang } from './translations';

/**
 * Resolves the BCP-47 locale tag for date/number formatting (#42).
 *
 * The browser locale wins (`navigator.languages[0]` / `navigator.language`), so the shape
 * follows the user's regional settings rather than the UI language: an English UI in a
 * de-AT browser still shows 24.09.2026. The app language only decides when the browser
 * yields nothing usable (de → de-DE, en → en-GB).
 *
 * The locale picks field order and separators, never the clock: pair every time format
 * with `hourCycle: 'h23'` so en-US hosts stay 24-hour too.
 */
export function localeTag(lang: Lang): string {
  return browserLocale() ?? (lang === 'de' ? 'de-DE' : 'en-GB');
}

// The browser locale does not change during a session; resolving it once keeps the hot
// paths (table cells, chart ticks) free of the Intl probe.
let cachedBrowserLocale: string | null | undefined;

function browserLocale(): string | null {
  if (cachedBrowserLocale === undefined) cachedBrowserLocale = resolveBrowserLocale();
  return cachedBrowserLocale;
}

function resolveBrowserLocale(): string | null {
  if (typeof navigator === 'undefined') return null;
  const candidates = [...(navigator.languages ?? []), navigator.language];
  for (const tag of candidates) {
    if (!tag) continue;
    try {
      // Throws on malformed tags, returns [] for tags the Intl data cannot serve.
      const [supported] = Intl.DateTimeFormat.supportedLocalesOf(tag);
      // Keep the regional field order and separators, but pin the Gregorian calendar and Latin
      // digits: th-TH would otherwise show the year 2569 and ar-EG Arabic-Indic digits, neither
      // of which the date field can read back.
      if (supported) return new Intl.Locale(supported, { calendar: 'gregory', numberingSystem: 'latn' }).toString();
    } catch {
      // Not a valid BCP-47 tag, try the next one.
    }
  }
  return null;
}
