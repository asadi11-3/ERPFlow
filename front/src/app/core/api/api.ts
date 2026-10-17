import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, retry, timer } from 'rxjs';

import { API_BASE_URL } from '../config';
import {
  Customer,
  HistoryEntry,
  NewItem,
  OrderStatus,
  Page,
  Product,
  SalesOrder,
} from './models';

const BASE = `${API_BASE_URL}/api`;
const TRANSIENT = new Set([0, 502, 503, 504]);

function utc(value: string | null): string | null {
  if (!value) return value;
  return /(Z|[+-]\d\d:?\d\d)$/.test(value) ? value : `${value}Z`;
}

function fixOrder(order: SalesOrder): SalesOrder {
  return {
    ...order,
    createdAt: utc(order.createdAt) as string,
    submittedAt: utc(order.submittedAt),
    decidedAt: utc(order.decidedAt),
    reservations: order.reservations.map((r) => ({
      ...r,
      createdAt: utc(r.createdAt) as string,
      releasedAt: utc(r.releasedAt),
    })),
  };
}

function retryReads<T>() {
  return retry<T>({
    count: 2,
    delay: (error, attempt) =>
      error instanceof HttpErrorResponse && TRANSIENT.has(error.status)
        ? timer(300 * 2 ** (attempt - 1))
        : (() => {
            throw error;
          })(),
  });
}

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  customers(): Observable<Customer[]> {
    return this.http.get<Customer[]>(`${BASE}/reference/customers`).pipe(retryReads());
  }

  products(): Observable<Product[]> {
    return this.http.get<Product[]>(`${BASE}/reference/products`).pipe(retryReads());
  }

  orders(page: number, pageSize: number, status?: OrderStatus): Observable<Page<SalesOrder>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (status !== undefined) params = params.set('status', status);
    return this.http.get<Page<SalesOrder>>(`${BASE}/sales-orders`, { params }).pipe(
      retryReads(),
      map((result) => ({ ...result, items: result.items.map(fixOrder) })),
    );
  }

  order(id: string): Observable<SalesOrder> {
    return this.http.get<SalesOrder>(`${BASE}/sales-orders/${id}`).pipe(retryReads(), map(fixOrder));
  }

  history(id: string): Observable<HistoryEntry[]> {
    return this.http.get<HistoryEntry[]>(`${BASE}/sales-orders/${id}/history`).pipe(
      retryReads(),
      map((entries) => entries.map((e) => ({ ...e, timestampUtc: utc(e.timestampUtc) as string }))),
    );
  }

  pending(): Observable<SalesOrder[]> {
    return this.http.get<SalesOrder[]>(`${BASE}/sales-orders/pending`).pipe(
      retryReads(),
      map((orders) => orders.map(fixOrder)),
    );
  }

  create(customerId: string, items: NewItem[]): Observable<SalesOrder> {
    return this.http.post<SalesOrder>(`${BASE}/sales-orders`, { customerId, items }).pipe(map(fixOrder));
  }

  submit(id: string): Observable<SalesOrder> {
    return this.http.post<SalesOrder>(`${BASE}/sales-orders/${id}/submit`, null).pipe(map(fixOrder));
  }

  approve(id: string, comments: string | null): Observable<SalesOrder> {
    return this.http.post<SalesOrder>(`${BASE}/sales-orders/${id}/approve`, { comments }).pipe(map(fixOrder));
  }

  reject(id: string, reason: string): Observable<SalesOrder> {
    return this.http.post<SalesOrder>(`${BASE}/sales-orders/${id}/reject`, { reason }).pipe(map(fixOrder));
  }
}
