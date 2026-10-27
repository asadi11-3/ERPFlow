import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';

import { routes } from './app.routes';
import { Api } from './core/api/api';
import { fakeToken } from './core/auth/fake-token';

const apiStub = {
  customers: () => of([]),
  products: () => of([]),
  pending: () => of([]),
  orders: (page: number, pageSize: number) => of({ items: [], page, pageSize, totalCount: 0, totalPages: 1 }),
  jobcards: () => of([]),
  machineAssets: () => of([]),
};

describe('App routing', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  function setup(): void {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes, withComponentInputBinding()),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Api, useValue: apiStub },
      ],
    });
  }

  it('shows the landing page to signed-out visitors', async () => {
    setup();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/');

    expect(TestBed.inject(Router).url).toBe('/');
    expect((harness.fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toContain('Orders go in');
  });

  it('sends signed-out visitors to the login page from a protected route', async () => {
    setup();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/orders');

    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Forders');
    expect((harness.fixture.nativeElement as HTMLElement).textContent).toContain('Welcome back');
  });

  it('sends a signed-in sales user to the orders list inside the shell', async () => {
    sessionStorage.setItem('erpflow.token', fakeToken({ sub: 'u1', name: 'Ada Lovelace', role: 'Sales' }));
    setup();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/');

    const el = harness.fixture.nativeElement as HTMLElement;
    expect(TestBed.inject(Router).url).toBe('/orders');
    expect(el.querySelector('h1')?.textContent).toContain('My Sales Orders');
    expect(el.textContent).toContain('Ada Lovelace');
  });

  it('sends an approver to the approvals queue', async () => {
    sessionStorage.setItem('erpflow.token', fakeToken({ sub: 'u2', name: 'Grace Hopper', role: 'Approver' }));
    setup();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/');

    expect(TestBed.inject(Router).url).toBe('/approvals');
    expect((harness.fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toContain('Pending Approvals');
  });

  it('sends a technician to the field service queue', async () => {
    sessionStorage.setItem('erpflow.token', fakeToken({ sub: 'u3', name: 'Tariq Al-Mansoor', role: 'Technician' }));
    setup();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/');

    expect(TestBed.inject(Router).url).toBe('/service');
    expect((harness.fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toContain('Service Jobcards');
  });

  it('keeps a signed-in user away from the login page and redirects by role', async () => {
    sessionStorage.setItem('erpflow.token', fakeToken({ sub: 'u3', name: 'Tariq Al-Mansoor', role: 'Technician' }));
    setup();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/login');

    expect(TestBed.inject(Router).url).toBe('/service');
  });

  it('redirects unauthorized roles when navigating to guarded pages', async () => {
    sessionStorage.setItem('erpflow.token', fakeToken({ sub: 'u3', name: 'Tariq Al-Mansoor', role: 'Technician' }));
    setup();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/approvals');

    expect(TestBed.inject(Router).url).toBe('/service');
  });
});
