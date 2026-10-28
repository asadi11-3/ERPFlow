import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { vi } from 'vitest';

import { authInterceptor } from '../auth/interceptor';
import { Session } from '../auth/session';
import { API_BASE_URL } from '../config';
import { ProductImages } from './product-images';
import { imageFileError, productImageError } from './product-image-errors';

describe('Product image HTTP contract', () => {
  const path = `${API_BASE_URL}/api/products/p1/image`;
  const version = '"AAAAAAAAAAE="';
  const expire = vi.fn();
  let api: ProductImages;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(),
      { provide: Session, useValue: { token: signal('test-bearer'), expire } },
    ] });
    api = TestBed.inject(ProductImages);
    http = TestBed.inject(HttpTestingController);
    expire.mockClear();
  });
  afterEach(() => http.verify());

  it('retrieves protected bytes with their exact quoted ETag', async () => {
    const result = firstValueFrom(api.read('p1'));
    const request = http.expectOne(path);
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
    expect(request.request.responseType).toBe('blob');
    const blob = new Blob(['png'], { type: 'image/png' });
    request.flush(blob, { headers: { ETag: version } });
    expect(await result).toEqual({ blob, version });
  });

  it('sends exactly one real file and lets the browser set the multipart boundary', async () => {
    const file = new File(['png'], 'photo.png', { type: 'image/png' });
    const result = firstValueFrom(api.upload('p1', file, null));
    const request = http.expectOne(path);
    expect(request.request.body instanceof FormData).toBe(true);
    expect(request.request.body.get('file')).toMatchObject({ name: 'photo.png', size: 3, type: 'image/png' });
    expect([...request.request.body.keys()]).toEqual(['file']);
    expect(request.request.headers.has('Content-Type')).toBe(false);
    expect(request.request.headers.get('If-None-Match')).toBe('*');
    expect(request.request.headers.has('If-Match')).toBe(false);
    request.flush({ version }, { status: 201, statusText: 'Created', headers: { ETag: version } });
    expect(await result).toBe(version);
  });

  it('preserves exact ETag on replace and never retries conflicting writes', async () => {
    const result = firstValueFrom(api.upload('p1', new File(['png'], 'photo.png'), version));
    const failed = expect(result).rejects.toMatchObject({ status: 412 });
    const request = http.expectOne(path);
    expect(request.request.headers.get('If-Match')).toBe(version);
    expect(request.request.headers.has('If-None-Match')).toBe(false);
    request.flush({}, { status: 412, statusText: 'Precondition Failed' });
    await failed;
    http.expectNone(path);
  });

  it('deletes with the exact image validator', async () => {
    const result = firstValueFrom(api.delete('p1', version));
    const request = http.expectOne(path);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.headers.get('If-Match')).toBe(version);
    request.flush(null, { status: 204, statusText: 'No Content' });
    await result;
  });

  it('expires the existing session when image authentication fails', async () => {
    const result = firstValueFrom(api.read('p1'));
    const failed = expect(result).rejects.toMatchObject({ status: 401 });
    http.expectOne(path).flush(null, { status: 401, statusText: 'Unauthorized' });
    await failed;
    expect(expire).toHaveBeenCalledOnce();
  });

  it('parses problem details returned as a Blob', async () => {
    const result = firstValueFrom(api.read('p1'));
    const failed = expect(result).rejects.toMatchObject({ status: 403, error: { detail: 'Denied' } });
    http.expectOne(path).flush(new Blob(['{"detail":"Denied"}'], { type: 'application/problem+json' }),
      { status: 403, statusText: 'Forbidden' });
    await failed;
  });

  it('allows read-only display but withholds mutations when CORS hides ETag', async () => {
    const result = firstValueFrom(api.read('p1'));
    http.expectOne(path).flush(new Blob(['png'], { type: 'image/png' }));
    expect((await result).version).toBeNull();
  });
});

describe('Product image file validation', () => {
  it.each([['jpg', 'image/jpeg'], ['jpeg', 'image/jpeg'], ['png', 'image/png'], ['webp', 'image/webp']])(
    'accepts a nonempty supported %s file', (extension, type) => {
      expect(imageFileError(new File(['image'], `image.${extension}`, { type }))).toBeNull();
    });
  it('rejects empty, oversized and mismatched declarations before upload', () => {
    expect(imageFileError(new File([], 'image.png', { type: 'image/png' }))).not.toBeNull();
    expect(imageFileError(new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'image.png', { type: 'image/png' }))).not.toBeNull();
    expect(imageFileError(new File(['image'], 'image.svg', { type: 'image/svg+xml' }))).not.toBeNull();
    expect(imageFileError(new File(['image'], 'image.png', { type: 'image/jpeg' }))).not.toBeNull();
  });
  it('accepts the exact 5 MiB boundary', () => {
    expect(imageFileError(new File([new Uint8Array(5 * 1024 * 1024)], 'image.png', { type: 'image/png' }))).toBeNull();
  });
  it('handles unexpected errors without exposing technical details', () => {
    expect(productImageError(new TypeError('private stack'))).not.toContain('private stack');
  });
});
