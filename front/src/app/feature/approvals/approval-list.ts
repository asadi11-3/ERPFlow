import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { HIGH_VALUE, reservationState, shortId } from '../../core/api/models';
import { PendingQueue } from '../../core/api/pending';
import { Reference } from '../../core/api/reference';
import { Avatar } from '../../ui/avatar/avatar';
import { Icon } from '../../ui/icon/icon';
import { Kpi } from '../../ui/kpi/kpi';
import { Pagination } from '../../ui/pagination/pagination';
import { Pill } from '../../ui/pill/pill';
import { RowMenu } from '../../ui/row-menu/row-menu';
import { Select, SelectOption } from '../../ui/select/select';
import { MiddleText, RecordRow, RecordToggle } from '../../ui/table-record/table-record';

@Component({
  selector: 'app-approval-list',
  imports: [RouterLink, CurrencyPipe, DatePipe, Avatar, Icon, Kpi, Pagination, Pill, RowMenu, Select, MiddleText, RecordRow, RecordToggle],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Pending Approvals</h1>
          <p>Review and decide on submitted sales orders. Oldest first.</p>
        </div>
      </header>

      <div class="kpis">
        <app-kpi label="Total pending" [value]="queue.count()" tone="amber" />
        <app-kpi label="Submitted today" [value]="stats().today" tone="blue" />
        <app-kpi label="High value" [value]="stats().high" hint="Orders of $10,000 or more" />
        <app-kpi label="Total value" [value]="stats().valueText" tone="green" />
        <app-kpi label="Average wait" [value]="stats().waitText" />
      </div>

      <div class="card">
        <div class="toolbar">
          <div class="field search">
            <label class="sr-only" for="q">Search</label>
            <input id="q" type="search" placeholder="Search by order number or customer..." [value]="query()" (input)="setQuery($any($event.target).value)" />
          </div>
          <label class="muted" for="size">Show</label>
          <app-select inputId="size" [options]="sizes" [value]="'' + pageSize()" (valueChange)="setSize($event)" />
        </div>

        @if (queue.failed()) {
          <div class="alert" role="alert">
            <app-icon name="alert" />
            <p>Could not load the queue. <button type="button" class="btn btn-ghost" (click)="queue.reload()">Retry</button></p>
          </div>
        } @else if (queue.loading() && queue.count() === 0) {
          <div role="status" aria-label="Loading approvals">
            @for (n of skeleton; track n) {
              <div class="skel-row" aria-hidden="true">
                <span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span>
              </div>
            }
          </div>
        } @else if (filtered().length === 0) {
          <p class="empty">{{ queue.count() === 0 ? 'Nothing is waiting for approval.' : 'No orders match your search.' }}</p>
        } @else {
          <div class="table-wrap">
            <table class="table">
              <thead>
                <tr>
                  <th>Order no.</th>
                  <th>Customer</th>
                  <th>Submitted at</th>
                  <th class="num">Total</th>
                  <th>Reservations</th>
                  <th><span class="sr-only">Review</span></th>
                </tr>
              </thead>
              <tbody>
                @for (row of pageRows(); track row.order.id) {
                  <tr appRecordRow>
                    <td class="main"><a class="row-link" [routerLink]="['/approvals', row.order.id]">{{ row.no }}</a></td>
                    <td class="span" data-label="Customer">
                      <span class="cell-customer"><app-avatar [name]="row.customer" /><app-middle-text [value]="row.customer" /></span>
                    </td>
                    <td class="muted span" data-label="Submitted">{{ row.order.submittedAt | date: 'd MMM y, h:mm a' }}</td>
                    <td class="num" data-label="Total">{{ row.order.totalAmount | currency }}</td>
                    <td data-label="Reservations"><app-pill [label]="row.reservation.label" [tone]="row.reservation.tone" /></td>
                    <td class="act">
                      <app-row-menu [label]="'Actions for order ' + row.no">
                        <a class="menu-item" role="menuitem" [routerLink]="['/approvals', row.order.id]">Review order</a>
                      </app-row-menu>
                    </td>
                    <td class="record-disclosure"><app-record-toggle /></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="table-foot">
            <span>Showing {{ from() }} to {{ to() }} of {{ filtered().length }} pending approvals</span>
            <app-pagination [(page)]="page" [pages]="pages()" />
          </div>
        }
      </div>
    </section>
  `,
  styles: `
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
  `,
})
export class ApprovalList {
  protected readonly queue = inject(PendingQueue);
  private readonly ref = inject(Reference);

  protected readonly sizes: SelectOption[] = [10, 25, 50].map((n) => ({ value: '' + n, label: '' + n }));
  protected readonly skeleton = [1, 2, 3, 4, 5];
  protected readonly query = signal('');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  protected readonly stats = computed(() => {
    const orders = this.queue.orders();
    const now = Date.now();
    const startOfDay = new Date().setHours(0, 0, 0, 0);
    const value = orders.reduce((sum, o) => sum + o.totalAmount, 0);
    const waits = orders.map((o) => now - new Date(o.submittedAt ?? o.createdAt).getTime());
    const avgMs = waits.length ? waits.reduce((a, b) => a + b, 0) / waits.length : 0;
    const days = avgMs / 86_400_000;
    return {
      today: orders.filter((o) => new Date(o.submittedAt ?? o.createdAt).getTime() >= startOfDay).length,
      high: orders.filter((o) => o.totalAmount >= HIGH_VALUE).length,
      valueText: new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value),
      waitText: days >= 1 ? `${days.toFixed(1)} days` : `${(avgMs / 3_600_000).toFixed(1)} hours`,
    };
  });

  protected readonly filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    const rows = this.queue.orders().map((order) => ({
      order,
      no: shortId(order.id),
      customer: this.ref.customerName(order.customerId),
      reservation: reservationState(order),
    }));
    return q ? rows.filter((r) => r.no.toLowerCase().includes(q) || r.customer.toLowerCase().includes(q)) : rows;
  });

  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.filtered().length / this.pageSize())));
  protected readonly pageRows = computed(() => {
    const start = (Math.min(this.page(), this.pages()) - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
  });
  protected readonly from = computed(() => (this.filtered().length === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1));
  protected readonly to = computed(() => Math.min(this.page() * this.pageSize(), this.filtered().length));

  protected setQuery(value: string): void {
    this.query.set(value);
    this.page.set(1);
  }

  protected setSize(value: string): void {
    this.pageSize.set(Number(value));
    this.page.set(1);
  }
}
