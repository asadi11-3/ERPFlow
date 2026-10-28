import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, from, map, mergeMap, throwError } from 'rxjs';
import type { Observable } from 'rxjs';

import { API_BASE_URL } from '../config';

export interface ProductImageBytes {
  readonly blob: Blob;
  readonly version: string | null;
}

export class ProductImageContractError extends Error {
  constructor() { super('The image response could not be verified. Refresh before making changes.'); }
}

export function strongImageVersion(value: unknown): value is string {
  return typeof value === 'string' && /^"[A-Za-z0-9+/]{11}="$/.test(value);
}

@Injectable({ providedIn: 'root' })
export class ProductImages {
  private readonly http = inject(HttpClient);

  read(id: string): Observable<ProductImageBytes> {
    return this.http.get(this.path(id), { responseType: 'blob', observe: 'response', cache: 'no-store' }).pipe(
      map((response) => {
        const blob = response.body;
        if (!blob || !['image/jpeg', 'image/png', 'image/webp'].includes(blob.type))
          throw new ProductImageContractError();
        const version = response.headers.get('ETag');
        return { blob, version: strongImageVersion(version) ? version : null };
      }),
      catchError((error: unknown) => {
        if (!(error instanceof HttpErrorResponse) || !(error.error instanceof Blob))
          return throwError(() => error);
        return from(error.error.text()).pipe(mergeMap((text) => {
          let body: unknown;
          try { body = JSON.parse(text); }
          catch (parseError: unknown) {
            if (!(parseError instanceof SyntaxError)) return throwError(() => parseError);
            body = null;
          }
          return throwError(() => new HttpErrorResponse({
            status: error.status, statusText: error.statusText, headers: error.headers,
            url: error.url ?? undefined, error: body,
          }));
        }));
      }),
    );
  }

  upload(id: string, file: File, version: string | null): Observable<string> {
    const body = new FormData();
    body.append('file', file, file.name);
    const headers = version ? new HttpHeaders().set('If-Match', version) : new HttpHeaders().set('If-None-Match', '*');
    return this.http.put<unknown>(this.path(id), body, { headers, observe: 'response' }).pipe(
      map((response) => {
        const etag = response.headers.get('ETag');
        if (strongImageVersion(etag)) return etag;
        if (typeof response.body === 'object' && response.body !== null &&
            'version' in response.body && strongImageVersion(response.body.version)) return response.body.version;
        throw new ProductImageContractError();
      }),
    );
  }

  delete(id: string, version: string): Observable<void> {
    return this.http.delete<void>(this.path(id), { headers: { 'If-Match': version } });
  }

  private path(id: string): string { return `${API_BASE_URL}/api/products/${encodeURIComponent(id)}/image`; }
}
