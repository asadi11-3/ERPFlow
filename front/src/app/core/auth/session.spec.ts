import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { API_BASE_URL } from '../config';
import { fakeToken } from './fake-token';
import { Session } from './session';

describe('Session', () => {
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
});
