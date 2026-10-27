import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { Session } from '../../core/auth/session';
import { PO_STATUS, PurchaseOrder, PurchaseHistory, localTimestamp } from '../../core/procurement/models';
import { ProcurementApi } from '../../core/procurement/procurement-api';
import { MiddleText } from '../../ui/table-record/table-record';

@Component({
  selector: 'app-purchase-detail', imports: [RouterLink, DatePipe, DecimalPipe, MiddleText],
  template: `
    <section class="page"><header class="page-head"><div><a routerLink="/purchase-orders">← Purchase orders</a><h1>{{ order()?.poNumber ?? 'Purchase order' }}</h1><p>Commitment, receiving progress and review history.</p></div>@if (order(); as o) { <span class="status-chip">{{ statuses[o.status] }}</span> }</header>
      @if (error()) { <div class="alert" role="alert"><p>{{ error() }}</p><button class="btn btn-ghost" (click)="load()">Reload document</button></div> }
      @if (notice()) { <p role="status" class="alert success-note">{{ notice() }}</p> }
      @if (loading()) { <div class="card loading-lines" role="status" aria-label="Loading purchase order"><span class="skeleton"></span><span class="skeleton"></span></div> }
      @else if (order(); as o) {
        <div class="split"><div class="stack"><article class="card stack"><h2>Order details</h2><dl class="document-meta"><div><dt>Supplier</dt><dd>{{ supplier() || o.supplierId }}</dd></div><div><dt>Receiving warehouse</dt><dd>{{ warehouse() || o.warehouseId }}</dd></div><div><dt>Created</dt><dd>{{ utc(o.createdAt) | date:'medium' }}</dd></div><div><dt>Created by</dt><dd>{{ o.createdByUserId === user()?.id ? 'You' : o.createdByUserId }}</dd></div></dl>
          @if (o.notes) { <p class="preserve-lines">{{ o.notes }}</p> }
          <div class="table-wrap"><table class="table"><thead><tr><th>Product</th><th class="num">Ordered</th><th class="num">Received</th><th class="num">Unit cost</th><th class="num">Line total</th></tr></thead><tbody>@for (line of o.items; track line.id) { <tr><td class="main"><app-middle-text [value]="ref.productName(line.productId)" /></td><td class="num" data-label="Ordered">{{ line.quantity }}</td><td class="num" data-label="Received">{{ line.receivedQuantity }}</td><td class="num" data-label="Unit cost">{{ line.unitCost | number:'1.2-4' }}</td><td class="num" data-label="Line total">{{ line.lineTotal | number:'1.2-4' }}</td></tr> }</tbody></table></div><div class="document-summary"><span>Order total</span><strong>{{ o.totalAmount | number:'1.2-4' }}</strong></div>
        </article><section class="card stack"><h2>Activity</h2>@if (historyError()) { <p class="alert" role="alert">{{ historyError() }}</p> } @else if (!history().length) { <p class="muted">No activity recorded.</p> } @for (entry of history(); track entry.id) { <div class="action-divider"><strong>{{ statuses[entry.fromStatus] }} → {{ statuses[entry.toStatus] }}</strong><span class="muted">{{ utc(entry.createdAt) | date:'medium' }} · Revision {{ entry.revision }}</span><span class="record-id">{{ entry.actorId === user()?.id ? 'You' : entry.actorId }}</span>@if (entry.comments) { <p class="preserve-lines">{{ entry.comments }}</p> }</div> }</section></div>
        <aside class="card stack"><h2>Next step</h2>
          @if (editable()) { <p class="muted">Check quantities and costs before sending for approval.</p><a class="btn btn-ghost" [routerLink]="['/purchase-orders', o.id, 'edit']">Edit draft</a><button class="btn btn-primary" [disabled]="busy()" (click)="action.set('submit')">Submit for approval</button> }
          @if (canDecide()) { <p class="muted">Review this commitment before approving purchasing.</p><button class="btn btn-primary" [disabled]="busy()" (click)="action.set('approve')">Approve</button><button class="btn btn-ghost" [disabled]="busy()" (click)="action.set('reject')">Reject</button> }
          @if (o.status === 1 && o.createdByUserId === user()?.id) { <p class="muted">Another authorized reviewer must decide this order. Creators cannot approve or reject their own orders.</p> }
          @if (o.status === 2 || o.status === 5 || o.status === 6) { <a class="btn btn-ghost" [routerLink]="['/receiving', o.id]">View receiving quantities</a> }
          @if (canCancel()) { <button class="btn btn-ghost" [disabled]="busy()" (click)="action.set('cancel')">Cancel order</button> }
          @if (o.status >= 3 && o.status !== 5) { <p class="muted">This document is read-only.</p> }
          @if (action(); as a) { <form class="action-divider" animate.enter="action-enter" animate.leave="action-leave" (submit)="$event.preventDefault(); execute()"><h3>Confirm {{ a }}</h3>@if (a !== 'submit') { <div class="field"><label for="decision-text">{{ a === 'approve' ? 'Comments (optional)' : 'Reason (required)' }}</label><textarea id="decision-text" rows="4" maxlength="1000" [required]="a !== 'approve'" [disabled]="busy()" [value]="comments()" (input)="comments.set($any($event.target).value)"></textarea></div> }<p class="muted">{{ a === 'cancel' ? 'Cancellation is final. Approved orders cannot be cancelled after receiving has begun.' : a === 'reject' ? 'Rejection is final; this order cannot return to draft.' : 'The server checks the latest order version.' }}</p><div class="actions"><button class="btn btn-ghost" type="button" [disabled]="busy()" (click)="action.set('')">Back</button><button class="btn btn-primary" [disabled]="busy()">{{ busy() ? 'Saving…' : 'Confirm ' + a }}</button></div></form> }
        </aside></div>
      }
    </section>
  `,
})
export class PurchaseDetail {
  private readonly api = inject(ProcurementApi);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  protected readonly user = inject(Session).user;
  protected readonly ref = inject(Reference);
  protected readonly order = signal<PurchaseOrder | null>(null);
  protected readonly history = signal<PurchaseHistory[]>([]);
  protected readonly supplier = signal('');
  protected readonly warehouse = signal('');
  protected readonly error = signal('');
  protected readonly historyError = signal('');
  protected readonly notice = signal('');
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly action = signal('');
  protected readonly comments = signal('');
  protected readonly statuses = PO_STATUS;
  protected readonly utc = localTimestamp;
  private readonly owns = computed(() => this.user()?.role === 'Administrator' || (this.user()?.role === 'Purchasing' && this.order()?.createdByUserId === this.user()?.id));
  protected readonly editable = computed(() => this.order()?.status === 0 && this.owns());
  protected readonly canDecide = computed(() => this.order()?.status === 1 && ['Administrator', 'Approver'].includes(this.user()?.role ?? '') && this.order()?.createdByUserId !== this.user()?.id);
  protected readonly canCancel = computed(() => ((this.order()?.status === 0 || this.order()?.status === 1) && this.owns()) || (this.order()?.status === 2 && this.user()?.role === 'Administrator' && this.order()?.items.every(l => l.receivedQuantity === 0)));
  constructor() { void this.load(); }
  protected async load(): Promise<void> {
    this.loading.set(true); this.error.set(''); this.historyError.set('');
    try {
      const order = await this.api.get<PurchaseOrder>(`purchase-orders/${this.id}`); this.order.set(order);
      const lookups = await Promise.allSettled([this.api.get<{ name: string }>(`suppliers/${order.supplierId}`), this.api.get<{ name: string }>(`warehouses/${order.warehouseId}`), this.api.get<PurchaseHistory[]>(`purchase-orders/${this.id}/history`)]);
      if (lookups[0].status === 'fulfilled') this.supplier.set(lookups[0].value.name);
      if (lookups[1].status === 'fulfilled') this.warehouse.set(lookups[1].value.name);
      if (lookups[2].status === 'fulfilled') this.history.set(lookups[2].value); else this.historyError.set(problemMessage(lookups[2].reason));
    } catch (e) { this.order.set(null); this.error.set(problemMessage(e)); }
    finally { this.loading.set(false); }
  }
  protected async execute(): Promise<void> {
    const o = this.order(), a = this.action(); if (!o || !a || this.busy()) return;
    if ((a === 'reject' || a === 'cancel') && !this.comments().trim()) { this.error.set('Enter a reason.'); return; }
    this.busy.set(true); this.error.set('');
    try {
      await this.api.post(`purchase-orders/${o.id}/${a}`, { rowVersion: o.rowVersion, ...(a === 'approve' ? { comments: this.comments().trim() || null } : a === 'submit' ? {} : { reason: this.comments().trim() }) });
      this.action.set(''); this.comments.set(''); this.notice.set('Purchase order updated.'); await this.load();
    } catch (e) { if ((e as { status?: number }).status === 409) { await this.load(); this.action.set(''); } this.error.set(problemMessage(e)); }
    finally { this.busy.set(false); }
  }
}
