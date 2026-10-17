import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { EnvironmentProviders, makeEnvironmentProviders, provideBrowserGlobalErrorListeners } from '@angular/core';

import { authInterceptor } from './auth/interceptor';

/**
 * Global singletons for the whole app. `app.config.ts` calls this once.
 * Zoneless is the Angular 22 default: never add zone.js or provideZoneChangeDetection().
 */
export function provideCore(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
  ]);
}
