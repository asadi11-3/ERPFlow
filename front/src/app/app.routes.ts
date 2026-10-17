import { inject } from '@angular/core';
import { Routes } from '@angular/router';

import { authGuard, guestGuard } from './core/auth/guards';
import { Session } from './core/auth/session';
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
        redirectTo: () => (inject(Session).user()?.role === 'Approver' ? 'approvals' : 'orders'),
      },
      { path: 'orders', loadChildren: () => import('./feature/orders/orders.routes') },
      { path: 'approvals', loadChildren: () => import('./feature/approvals/approvals.routes') },
      { path: 'reference', loadChildren: () => import('./feature/reference/reference.routes') },
    ],
  },
  { path: '**', redirectTo: '' },
];
