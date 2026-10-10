import { Injectable, effect, inject } from '@angular/core';
import { MatDatepickerIntl } from '@angular/material/datepicker';
import { LanguageService } from '../../core/i18n/language.service';

/**
 * Screen-reader labels of the calendar popup in the app language. Material ships them in
 * English only; `changes` makes an open calendar pick up a language switch.
 */
@Injectable()
export class KnxDatepickerIntl extends MatDatepickerIntl {
  private readonly lang = inject(LanguageService);

  constructor() {
    super();
    effect(() => {
      const t = (key: string) => this.lang.translate(key);
      this.calendarLabel = t('dateTime.calendar');
      this.openCalendarLabel = t('dateTime.openCalendar');
      this.closeCalendarLabel = t('dateTime.closeCalendar');
      this.prevMonthLabel = t('dateTime.prevMonth');
      this.nextMonthLabel = t('dateTime.nextMonth');
      this.prevYearLabel = t('dateTime.prevYear');
      this.nextYearLabel = t('dateTime.nextYear');
      this.prevMultiYearLabel = t('dateTime.prevYears');
      this.nextMultiYearLabel = t('dateTime.nextYears');
      this.switchToMonthViewLabel = t('dateTime.chooseDate');
      this.switchToMultiYearViewLabel = t('dateTime.chooseMonthYear');
      this.changes.next();
    });
  }
}
