import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Router } from '@angular/router';
import { vi } from 'vitest';

import { API_BASE_URL } from '../config';
import { fakeToken } from './fake-token';
import { homeForRole, Session } from './session';

describe('Session', () => {
  afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks(); });
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('login stores the token and exposes the decoded user', async () => {
    const session = TestBed.inject(Session);
    const http = TestBed.inject(HttpTestingController);
    const token = fakeToken({ sub: 'u1', name: 'Zoë Ångström', email: 'z@erpflow.local', role: 'Approver' });

    const done = session.login('z@erpflow.local', 'pw');
    http.expectOne(`${API_BASE_URL}/api/auth/login`).flush({ accessToken: token, expiresAt: '' });
    await done;

    expect(session.isActive()).toBe(true);
    expect(session.user()).toEqual({ id: 'u1', name: 'Zoë Ångström', email: 'z@erpflow.local', role: 'Approver' });
    expect(sessionStorage.getItem('erpflow.token')).toBe(token);
  });

  it('treats an expired token as signed out and clears it', () => {
    sessionStorage.setItem('erpflow.token', fakeToken({ sub: 'u1', exp: 1 }));
    const session = TestBed.inject(Session);

    expect(session.isActive()).toBe(false);
    expect(session.user()).toBeNull();
    expect(sessionStorage.getItem('erpflow.token')).toBeNull();
  });

  it('homeForRole maps roles to expected root paths', () => {
    expect(homeForRole('Approver')).toBe('/approvals');
    expect(homeForRole('Technician')).toBe('/service');
    expect(homeForRole('Purchasing')).toBe('/purchase-orders');
    expect(homeForRole('WarehouseOperator')).toBe('/receiving');
    expect(homeForRole('Sales')).toBe('/orders');
    expect(homeForRole('Administrator')).toBe('/orders');
    expect(homeForRole(null)).toBe('/orders');
  });

  it('expires while open and preserves the return URL', async () => {
    vi.useFakeTimers();
    const now = Math.floor(Date.now() / 1000) * 1000;
    vi.setSystemTime(now);
    sessionStorage.setItem('erpflow.token', fakeToken({ sub: 'u1', exp: now / 1000 + 10 }));
    const session = TestBed.inject(Session);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(10_000);
    expect(session.token()).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/' } });
  });

  it('rechecks wall-clock expiry after suspension without waiting for timers', () => {
    vi.useFakeTimers();
    const now = Date.now();
    sessionStorage.setItem('erpflow.token', fakeToken({ sub: 'u1', exp: Math.floor(now / 1000) + 60 }));
    const session = TestBed.inject(Session);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    expect(session.user()?.id).toBe('u1');
    vi.setSystemTime(now + 120_000);
    window.dispatchEvent(new Event('focus'));
    expect(session.user()).toBeNull();
    expect(sessionStorage.getItem('erpflow.token')).toBeNull();
    expect(navigate).toHaveBeenCalledOnce();
  });
});
