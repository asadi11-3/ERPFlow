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
    Object.defineProperties(HTMLDialogElement.prototype, {
      showModal: { configurable: true, value: function (this: HTMLDialogElement) { this.open = true; } },
      close: { configurable: true, value: function (this: HTMLDialogElement) { this.open = false; } },
    });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: Session, useValue: { user, token: signal('token') } }] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify(); vi.restoreAllMocks();
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

  it('opens preview, reconciles a missing image, and closes with Escape', async () => {
    const fixture = TestBed.createComponent(ProductImage);
    fixture.componentRef.setInput('product', product);
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;
    host.querySelector<HTMLButtonElement>('.thumbnail')?.click();
    await fixture.whenStable();
    http.expectOne(`${API_BASE_URL}/api/products/p1/image`).flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    const dialog = host.querySelector('dialog');
    expect(dialog?.open).toBe(true);
    dialog?.dispatchEvent(new Event('cancel', { cancelable: true }));
    await fixture.whenStable();
    expect(dialog?.open).toBe(false);
  });
});
