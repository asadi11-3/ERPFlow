import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import { Session } from '../auth/session';
import type { Role } from '../auth/session';
import { API_BASE_URL } from '../config';
import { ProductImageState } from './product-image-state';

describe('Product image lifecycle state', () => {
  const path = `${API_BASE_URL}/api/products/p1/image`;
  const version = '"AAAAAAAAAAE="';
  const nextVersion = '"AAAAAAAAAAI="';
  const product = { id: 'p1', name: 'Scanner', sku: 'SCAN', unitPrice: 20, availableStock: 5,
    hasImage: true, imageVersion: version };
  const token = signal<string | null>('token');
  const user = signal<{ role: Role } | null>({ role: 'Administrator' });
  const revoke = vi.fn();
  const create = vi.fn();
  let state: ProductImageState;
  let http: HttpTestingController;

  beforeEach(() => {
    token.set('token'); user.set({ role: 'Administrator' });
    let number = 0;
    create.mockImplementation(() => `blob:image-${++number}`);
    revoke.mockClear();
    vi.stubGlobal('URL', class extends URL {
      static override createObjectURL = create;
      static override revokeObjectURL = revoke;
    });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), ProductImageState,
      { provide: Session, useValue: { token, user } }] });
    state = TestBed.inject(ProductImageState);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); vi.unstubAllGlobals(); });

  function read(versionValue = version): void {
    http.expectOne(path).flush(new Blob(['pixels'], { type: 'image/png' }), { headers: { ETag: versionValue } });
  }

  it('loads thumbnails only for products with images', () => {
    state.connect({ ...product, hasImage: false, imageVersion: null });
    http.expectNone(path);
    expect(state.url()).toBeNull();
  });

  it('revokes superseded URLs before publishing refreshed bytes', () => {
    state.connect(product); read();
    const previous = state.url();
    state.refresh();
    expect(revoke).toHaveBeenCalledWith(previous);
    expect(state.url()).toBeNull();
    read(nextVersion);
    expect(state.version()).toBe(nextVersion);
    expect(state.url()).not.toBe(previous);
  });

  it('cancels stale reads when reference metadata reports deletion', () => {
    state.connect(product);
    const stale = http.expectOne(path);
    state.connect({ ...product, hasImage: false, imageVersion: null });
    expect(stale.cancelled).toBe(true);
    expect(state.url()).toBeNull();
    expect(state.exists()).toBe(false);
  });

  it('updates the current ETag and retrieves normalized bytes after replacement', () => {
    state.connect(product); read();
    state.select(new File(['image'], 'new.png', { type: 'image/png' }));
    const local = state.selectionUrl();
    state.upload();
    const request = http.expectOne(path);
    expect(request.request.headers.get('If-Match')).toBe(version);
    request.flush({ version: nextVersion }, { headers: { ETag: nextVersion } });
    expect(revoke).toHaveBeenCalledWith(local);
    expect(state.selectionUrl()).toBeNull();
    expect(state.url()).toBeNull();
    read(nextVersion);
    expect(state.version()).toBe(nextVersion);
    expect(state.busy()).toBe(false);
  });

  it('clears deleted bytes and association immediately without a blind follow-up write', () => {
    state.connect(product); read();
    const old = state.url();
    const changes = vi.fn(); state.changed.subscribe(changes);
    state.delete();
    http.expectOne(path).flush(null, { status: 204, statusText: 'No Content' });
    expect(state.url()).toBeNull();
    expect(state.exists()).toBe(false);
    expect(state.version()).toBeNull();
    expect(revoke).toHaveBeenCalledWith(old);
    expect(changes).toHaveBeenCalledWith({ id: 'p1', version: null });
    http.expectNone(path);
  });

  it.each([412, 428, 404, 403, 503, 0])('reconciles status %i without automatically repeating the mutation', (status) => {
    state.connect(product); read();
    state.select(new File(['image'], 'new.png', { type: 'image/png' }));
    state.upload();
    const mutation = http.expectOne(path);
    if (status === 0) mutation.error(new ProgressEvent('error'));
    else mutation.flush({}, { status, statusText: 'Image error' });
    expect(state.error()).not.toBe('');
    expect(state.selectionUrl()).toBeNull();
    const refresh = http.expectOne(path);
    expect(refresh.request.method).toBe('GET');
    refresh.flush(new Blob(['new image'], { type: 'image/png' }), { headers: { ETag: nextVersion } });
    expect(state.version()).toBe(nextVersion);
    expect(state.error()).not.toBe('');
    http.expectNone(path);
  });

  it.each([400, 413, 415])('retains the selection and shows validation for status %i', (status) => {
    state.connect(product); read();
    state.select(new File(['image'], 'new.png', { type: 'image/png' }));
    state.upload();
    http.expectOne(path).flush({ detail: 'Invalid file' }, { status, statusText: 'Invalid' });
    expect(state.error()).not.toBe('');
    expect(state.selectionUrl()).not.toBeNull();
    expect(state.busy()).toBe(false);
    http.expectNone(path);
  });

  it('clears absent images after a 404 read', () => {
    state.connect(product);
    http.expectOne(path).flush(null, { status: 404, statusText: 'Not Found' });
    expect(state.exists()).toBe(false);
    expect(state.url()).toBeNull();
    expect(state.version()).toBeNull();
  });

  it('prevents mutations by read-only users and duplicate submissions while busy', () => {
    state.connect(product); read();
    state.select(new File(['image'], 'new.png', { type: 'image/png' }));
    user.set({ role: 'Sales' });
    state.upload(); state.delete();
    http.expectNone(path);
    user.set({ role: 'Administrator' });
    state.upload(); state.upload(); state.delete();
    http.expectOne(path).flush({ version: nextVersion }, { headers: { ETag: nextVersion } });
    read(nextVersion);
  });

  it('revokes current and selected URLs on session change and rejects old reads', () => {
    state.connect(product); read();
    const current = state.url();
    state.select(new File(['image'], 'new.png', { type: 'image/png' }));
    const selected = state.selectionUrl();
    state.refresh();
    const request = http.expectOne(path);
    token.set(null); user.set(null); TestBed.tick();
    expect(request.cancelled).toBe(true);
    expect(revoke).toHaveBeenCalledWith(current);
    expect(revoke).toHaveBeenCalledWith(selected);
    expect(state.url()).toBeNull();
    expect(state.selectionUrl()).toBeNull();
  });

  it('revokes all object URLs when its component scope is destroyed', () => {
    state.connect(product); read();
    const current = state.url();
    state.select(new File(['image'], 'new.png', { type: 'image/png' }));
    const selected = state.selectionUrl();
    TestBed.resetTestingModule();
    expect(revoke).toHaveBeenCalledWith(current);
    expect(revoke).toHaveBeenCalledWith(selected);
  });
});
