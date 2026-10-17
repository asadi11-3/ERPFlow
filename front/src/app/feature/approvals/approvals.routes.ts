import { Routes } from '@angular/router';

import { roleGuard } from '../../core/auth/guards';
import { ApprovalList } from './approval-list';
import { ApprovalReview } from './approval-review';

export default [
  { path: '', component: ApprovalList, canActivate: [roleGuard('Approver', 'Administrator')] },
  { path: ':id', component: ApprovalReview, canActivate: [roleGuard('Approver', 'Administrator')] },
] satisfies Routes;
