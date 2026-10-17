import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { firstValueFrom, forkJoin } from 'rxjs';

import { Api } from '../../core/api/api';
import { shortId, STATUS } from '../../core/api/models';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { PendingQueue } from '../../core/api/pending';
import { Session } from '../../core/auth/session';
import { OrderHistory } from '../../pattern/order-view/order-history';
import { OrderItems } from '../../pattern/order-view/order-items';
import { OrderReservations } from '../../pattern/order-view/order-reservations';
import { OrderWorkflow } from '../../pattern/order-view/order-workflow';
import { ConfirmDialog } from '../../ui/confirm-dialog/confirm-dialog';
import { Icon } from '../../ui/icon/icon';
import { Pill } from '../../ui/pill/pill';

type Tab = 'items' | 'reservations' | 'history';

@Component({
  selector: 'app-order-detail',
  imports: [CurrencyPipe, DatePipe, RouterLink, ConfirmDialog, Icon, Pill, OrderItems, OrderReservations, OrderHistory, OrderWorkflow],
  template: `
    <section class="page">
      <a class="back" routerLink="/orders"><app-icon name="arrow-left" /> Sales Orders</a>

      @if (data.status() === 'error') {
        <div class="alert" role="alert">
          <app-icon name="alert" />
          <p>{{ loadError() }} <button type="button" class="btn btn-ghost" (click)="data.reload()">Retry</button></p>
        </div>
      } @else if (view(); as v) {
        <header class="page-head">
          <div>
            <h1>Sales Order {{ no() }}</h1>
            <p>
              <app-pill [label]="status().label" [tone]="status().tone" />
              <span class="muted"> Created on {{ v.order.createdAt | date: 'd MMM y, h:mm a' }}</span>
            </p>
          </div>
          <div class="actions">
            @if (canSubmit()) {
              <button type="button" class="btn btn-primary" [disabled]="busy()" (click)="confirmOpen.set(true)">Submit for Approval</button>
            }
            @if (canReview()) {
              <a class="btn btn-primary" [routerLink]="['/approvals', v.order.id]">Review order</a>
            }
          </div>
        </header>

        @if (actionError()) {
          <div class="alert" role="alert">
            <app-icon name="alert" />
            <p>{{ actionError() }}</p>
          </div>
        }

        <div class="tiles">
          <div class="tile">
            <span>Customer</span><strong>{{ ref.customerName(v.order.customerId) }}</strong>
          </div>
          <div class="tile">
            <span>Created by</span><strong>{{ creator() }}</strong>
          </div>
          <div class="tile">
            <span>Total amount</span><strong>{{ v.order.totalAmount | currency }}</strong>
          </div>
          <div class="tile">
            <span>Submitted at</span>
            <strong>{{ v.order.submittedAt ? (v.order.submittedAt | date: 'd MMM y, h:mm a') : 'Not submitted' }}</strong>
          </div>
          <div class="tile">
            <span>Decision date</span>
            <strong>{{ v.order.decidedAt ? (v.order.decidedAt | date: 'd MMM y, h:mm a') : 'Pending' }}</strong>
          </div>
        </div>

        <div class="split">
          <div class="card">
            <div class="tabs" role="tablist">
              <button type="button" role="tab" class="tab" [attr.aria-selected]="tab() === 'items'" (click)="tab.set('items')">
                Items ({{ v.order.items.length }})
              </button>
              <button type="button" role="tab" class="tab" [attr.aria-selected]="tab() === 'reservations'" (click)="tab.set('reservations')">
                Reservations
              </button>
              <button type="button" role="tab" class="tab" [attr.aria-selected]="tab() === 'history'" (click)="tab.set('history')">
                History
              </button>
            </div>
            @switch (tab()) {
              @case ('items') {
                <app-order-items [order]="v.order" />
              }
              @case ('reservations') {
                <app-order-reservations [order]="v.order" />
              }
              @case ('history') {
                <app-order-history [entries]="v.history" />
              }
            }
          </div>
          <div class="card">
            <h2>Order Workflow</h2>
            <app-order-workflow [order]="v.order" />
          </div>
        </div>
      } @else {
        <div class="stack" role="status" aria-label="Loading order">
          <span class="skeleton" style="height: 32px; width: 280px"></span>
          <span class="skeleton" style="height: 90px"></span>
          <div class="split">
            <span class="skeleton" style="height: 280px"></span>
            <span class="skeleton" style="height: 280px"></span>
          </div>
        </div>
      }
    </section>

    <app-confirm-dialog
      [(open)]="confirmOpen"
      [heading]="'Submit order ' + no() + ' for approval?'"
      message="Stock is reserved for every line and the order enters the approval queue."
      confirmLabel="Submit"
      [busy]="busy()"
      (confirmed)="submit()"
    />
  `,
})
export class OrderDetail {
  private readonly api = inject(Api);
  private readonly session = inject(Session);
  private readonly pending = inject(PendingQueue);
  protected readonly ref = inject(Reference);

  readonly id = input.required<string>();

  protected readonly tab = signal<Tab>('items');
  protected readonly busy = signal(false);
  protected readonly actionError = signal('');

  protected readonly data = rxResource({
    params: () => ({ id: this.id() }),
    stream: ({ params }) => forkJoin({ order: this.api.order(params.id), history: this.api.history(params.id) }),
  });

  protected readonly view = computed(() => (this.data.hasValue() ? this.data.value() : null));
  protected readonly loadError = computed(() => problemMessage(this.data.error()));
  protected readonly no = computed(() => shortId(this.id()));
  protected readonly status = computed(() => STATUS[this.view()?.order.status ?? 0]);

  protected readonly creator = computed(() => {
    const order = this.view()?.order;
    if (!order) return '';
    return order.createdByUserId === this.session.user()?.id ? 'You' : shortId(order.createdByUserId);
  });

  protected readonly canSubmit = computed(() => {
    const order = this.view()?.order;
    const user = this.session.user();
    if (!order || !user || order.status !== 0) return false;
    return user.role === 'Administrator' || (user.role === 'Sales' && order.createdByUserId === user.id);
  });

  protected readonly canReview = computed(() => {
    const role = this.session.user()?.role;
    return this.view()?.order.status === 1 && (role === 'Approver' || role === 'Administrator');
  });

  protected readonly confirmOpen = signal(false);

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.actionError.set('');
    try {
      await firstValueFrom(this.api.submit(this.id()));
      this.ref.reload();
      this.pending.reload();
    } catch (err) {
      this.actionError.set(problemMessage(err));
    } finally {
      this.busy.set(false);
      this.confirmOpen.set(false);
      this.data.reload();
    }
  }
}
