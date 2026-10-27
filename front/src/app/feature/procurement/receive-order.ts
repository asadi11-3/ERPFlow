import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Reference } from '../../core/api/reference';
import { problemMessage } from '../../core/api/problem';
import { Session } from '../../core/auth/session';
import { OperationalOrder, PO_STATUS, Receipt } from '../../core/procurement/models';
import { ProcurementApi } from '../../core/procurement/procurement-api';
import { MiddleText } from '../../ui/table-record/table-record';

@Component({
  selector: 'app-receive-order', imports: [RouterLink, MiddleText],
  template: `
    <section class="page"><header class="page-head"><div><a routerLink="/receiving">← Receiving</a><h1>{{ order()?.poNumber ?? 'Order receiving' }}</h1><p>Record only the quantities physically received.</p></div>@if (order(); as o) { <span class="status-chip">{{ statuses[o.status] }}</span> }</header>
      @if (error()) { <div class="alert" role="alert"><p>{{ error() }}</p>@if (!pending()) { <button class="btn btn-ghost" (click)="load()">Reload quantities</button> }</div> }
      @if (loading()) { <div class="card loading-lines" role="status" aria-label="Loading order"><span class="skeleton"></span><span class="skeleton"></span></div> }
      @else if (order(); as o) { <form class="stack" (submit)="$event.preventDefault(); save()"><div class="card stack"><dl class="document-meta"><div><dt>Supplier ID</dt><dd class="record-id">{{ o.supplierId }}</dd></div><div><dt>Receiving warehouse</dt><dd>{{ warehouse() || o.warehouseId }}</dd></div></dl>
        <div class="table-wrap"><table class="table"><thead><tr><th>Product</th><th class="num">Ordered</th><th class="num">Received</th><th class="num">Remaining</th>@if (canReceive && (o.status === 2 || o.status === 5)) { <th>Receive now</th> }</tr></thead><tbody>@for (line of o.items; track line.purchaseOrderItemId) { <tr><td class="main"><app-middle-text [value]="ref.productName(line.productId)" /></td><td class="num" data-label="Ordered">{{ line.quantity }}</td><td class="num" data-label="Received">{{ line.receivedQuantity }}</td><td class="num" data-label="Remaining">{{ line.remainingQuantity }}</td>@if (canReceive && (o.status === 2 || o.status === 5)) { <td data-label="Receive now"><div class="field"><label class="sr-only" [for]="'receive-' + line.purchaseOrderItemId">Receive {{ ref.productName(line.productId) }}</label><input [id]="'receive-' + line.purchaseOrderItemId" type="number" min="0" [max]="line.remainingQuantity" step="1" [disabled]="busy() || pending() || line.remainingQuantity === 0" [value]="quantities()[line.purchaseOrderItemId] ?? 0" (input)="quantity(line.purchaseOrderItemId, +$any($event.target).value)"></div></td> }</tr> }</tbody></table></div>
        @if (canReceive && (o.status === 2 || o.status === 5)) { <div class="form-grid"><div class="field"><label for="delivery-reference">Supplier delivery reference (optional)</label><input id="delivery-reference" maxlength="100" [disabled]="busy() || pending()" [value]="delivery()" (input)="delivery.set($any($event.target).value)"></div><div class="field"><label for="receipt-notes">Notes (optional)</label><textarea id="receipt-notes" rows="3" maxlength="1000" [disabled]="busy() || pending()" [value]="notes()" (input)="notes.set($any($event.target).value)"></textarea></div></div><p class="muted">Saving creates a receipt draft. Review and post the receipt to increase stock. Warehouse inventory must be enabled by your administrator.</p> }
      </div><div class="actions">@if (canReceive) { <a class="btn btn-ghost" routerLink="/goods-receipts" [queryParams]="{purchaseOrderId: o.id}">View receipts</a> } @if (canReceive && (o.status === 2 || o.status === 5)) { <button class="btn btn-primary" [disabled]="busy()">{{ busy() ? 'Saving…' : pending() ? 'Retry same receipt request' : 'Create receipt draft' }}</button> }</div></form> }
    </section>
  `,
})
export class ReceiveOrder {
  private readonly api = inject(ProcurementApi);
  private readonly router = inject(Router);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  protected readonly ref = inject(Reference);
  protected readonly canReceive = ['Administrator', 'WarehouseOperator'].includes(inject(Session).user()?.role ?? '');
  protected readonly order = signal<OperationalOrder | null>(null);
  protected readonly warehouse = signal('');
  protected readonly quantities = signal<Record<string, number>>({});
  protected readonly delivery = signal('');
  protected readonly notes = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly loading = signal(false);
  protected readonly pending = signal(false);
  protected readonly statuses = PO_STATUS;
  private body: object | null = null;
  constructor() { void this.load(); }
  protected quantity(id: string, value: number): void { this.quantities.update(q => ({ ...q, [id]: value })); }
  protected async load(): Promise<void> {
    this.loading.set(true); this.error.set('');
    try { const o = await this.api.get<OperationalOrder>(`purchase-orders/operational/${this.id}`); this.order.set(o); this.quantities.set({}); const w = await this.api.get<{ name: string }>(`warehouses/${o.warehouseId}`); this.warehouse.set(w.name); }
    catch (e) { this.error.set(problemMessage(e)); }
    finally { this.loading.set(false); }
  }
  protected async save(): Promise<void> {
    const o = this.order(); if (!o || !this.canReceive || this.busy()) return;
    const items = o.items.map(l => ({ purchaseOrderItemId: l.purchaseOrderItemId, quantity: this.quantities()[l.purchaseOrderItemId] ?? 0 })).filter(l => l.quantity !== 0);
    if (!items.length || items.some(l => !Number.isInteger(l.quantity) || l.quantity < 1 || l.quantity > o.items.find(i => i.purchaseOrderItemId === l.purchaseOrderItemId)!.remainingQuantity)) { this.error.set('Enter positive whole quantities within the remaining quantity for at least one product.'); return; }
    this.body ??= { idempotencyKey: crypto.randomUUID(), purchaseOrderId: o.id, purchaseOrderRowVersion: o.rowVersion, items, supplierDeliveryReference: this.delivery().trim() || null, notes: this.notes().trim() || null };
    this.busy.set(true); this.pending.set(true); this.error.set('');
    try { const receipt = await this.api.post<Receipt>('goods-receipts', this.body); await this.router.navigate(['/goods-receipts', receipt.id]); }
    catch (e) { const status = (e as { status?: number }).status; if (status && status >= 400 && status < 500 && status !== 408) { this.body = null; this.pending.set(false); } this.error.set(problemMessage(e)); }
    finally { this.busy.set(false); }
  }
}
