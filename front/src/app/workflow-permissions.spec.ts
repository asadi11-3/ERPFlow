import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';

import { routes } from './app.routes';
import { Api } from './core/api/api';
import type { SalesOrder } from './core/api/models';
import { fakeToken } from './core/auth/fake-token';
import type { Role } from './core/auth/session';

const order: SalesOrder = {
  id: 'order-1', customerId: 'customer-1', createdByUserId: 'sales-1', status: 2,
  rowVersion: 'AAAAAAAAAAE=', createdAt: '2026-10-25T00:00:00Z', submittedAt: null, decidedAt: null,
  totalAmount: 10, items: [{ id: 'line-1', productId: 'product-1', quantity: 1, unitPrice: 10, lineTotal: 10 }],
  reservations: [{ id: 'reservation-1', productId: 'product-1', quantity: 1, isReleased: false,
    createdAt: '2026-10-25T00:00:00Z', releasedAt: null }],
};

describe('Workflow permissions', () => {
  beforeEach(() => sessionStorage.clear());

  function setup(role: Role, current: SalesOrder = order): void {
    sessionStorage.setItem('erpflow.token', fakeToken({ sub: 'sales-1', name: role, role }));
    TestBed.configureTestingModule({
      providers: [provideRouter(routes, withComponentInputBinding()), provideHttpClient(), provideHttpClientTesting(),
        { provide: Api, useValue: {
          customers: () => of([]), products: () => of([]), pending: () => of([]), jobcards: () => of([]),
          machineAssets: () => of([]), order: () => of(current), history: () => of([]),
          orders: (page: number, pageSize: number) => of({ items: [], page, pageSize, totalCount: 0, totalPages: 0 }),
        } },
      ],
    });
  }

  for (const role of ['Sales', 'Approver', 'Technician', 'Administrator'] satisfies Role[]) {
    it(`lands ${role} on the right screen and exposes order discovery`, async () => {
      setup(role);
      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/');
      await harness.fixture.whenStable();
      const expected = role === 'Approver' ? '/approvals' : role === 'Technician' ? '/service' : '/orders';
      expect(TestBed.inject(Router).url).toBe(expected);
      expect(harness.routeNativeElement?.querySelector('a[href="/orders"]')).not.toBeNull();
    });

    it(`shows dispatch only when ${role} is an authorized dispatcher`, async () => {
      setup(role);
      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/orders/order-1');
      await harness.fixture.whenStable();
      harness.detectChanges();
      const buttons = [...(harness.routeNativeElement?.querySelectorAll('button') ?? [])];
      const visible = buttons.some(button => button.textContent?.includes('Dispatch & Register Assets'));
      expect(visible).toBe(role === 'Technician' || role === 'Administrator');
    });

    it(`keeps service reads but restricts creation for ${role}`, async () => {
      setup(role);
      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/service');
      await harness.fixture.whenStable();
      harness.detectChanges();
      const allowed = role === 'Technician' || role === 'Administrator';
      expect(!!harness.routeNativeElement?.querySelector('a[href="/service/new"]')).toBe(allowed);
      await harness.navigateByUrl('/service/new');
      expect(TestBed.inject(Router).url === '/service/new').toBe(allowed);
    });
  }

  it('hides dispatch after all reservations are closed', async () => {
    setup('Technician', { ...order, reservations: order.reservations.map(r => ({ ...r, isReleased: true })) });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/orders/order-1');
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).not.toContain('Dispatch & Register Assets');
  });

  it('offers editing only for an owned Draft', async () => {
    setup('Sales', { ...order, status: 0, reservations: [] });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/orders/order-1');
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('a[href="/orders/order-1/edit"]')).not.toBeNull();
  });
});
