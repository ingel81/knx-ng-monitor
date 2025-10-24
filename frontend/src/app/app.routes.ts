import { Routes } from '@angular/router';
import { authGuard } from './shared/guards/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'dashboard',
    loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent),
    canActivate: [authGuard]
  },
  {
    path: 'live-view',
    loadComponent: () => import('./features/live-view/live-view.component').then(m => m.LiveViewComponent),
    canActivate: [authGuard]
  },
  {
    path: '',
    redirectTo: '/live-view',
    pathMatch: 'full'
  },
  {
    path: '**',
    redirectTo: '/live-view'
  }
];
