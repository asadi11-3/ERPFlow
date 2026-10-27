import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { EnvironmentProviders, isDevMode, makeEnvironmentProviders, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideServiceWorker } from '@angular/service-worker';

import { authInterceptor } from './auth/interceptor';
import { connectivityInterceptor } from './pwa/interceptor';

/**
 * Global singletons for the whole app. `app.config.ts` calls this once.
 * Zoneless is the Angular 22 default: never add zone.js or provideZoneChangeDetection().
 */
export function provideCore(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch(), withInterceptors([connectivityInterceptor, authInterceptor])),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ]);
}
