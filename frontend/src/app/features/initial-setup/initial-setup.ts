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
    MatProgressSpinnerModule
  ],
  templateUrl: './initial-setup.html',
  styleUrl: './initial-setup.scss'
})
export class InitialSetup {
  private authService = inject(AuthService);
  private router = inject(Router);
  private toast = inject(ToastService);

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
      this.errorMessage = 'Please enter username and password';
      return;
    }

    if (this.credentials.password.length < 8) {
      this.errorMessage = 'Password must be at least 8 characters long';
      return;
    }

    if (this.credentials.password !== this.confirmPassword) {
      this.errorMessage = 'Passwords do not match';
      return;
    }

    this.isLoading = true;

    this.authService.initialSetup(this.credentials).subscribe({
      next: () => {
        this.toast.success('Admin account created successfully!');
        this.router.navigate(['/live-view']);
      },
      error: (error) => {
        this.isLoading = false;
        const message = error.error?.message || 'Setup failed. Please try again.';
        this.errorMessage = message;
        this.toast.error(message);
      }
    });
  }
}
