import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuthService } from '../../core/services/auth.service';
import { SignalrService } from '../../core/services/signalr.service';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-layout',
  imports: [CommonModule, RouterModule, MatIconModule, MatButtonModule, MatTooltipModule],
  templateUrl: './layout.html',
  styleUrl: './layout.scss',
})
export class Layout {
  private authService = inject(AuthService);
  private signalr = inject(SignalrService);
  private router = inject(Router);
  private toast = inject(ToastService);

  currentUser$ = this.authService.currentUser$;

  async logout(): Promise<void> {
    try {
      await this.signalr.stopConnection();
    } catch {
      // ignore — proceed with logout anyway
    }

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
