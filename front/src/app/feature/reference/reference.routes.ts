import { Routes } from '@angular/router';

import { ReferenceView } from './reference-view';
import { roleGuard } from '../../core/auth/guards';

export default [
  { path: 'products/new', canActivate: [roleGuard('Administrator')], loadComponent: () => import('./product-form').then(m => m.ProductForm) },
  { path: 'products/:id/edit', canActivate: [roleGuard('Administrator')], loadComponent: () => import('./product-form').then(m => m.ProductForm) },
  { path: 'products/:id', data: { readOnly: true }, loadComponent: () => import('./product-form').then(m => m.ProductForm) },
  { path: '', component: ReferenceView },
] satisfies Routes;
