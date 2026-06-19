import { Component, inject, OnInit } from '@angular/core';
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
import { LoginRequest } from '../../core/models/auth.models';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LoggerService } from '../../core/logging/logger.service';

@Component({
  selector: 'app-login',
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
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent implements OnInit {
  private authService = inject(AuthService);
  private router = inject(Router);
  private toast = inject(ToastService);
  private lang = inject(LanguageService);
  private logger = inject(LoggerService);

  credentials: LoginRequest = {
    username: '',
    password: ''
  };

  errorMessage = '';
  isLoading = false;
  hidePassword = true;

  ngOnInit(): void {
    // Check if initial setup is needed
    this.authService.needsSetup().subscribe({
      next: (response) => {
        if (response.needsSetup) {
          this.router.navigate(['/setup']);
        }
      },
      error: (error) => {
        this.logger.error('Failed to check setup status:', error);
      }
    });
  }

  onSubmit(): void {
    if (!this.credentials.username || !this.credentials.password) {
      this.errorMessage = this.lang.translate('login.enterCredentials');
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';

    this.authService.login(this.credentials).subscribe({
      next: () => {
        this.toast.success(this.lang.translate('login.success'));
        this.router.navigate(['/live-view']);
      },
      error: (error) => {
        this.isLoading = false;
        const message = error.error?.message || this.lang.translate('login.failed');
        this.errorMessage = message;
        this.toast.error(message);
      }
    });
  }
}
