import { Routes } from '@angular/router';

import { roleGuard } from '../../core/auth/guards';
import { OrderDetail } from './order-detail';
import { OrderList } from './order-list';
import { OrderNew } from './order-new';

export default [
  { path: '', component: OrderList },
  { path: 'new', component: OrderNew, canActivate: [roleGuard('Sales', 'Administrator')] },
  { path: ':id/edit', component: OrderNew, canActivate: [roleGuard('Sales', 'Administrator')] },
  { path: ':id', component: OrderDetail },
] satisfies Routes;
