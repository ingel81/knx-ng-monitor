import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
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

@Component({
  selector: 'app-layout',
  imports: [CommonModule, RouterModule, MatIconModule, MatButtonModule, MatTooltipModule, MatMenuModule, TranslatePipe],
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

  currentUser$ = this.authService.currentUser$;
  readonly lang = this.language.lang;
  readonly theme = this.themeSvc.theme;

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
