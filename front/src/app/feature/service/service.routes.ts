import { Routes } from '@angular/router';

export default [
  {
    path: '',
    loadComponent: () => import('./jobcard-list').then((m) => m.JobcardList),
  },
  {
    path: 'new',
    loadComponent: () => import('./jobcard-new').then((m) => m.JobcardNew),
  },
  {
    path: ':id',
    loadComponent: () => import('./jobcard-detail').then((m) => m.JobcardDetail),
  },
] as Routes;
