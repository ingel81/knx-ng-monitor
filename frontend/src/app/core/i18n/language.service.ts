import { Injectable, signal } from '@angular/core';
import { Lang, TRANSLATIONS } from './translations';

const LANG_KEY = 'knx.lang';

/**
 * Laufzeit-i18n ohne Reload. Hält die aktive Sprache als Signal; die unreine
 * `translate`-Pipe liest dieses Signal und re-evaluiert beim Sprachwechsel
 * sofort. Persistiert in localStorage (Default: 'en').
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  readonly lang = signal<Lang>(this.readLang());

  setLang(l: Lang): void {
    this.lang.set(l);
    localStorage.setItem(LANG_KEY, l);
  }

  /**
   * Liefert die Übersetzung für `key` in der aktiven Sprache, fällt auf EN
   * zurück, dann auf den Key selbst. Liest das `lang`-Signal, damit unreine
   * Pipes / Computeds beim Sprachwechsel neu berechnen. Unterstützt
   * {{name}}-Platzhalter via `params`.
   */
  translate(key: string, params?: Record<string, string | number>): string {
    const active = this.lang();
    const dict = TRANSLATIONS[active] ?? TRANSLATIONS.en;
    let text = dict[key] ?? TRANSLATIONS.en[key] ?? key;
    if (params) {
      for (const [name, value] of Object.entries(params)) {
        text = text.replace(new RegExp(`\\{\\{\\s*${name}\\s*\\}\\}`, 'g'), String(value));
      }
    }
    return text;
  }

  private readLang(): Lang {
    const stored = localStorage.getItem(LANG_KEY);
    return stored === 'de' ? 'de' : 'en';
  }
}
