import { CurrencyPipe } from '@angular/common';
import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { Reference } from '../../core/api/reference';
import { ProductImage } from '../../pattern/product-image/product-image';
import { Session } from '../../core/auth/session';
import { Avatar } from '../../ui/avatar/avatar';
import { Icon } from '../../ui/icon/icon';
import { Pagination } from '../../ui/pagination/pagination';
import { Pill } from '../../ui/pill/pill';
import { Select, SelectOption } from '../../ui/select/select';
import { MiddleText } from '../../ui/table-record/table-record';

type Tab = 'customers' | 'products';

@Component({
  selector: 'app-reference-view',
  imports: [CurrencyPipe, Avatar, Icon, Pagination, Pill, Select, MiddleText, ProductImage, RouterLink],
  template: `
    <section class="page reference-page">
      <header class="page-head">
        <div>
          <span class="eyebrow">Reference data</span>
          <h1>{{ tab() === 'customers' ? 'Customers' : 'Products' }}</h1>
          <p>{{ tab() === 'customers' ? 'Active customers available for sales orders.' : 'Catalog products, prices, and available stock.' }}</p>
        </div>
        @if (administrator() && tab() === 'products') { <a class="btn btn-primary" routerLink="/reference/products/new">Add product</a> }
      </header>

      <div class="tabs" role="group" aria-label="Reference lists">
          <button type="button" class="tab" [attr.aria-pressed]="tab() === 'customers'" (click)="setTab('customers')">
            Customers ({{ ref.customers().length }})
          </button>
          <button type="button" class="tab" [attr.aria-pressed]="tab() === 'products'" (click)="setTab('products')">
            Products ({{ ref.products().length }})
          </button>
      </div>

      <div class="card reference-list">
        <div class="list-heading">
          <div><h2>{{ tab() === 'customers' ? 'Customer directory' : 'Product catalog' }}</h2>
            <p class="muted">{{ tab() === 'customers' ? 'Active customers only' : 'Current catalog and stock availability' }}</p></div>
          <span class="list-count">{{ total() }} {{ tab() }}</span>
        </div>
        <div class="reference-filters">
          <div class="field search">
            <label for="q">Search {{ tab() }}</label>
            <input
              id="q"
              type="search"
              [placeholder]="tab() === 'customers' ? 'Name or email' : 'SKU or product name'"
              [value]="query()"
              (input)="setQuery($any($event.target).value)"
            />
          </div>
          @if (tab() === 'products') { <div class="field"><label for="product-availability">Availability</label><app-select inputId="product-availability" ariaLabel="Product availability" [options]="availabilityOptions" [value]="availability()" (valueChange)="availability.set($event)" /></div> }
          <div class="field page-size"><label for="size">Rows per page</label><app-select inputId="size" ariaLabel="Rows per page" [options]="sizes" [value]="'' + pageSize()" (valueChange)="setSize($event)" /></div>
        </div>

        @if (ref.failed()) {
          <div class="alert" role="alert">
            <app-icon name="alert" />
            <p>Could not load reference data. <button type="button" class="btn btn-ghost" (click)="ref.reload()">Retry</button></p>
          </div>
        } @else if (ref.loading()) {
          <div role="status" aria-label="Loading reference data">
            @for (n of skeleton; track n) {
              <div class="skel-row" aria-hidden="true">
                <span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span>
              </div>
            }
          </div>
        } @else if (total() === 0) {
          <p class="empty">{{ query().trim() || (tab() === 'products' && availability() !== 'all') ? 'No matching ' + tab() + '.' : tab() === 'customers' ? 'No active customers available.' : 'No products in the catalog.' }}</p>
        } @else {
          <div class="table-wrap reference-table">
            @if (tab() === 'customers') {
              <table class="table">
                <caption class="sr-only">Active customers</caption>
                <thead>
                  <tr>
                    <th>Customer</th>
                    <th>Email</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  @for (c of customerRows(); track c.id) {
                    <tr>
                      <td class="main"><span class="cell-customer"><app-avatar [name]="c.name" /><app-middle-text [value]="c.name" /></span></td>
                      <td class="muted span" data-label="Email"><app-middle-text [value]="c.email" /></td>
                      <td class="span" data-label="Status"><app-pill label="Active" tone="green" /></td>
                    </tr>
                  }
                </tbody>
              </table>
            } @else {
              <table class="table">
                <caption class="sr-only">Catalog products</caption>
                <thead>
                  <tr>
                    <th>SKU</th>
                    <th>Product</th>
                    <th class="num">Unit price</th>
                    <th class="num">Available</th>
                    <th>Status</th><th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  @for (p of productRows(); track p.id) {
                    <tr>
                      <td class="muted" data-label="SKU"><app-middle-text [value]="p.sku" /></td>
                       <td class="main"><span class="product-identity"><app-product-image [product]="p" (imageChanged)="ref.updateProductImage($event.id, $event.version)" /><a class="text-link product-name" [routerLink]="['/reference/products', p.id]" [title]="p.name">{{ p.name }}</a></span></td>
                      <td class="num" data-label="Unit price">{{ p.unitPrice | currency }}</td>
                      <td class="num" data-label="Available">
                        <app-pill [label]="'' + p.availableStock" [tone]="p.availableStock === 0 ? 'red' : 'green'" />
                      </td>
                      <td data-label="Status"><app-pill [label]="p.isActive === false ? 'Inactive' : 'Active'" [tone]="p.isActive === false ? 'gray' : 'green'" /></td>
                      <td data-label="Actions"><a class="btn btn-ghost" [routerLink]="administrator() ? ['/reference/products', p.id, 'edit'] : ['/reference/products', p.id]">{{ administrator() ? 'Edit product' : 'View details' }}</a></td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </div>
          <div class="table-foot">
            <span>Showing {{ from() }} to {{ to() }} of {{ total() }} {{ tab() }}.</span>
            <app-pagination [(page)]="page" [pages]="pages()" />
          </div>
        }
      </div>
    </section>
  `,
  styles: `
    .reference-page { max-width:1440px; }
    .reference-page .tabs { margin-bottom:0; }
    .reference-page .tab[aria-pressed='true'] { color:var(--primary); border-bottom-color:var(--primary); }
    .list-heading { display:flex; align-items:start; justify-content:space-between; flex-wrap:wrap; gap:8px 16px; padding-bottom:16px; border-bottom:1px solid var(--line); }
    .list-heading h2 { margin:0; } .list-heading p { margin-top:2px; font-size:.875rem; }
    .list-count { padding:5px 10px; border-radius:8px; background:var(--tint); color:var(--ink); font-size:.8125rem; font-weight:600; white-space:nowrap; }
    .reference-filters { display:grid; grid-template-columns:minmax(0,1fr) minmax(160px,200px) 140px; align-items:end; gap:12px; padding:16px 0; }
    .reference-filters:has(> .field:nth-child(2):last-child) { grid-template-columns:minmax(0,1fr) 140px; }
    .reference-filters .field { min-width:0; } .reference-filters app-select { min-width:0; }
    .product-identity { display: flex; align-items: center; gap: 12px; min-width: 0; }
    .product-name { min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
    .reference-list .table td { min-width:0; }
    .reference-list .table td.main { font-weight:600; }
    .reference-list .table-foot { padding-top:4px; }
    @media(max-width:700px) { .reference-filters { grid-template-columns:repeat(2,minmax(0,1fr)); } .reference-filters:has(> .field:nth-child(2):last-child) { grid-template-columns:minmax(0,1fr) 140px; } .reference-filters .search { grid-column:1/-1; } }
    @media(max-width:420px) { .reference-filters, .reference-filters:has(> .field:nth-child(2):last-child) { grid-template-columns:minmax(0,1fr); } .reference-filters .search { grid-column:auto; } }
  `,
})
export class ReferenceView {
  protected readonly ref = inject(Reference);
  private readonly session = inject(Session);
  protected readonly administrator = computed(() => this.session.user()?.role === 'Administrator');
  protected readonly availability = signal('all');
  protected readonly availabilityOptions = [{ value: 'all', label: 'All products' }, { value: 'active', label: 'Active' }, { value: 'inactive', label: 'Inactive' }];

  protected readonly sizes: SelectOption[] = [10, 25, 50].map((n) => ({ value: '' + n, label: '' + n }));
  protected readonly skeleton = [1, 2, 3, 4, 5];
  protected readonly tab = signal<Tab>(inject(ActivatedRoute).snapshot.queryParamMap.get('tab') === 'products' ? 'products' : 'customers');
  protected readonly query = signal('');
  protected readonly pageSize = signal(10);
  protected readonly page = linkedSignal<unknown, number>({ source: () => [this.tab(), this.query(), this.pageSize(), this.availability()], computation: () => 1 });

  private readonly customersFiltered = computed(() => {
    const q = this.query().trim().toLowerCase();
    return this.ref.customers().filter((c) => !q || c.name.toLowerCase().includes(q) || c.email.toLowerCase().includes(q));
  });

  private readonly productsFiltered = computed(() => {
    const q = this.query().trim().toLowerCase();
    return this.ref.products().filter((p) => (this.availability() === 'all' || (p.isActive !== false) === (this.availability() === 'active')) &&
      (!q || p.name.toLowerCase().includes(q) || p.sku.toLowerCase().includes(q)));
  });

  protected readonly total = computed(() =>
    this.tab() === 'customers' ? this.customersFiltered().length : this.productsFiltered().length,
  );
  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  private readonly start = computed(() => (this.page() - 1) * this.pageSize());
  protected readonly customerRows = computed(() => this.customersFiltered().slice(this.start(), this.start() + this.pageSize()));
  protected readonly productRows = computed(() => this.productsFiltered().slice(this.start(), this.start() + this.pageSize()));
  protected readonly from = computed(() => (this.total() === 0 ? 0 : this.start() + 1));
  protected readonly to = computed(() => Math.min(this.start() + this.pageSize(), this.total()));

  protected setTab(tab: Tab): void {
    this.tab.set(tab);
  }

  protected setQuery(value: string): void {
    this.query.set(value);
  }

  protected setSize(value: string): void {
    this.pageSize.set(Number(value));
  }
}
