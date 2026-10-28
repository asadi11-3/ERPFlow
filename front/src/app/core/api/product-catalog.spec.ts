import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from '../config';
import { ProductCatalog, catalogValidation } from './product-catalog';
import type { CatalogProduct } from './product-catalog';

export const catalogFixture: CatalogProduct = {
  id: 'p1', name: 'Scanner', sku: 'SCAN', unitPrice: 20, isActive: true,
  rowVersion: 'AAAAAAAAAAE=', createdAt: '2026-10-28T00:00:00Z',
  hasImage: true, imageVersion: '"AAAAAAAAAAI="', history: [],
};

describe('Product catalog HTTP contract', () => {
  let api: ProductCatalog;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ProductCatalog); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('creates with catalog fields only and never sends stock quantities', async () => {
    const result = firstValueFrom(api.create({ name: ' Scanner ', sku: ' SCAN ', unitPrice: 20 }));
    const request = http.expectOne(`${API_BASE_URL}/api/products`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Scanner', sku: 'SCAN', unitPrice: 20 });
    request.flush(catalogFixture); expect(await result).toEqual(catalogFixture);
  });

  it('updates using catalog rowVersion without mixing image ETags or balances', async () => {
    const result = firstValueFrom(api.update(catalogFixture, { name: 'Updated', sku: 'SCAN', unitPrice: 30 }));
    const request = http.expectOne(`${API_BASE_URL}/api/products/p1`);
    expect(request.request.body).toEqual({ name: 'Updated', sku: 'SCAN', unitPrice: 30, rowVersion: 'AAAAAAAAAAE=' });
    expect(request.request.headers.has('If-Match')).toBe(false);
    request.flush({ ...catalogFixture, name: 'Updated' }); await result;
  });

  it.each([true, false])('transitions an active=%s product using a versioned POST', async isActive => {
    const result = firstValueFrom(api.setActive({ ...catalogFixture, isActive }));
    const request = http.expectOne(`${API_BASE_URL}/api/products/p1/${isActive ? 'deactivate' : 'activate'}`);
    expect(request.request.body).toEqual({ rowVersion: 'AAAAAAAAAAE=' });
    request.flush({ ...catalogFixture, isActive: !isActive }); await result;
  });

  it('does not retry stale catalog writes', async () => {
    const result = firstValueFrom(api.update(catalogFixture, { name: 'X', sku: 'SCAN', unitPrice: 1 }));
    const failed = expect(result).rejects.toMatchObject({ status: 409 });
    http.expectOne(`${API_BASE_URL}/api/products/p1`).flush({}, { status: 409, statusText: 'Conflict' });
    await failed; http.expectNone(`${API_BASE_URL}/api/products/p1`);
  });
});

describe('Product catalog validation', () => {
  it.each([
    { name: '', sku: 'X', unitPrice: 1 },
    { name: 'X', sku: '', unitPrice: 1 },
    { name: 'X', sku: 'X', unitPrice: -1 },
    { name: 'X', sku: 'X', unitPrice: 1.234 },
    { name: 'X', sku: 'X', unitPrice: NaN },
    { name: 'X', sku: 'X', unitPrice: 10000000000000 },
    { name: 'X', sku: 'BAD\nSKU', unitPrice: 0 },
    { name: 'X'.repeat(201), sku: 'X', unitPrice: 0 },
    { name: 'X', sku: 'X'.repeat(65), unitPrice: 0 },
  ])('rejects invalid catalog draft %#', draft => expect(catalogValidation(draft)).not.toBeNull());

  it.each([0, 12.34, 0.1, 9999999999999.99])('accepts a valid price %s', unitPrice => {
    expect(catalogValidation({ name: 'Scanner', sku: 'SCAN', unitPrice })).toBeNull();
  });
});
