import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuthService } from '../../core/services/auth.service';
import { SignalrService } from '../../core/services/signalr.service';
import { ToastService } from '../../core/services/toast.service';
import { LanguageService } from '../../core/i18n/language.service';
import { Lang } from '../../core/i18n/translations';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

@Component({
  selector: 'app-layout',
  imports: [CommonModule, RouterModule, MatIconModule, MatButtonModule, MatTooltipModule, TranslatePipe],
  templateUrl: './layout.html',
  styleUrl: './layout.scss',
})
export class Layout {
  private authService = inject(AuthService);
  private signalr = inject(SignalrService);
  private router = inject(Router);
  private toast = inject(ToastService);
  private language = inject(LanguageService);

  currentUser$ = this.authService.currentUser$;
  readonly lang = this.language.lang;

  setLang(l: Lang): void { this.language.setLang(l); }

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
}
