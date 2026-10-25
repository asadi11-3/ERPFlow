import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { Role, Session } from './session';

export const authGuard: CanActivateFn = (_route, state) =>
  inject(Session).isActive()
    ? true
    : inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });

export const guestGuard: CanActivateFn = () => {
  const session = inject(Session);
  if (!session.isActive()) return true;
  const role = session.user()?.role;
  return inject(Router).createUrlTree([role === 'Approver' ? '/approvals' : role === 'Technician' ? '/service' : '/orders']);
};

export const roleGuard =
  (...roles: Role[]): CanActivateFn =>
  () => {
    const role = inject(Session).user()?.role;
    return role && roles.includes(role) ? true : inject(Router).createUrlTree(['/']);
  };
