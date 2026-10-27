import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../config';
import { Page } from '../api/models';
import { Warehouse, Supplier } from './models';

export type Query = Record<string, string | number | boolean>;

@Injectable({ providedIn: 'root' })
export class ProcurementApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/api`;

  get<T>(path: string, params: Query = {}): Promise<T> {
    return firstValueFrom(this.http.get<T>(`${this.base}/${path}`, { params }));
  }

  post<T>(path: string, body: unknown, key?: string): Promise<T> {
    const headers = key ? new HttpHeaders({ 'Idempotency-Key': key }) : undefined;
    return firstValueFrom(this.http.post<T>(`${this.base}/${path}`, body, { headers }));
  }

  put<T>(path: string, body: unknown): Promise<T> {
    return firstValueFrom(this.http.put<T>(`${this.base}/${path}`, body));
  }

  async warehouses(): Promise<Warehouse[]> {
    return this.allPages<Warehouse>('warehouses');
  }

  async suppliers(): Promise<Supplier[]> {
    return this.allPages<Supplier>('suppliers');
  }

  private async allPages<T>(path: string): Promise<T[]> {
    const rows: T[] = [];
    let page = 1;
    let pages: number;
    do {
      const result = await this.get<Page<T>>(path, { page, pageSize: 100 });
      rows.push(...result.items);
      pages = result.totalPages;
      page++;
    } while (page <= pages);
    return rows;
  }
}
