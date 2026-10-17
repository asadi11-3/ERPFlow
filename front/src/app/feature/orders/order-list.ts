import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { forkJoin, map } from 'rxjs';

import { Api } from '../../core/api/api';
import { OrderStatus, reservationState, shortId, STATUS, STATUSES } from '../../core/api/models';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { Session } from '../../core/auth/session';
import { Avatar } from '../../ui/avatar/avatar';
import { Icon } from '../../ui/icon/icon';
import { Kpi } from '../../ui/kpi/kpi';
import { Pagination } from '../../ui/pagination/pagination';
import { Pill } from '../../ui/pill/pill';
import { RowMenu } from '../../ui/row-menu/row-menu';
import { Select, SelectOption } from '../../ui/select/select';

@Component({
  selector: 'app-order-list',
  imports: [RouterLink, CurrencyPipe, DatePipe, Avatar, Icon, Kpi, Pagination, Pill, RowMenu, Select],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>{{ title() }}</h1>
          <p>View and track sales orders.</p>
        </div>
        @if (canCreate()) {
          <a class="btn btn-primary" routerLink="/orders/new"><app-icon name="plus" /> Create Order</a>
        }
      </header>

      <div class="kpis">
        <app-kpi label="Total orders" [value]="counts().total" tone="blue" />
        <app-kpi label="Draft" [value]="counts().draft" />
        <app-kpi label="Pending approval" [value]="counts().pending" tone="amber" />
        <app-kpi label="Approved" [value]="counts().approved" tone="green" />
        <app-kpi label="Rejected" [value]="counts().rejected" tone="red" />
      </div>

      <div class="card">
        <div class="toolbar">
          <label class="muted" for="status">Status</label>
          <app-select inputId="status" [options]="statusOptions" [value]="'' + status()" (valueChange)="setStatus($event)" />
          <label class="muted" for="size">Show</label>
          <app-select inputId="size" [options]="sizeOptions" [value]="'' + pageSize()" (valueChange)="setSize($event)" />
        </div>

        @if (list.status() === 'error') {
          <div class="alert" role="alert">
            <app-icon name="alert" />
            <p>{{ error() }} <button type="button" class="btn btn-ghost" (click)="list.reload()">Retry</button></p>
          </div>
        } @else if (list.isLoading() && !list.hasValue()) {
          <div role="status" aria-label="Loading orders">
            @for (n of skeleton; track n) {
              <div class="skel-row" aria-hidden="true">
                <span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span>
              </div>
            }
          </div>
        } @else if (rows().length === 0) {
          <p class="empty">No orders found.</p>
        } @else {
          <div class="table-wrap wide">
            <table class="table">
              <thead>
                <tr>
                  <th>Order no.</th>
                  <th>Customer</th>
                  <th>Status</th>
                  <th>Created at</th>
                  <th class="num">Total</th>
                  <th class="num">Items</th>
                  <th>Reservations</th>
                  <th><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (row of rows(); track row.order.id) {
                  <tr>
                    <td class="main">
                      <a class="row-link" [routerLink]="['/orders', row.order.id]">{{ row.no }}</a>
                    </td>
                    <td class="span" data-label="Customer">
                      <span class="cell-customer">
                        <app-avatar [name]="row.customer" /><span class="clip" [title]="row.customer">{{ row.customer }}</span>
                      </span>
                    </td>
                    <td data-label="Status"><app-pill [label]="row.statusLabel" [tone]="row.statusTone" /></td>
                    <td class="muted span" data-label="Created" [title]="row.order.createdAt | date: 'd MMM y, h:mm a'">{{ row.order.createdAt | date: 'd MMM y' }}</td>
                    <td class="num" data-label="Total">{{ row.order.totalAmount | currency }}</td>
                    <td class="num" data-label="Items">{{ row.order.items.length }}</td>
                    <td data-label="Reservations"><app-pill [label]="row.reservation.label" [tone]="row.reservation.tone" /></td>
                    <td class="act">
                      <app-row-menu [label]="'Actions for order ' + row.no">
                        <a class="menu-item" role="menuitem" [routerLink]="['/orders', row.order.id]">Open order</a>
                      </app-row-menu>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="table-foot">
            <span>Showing {{ from() }} to {{ to() }} of {{ total() }} orders</span>
            <app-pagination [(page)]="page" [pages]="pages()" />
          </div>
        }
      </div>
    </section>
  `,
})
export class OrderList {
  private readonly api = inject(Api);
  private readonly session = inject(Session);
  private readonly ref = inject(Reference);

  protected readonly statusOptions: SelectOption[] = [
    { value: 'all', label: 'All statuses' },
    ...STATUSES.map((s) => ({ value: '' + s, label: STATUS[s].label })),
  ];
  protected readonly sizeOptions: SelectOption[] = [10, 25, 50].map((n) => ({ value: '' + n, label: '' + n }));
  protected readonly skeleton = [1, 2, 3, 4, 5];

  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly status = signal<'all' | OrderStatus>('all');

  protected readonly list = rxResource({
    params: () => ({ page: this.page(), size: this.pageSize(), status: this.status() }),
    stream: ({ params }) =>
      this.api.orders(params.page, params.size, params.status === 'all' ? undefined : params.status),
  });

  private readonly countsData = rxResource({
    params: () => (this.session.user() ? {} : undefined),
    stream: () =>
      forkJoin([this.api.orders(1, 1), ...STATUSES.map((s) => this.api.orders(1, 1, s))]).pipe(
        map((totals) => totals.map((t) => t.totalCount)),
      ),
  });

  protected readonly counts = computed(() => {
    const [total, draft, pending, approved, rejected] = this.countsData.hasValue()
      ? this.countsData.value()
      : [0, 0, 0, 0, 0];
    return { total, draft, pending, approved, rejected };
  });

  protected readonly title = computed(() => (this.session.user()?.role === 'Sales' ? 'My Sales Orders' : 'Sales Orders'));
  protected readonly canCreate = computed(() => {
    const role = this.session.user()?.role;
    return role === 'Sales' || role === 'Administrator';
  });

  protected readonly error = computed(() => problemMessage(this.list.error()));
  protected readonly total = computed(() => (this.list.hasValue() ? this.list.value().totalCount : 0));
  protected readonly pages = computed(() => (this.list.hasValue() ? Math.max(1, this.list.value().totalPages) : 1));
  protected readonly from = computed(() => (this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1));
  protected readonly to = computed(() => Math.min(this.page() * this.pageSize(), this.total()));

  protected readonly rows = computed(() =>
    (this.list.hasValue() ? this.list.value().items : []).map((order) => ({
      order,
      no: shortId(order.id),
      customer: this.ref.customerName(order.customerId),
      statusLabel: STATUS[order.status].label,
      statusTone: STATUS[order.status].tone,
      reservation: reservationState(order),
    })),
  );

  protected setStatus(value: string): void {
    this.status.set(value === 'all' ? 'all' : (Number(value) as OrderStatus));
    this.page.set(1);
  }

  protected setSize(value: string): void {
    this.pageSize.set(Number(value));
    this.page.set(1);
  }
}
