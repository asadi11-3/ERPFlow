import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { Session } from '../../core/auth/session';
import { Select } from '../../ui/select/select';
import { PurchaseLine, PurchaseOrder, Supplier, Transfer, Warehouse, validLines } from '../../core/procurement/models';
import { ProcurementApi } from '../../core/procurement/procurement-api';

@Component({
  selector: 'app-purchase-form', imports: [RouterLink, DecimalPipe, Select],
  template: `
    <section class="page"><header class="page-head"><div><a [routerLink]="base">← {{ transfer ? 'Stock transfers' : 'Purchase orders' }}</a><h1>{{ transfer ? 'New stock transfer' : id ? 'Edit purchase order' : 'New purchase order' }}</h1><p>{{ transfer ? 'Create a draft. Stock moves only when you post it.' : 'Prepare supplier, destination and quantities before review.' }}</p></div></header>
      @if (loading()) { <div class="card loading-lines" role="status" aria-label="Loading form"><span class="skeleton"></span><span class="skeleton"></span></div> }
      @else if (loadError()) { <div class="alert" role="alert"><p>{{ loadError() }}</p><button class="btn btn-ghost" (click)="load()">Reload form</button></div> }
      @else {
        @if (ref.failed()) { <div class="alert" role="alert">Product choices could not load. <button class="btn btn-ghost" (click)="ref.reload()">Retry products</button></div> }
        <form class="stack" (submit)="$event.preventDefault(); save()">
          <fieldset class="card stack" [disabled]="busy() || locked()">
            <h2>{{ transfer ? 'Transfer route' : 'Order details' }}</h2><div class="form-grid">
              <div class="field"><label for="order-party">{{ transfer ? 'Source warehouse' : 'Supplier' }}</label><app-select inputId="order-party" [searchable]="true" [options]="partyOptions()" [(value)]="party" [disabled]="busy() || locked()" /></div>
              <div class="field"><label for="order-destination">{{ transfer ? 'Destination warehouse' : 'Receiving warehouse' }}</label><app-select inputId="order-destination" [searchable]="true" [options]="destinationOptions()" [(value)]="destination" [disabled]="busy() || locked()" /></div>
            </div>
            @if (!transfer) { <div class="field"><label for="order-notes">Notes <span class="muted">(optional)</span></label><textarea id="order-notes" rows="3" maxlength="1000" [value]="notes()" (input)="notes.set($any($event.target).value)"></textarea></div> }
          </fieldset>
          <fieldset class="card stack" [disabled]="busy() || locked()"><div class="section-heading"><h2>Line items</h2><span class="muted">{{ lines().length }} / 100</span></div>
            @for (line of lines(); track line.key; let i = $index) {
              <div class="line-editor"><div class="field"><label [for]="'product-' + line.key">Product {{ i + 1 }}</label><app-select [inputId]="'product-' + line.key" [searchable]="true" [options]="productOptions()[i]" [value]="line.productId" (valueChange)="change(i, 'productId', $event)" [disabled]="busy() || locked()" /></div>
                <div class="field"><label [for]="'quantity-' + line.key">Quantity</label><input [id]="'quantity-' + line.key" type="number" required min="1" max="2147483647" step="1" [value]="line.quantity" (input)="change(i, 'quantity', +$any($event.target).value)"></div>
                @if (!transfer) { <div class="field"><label [for]="'cost-' + line.key">Unit cost</label><input [id]="'cost-' + line.key" type="number" required min="0" max="999999999999999" step="0.0001" [value]="line.unitCost" (input)="change(i, 'unitCost', +$any($event.target).value)"></div> }
                <button class="btn btn-ghost" type="button" [attr.aria-label]="'Remove line ' + (i + 1)" (click)="remove(i)">Remove</button>
              </div>
            }
            <button class="btn btn-ghost" type="button" [disabled]="lines().length >= 100" (click)="add()">Add product</button>
            @if (!transfer) { <div class="document-summary"><span>Estimated total</span><strong>{{ estimate() | number:'1.2-4' }}</strong></div><p class="muted">The server confirms costs and totals when saved.</p> }
          </fieldset>
          @if (error()) { <p class="alert" role="alert">{{ error() }}</p> }
          @if (locked()) { <p role="status">A transfer request is awaiting confirmation. Retry uses the same payload and request key.</p> }
          <div class="actions"><a class="btn btn-ghost" [routerLink]="base">Back to list</a><button class="btn btn-primary" [disabled]="busy() || ref.loading() || ref.failed()">{{ busy() ? 'Saving…' : locked() ? 'Retry same request' : 'Save draft' }}</button></div>
        </form>
      }
    </section>
  `,
})
export class PurchaseForm {
  protected readonly partyOptions = computed(() => [{ value: '', label: this.transfer ? 'Choose a warehouse' : 'Choose a supplier' }, ...(this.transfer ? this.warehouses().map(w => ({ value: w.id, label: `${w.code} · ${w.name}${w.isActive ? '' : ' (inactive)'}`, disabled: !w.isActive })) : this.suppliers().map(s => ({ value: s.id, label: `${s.supplierCode} · ${s.name}${s.isActive ? '' : ' (inactive)'}`, disabled: !s.isActive })))]);
  protected readonly destinationOptions = computed(() => [{ value: '', label: 'Choose a warehouse' }, ...this.warehouses().map(w => ({ value: w.id, label: `${w.code} · ${w.name}${w.isActive ? '' : ' (inactive)'}`, disabled: !w.isActive || (this.transfer && w.id === this.party()) }))]);
  protected readonly productOptions = computed(() => this.lines().map((_, index, lines) => [{ value: '', label: 'Choose a product' }, ...this.ref.products().map(p => ({ value: p.id, label: `${p.sku} · ${p.name}`, disabled: lines.some((line, i) => i !== index && line.productId === p.id) }))]));
  private readonly api = inject(ProcurementApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly session = inject(Session);
  protected readonly ref = inject(Reference);
  protected readonly transfer = !!this.route.snapshot.data['transfer'];
  protected readonly id = this.route.snapshot.paramMap.get('id');
  protected readonly base = this.transfer ? '/stock-transfers' : '/purchase-orders';
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly suppliers = signal<Supplier[]>([]);
  protected readonly party = signal('');
  protected readonly destination = signal('');
  protected readonly notes = signal('');
  protected readonly lines = signal<(PurchaseLine & { key: number })[]>([{ key: 0, productId: '', quantity: 1, unitCost: 0 }]);
  protected readonly estimate = computed(() => this.lines().reduce((sum, l) => sum + l.quantity * l.unitCost, 0));
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly locked = signal(false);
  protected readonly error = signal('');
  protected readonly loadError = signal('');
  private version = '';
  private nextKey = 1;
  private pending: { key: string; body: object } | null = null;
  constructor() { void this.load(); }
  protected change(index: number, field: keyof PurchaseLine, value: string | number): void { this.lines.update(lines => lines.map((line, i) => i === index ? { ...line, [field]: value } : line)); }
  protected add(): void { this.lines.update(lines => [...lines, { key: this.nextKey++, productId: '', quantity: 1, unitCost: 0 }]); }
  protected remove(index: number): void { this.lines.update(lines => lines.filter((_, i) => i !== index)); }
  protected async load(): Promise<void> {
    this.loading.set(true); this.loadError.set('');
    try {
      const [warehouses, suppliers] = await Promise.all([this.api.warehouses(), this.transfer ? Promise.resolve([]) : this.api.suppliers()]);
      this.warehouses.set(warehouses); this.suppliers.set(suppliers);
      if (this.id) {
        const order = await this.api.get<PurchaseOrder>(`purchase-orders/${this.id}`);
        const user = this.session.user();
        if (order.status !== 0 || (user?.role !== 'Administrator' && order.createdByUserId !== user?.id)) throw new Error('Only an editable draft can be changed.');
        this.party.set(order.supplierId); this.destination.set(order.warehouseId); this.notes.set(order.notes ?? ''); this.version = order.rowVersion;
        this.lines.set(order.items.map(l => ({ key: this.nextKey++, productId: l.productId, quantity: l.quantity, unitCost: l.unitCost })));
      }
    } catch (e) { this.loadError.set(e instanceof Error && !( 'status' in e) ? e.message : problemMessage(e)); }
    finally { this.loading.set(false); }
  }
  protected async save(): Promise<void> {
    if (this.busy()) return;
    if (!this.party() || !this.destination() || (this.transfer && this.party() === this.destination()) || !validLines(this.lines())) { this.error.set('Choose valid locations and enter 1–100 distinct products with positive whole quantities and nonnegative costs (up to 4 decimal places).'); return; }
    this.busy.set(true); this.error.set('');
    try {
      if (this.transfer) {
        this.pending ??= { key: crypto.randomUUID(), body: { sourceWarehouseId: this.party(), destinationWarehouseId: this.destination(), items: this.lines().map(l => ({ productId: l.productId, quantity: l.quantity })) } };
        this.locked.set(true);
        const saved = await this.api.post<Transfer>('stock-transfers', this.pending.body, this.pending.key);
        await this.router.navigate([this.base, saved.id]);
      } else {
        const body = { supplierId: this.party(), warehouseId: this.destination(), notes: this.notes().trim() || null, items: this.lines().map(({ productId, quantity, unitCost }) => ({ productId, quantity, unitCost })) };
        const saved = this.id ? await this.api.put<PurchaseOrder>(`purchase-orders/${this.id}`, { ...body, rowVersion: this.version }) : await this.api.post<PurchaseOrder>('purchase-orders', body);
        await this.router.navigate([this.base, saved.id]);
      }
    } catch (e) {
      const status = (e as { status?: number }).status;
      if (this.transfer && status && status >= 400 && status < 500 && status !== 408 && status !== 409) { this.pending = null; this.locked.set(false); }
      this.error.set(problemMessage(e) + (status === 409 ? ' Your entries are preserved. Review the current document before trying again.' : ''));
    } finally { this.busy.set(false); }
  }
}
