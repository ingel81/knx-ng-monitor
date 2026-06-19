import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from './language.service';

/**
 * Unreine Übersetzungs-Pipe: re-evaluiert bei jedem Change-Detection-Lauf und
 * liest dabei das `lang`-Signal des LanguageService — damit schaltet die UI
 * beim `setLang` ohne Reload sofort um.
 *
 * Verwendung: {{ 'key' | translate }} oder {{ 'key' | translate:{ name: x } }}
 */
@Pipe({ name: 'translate', standalone: true, pure: false })
export class TranslatePipe implements PipeTransform {
  private lang = inject(LanguageService);

  transform(key: string, params?: Record<string, string | number>): string {
    return this.lang.translate(key, params);
  }
}
