import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import { Session } from '../../core/auth/session';
import type { Role } from '../../core/auth/session';
import { API_BASE_URL } from '../../core/config';
import { ProductImage } from './product-image';

describe('Product image controls', () => {
  const user = signal<{ role: Role } | null>({ role: 'Administrator' });
  const product = { id: 'p1', name: 'Scanner', sku: 'SCAN', unitPrice: 20, availableStock: 5,
    hasImage: false, imageVersion: null };
  let http: HttpTestingController;
  const originalShow = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'showModal');
  const originalClose = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'close');

  beforeEach(() => {
    user.set({ role: 'Administrator' });
    vi.stubGlobal('URL', { createObjectURL: vi.fn(() => 'blob:preview'), revokeObjectURL: vi.fn() });
    Object.defineProperties(HTMLDialogElement.prototype, {
      showModal: { configurable: true, value: function (this: HTMLDialogElement) { this.open = true; } },
      close: { configurable: true, value: function (this: HTMLDialogElement) { this.open = false; } },
    });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: Session, useValue: { user, token: signal('token') } }] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify(); vi.restoreAllMocks(); vi.unstubAllGlobals();
    if (originalShow) Object.defineProperty(HTMLDialogElement.prototype, 'showModal', originalShow);
    else Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
    if (originalClose) Object.defineProperty(HTMLDialogElement.prototype, 'close', originalClose);
    else Reflect.deleteProperty(HTMLDialogElement.prototype, 'close');
  });

  it('renders an accessible placeholder and real labelled file input for Administrators', async () => {
    const fixture = TestBed.createComponent(ProductImage);
    fixture.componentRef.setInput('product', product);
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelector('button')?.getAttribute('aria-label')).toContain('Scanner');
    const input = host.querySelector('input');
    expect(input?.type).toBe('file');
    expect(input?.accept).toContain('image/webp');
    expect(host.querySelector('label')?.htmlFor).toBe(input?.id);
    expect(host.querySelector('img')).toBeNull();
  });

  it.each<Role>(['Sales', 'Approver', 'Technician', 'Purchasing', 'WarehouseOperator'])(
    'keeps preview but hides all mutation controls from %s', async (role) => {
      user.set({ role });
      const fixture = TestBed.createComponent(ProductImage);
      fixture.componentRef.setInput('product', product);
      await fixture.whenStable();
      const host: HTMLElement = fixture.nativeElement;
      expect(host.querySelector('.thumbnail')).not.toBeNull();
      expect(host.querySelector('input[type=file]')).toBeNull();
      expect(host.querySelector('.btn-danger')).toBeNull();
    });

  it('opens preview, reconciles an explicitly missing image, and closes with Escape', async () => {
    const fixture = TestBed.createComponent(ProductImage);
    fixture.componentRef.setInput('product', product);
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    host.querySelector<HTMLButtonElement>('.thumbnail')?.click();
    await fixture.whenStable();
    http.expectOne(`${API_BASE_URL}/api/products/p1/image`).flush(
      new Blob(['{"code":"product_image_missing"}'], { type: 'application/problem+json' }),
      { status: 404, statusText: 'Not Found' },
    );
    await fixture.whenStable();
    const dialog = host.querySelector('dialog');
    expect(dialog?.open).toBe(true);
    dialog?.dispatchEvent(new Event('cancel', { cancelable: true }));
    await fixture.whenStable();
    expect(dialog?.open).toBe(false);
  });

  it('shows a missing product as unavailable and withholds the upload control', async () => {
    const fixture = TestBed.createComponent(ProductImage);
    fixture.componentRef.setInput('product', product);
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    host.querySelector<HTMLButtonElement>('.thumbnail')?.click();
    http.expectOne(`${API_BASE_URL}/api/products/p1/image`).flush(
      new Blob(['{"code":"product_not_found"}'], { type: 'application/problem+json' }),
      { status: 404, statusText: 'Not Found' },
    );
    await fixture.whenStable();
    expect(host.querySelector('.placeholder')?.textContent).toContain('Product unavailable');
    expect(host.querySelector('[role=alert]')?.textContent).toContain('product is no longer available');
    expect(host.querySelector<HTMLInputElement>('input[type=file]')?.disabled).toBe(true);
  });

  it('captures the native file before clearing its input, creates it, and refreshes its ETag', async () => {
    const fixture = TestBed.createComponent(ProductImage);
    fixture.componentRef.setInput('product', product);
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    host.querySelector<HTMLButtonElement>('.thumbnail')?.click();
    http.expectOne(`${API_BASE_URL}/api/products/p1/image`).flush(
      new Blob(['{"code":"product_image_missing"}'], { type: 'application/problem+json' }),
      { status: 404, statusText: 'Not Found' },
    );
    await fixture.whenStable();
    const input = host.querySelector<HTMLInputElement>('input[type=file]');
    if (!input) throw new TypeError('Missing product image input');
    const file = new File(['image'], 'first.png', { type: 'image/png' });
    Object.defineProperty(input, 'files', { configurable: true, value: { item: (index: number) => index === 0 ? file : null } });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(input.value).toBe('');
    expect(host.querySelector('.selected-name')?.textContent).toContain('first.png');
    const save = Array.from(host.querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent?.trim() === 'Upload image');
    if (!save) throw new TypeError('Missing product image save button');
    save.click();
    const mutation = http.expectOne(`${API_BASE_URL}/api/products/p1/image`);
    expect(mutation.request.headers.has('If-Match')).toBe(false);
    expect(mutation.request.headers.get('If-None-Match')).toBe('*');
    if (!(mutation.request.body instanceof FormData)) throw new TypeError('Product image request must use FormData');
    const uploaded = mutation.request.body.get('file');
    if (!(uploaded instanceof File)) throw new TypeError('Product image request must contain a file');
    expect(uploaded.name).toBe(file.name);
    expect(uploaded.type).toBe(file.type);
    expect(uploaded.size).toBe(file.size);
    expect(await uploaded.text()).toBe('image');
    mutation.flush({}, { status: 201, statusText: 'Created', headers: { ETag: '"AAAAAAAAAAI="' } });
    http.expectOne(`${API_BASE_URL}/api/products/p1/image`).flush(
      new Blob(['pixels'], { type: 'image/png' }), { headers: { ETag: '"AAAAAAAAAAI="' } },
    );
    await fixture.whenStable();
    expect(host.querySelector('.selected-name')).toBeNull();
    expect(host.querySelector('[role=alert]')).toBeNull();
    expect(host.querySelector('img')).not.toBeNull();
  });
});
