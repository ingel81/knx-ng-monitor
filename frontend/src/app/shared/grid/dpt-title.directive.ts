import { Directive, ElementRef, HostListener, inject, input } from '@angular/core';
import { LanguageService } from '../../core/i18n/language.service';
import { dptDescription } from './knx-grid.util';

/**
 * Natives title-Tooltip mit der DPT-Beschreibung (z. B. "1.001 · Schalten ·
 * DPT_Switch") am Host-Element. Wird erst beim Mouseenter berechnet — kein
 * Overhead im Virtual-Scroll-Rendering, Sprache immer aktuell. Ohne bekannte
 * Beschreibung fällt es auf das Overflow-Verhalten von `knxOverflowTitle`
 * zurück (Volltext-Tooltip nur bei abgeschnittenem Inhalt).
 */
@Directive({ selector: '[knxDptTitle]', standalone: true })
export class DptTitleDirective {
  readonly knxDptTitle = input<string | null | undefined>();

  private readonly el = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly lang = inject(LanguageService);

  @HostListener('mouseenter')
  onEnter(): void {
    const e = this.el.nativeElement;
    const desc = dptDescription(this.knxDptTitle(), this.lang.lang());
    const text = desc || (e.scrollWidth > e.clientWidth + 1 ? (e.textContent ?? '').trim() : '');
    if (text) {
      if (e.getAttribute('title') !== text) e.setAttribute('title', text);
    } else if (e.hasAttribute('title')) {
      e.removeAttribute('title');
    }
  }
}
