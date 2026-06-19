import { Lang } from './translations';

/**
 * Maps the runtime app language to a BCP-47 locale tag for date/number formatting.
 * Both tags use **24-hour** time; the date order follows the language
 * (de-DE → DD.MM.YYYY, en-GB → DD/MM/YYYY). Pair with `hour12: false` to force
 * 24-hour even on hosts whose default would be 12-hour.
 */
export function localeTag(lang: Lang): string {
  return lang === 'de' ? 'de-DE' : 'en-GB';
}
