import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-layout',
  imports: [CommonModule, RouterModule, MatIconModule, MatButtonModule],
  templateUrl: './layout.html',
  styleUrl: './layout.scss',
})
export class Layout {
  private authService = inject(AuthService);
  private router = inject(Router);
  private toast = inject(ToastService);

  currentUser$ = this.authService.currentUser$;

  logout(): void {
    this.authService.logout().subscribe({
      next: () => {
        this.toast.info('Logged out successfully');
        this.router.navigate(['/login']);
      },
      error: () => {
        this.toast.warning('Logout failed, redirecting to login');
        this.router.navigate(['/login']);
      }
    });
  }
}
