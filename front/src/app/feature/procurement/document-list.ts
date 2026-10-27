import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Page } from '../../core/api/models';
import { problemMessage } from '../../core/api/problem';
import { Session } from '../../core/auth/session';
import { DOCUMENT_STATUS, PO_STATUS, OperationalOrder, PurchaseOrder, Receipt, Transfer, Warehouse, localTimestamp } from '../../core/procurement/models';
import { ProcurementApi, Query } from '../../core/procurement/procurement-api';
import { Pagination } from '../../ui/pagination/pagination';
import { Select } from '../../ui/select/select';
import { RowMenu } from '../../ui/row-menu/row-menu';
import { MiddleText, RecordRow, RecordToggle } from '../../ui/table-record/table-record';

type Document = PurchaseOrder | OperationalOrder | Receipt | Transfer;
@Component({
  selector: 'app-document-list', imports: [RouterLink, DatePipe, DecimalPipe, Pagination, Select, RowMenu, MiddleText, RecordRow, RecordToggle],
  template: `
    <section class="page">
      <header class="page-head"><div><span class="eyebrow">{{ kind === 'purchase' ? 'Purchasing' : 'Warehouse operations' }}</span><h1>{{ title }}</h1><p>{{ description }}</p></div>
        @if (kind === 'purchase' && canCreate) { <a class="btn btn-primary" routerLink="/purchase-orders/new">New purchase order</a> }
        @if (kind === 'transfer') { <a class="btn btn-primary" routerLink="/stock-transfers/new">New transfer</a> }
        @if (kind === 'receipt') { <a class="btn btn-primary" routerLink="/receiving">Receive an order</a> }
      </header>
      @if (lookupError()) { <p class="alert" role="alert">{{ lookupError() }} Document identifiers are still available.</p> }
      <div class="card">
        <div class="toolbar filter-bar"><div class="field field-compact"><label for="document-status">Status</label><app-select inputId="document-status" [options]="statusOptions" [value]="status()" (valueChange)="status.set($event); page.set(1); load()" /></div>
          <div class="field field-size"><label for="document-size">Show</label><app-select inputId="document-size" [options]="sizeOptions" [value]="'' + pageSize()" (valueChange)="pageSize.set(+$event); page.set(1); load()" /></div>
          @if (kind === 'transfer') { <div class="field"><label for="document-warehouse">Warehouse</label><app-select inputId="document-warehouse" [options]="warehouseOptions()" [searchable]="true" [value]="warehouse()" (valueChange)="warehouse.set($event); page.set(1); load()" /></div> }
          <button class="btn btn-ghost" (click)="load()" [disabled]="loading()">Refresh</button>
        </div>
        @if (purchaseOrderId) { <p class="muted">Filtered to purchase order <span class="record-id">{{ purchaseOrderId }}</span>. <a routerLink="/goods-receipts">Show all receipts</a></p> }
        @if (error()) { <div class="alert" role="alert"><h2>Could not load {{ title.toLowerCase() }}</h2><p>{{ error() }}</p><button class="btn btn-ghost" (click)="load()">Try again</button></div> }
        @else if (loading()) { <div class="loading-lines" role="status" aria-label="Loading documents"><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span></div> }
        @else if (result(); as data) {
          @if (!data.items.length) { <div class="empty"><h2>No documents to show</h2><p>Try another status, or return when a document is ready for this step.</p></div> }
          @else { <div class="table-wrap wide"><table class="table"><thead><tr><th>Document</th><th>{{ kind === 'transfer' ? 'Route' : 'Warehouse' }}</th><th>Status</th><th>Created</th>@if (kind === 'purchase') { <th class="num">Total</th> }<th><span class="sr-only">Open document</span></th></tr></thead><tbody>
            @for (row of data.items; track row.id) { <tr appRecordRow><td class="main"><a class="row-link" [routerLink]="[base, row.id]"><app-middle-text [value]="number(row)" /></a></td><td class="span" data-label="Warehouse"><app-middle-text [value]="location(row)" /></td><td data-label="Status"><span class="status-chip" [class.pending]="kind === 'purchase' && row.status === 1">{{ statuses[row.status] ?? 'Unknown' }}</span></td><td class="muted span" data-label="Created" [title]="utc(row.createdAt) | date:'d MMM y, h:mm a'">{{ utc(row.createdAt) | date:'d MMM y' }}</td>@if (kind === 'purchase') { <td class="num" data-label="Total">{{ total(row) | number:'1.2-4' }}</td> }<td class="act"><app-row-menu [label]="'Actions for ' + number(row)"><a class="menu-item" role="menuitem" [routerLink]="[base, row.id]">Open document</a></app-row-menu></td><td class="record-disclosure"><app-record-toggle /></td></tr> }
          </tbody></table></div> }
          <div class="table-foot"><span>Showing {{ data.totalCount ? (page() - 1) * pageSize() + 1 : 0 }} to {{ Math.min(page() * pageSize(), data.totalCount) }} of {{ data.totalCount }} documents</span><app-pagination [page]="page()" (pageChange)="page.set($event); load()" [pages]="data.totalPages" /></div>
        }
      </div>
    </section>
  `,
})
export class DocumentList {
  private readonly api = inject(ProcurementApi);
  private readonly route = inject(ActivatedRoute);
  protected readonly kind = this.route.snapshot.data['kind'] as 'purchase' | 'receiving' | 'receipt' | 'transfer';
  protected readonly canCreate = ['Administrator', 'Purchasing'].includes(inject(Session).user()?.role ?? '');
  protected readonly title = { purchase: 'Purchase orders', receiving: 'Receiving', receipt: 'Goods receipts', transfer: 'Stock transfers' }[this.kind];
  protected readonly description = { purchase: 'Draft, review and track purchasing commitments.', receiving: 'Inspect order quantities before recording a delivery.', receipt: 'Track received quantities and their stock postings.', transfer: 'Move available stock between warehouse locations.' }[this.kind];
  protected readonly base = { purchase: '/purchase-orders', receiving: '/receiving', receipt: '/goods-receipts', transfer: '/stock-transfers' }[this.kind];
  private readonly path = this.kind === 'receiving' ? 'purchase-orders/operational' : this.base.slice(1);
  protected readonly statuses = this.kind === 'purchase' || this.kind === 'receiving' ? PO_STATUS : DOCUMENT_STATUS;
  protected readonly purchaseOrderId = this.kind === 'receipt' ? this.route.snapshot.queryParamMap.get('purchaseOrderId') : null;
  protected readonly status = signal('');
  protected readonly Math = Math;
  protected readonly pageSize = signal(10);
  protected readonly sizeOptions = [10, 25, 50].map(n => ({ value: '' + n, label: '' + n }));
  protected readonly statusOptions = [{ value: '', label: 'All permitted statuses' }, ...this.statuses.map((label, i) => ({ value: '' + i, label }))];
  protected readonly warehouse = signal('');
  protected readonly page = signal(1);
  protected readonly result = signal<Page<Document> | null>(null);
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly warehouseOptions = computed(() => [{ value: '', label: 'All warehouses' }, ...this.warehouses().map(w => ({ value: w.id, label: `${w.code} · ${w.name}` }))]);
  protected readonly error = signal('');
  protected readonly lookupError = signal('');
  protected readonly loading = signal(false);
  protected readonly utc = localTimestamp;
  private request = 0;
  constructor() { void this.api.warehouses().then(v => this.warehouses.set(v)).catch(e => this.lookupError.set(problemMessage(e))); void this.load(); }
  protected number(row: Document): string { return 'poNumber' in row ? row.poNumber : row.number; }
  protected total(row: Document): number | null { return 'totalAmount' in row ? row.totalAmount : null; }
  private warehouseName(id: string): string { return this.warehouses().find(w => w.id === id)?.name ?? id; }
  protected location(row: Document): string { return 'warehouseId' in row ? this.warehouseName(row.warehouseId) : `${this.warehouseName(row.sourceWarehouseId)} → ${this.warehouseName(row.destinationWarehouseId)}`; }
  protected async load(): Promise<void> {
    const request = ++this.request; this.loading.set(true); this.error.set('');
    const query: Query = { page: this.page(), pageSize: this.pageSize() };
    if (this.status()) query['status'] = this.status();
    if (this.warehouse()) query['warehouseId'] = this.warehouse();
    if (this.purchaseOrderId) query['purchaseOrderId'] = this.purchaseOrderId;
    try { const result = await this.api.get<Page<Document>>(this.path, query); if (request === this.request) this.result.set(result); }
    catch (e) { if (request === this.request) this.error.set(problemMessage(e)); }
    finally { if (request === this.request) this.loading.set(false); }
  }
}
