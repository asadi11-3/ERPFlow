import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { homeForRole, Role, Session } from './session';

export const authGuard: CanActivateFn = (_route, state) =>
  inject(Session).isActive()
    ? true
    : inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });

export const guestGuard: CanActivateFn = () => {
  const session = inject(Session);
  if (!session.isActive()) return true;
  return inject(Router).createUrlTree([homeForRole(session.user()?.role)]);
};

export const roleGuard =
  (...roles: Role[]): CanActivateFn =>
  () => {
    const session = inject(Session);
    if (!session.isActive()) {
      return inject(Router).createUrlTree(['/login']);
    }
    const role = session.user()?.role;
    if (role && roles.includes(role)) return true;
    return inject(Router).createUrlTree([homeForRole(role)]);
  };
