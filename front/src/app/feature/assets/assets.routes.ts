import { Routes } from '@angular/router';

export default [
  {
    path: '',
    loadComponent: () => import('./asset-list').then((m) => m.AssetList),
  },
  {
    path: ':id',
    loadComponent: () => import('./asset-detail').then((m) => m.AssetDetail),
  },
] as Routes;
