import { Directive, ElementRef, HostListener, inject, output } from '@angular/core';

/**
 * Keyboard shortcut `/` for a page's primary search field: put it on the input and the slash
 * jumps there instead of being typed. On any other host (the charts GA picker is a mat-select
 * whose search field only exists while its panel is open) nothing is focused; the host reacts
 * to `slashFocus` instead.
 */
@Directive({
  selector: '[knxSlashFocus]',
  standalone: true,
  host: { 'aria-keyshortcuts': '/' }
})
export class SlashFocusDirective {
  private el = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Fired when the shortcut was taken, after an input host has been focused. */
  readonly slashFocus = output<void>();

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    // Checked by key, not code: on a German layout the slash is Shift+7, so Shift stays allowed.
    if (event.key !== '/' || event.ctrlKey || event.altKey || event.metaKey || event.defaultPrevented) return;
    if (isTypingTarget(event.target)) return;

    const host = this.el.nativeElement;
    // A field hidden by the current breakpoint or mode has no boxes; leave the key alone then.
    if (host.getClientRects().length === 0) return;

    event.preventDefault();
    if (host instanceof HTMLInputElement || host instanceof HTMLTextAreaElement) {
      host.focus();
      host.select();
    }
    this.slashFocus.emit();
  }
}

/**
 * Typing somewhere already, or inside the CDK overlay (menus, dialogs, select panels), where the
 * key belongs to whatever is focused there. Same rule as the Space shortcut in the monitor.
 */
function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  const tag = target.tagName.toLowerCase();
  return tag === 'input' || tag === 'textarea' || tag === 'select'
    || target.isContentEditable
    || !!target.closest('.cdk-overlay-container');
}
