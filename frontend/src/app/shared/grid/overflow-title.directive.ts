import { Directive, ElementRef, HostListener, inject } from '@angular/core';

/**
 * Setzt `title` (nativer Tooltip) nur dann, wenn der Inhalt tatsächlich
 * abgeschnitten ist (`scrollWidth > clientWidth`). Wird beim Mouseenter
 * geprüft — kein Overhead im Virtual-Scroll-Rendering.
 */
@Directive({
  selector: '[knxOverflowTitle]',
  standalone: true
})
export class OverflowTitleDirective {
  private el = inject<ElementRef<HTMLElement>>(ElementRef);

  @HostListener('mouseenter')
  onEnter(): void {
    const e = this.el.nativeElement;
    if (e.scrollWidth > e.clientWidth + 1) {
      const text = (e.textContent ?? '').trim();
      if (text && e.getAttribute('title') !== text) e.setAttribute('title', text);
    } else if (e.hasAttribute('title')) {
      e.removeAttribute('title');
    }
  }
}
