import { Routes } from '@angular/router';
import { authGuard } from './shared/guards/auth.guard';

export const routes: Routes = [
  {
    path: 'setup',
    loadComponent: () => import('./features/initial-setup/initial-setup').then(m => m.InitialSetup)
  },
  {
    path: 'login',
    loadComponent: () => import('./features/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: '',
    loadComponent: () => import('./shared/layout/layout').then(m => m.Layout),
    canActivate: [authGuard],
    children: [
      {
        path: 'monitor',
        loadComponent: () => import('./features/monitor/monitor.component').then(m => m.MonitorComponent)
      },
      // Alte Pfade auf den vereinten Monitor umleiten (Lesezeichen/alte Links bleiben gültig).
      {
        path: 'live-view',
        redirectTo: 'monitor',
        pathMatch: 'full'
      },
      {
        path: 'history',
        redirectTo: 'monitor',
        pathMatch: 'full'
      },
      {
        path: 'charts',
        loadComponent: () => import('./features/charts/charts.component').then(m => m.ChartsComponent)
      },
      {
        path: 'stats',
        loadComponent: () => import('./features/stats/stats.component').then(m => m.StatsComponent)
      },
      {
        path: 'topology',
        loadComponent: () => import('./features/topology/topology.component').then(m => m.TopologyComponent)
      },
      {
        path: 'graph',
        loadComponent: () => import('./features/graph/graph.component').then(m => m.GraphComponent)
      },
      {
        path: 'group-addresses',
        loadComponent: () => import('./features/group-addresses/group-addresses.component').then(m => m.GroupAddressesComponent)
      },
      {
        path: 'projects',
        loadComponent: () => import('./features/projects/projects.component').then(m => m.ProjectsComponent)
      },
      {
        path: 'settings',
        loadComponent: () => import('./features/settings/settings').then(m => m.Settings)
      },
      {
        path: '',
        redirectTo: 'monitor',
        pathMatch: 'full'
      }
    ]
  },
  {
    path: '**',
    redirectTo: ''
  }
];
