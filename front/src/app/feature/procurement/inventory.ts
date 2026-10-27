import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Page } from '../../core/api/models';
import { Reference } from '../../core/api/reference';
import { problemMessage } from '../../core/api/problem';
import { Movement, Stock, Warehouse, localTimestamp } from '../../core/procurement/models';
import { ProcurementApi, Query } from '../../core/procurement/procurement-api';
import { Pagination } from '../../ui/pagination/pagination';
import { Select } from '../../ui/select/select';
import { RowMenu } from '../../ui/row-menu/row-menu';
import { MiddleText, RecordRow, RecordToggle } from '../../ui/table-record/table-record';

@Component({
  selector: 'app-inventory', imports: [DatePipe, Pagination, Select, RowMenu, MiddleText, RecordRow, RecordToggle],
  template: `
    <section class="page"><header class="page-head"><div><span class="eyebrow">Warehouse operations</span><h1>Inventory</h1><p>Warehouse balances and the movements behind them.</p></div></header>
      <nav class="tabs" aria-label="Inventory views"><button type="button" [class.active]="tab() === 'stocks'" [attr.aria-current]="tab() === 'stocks' ? 'page' : null" (click)="switchTab('stocks')">Stock balances</button><button type="button" [class.active]="tab() === 'movements'" [attr.aria-current]="tab() === 'movements' ? 'page' : null" (click)="switchTab('movements')">Movement ledger</button></nav>
      <div class="card stack"><form class="toolbar filter-bar" (submit)="$event.preventDefault(); page.set(1); load()"><div class="field"><label for="inventory-warehouse">Warehouse</label><app-select inputId="inventory-warehouse" [searchable]="true" [options]="warehouseOptions()" [(value)]="warehouse" /></div><div class="field"><label for="inventory-product">Product</label><app-select inputId="inventory-product" [searchable]="true" [options]="productOptions()" [(value)]="product" /></div>
        @if (tab() === 'movements') { <div class="field"><label for="movement-from">From (local time)</label><input id="movement-from" type="datetime-local" [value]="from()" (input)="from.set($any($event.target).value)"></div><div class="field"><label for="movement-to">To (local time)</label><input id="movement-to" type="datetime-local" [value]="to()" (input)="to.set($any($event.target).value)"></div> }<div class="field field-size"><label for="inventory-size">Show</label><app-select inputId="inventory-size" [options]="sizeOptions" [value]="'' + pageSize()" (valueChange)="pageSize.set(+$event); page.set(1); load()" /></div><button class="btn btn-primary" [disabled]="loading()">Apply filters</button></form>
        @if (lookupError()) { <p class="alert" role="alert">{{ lookupError() }}</p> }
        @if (error()) { <div class="alert" role="alert"><h2>Inventory unavailable</h2><p>{{ error() }}</p><p>Balances are not shown as zero when inventory is unavailable.</p><button class="btn btn-ghost" (click)="load()">Retry</button></div> }
        @else if (loading()) { <div class="loading-lines" role="status" aria-label="Loading inventory"><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span></div> }
        @else {
          @if (tab() === 'stocks') { @if (!stocks().length) { <div class="empty"><h2>No matching balances</h2><p>Change the warehouse or product filter.</p></div> } @else { <div class="table-wrap"><table class="table"><thead><tr><th>Product</th><th>Warehouse</th><th class="num">On hand</th><th class="num">Reserved</th><th class="num">Available</th><th><span class="sr-only">Ledger</span></th></tr></thead><tbody>@for (s of stocks(); track s.id) {
            <tr appRecordRow><td class="main"><app-middle-text [value]="s.productName" /><small class="cell-sub"><app-middle-text [value]="s.productSku" /></small></td><td data-label="Warehouse"><app-middle-text [value]="s.warehouseCode" /></td><td class="num" data-label="On hand">{{ s.stockOnHand }}</td><td class="num" data-label="Reserved">{{ s.reservedQuantity }}</td><td class="num" data-label="Available">{{ s.availableStock }}</td><td class="act"><app-row-menu><button class="menu-item" role="menuitem" (click)="warehouse.set(s.warehouseId); product.set(s.productId); switchTab('movements')">View movements</button></app-row-menu></td><td class="record-disclosure"><app-record-toggle /></td></tr>
          }</tbody></table></div> } }
          @else { <p class="muted">Physical and reserved changes are separate. Sequence numbers are per warehouse/product balance.</p>@if (!movements().length) { <div class="empty"><h2>No matching movements</h2><p>Broaden the date range or change filters.</p></div> } @else { <div class="table-wrap"><table class="table"><thead><tr><th>Product / warehouse</th><th>When</th><th class="num">Physical Δ</th><th class="num">Reserved Δ</th><th>After</th><th>Source</th></tr></thead><tbody>@for (m of movements(); track m.id) {
            <tr appRecordRow><td class="main"><app-middle-text [value]="ref.productName(m.productId)" /><small class="cell-sub"><app-middle-text [value]="warehouseName(m.warehouseId)" /> · #{{ m.sequence }}</small></td><td data-label="When">{{ utc(m.createdAt) | date:'medium' }}</td><td class="num" data-label="Physical change">{{ m.physicalDelta > 0 ? '+' : '' }}{{ m.physicalDelta }}</td><td class="num" data-label="Reserved change">{{ m.reservedDelta > 0 ? '+' : '' }}{{ m.reservedDelta }}</td><td data-label="After">{{ m.afterOnHand }} on hand / {{ m.afterReserved }} reserved<small class="cell-sub">Before: {{ m.beforeOnHand }} / {{ m.beforeReserved }}</small></td><td data-label="Source"><app-middle-text [value]="m.sourceType" /><small class="cell-sub"><app-middle-text [value]="m.sourceNumber || m.sourceDocumentId" /></small><small class="cell-sub">Actor: <app-middle-text [value]="m.actorId" /></small></td><td class="record-disclosure"><app-record-toggle /></td></tr>
          }</tbody></table></div> } }
          <div class="table-foot"><span>Showing {{ total() ? (page() - 1) * pageSize() + 1 : 0 }}–{{ math.min(page() * pageSize(), total()) }} of {{ total() }}</span><app-pagination [page]="page()" (pageChange)="page.set($event); load()" [pages]="pages()" /></div>
        }
      </div>
    </section>
  `,
})
export class Inventory {
  protected readonly math = Math;
  protected readonly pageSize = signal(10);
  protected readonly sizeOptions = [10, 25, 50].map(value => ({ value: String(value), label: String(value) }));
  protected readonly warehouseOptions = computed(() => [{ value: '', label: 'All warehouses' }, ...this.warehouses().map(w => ({ value: w.id, label: `${w.code} · ${w.name}` }))]);
  protected readonly productOptions = computed(() => [{ value: '', label: 'All products' }, ...this.ref.products().map(p => ({ value: p.id, label: `${p.sku} · ${p.name}` }))]);
  private readonly api = inject(ProcurementApi);
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;
  protected readonly ref = inject(Reference);
  protected readonly tab = signal(this.query.get('tab') === 'movements' ? 'movements' : 'stocks');
  protected readonly warehouse = signal(this.query.get('warehouseId') ?? '');
  protected readonly product = signal(this.query.get('productId') ?? '');
  protected readonly from = signal(''); protected readonly to = signal('');
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly stocks = signal<Stock[]>([]);
  protected readonly movements = signal<Movement[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal(''); protected readonly lookupError = signal('');
  protected readonly page = signal(1); protected readonly pages = signal(0); protected readonly total = signal(0);
  protected readonly utc = localTimestamp;
  private request = 0;
  constructor() { void this.api.warehouses().then(w => this.warehouses.set(w)).catch(e => this.lookupError.set(problemMessage(e))); void this.load(); }
  protected warehouseName(id: string): string { return this.warehouses().find(w => w.id === id)?.code ?? id; }
  protected switchTab(tab: string): void { this.tab.set(tab); this.page.set(1); void this.load(); }
  protected async load(): Promise<void> {
    const request = ++this.request;
    this.error.set(''); const query: Query = { page: this.page(), pageSize: this.pageSize() };
    if (this.warehouse()) query['warehouseId'] = this.warehouse(); if (this.product()) query['productId'] = this.product();
    if (this.tab() === 'movements') {
      const from = this.from() ? new Date(this.from()) : null, to = this.to() ? new Date(this.to()) : null;
      if ((from && !Number.isFinite(+from)) || (to && !Number.isFinite(+to)) || (from && to && from >= to)) { this.loading.set(false); this.error.set('Choose a valid date range with From earlier than To.'); return; }
      if (from) query['from'] = from.toISOString(); if (to) query['to'] = to.toISOString();
    }
    this.loading.set(true); const tab = this.tab();
    try {
      const data = await this.api.get<Page<Stock> | Page<Movement>>(`inventory/${tab}`, query);
      if (request !== this.request) return;
      if (tab === 'stocks') this.stocks.set(data.items as Stock[]); else this.movements.set(data.items as Movement[]);
      this.total.set(data.totalCount); this.pages.set(data.totalPages);
    } catch (e) { if (request === this.request) this.error.set(problemMessage(e)); }
    finally { if (request === this.request) this.loading.set(false); }
  }
}
