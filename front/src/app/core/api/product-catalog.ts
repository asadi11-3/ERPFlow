import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { Observable } from 'rxjs';

import { API_BASE_URL } from '../config';
import type { Product } from './models';

export interface CatalogChange {
  readonly id: string;
  readonly actorId: string;
  readonly actorName: string;
  readonly action: string;
  readonly previousName: string | null;
  readonly name: string;
  readonly previousUnitPrice: number | null;
  readonly unitPrice: number;
  readonly previousIsActive: boolean | null;
  readonly isActive: boolean;
  readonly createdAt: string;
}

export interface CatalogProduct extends Omit<Product, 'availableStock'> {
  readonly isActive: boolean;
  readonly rowVersion: string;
  readonly createdAt: string;
  readonly history: readonly CatalogChange[];
}

export interface CatalogDraft {
  readonly name: string;
  readonly sku: string;
  readonly unitPrice: number;
}

export function catalogValidation(draft: CatalogDraft): string | null {
  if (!draft.name.trim() || draft.name.trim().length > 200) return 'Enter a product name of 1–200 characters.';
  if (!draft.sku.trim() || draft.sku.trim().length > 64 || Array.from(draft.sku).some(char => char.charCodeAt(0) < 32 || char.charCodeAt(0) === 127))
    return 'Enter a SKU of 1–64 characters without control characters.';
  if (!Number.isFinite(draft.unitPrice) || draft.unitPrice < 0 || draft.unitPrice > 9999999999999.99 ||
      !/^\d+(?:\.\d{1,2})?$/.test(String(draft.unitPrice)))
    return 'Enter a nonnegative unit price with at most two decimal places.';
  return null;
}

@Injectable({ providedIn: 'root' })
export class ProductCatalog {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/api/products`;

  get(id: string): Observable<CatalogProduct> {
    return this.http.get<CatalogProduct>(`${this.base}/${encodeURIComponent(id)}`);
  }
  create(draft: CatalogDraft): Observable<CatalogProduct> {
    return this.http.post<CatalogProduct>(this.base, { name: draft.name.trim(), sku: draft.sku.trim(), unitPrice: draft.unitPrice });
  }
  update(product: CatalogProduct, draft: CatalogDraft): Observable<CatalogProduct> {
    return this.http.put<CatalogProduct>(`${this.base}/${encodeURIComponent(product.id)}`, {
      name: draft.name.trim(), sku: draft.sku.trim(), unitPrice: draft.unitPrice, rowVersion: product.rowVersion,
    });
  }
  setActive(product: CatalogProduct): Observable<CatalogProduct> {
    return this.http.post<CatalogProduct>(`${this.base}/${encodeURIComponent(product.id)}/${product.isActive ? 'deactivate' : 'activate'}`,
      { rowVersion: product.rowVersion });
  }
}
