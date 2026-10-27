import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SwUpdate } from '@angular/service-worker';
import { firstValueFrom } from 'rxjs';
import { vi } from 'vitest';

import { authInterceptor } from '../auth/interceptor';
import { Session } from '../auth/session';
import { API_BASE_URL } from '../config';
import { connectivityInterceptor } from './interceptor';
import { Pwa } from './pwa';

describe('connectivityInterceptor', () => {
  const url = `${API_BASE_URL}/api/sales-orders`;
  const expire = vi.fn();

  beforeEach(() => {
    expire.mockClear();
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([connectivityInterceptor, authInterceptor])),
      provideHttpClientTesting(),
      { provide: SwUpdate, useValue: { isEnabled: false } },
      { provide: Session, useValue: { token: () => 'test-token', expire } },
    ] });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });

  it('rejects offline writes without sending, queuing, or expiring the session', async () => {
    const online = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    const error = await firstValueFrom(TestBed.inject(HttpClient).post(url, {})).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect((error as HttpErrorResponse).statusText).toBe('Offline');
    expect(expire).not.toHaveBeenCalled();
    online.mockReturnValue(true);
    window.dispatchEvent(new Event('online'));
    TestBed.inject(HttpTestingController).expectNone(url);
    expect(TestBed.inject(Pwa).pendingWrites()).toBe(0);
  });

  it('bypasses HTTP cache and preserves bearer and idempotency headers', async () => {
    const done = firstValueFrom(TestBed.inject(HttpClient).post(url, {}, { headers: { 'Idempotency-Key': 'same-command' } }));
    const pwa = TestBed.inject(Pwa);
    expect(pwa.pendingWrites()).toBe(1);
    const request = TestBed.inject(HttpTestingController).expectOne(url);
    expect(request.request.cache).toBe('no-store');
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-token');
    expect(request.request.headers.get('Idempotency-Key')).toBe('same-command');
    request.flush({});
    await done;
    expect(pwa.pendingWrites()).toBe(0);
  });

  it('does not retry a command whose response is lost', async () => {
    const done = firstValueFrom(TestBed.inject(HttpClient).put(url, {})).catch((e: unknown) => e);
    TestBed.inject(HttpTestingController).expectOne(url).error(new ProgressEvent('error'));
    await done;
    window.dispatchEvent(new Event('online'));
    TestBed.inject(HttpTestingController).expectNone(url);
    expect(TestBed.inject(Pwa).pendingWrites()).toBe(0);
    expect(expire).not.toHaveBeenCalled();
  });

  it('keeps reads network-only and leaves external requests untouched', async () => {
    const http = TestBed.inject(HttpClient);
    const controller = TestBed.inject(HttpTestingController);
    const read = firstValueFrom(http.get(url));
    const req = controller.expectOne(url);
    expect(req.request.cache).toBe('no-store');
    req.flush([]);
    await read;
    const external = firstValueFrom(http.get('/manifest.webmanifest'));
    const asset = controller.expectOne('/manifest.webmanifest');
    expect(asset.request.cache).toBeUndefined();
    expect(asset.request.headers.has('Authorization')).toBe(false);
    asset.flush({});
    await external;
  });
});
