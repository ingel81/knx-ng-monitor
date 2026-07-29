import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from './language.service';
import { KnxDateStyle, formatKnxDate } from './date.util';

/**
 * Unreine Datums-Pipe — Gegenstück zur `translate`-Pipe: sie liest das
 * `lang`-Signal des LanguageService, damit Datumsangaben beim Sprachwechsel
 * ohne Reload sofort umschalten. Ersetzt Angulars `date`-Pipe, die an das
 * statische `LOCALE_ID` (Default en-US, 12-Stunden) gebunden wäre.
 *
 * Verwendung: {{ ts | knxDate }} oder {{ ts | knxDate:'dateTimeMs' }}
 */
@Pipe({ name: 'knxDate', standalone: true, pure: false })
export class KnxDatePipe implements PipeTransform {
  private langSvc = inject(LanguageService);

  transform(value: unknown, style: KnxDateStyle = 'dateTime', fallback = ''): string {
    return formatKnxDate(value, style, this.langSvc.lang(), fallback);
  }
}
