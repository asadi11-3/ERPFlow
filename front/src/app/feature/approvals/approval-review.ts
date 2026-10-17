import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom, forkJoin, Observable } from 'rxjs';

import { Api } from '../../core/api/api';
import { shortId, STATUS } from '../../core/api/models';
import { PendingQueue } from '../../core/api/pending';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { OrderHistory } from '../../pattern/order-view/order-history';
import { OrderItems } from '../../pattern/order-view/order-items';
import { OrderWorkflow } from '../../pattern/order-view/order-workflow';
import { ConfirmDialog } from '../../ui/confirm-dialog/confirm-dialog';
import { Icon } from '../../ui/icon/icon';
import { Pill } from '../../ui/pill/pill';

@Component({
  selector: 'app-approval-review',
  imports: [CurrencyPipe, DatePipe, RouterLink, ConfirmDialog, Icon, Pill, OrderItems, OrderHistory, OrderWorkflow],
  template: `
    <section class="page">
      <a class="back" routerLink="/approvals"><app-icon name="arrow-left" /> Pending Approvals</a>

      @if (data.status() === 'error') {
        <div class="alert" role="alert">
          <app-icon name="alert" />
          <p>{{ loadError() }} <button type="button" class="btn btn-ghost" (click)="data.reload()">Retry</button></p>
        </div>
      } @else if (view(); as v) {
        <header class="page-head">
          <div>
            <h1>Approval Review</h1>
            <p>Review the order details, then approve or reject this sales order.</p>
          </div>
        </header>

        <div class="split">
          <div class="stack">
            <div class="card">
              <h2>
                Sales Order {{ no() }}
                <app-pill [label]="status().label" [tone]="status().tone" />
              </h2>
              <div class="tiles">
                <div class="tile">
                  <span>Customer</span><strong>{{ ref.customerName(v.order.customerId) }}</strong>
                </div>
                <div class="tile">
                  <span>Customer email</span><strong>{{ ref.customer(v.order.customerId)?.email ?? '-' }}</strong>
                </div>
                <div class="tile">
                  <span>Created at</span><strong>{{ v.order.createdAt | date: 'd MMM y, h:mm a' }}</strong>
                </div>
                <div class="tile">
                  <span>Submitted at</span>
                  <strong>{{ v.order.submittedAt ? (v.order.submittedAt | date: 'd MMM y, h:mm a') : '-' }}</strong>
                </div>
              </div>
            </div>

            <div class="card">
              <h2>Order Items ({{ v.order.items.length }})</h2>
              <app-order-items [order]="v.order" />
            </div>

            <div class="card">
              <h2>Reservation Summary</h2>
              <div class="tiles">
                <div class="tile">
                  <span>Lines reserved</span><strong>{{ reserved().lines }}</strong>
                </div>
                <div class="tile">
                  <span>Total quantity</span><strong>{{ reserved().quantity }}</strong>
                </div>
                <div class="tile">
                  <span>Order total</span><strong>{{ v.order.totalAmount | currency }}</strong>
                </div>
              </div>
            </div>

            <div class="card">
              <h2>History</h2>
              <app-order-history [entries]="v.history" />
            </div>
          </div>

          <aside class="stack">
            @if (v.order.status === 1) {
              <div class="card">
                <h2>Approval Decision</h2>
                <div class="field">
                  <label for="comments">Comments</label>
                  <textarea
                    id="comments"
                    rows="4"
                    maxlength="500"
                    placeholder="Optional for approval, required for rejection"
                    [value]="comments()"
                    [disabled]="busy()"
                    (input)="comments.set($any($event.target).value)"
                  ></textarea>
                </div>
                @if (actionError()) {
                  <div class="alert" role="alert" style="margin-top: 12px">
                    <app-icon name="alert" />
                    <p>{{ actionError() }}</p>
                  </div>
                }
                <div class="actions decide" style="margin-top: 14px">
                  <button type="button" class="btn btn-success" [disabled]="busy()" (click)="ask('approve')">Approve Order</button>
                  <button type="button" class="btn btn-danger" [disabled]="busy() || !comments().trim()" (click)="ask('reject')">
                    Reject Order
                  </button>
                </div>
                <div class="alert alert-warn" style="margin-top: 14px">
                  <app-icon name="alert" />
                  <p>Rejecting this order releases all of its reserved stock back to inventory.</p>
                </div>
              </div>
            } @else {
              <div class="card">
                <h2>Decision</h2>
                <p class="muted">This order is {{ status().label.toLowerCase() }}. No decision is needed.</p>
              </div>
            }
            <div class="card">
              <h2>Approval Workflow</h2>
              <app-order-workflow [order]="v.order" />
            </div>
          </aside>
        </div>
      } @else {
        <div class="stack" role="status" aria-label="Loading approval">
          <span class="skeleton" style="height: 32px; width: 280px"></span>
          <div class="split">
            <span class="skeleton" style="height: 320px"></span>
            <span class="skeleton" style="height: 320px"></span>
          </div>
        </div>
      }
    </section>

    <app-confirm-dialog
      [(open)]="confirmOpen"
      [heading]="kind() === 'approve' ? 'Approve order ' + no() + '?' : 'Reject order ' + no() + '?'"
      [message]="
        kind() === 'approve'
          ? 'The order moves to Approved and its reserved stock stays allocated.'
          : 'The order moves to Rejected and all of its reserved stock is released back to inventory.'
      "
      [confirmLabel]="kind() === 'approve' ? 'Approve' : 'Reject'"
      [tone]="kind() === 'approve' ? 'success' : 'danger'"
      [busy]="busy()"
      (confirmed)="confirm()"
    />
  `,
})
export class ApprovalReview {
  private readonly api = inject(Api);
  private readonly queue = inject(PendingQueue);
  private readonly router = inject(Router);
  protected readonly ref = inject(Reference);

  readonly id = input.required<string>();

  protected readonly comments = signal('');
  protected readonly busy = signal(false);
  protected readonly actionError = signal('');

  protected readonly data = rxResource({
    params: () => ({ id: this.id() }),
    stream: ({ params }) => forkJoin({ order: this.api.order(params.id), history: this.api.history(params.id) }),
  });

  protected readonly view = computed(() => (this.data.hasValue() ? this.data.value() : null));
  protected readonly loadError = computed(() => problemMessage(this.data.error()));
  protected readonly no = computed(() => shortId(this.id()));
  protected readonly status = computed(() => STATUS[this.view()?.order.status ?? 1]);

  protected readonly reserved = computed(() => {
    const active = (this.view()?.order.reservations ?? []).filter((r) => !r.isReleased);
    return { lines: active.length, quantity: active.reduce((sum, r) => sum + r.quantity, 0) };
  });

  protected readonly kind = signal<'approve' | 'reject'>('approve');
  protected readonly confirmOpen = signal(false);

  protected ask(kind: 'approve' | 'reject'): void {
    this.kind.set(kind);
    this.confirmOpen.set(true);
  }

  protected async confirm(): Promise<void> {
    await (this.kind() === 'approve' ? this.approve() : this.reject());
    this.confirmOpen.set(false);
  }

  protected approve(): Promise<void> {
    const text = this.comments().trim();
    return this.decide(this.api.approve(this.id(), text || null));
  }

  protected reject(): Promise<void> {
    return this.decide(this.api.reject(this.id(), this.comments().trim()));
  }

  private async decide(call: Observable<unknown>): Promise<void> {
    this.busy.set(true);
    this.actionError.set('');
    try {
      await firstValueFrom(call);
      this.queue.reload();
      await this.router.navigate(['/approvals']);
    } catch (err) {
      this.actionError.set(problemMessage(err));
      this.data.reload();
      this.queue.reload();
    } finally {
      this.busy.set(false);
    }
  }
}
