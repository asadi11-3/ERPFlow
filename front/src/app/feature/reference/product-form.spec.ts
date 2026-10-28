import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { vi } from 'vitest';

import { Reference } from '../../core/api/reference';
import { Session } from '../../core/auth/session';
import { API_BASE_URL } from '../../core/config';
import type { Role } from '../../core/auth/session';
import type { CatalogProduct } from '../../core/api/product-catalog';
import { ProductForm } from './product-form';

describe('Catalog form interactions', () => {
  const current: CatalogProduct = { id: 'p1', name: 'Scanner', sku: 'SCAN', unitPrice: 20, isActive: true,
    rowVersion: 'AAAAAAAAAAE=', createdAt: '2026-10-28T00:00:00Z', hasImage: false, history: [] };
  const user = signal<{ role: Role }>({ role: 'Administrator' });
  const reload = vi.fn();
  let http: HttpTestingController;

  function setup(id: string | null, readOnly = false) {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}), data: { readOnly } } } },
      { provide: Session, useValue: { user, token: signal('test') } },
      { provide: Reference, useValue: { reload, updateProductImage: vi.fn() } }] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProductForm);
    return fixture;
  }

  beforeEach(() => { user.set({ role: 'Administrator' }); reload.mockClear(); });
  afterEach(() => http.verify());

  it('renders zero-stock creation guidance and validates blank input without writes', async () => {
    const fixture = setup(null); await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    host.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
    expect(host.querySelector('[role=alert]')).not.toBeNull();
    expect(host.querySelector('input[id*=stock]')).toBeNull();
    http.expectNone(`${API_BASE_URL}/api/products`);
  });

  it('loads an edit with immutable SKU and preserves entries on 409 until explicit reload', async () => {
    const fixture = setup('p1');
    http.expectOne(`${API_BASE_URL}/api/products/p1`).flush(current);
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelector<HTMLInputElement>('#product-sku')?.readOnly).toBe(true);
    const name = host.querySelector<HTMLInputElement>('#product-name');
    if (!name) throw new TypeError('Missing name field');
    name.value = 'My unsaved name'; name.dispatchEvent(new Event('input'));
    host.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne(`${API_BASE_URL}/api/products/p1`).flush({}, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    expect(name.value).toBe('My unsaved name');
    const reloadButton = Array.from(host.querySelectorAll('button')).find(button => button.textContent?.trim() === 'Reload current product');
    expect(reloadButton).toBeDefined(); reloadButton?.click();
    http.expectOne(`${API_BASE_URL}/api/products/p1`).flush({ ...current, name: 'Server name', rowVersion: 'AAAAAAAAAAM=' });
    await fixture.whenStable();
    expect(host.querySelector<HTMLInputElement>('#product-name')?.value).toBe('Server name');
  });

  it('refreshes references after saving and keeps the updated detail', async () => {
    const fixture = setup('p1'); http.expectOne(`${API_BASE_URL}/api/products/p1`).flush(current);
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    host.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne(`${API_BASE_URL}/api/products/p1`).flush({ ...current, rowVersion: 'AAAAAAAAAAM=' });
    await fixture.whenStable();
    expect(reload).toHaveBeenCalledOnce();
    expect(host.querySelector('[role=status]')).not.toBeNull();
  });

  it('allows correction of a duplicate SKU on creation without a reload dead end', async () => {
    const fixture = setup(null); await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    for (const [id, value] of [['product-name', 'Scanner'], ['product-sku', 'DUPLICATE']]) {
      const input = host.querySelector<HTMLInputElement>(`#${id}`);
      if (!input) throw new TypeError('Missing field');
      input.value = value; input.dispatchEvent(new Event('input'));
    }
    host.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne(`${API_BASE_URL}/api/products`).flush({ detail: 'SKU exists' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    expect(host.querySelector<HTMLButtonElement>('button.btn-primary')?.disabled).toBe(false);
  });

  it('shows read-only details and image preview to non-administrators without catalog actions', async () => {
    user.set({ role: 'Sales' });
    const fixture = setup('p1', true); http.expectOne(`${API_BASE_URL}/api/products/p1`).flush(current);
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelector('fieldset')?.disabled).toBe(true);
    expect(host.querySelector('button.btn-primary')).toBeNull();
    expect(host.querySelector('app-product-image')).not.toBeNull();
  });
});
