import { inject } from '@angular/core';
import { Routes } from '@angular/router';

import { authGuard, guestGuard, roleGuard } from './core/auth/guards';
import { homeForRole, Session } from './core/auth/session';
import { Shell } from './layout/shell/shell';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadChildren: () => import('./feature/login/login.routes'),
  },
  {
    path: '',
    pathMatch: 'full',
    canActivate: [guestGuard],
    loadChildren: () => import('./feature/landing/landing.routes'),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      {
        path: '',
        pathMatch: 'full',
        redirectTo: () => {
          const role = inject(Session).user()?.role;
          return homeForRole(role).replace(/^\//, '');
        },
      },
      {
        path: 'orders',
        canActivate: [roleGuard('Sales', 'Approver', 'Technician', 'Administrator')],
        loadChildren: () => import('./feature/orders/orders.routes'),
      },
      {
        path: 'approvals',
        canActivate: [roleGuard('Approver', 'Administrator')],
        loadChildren: () => import('./feature/approvals/approvals.routes'),
      },
      { path: 'assets', loadChildren: () => import('./feature/assets/assets.routes') },
      { path: 'service', canActivate: [roleGuard('Technician', 'Administrator')], loadChildren: () => import('./feature/service/service.routes') },
      { path: 'reference', loadChildren: () => import('./feature/reference/reference.routes') },
      { path: 'analytics', loadChildren: () => import('./feature/analytics/analytics.routes') },
      { path: '', loadChildren: () => import('./feature/procurement/procurement.routes') },
    ],
  },
  { path: '**', redirectTo: '' },
];
