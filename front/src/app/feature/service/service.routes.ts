import { Routes } from '@angular/router';

import { roleGuard } from '../../core/auth/guards';

export default [
  {
    path: '',
    loadComponent: () => import('./jobcard-list').then((m) => m.JobcardList),
  },
  {
    path: 'new',
    canActivate: [roleGuard('Technician', 'Administrator')],
    loadComponent: () => import('./jobcard-new').then((m) => m.JobcardNew),
  },
  {
    path: ':id',
    loadComponent: () => import('./jobcard-detail').then((m) => m.JobcardDetail),
  },
] as Routes;
