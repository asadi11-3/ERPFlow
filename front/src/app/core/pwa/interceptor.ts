import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { defer, finalize, throwError } from 'rxjs';

import { API_BASE_URL } from '../config';
import { Pwa } from './pwa';

export const connectivityInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith(`${API_BASE_URL}/api/`)) return next(request);
  const pwa = inject(Pwa);
  const write = !['GET', 'HEAD', 'OPTIONS'].includes(request.method);
  // Business data always comes from the network, including the browser HTTP cache.
  const fresh = request.clone({ cache: 'no-store' });
  return defer(() => {
    if (write && !navigator.onLine) {
      return throwError(() => new HttpErrorResponse({
        status: 0,
        statusText: 'Offline',
        url: request.url,
        error: { detail: 'You are offline. Reconnect before saving. Nothing has been queued.' },
      }));
    }
    if (write) pwa.pendingWrites.update((count) => count + 1);
    return next(fresh).pipe(finalize(() => {
      if (write) pwa.pendingWrites.update((count) => count - 1);
    }));
  });
};
