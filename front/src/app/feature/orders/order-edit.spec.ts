import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';

import type { SalesOrder } from '../../core/api/models';
import { fakeToken } from '../../core/auth/fake-token';
import { API_BASE_URL } from '../../core/config';
import { OrderNew } from './order-new';

const draft: SalesOrder = {
  id: 'order-1', customerId: 'customer-1', createdByUserId: 'sales-1', status: 0, rowVersion: 'AAAAAAAAAAE=',
  createdAt: '2026-10-25T00:00:00Z', submittedAt: null, decidedAt: null, totalAmount: 10,
  items: [{ id: 'line-1', productId: 'product-1', quantity: 1, unitPrice: 10, lineTotal: 10 }], reservations: [],
};

describe('Persisted draft editor', () => {
  beforeEach(() => {
    sessionStorage.clear();
    sessionStorage.setItem('erpflow.token', fakeToken({ sub: 'sales-1', role: 'Sales' }));
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'order-1' }) } } },
    ] });
  });

  async function editor(current: SalesOrder = draft) {
    const fixture = TestBed.createComponent(OrderNew);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${API_BASE_URL}/api/sales-orders/order-1`).flush(current);
    for (const req of http.match(r => r.url.includes('/reference/'))) {
      req.flush(req.request.url.endsWith('/customers')
        ? [{ id: 'customer-1', name: 'Customer', email: 'customer@example.test' }]
        : [{ id: 'product-1', name: 'Product', sku: 'SKU', unitPrice: 20, availableStock: 10 }]);
    }
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, http };
  }

  it('loads existing lines and sends the row version and quantities using PUT', async () => {
    const { fixture, http } = await editor();
    const quantity = fixture.nativeElement.querySelector('input[type="number"]');
    quantity.value = '3';
    quantity.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const save = [...fixture.nativeElement.querySelectorAll('button')].find((b: HTMLButtonElement) => b.textContent?.includes('Save Draft'));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    save.click();
    const request = http.expectOne(`${API_BASE_URL}/api/sales-orders/order-1`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ customerId: 'customer-1', items: [{ productId: 'product-1', quantity: 3 }], rowVersion: 'AAAAAAAAAAE=' });
    request.flush({ ...draft, rowVersion: 'AAAAAAAAAAI=', totalAmount: 60 });
    await fixture.whenStable();
    expect(navigate).toHaveBeenCalledWith(['/orders', 'order-1']);
    http.verify();
  });

  it('preserves entered quantities and displays a stale-edit conflict', async () => {
    const { fixture, http } = await editor();
    const quantity = fixture.nativeElement.querySelector('input[type="number"]');
    quantity.value = '4';
    quantity.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const save = [...fixture.nativeElement.querySelectorAll('button')].find((b: HTMLButtonElement) => b.textContent?.includes('Save Draft'));
    save.click();
    http.expectOne(`${API_BASE_URL}/api/sales-orders/order-1`).flush({ detail: 'Reload before editing.' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Reload before editing.');
    expect(fixture.nativeElement.querySelector('input[type="number"]').value).toBe('4');
    http.verify();
  });

  it('does not enable saving a non-Draft order', async () => {
    const { fixture, http } = await editor({ ...draft, status: 1 });
    const save = [...fixture.nativeElement.querySelectorAll('button')].find((b: HTMLButtonElement) => b.textContent?.includes('Save Draft'));
    expect(save.disabled).toBe(true);
    http.expectNone(r => r.method === 'PUT');
    http.verify();
  });
});
