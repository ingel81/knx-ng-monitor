import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { InitialSetupRequest } from '../../core/models/auth.models';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { VersionService } from '../../core/services/version.service';

@Component({
  selector: 'app-initial-setup',
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    TranslatePipe
  ],
  templateUrl: './initial-setup.html',
  styleUrl: './initial-setup.scss'
})
export class InitialSetup {
  private authService = inject(AuthService);
  private router = inject(Router);
  private toast = inject(ToastService);
  private lang = inject(LanguageService);

  /** Shown in the footer — the real assembly version, not a hard-coded literal. */
  readonly version$ = inject(VersionService).version$;

  credentials: InitialSetupRequest = {
    username: '',
    password: ''
  };

  confirmPassword = '';
  errorMessage = '';
  isLoading = false;
  hidePassword = true;
  hideConfirmPassword = true;

  onSubmit(): void {
    this.errorMessage = '';

    if (!this.credentials.username || !this.credentials.password) {
      this.errorMessage = this.lang.translate('setup.enterCredentials');
      return;
    }

    if (this.credentials.password.length < 8) {
      this.errorMessage = this.lang.translate('setup.passwordTooShort');
      return;
    }

    if (this.credentials.password !== this.confirmPassword) {
      this.errorMessage = this.lang.translate('setup.passwordsMismatch');
      return;
    }

    this.isLoading = true;

    this.authService.initialSetup(this.credentials).subscribe({
      next: () => {
        this.toast.success(this.lang.translate('setup.success'));
        this.router.navigate(['/live-view']);
      },
      error: (error) => {
        this.isLoading = false;
        const message = error.error?.message || this.lang.translate('setup.failed');
        this.errorMessage = message;
        this.toast.error(message);
      }
    });
  }
}
