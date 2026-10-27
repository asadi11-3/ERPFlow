import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { DOCUMENT_STATUS, Receipt, Transfer, Warehouse, localTimestamp } from '../../core/procurement/models';
import { ProcurementApi } from '../../core/procurement/procurement-api';
import { MiddleText } from '../../ui/table-record/table-record';

@Component({
  selector: 'app-stock-document', imports: [RouterLink, DatePipe, MiddleText],
  template: `
    <section class="page"><header class="page-head"><div><a [routerLink]="'/' + path">← {{ receipt ? 'Goods receipts' : 'Stock transfers' }}</a><h1>{{ document()?.number ?? (receipt ? 'Goods receipt' : 'Stock transfer') }}</h1><p>{{ receipt ? 'Receipt quantities and inventory posting.' : 'Warehouse movement and posting record.' }}</p></div>@if (document(); as d) { <span class="status-chip">{{ statuses[d.status] }}</span> }</header>
      @if (error()) { <div class="alert" role="alert"><p>{{ error() }}</p>@if (!pending()) { <button class="btn btn-ghost" (click)="load()">Reload document</button> }</div> }
      @if (notice()) { <p class="alert success-note" role="status">{{ notice() }}</p> }
      @if (loading()) { <div class="card loading-lines" role="status" aria-label="Loading stock document"><span class="skeleton"></span><span class="skeleton"></span></div> }
      @else if (document(); as d) { <div class="split"><article class="card stack"><h2>{{ receipt ? 'Received items' : 'Transfer items' }}</h2><dl class="document-meta"><div><dt>Warehouse{{ receipt ? '' : ' route' }}</dt><dd>{{ location(d) }}</dd></div><div><dt>Created</dt><dd>{{ utc(d.createdAt) | date:'medium' }}</dd></div>@if (d.postedAt) { <div><dt>Posted</dt><dd>{{ utc(d.postedAt) | date:'medium' }}</dd></div> }</dl>
        @if (asReceipt(d); as r) { <a class="text-link" [routerLink]="['/receiving', r.purchaseOrderId]">View purchase order quantities</a>@if (r.supplierDeliveryReference) { <p>Delivery reference: {{ r.supplierDeliveryReference }}</p> }@if (r.notes) { <p class="preserve-lines">{{ r.notes }}</p> } }
        <div class="table-wrap"><table class="table"><thead><tr><th>Product</th><th class="num">Quantity</th><th>Stock ledger</th></tr></thead><tbody>@for (line of d.items; track line.id) { <tr><td class="main"><app-middle-text [value]="ref.productName(line.productId)" /></td><td class="num" data-label="Quantity">{{ line.quantity }}</td><td data-label="Stock ledger">@if (d.status === 1) { <a routerLink="/inventory" [queryParams]="ledgerParams(d, line.productId)">View movements</a> } @else { <span class="muted">Not posted</span> }</td></tr> }</tbody></table></div>
        @if (d.cancellationReason) { <div class="action-divider"><h3>Cancellation reason</h3><p class="preserve-lines">{{ d.cancellationReason }}</p><span class="muted">{{ utc(d.cancelledAt) | date:'medium' }}</span></div> }
      </article><aside class="card stack"><h2>{{ d.status === 0 ? 'Review and post' : 'Document complete' }}</h2>
        @if (d.status === 0) { <p class="muted">{{ receipt ? 'Posting adds stock and updates the purchase order’s received quantities.' : 'Posting moves available stock atomically. Reserved stock stays at its source.' }} Posted documents cannot be edited or reversed here.</p><p class="muted">Warehouse inventory must be enabled before posting.</p><button class="btn btn-primary" [disabled]="busy()" (click)="action.set('post')">{{ pending() ? 'Retry posting' : 'Post ' + (receipt ? 'receipt' : 'transfer') }}</button><button class="btn btn-ghost" [disabled]="busy() || pending()" (click)="action.set('cancel')">Cancel draft</button>
          @if (action(); as a) { <form class="action-divider" animate.enter="action-enter" animate.leave="action-leave" (submit)="$event.preventDefault(); execute()"><h3>Confirm {{ a === 'post' ? 'stock posting' : 'cancellation' }}</h3>@if (a === 'cancel') { <div class="field"><label for="cancel-reason">Reason (required)</label><textarea id="cancel-reason" required maxlength="1000" rows="4" [disabled]="busy()" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea></div> }<div class="actions"><button class="btn btn-ghost" type="button" [disabled]="busy()" (click)="action.set('')">Back</button><button class="btn btn-primary" [disabled]="busy()">{{ busy() ? 'Saving…' : 'Confirm ' + a }}</button></div></form> }
        } @else { <p class="muted">This document is read-only.</p> }
      </aside></div> }
    </section>
  `,
})
export class StockDocument {
  private readonly api = inject(ProcurementApi);
  private readonly route = inject(ActivatedRoute);
  private readonly id = this.route.snapshot.paramMap.get('id')!;
  protected readonly receipt = this.route.snapshot.data['kind'] === 'receipt';
  protected readonly path = this.receipt ? 'goods-receipts' : 'stock-transfers';
  protected readonly ref = inject(Reference);
  protected readonly document = signal<Receipt | Transfer | null>(null);
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly pending = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly action = signal('');
  protected readonly reason = signal('');
  protected readonly statuses = DOCUMENT_STATUS;
  protected readonly utc = localTimestamp;
  private postRequest: { key: string; version: string } | null = null;
  constructor() { void this.load(); }
  protected asReceipt(d: Receipt | Transfer): Receipt | null { return 'purchaseOrderId' in d ? d : null; }
  protected location(d: Receipt | Transfer): string { const name = (id: string) => this.warehouses().find(w => w.id === id)?.name ?? id; return 'warehouseId' in d ? name(d.warehouseId) : `${name(d.sourceWarehouseId)} → ${name(d.destinationWarehouseId)}`; }
  protected ledgerParams(d: Receipt | Transfer, productId: string): object { return { tab: 'movements', warehouseId: 'warehouseId' in d ? d.warehouseId : d.sourceWarehouseId, productId }; }
  protected async load(): Promise<void> {
    this.loading.set(true); this.error.set('');
    try { this.document.set(await this.api.get<Receipt | Transfer>(`${this.path}/${this.id}`)); this.warehouses.set(await this.api.warehouses()); }
    catch (e) { this.error.set(problemMessage(e)); }
    finally { this.loading.set(false); }
  }
  protected async execute(): Promise<void> {
    const d = this.document(), action = this.action(); if (!d || !action || this.busy()) return;
    if (action === 'cancel' && !this.reason().trim()) { this.error.set('Enter a cancellation reason.'); return; }
    this.busy.set(true); this.error.set('');
    try {
      if (action === 'post') {
        this.postRequest ??= { key: crypto.randomUUID(), version: d.rowVersion }; this.pending.set(true);
        await this.api.post(`${this.path}/${d.id}/post`, { rowVersion: this.postRequest.version, ...(this.receipt ? { idempotencyKey: this.postRequest.key } : {}) }, this.receipt ? undefined : this.postRequest.key);
      } else await this.api.post(`${this.path}/${d.id}/cancel`, { rowVersion: d.rowVersion, reason: this.reason().trim() });
      this.postRequest = null; this.pending.set(false); this.action.set(''); this.notice.set(action === 'post' ? 'Stock posting confirmed.' : 'Draft cancelled.'); await this.load();
    } catch (e) {
      const status = (e as { status?: number }).status;
      if (status && status >= 400 && status < 500 && status !== 408) { this.postRequest = null; this.pending.set(false); if (status === 409) { await this.load(); this.action.set(''); } }
      this.error.set(problemMessage(e));
    } finally { this.busy.set(false); }
  }
}
