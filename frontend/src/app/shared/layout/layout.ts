import { Component, DestroyRef, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { A11yModule } from '@angular/cdk/a11y';
import { NavigationEnd, Router, RouterModule } from '@angular/router';
import { filter, map } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatMenuModule } from '@angular/material/menu';
import { AuthService } from '../../core/services/auth.service';
import { SignalrService } from '../../core/services/signalr.service';
import { ToastService } from '../../core/services/toast.service';
import { LanguageService } from '../../core/i18n/language.service';
import { Lang } from '../../core/i18n/translations';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ThemeService, ThemeMode } from '../../core/services/theme.service';

/**
 * Ein Navigationseintrag. `slot` entscheidet, wo er auf Mobil landet:
 * `tab` = eigener Platz in der Bottom-Nav, `more` = im "Mehr"-Sheet.
 * Die Top-Nav (Desktop) zeigt unabhängig davon alle Einträge in Listenreihenfolge.
 */
export interface NavItem {
  route: string;
  /** i18n-Key des vollständigen Labels (Top-Nav, More-Sheet, aria-label). */
  label: string;
  /** i18n-Key eines Kurzlabels für die Bottom-Nav (nur für `slot: 'tab'` wirksam). */
  short?: string;
  /** Material-Ligatur. */
  icon?: string;
  /** Registriertes SVG-Icon — hat Vorrang vor `icon`. */
  svgIcon?: string;
  alpha?: boolean;
  slot: 'tab' | 'more';
}

/**
 * Die Grenzen der Layout-Bänder aus layout.scss. Wird eine davon überschritten,
 * blendet CSS ein womöglich offenes Overlay aus — der Zustand muss mitziehen.
 * Änderungen hier und in layout.scss gehören zusammen.
 */
const BAND_EDGES = [
  '(max-width: 767.98px)',
  '(max-width: 1023.98px)',
  '(max-width: 1559.98px)',
] as const;

@Component({
  selector: 'app-layout',
  imports: [CommonModule, RouterModule, A11yModule, MatIconModule, MatButtonModule, MatTooltipModule, MatMenuModule, TranslatePipe],
  templateUrl: './layout.html',
  styleUrl: './layout.scss',
})
export class Layout {
  private authService = inject(AuthService);
  private signalr = inject(SignalrService);
  private router = inject(Router);
  private toast = inject(ToastService);
  private language = inject(LanguageService);
  private themeSvc = inject(ThemeService);
  private destroyRef = inject(DestroyRef);

  currentUser$ = this.authService.currentUser$;
  readonly lang = this.language.lang;
  readonly theme = this.themeSvc.theme;

  // ---------------------------------------------------------------------
  // Navigation — eine Quelle der Wahrheit für Top-Nav und Bottom-Nav.
  // Reihenfolge = Reihenfolge der Top-Nav.
  // ---------------------------------------------------------------------
  readonly navItems: NavItem[] = [
    { route: '/monitor',         label: 'nav.monitor',                                          svgIcon: 'knx:monitor',  slot: 'tab' },
    { route: '/charts',          label: 'nav.charts',                                           icon: 'show_chart',      slot: 'tab' },
    // Topologie ist Teil derselben Arbeitssitzung am Bus: beim Mitlesen löst sie
    // eine Quelladresse wie 1.1.9 in ein Gerät auf. Statistik dagegen wird im
    // Alltag am seltensten gebraucht und liegt deshalb im Überlauf.
    { route: '/group-addresses', label: 'nav.groupAddresses', short: 'nav.groupAddressesShort', icon: 'lan',             slot: 'tab' },
    { route: '/topology',        label: 'nav.topology',       short: 'nav.topologyShort',       icon: 'account_tree',    slot: 'tab' },
    { route: '/stats',           label: 'nav.stats',                                            icon: 'bar_chart',       slot: 'more' },
    { route: '/graph',           label: 'nav.graph',                                            icon: 'hub',             slot: 'more', alpha: true },
    { route: '/logs',            label: 'nav.logs',                                             icon: 'subject',         slot: 'more' },
    { route: '/projects',        label: 'nav.projects',                                         svgIcon: 'knx:folder',   slot: 'more' },
    { route: '/settings',        label: 'nav.settings',                                         svgIcon: 'knx:settings', slot: 'more' },
  ];

  /** Die vier Einträge mit eigenem Platz in der Bottom-Nav. */
  readonly tabItems = this.navItems.filter(i => i.slot === 'tab');
  /** Alles, was auf Mobil hinter "Mehr" liegt. */
  readonly moreItems = this.navItems.filter(i => i.slot === 'more');

  /** Bottom-Sheet der Mobil-Navigation. */
  readonly moreOpen = signal(false);
  /** Überlauf-Menü der Kopfleiste (Tablet / schmaler Desktop). */
  readonly navMoreOpen = signal(false);
  /** Ein gemeinsamer Backdrop für beide — es ist immer höchstens einer sichtbar. */
  readonly anyMoreOpen = computed(() => this.moreOpen() || this.navMoreOpen());

  private readonly moreTrigger = viewChild<ElementRef<HTMLButtonElement>>('moreTrigger');
  private readonly navMoreTrigger = viewChild<ElementRef<HTMLButtonElement>>('navMoreTrigger');

  /** Aktuelle URL als Signal — Abhängigkeit für `moreActive`. */
  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map(e => e.urlAfterRedirects)
    ),
    { initialValue: this.router.url }
  );

  /** Beide "Mehr"-Trigger leuchten, wenn eine der dahinterliegenden Routen aktiv ist. */
  readonly moreActive = computed(() => {
    this.currentUrl();
    return this.moreItems.some(i => this.router.isActive(i.route, {
      paths: 'subset', queryParams: 'ignored', fragment: 'ignored', matrixParams: 'ignored',
    }));
  });

  constructor() {
    // Jede Navigation schließt beide Überläufe — auch die per Browser-Zurück.
    effect(() => {
      this.currentUrl();
      this.closeOverlaysSilently();
    });

    // Ein offener Überlauf überlebt sonst den Breitenwechsel: das Overlay wird
    // per CSS ausgeblendet, die Signale bleiben aber true — zurück bliebe ein
    // bildschirmfüllender Backdrop (mobil sogar abgedunkelt), der Klicks
    // schluckt. Die Liste spiegelt exakt die Bandgrenzen aus layout.scss.
    for (const query of BAND_EDGES) {
      const mq = window.matchMedia(query);
      const onChange = () => this.closeOverlaysSilently();
      mq.addEventListener('change', onChange);
      this.destroyRef.onDestroy(() => mq.removeEventListener('change', onChange));
    }
  }

  /**
   * WCAG 2.5.3 "Label in Name": sichtbar steht in der Bottom-Nav das Kurzlabel,
   * der Accessible Name muss es deshalb enthalten — sonst greift Sprachsteuerung
   * ("Klick GAs") den Tab nicht. Der volle Name kommt dahinter, solange er sich
   * unterscheidet (DE: Kurzform und Langform von "Statistik" sind identisch).
   */
  tabAriaLabel(item: NavItem): string {
    const full = this.language.translate(item.label);
    if (!item.short) return full;
    const short = this.language.translate(item.short);
    return short === full ? full : `${short} – ${full}`;
  }

  /** Schließt beide Overlays, ohne den Fokus zu versetzen. */
  private closeOverlaysSilently(): void {
    this.moreOpen.set(false);
    this.navMoreOpen.set(false);
  }

  toggleMore(): void { this.moreOpen.update(v => !v); }

  closeMore(): void {
    this.moreOpen.set(false);
    // cdkTrapFocus gibt den Fokus beim Zerstören nicht zuverlässig zurück
    // (z. B. wenn der Klick den Button gar nicht fokussiert hat) — selbst tun.
    // Auf einen unsichtbaren Trigger zu fokussieren ist ein No-op, deshalb
    // braucht es keine Breakpoint-Abfrage.
    this.moreTrigger()?.nativeElement.focus();
  }

  toggleNavMore(): void { this.navMoreOpen.update(v => !v); }

  closeNavMore(): void {
    this.navMoreOpen.set(false);
    this.navMoreTrigger()?.nativeElement.focus();
  }

  /** Klick auf den Backdrop — schließt, was gerade offen ist. */
  closeMoreOverlays(): void {
    if (this.navMoreOpen()) this.closeNavMore();
    if (this.moreOpen()) this.closeMore();
  }

  setLang(l: Lang): void { this.language.setLang(l); }
  setTheme(m: ThemeMode): void { this.themeSvc.setTheme(m); }

  async logout(): Promise<void> {
    try {
      await this.signalr.stopConnection();
    } catch {
      // ignore — proceed with logout anyway
    }

    this.authService.logout().subscribe({
      next: () => {
        this.toast.info(this.language.translate('auth.loggedOut'));
        this.router.navigate(['/login']);
      },
      error: () => {
        this.toast.warning(this.language.translate('auth.logoutFailed'));
        this.router.navigate(['/login']);
      }
    });
  }

  /** Revoke every active session (all devices) and return to the login screen. */
  async logoutEverywhere(): Promise<void> {
    try {
      await this.signalr.stopConnection();
    } catch {
      // ignore — proceed anyway
    }

    this.authService.logoutAll().subscribe({
      next: () => {
        this.toast.info(this.language.translate('auth.loggedOut'));
        this.router.navigate(['/login']);
      },
      error: () => {
        this.toast.warning(this.language.translate('auth.logoutFailed'));
        this.router.navigate(['/login']);
      }
    });
  }
}
