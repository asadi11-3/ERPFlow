import { CurrencyPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { Api } from '../../core/api/api';
import { SalesOrder } from '../../core/api/models';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { Session } from '../../core/auth/session';
import { ConfirmDialog } from '../../ui/confirm-dialog/confirm-dialog';
import { Icon } from '../../ui/icon/icon';
import { Select, SelectOption } from '../../ui/select/select';

interface Line {
  key: number;
  productId: string;
  quantity: number;
}

@Component({
  selector: 'app-order-new',
  imports: [CurrencyPipe, RouterLink, ConfirmDialog, Icon, Select],
  template: `
    <section class="page">
      <a class="back" routerLink="/orders"><app-icon name="arrow-left" /> Sales Orders</a>
      <header class="page-head">
        <div>
          <h1>{{ editId ? 'Edit Draft Order' : 'Create Sales Order' }}</h1>
          <p>Pick a customer and add products. Prices come from the product catalog.</p>
        </div>
      </header>

      <div class="split">
        <div class="stack">
          <div class="card">
            <h2>1. Customer</h2>
            <div class="field">
              <label for="customer">Customer</label>
              <app-select
                inputId="customer"
                placeholder="Select a customer"
                [options]="customerOptions()"
                [value]="customerId()"
                [disabled]="locked()"
                (valueChange)="customerId.set($event)"
              />
            </div>
          </div>

          <div class="card">
            <h2>2. Items</h2>
            <div class="table-wrap wide order-item-editor">
              <table class="table">
                <thead>
                  <tr>
                    <th>Product</th>
                    <th class="num">Available</th>
                    <th class="num">Quantity</th>
                    <th class="num">Unit price</th>
                    <th class="num">Line total</th>
                    <th><span class="sr-only">Remove</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (line of lines(); track line.key) {
                    <tr>
                      <td class="main">
                        <app-select
                          placeholder="Select a product"
                          [ariaLabel]="'Product for line ' + ($index + 1)"
                          [options]="productOptions().get(line.key) ?? []"
                          [value]="line.productId"
                          [disabled]="locked()"
                          (valueChange)="setProduct(line.key, $event)"
                        />
                      </td>
                      <td class="num" data-label="Available"><span class="line-value">{{ ref.product(line.productId)?.availableStock ?? '-' }}</span></td>
                      <td class="num" data-label="Quantity">
                        <input
                          type="number"
                          min="1"
                          step="1"
                          class="qty"
                          [attr.aria-label]="'Quantity for line ' + ($index + 1)"
                          [value]="line.quantity"
                          [disabled]="locked()"
                          (input)="setQuantity(line.key, $any($event.target).value)"
                        />
                      </td>
                      <td class="num" data-label="Unit price"><span class="line-value">{{ (ref.product(line.productId)?.unitPrice ?? 0) | currency }}</span></td>
                      <td class="num" data-label="Line total"><span class="line-value">{{ lineTotal(line) | currency }}</span></td>
                      <td class="act">
                        <button
                          type="button"
                          class="icon-btn"
                          aria-label="Remove line"
                          [disabled]="locked() || lines().length === 1"
                          (click)="remove(line.key)"
                        >
                          <app-icon name="trash" />
                        </button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <button type="button" class="btn btn-ghost add" [disabled]="locked()" (click)="add()">
              <app-icon name="plus" /> Add Item
            </button>
          </div>
        </div>

        <aside class="stack">
          <div class="card">
            <h2>Order Summary</h2>
            <p class="summary-row">
              <span class="muted">Items</span><strong>{{ filled().length }}</strong>
            </p>
            <p class="summary-row total">
              <span>Total amount</span><strong>{{ total() | currency }}</strong>
            </p>
            <div class="alert alert-warn">
              <app-icon name="alert" />
              <p>Stock is reserved when you submit. It is released if the order is rejected and kept if approved.</p>
            </div>
          </div>

          <div class="card">
            <h2>Validation</h2>
            <ul class="checklist">
              <li [class.ok]="!!customerId()">Select a customer</li>
              <li [class.ok]="filled().length > 0">Add at least one item</li>
              <li [class.ok]="filled().length > 0 && quantitiesOk()">Items must have a valid quantity</li>
            </ul>
          </div>

          @if (error()) {
            <div class="alert" role="alert">
              <app-icon name="alert" />
              <p>{{ error() }}</p>
              @if (editId) {
                <button type="button" class="btn btn-ghost" [disabled]="busy() || loading()" (click)="loadDraft()">Reload draft</button>
              }
            </div>
          }

          @if (draft(); as d) {
            <div class="card">
              <h2>Draft saved</h2>
              <p class="muted">The order was saved but not submitted.</p>
              <div class="actions">
                <button type="button" class="btn btn-primary" [disabled]="busy()" (click)="submitDraft()">Retry submit</button>
                <a class="btn btn-ghost" [routerLink]="['/orders', d.id]">Open draft</a>
              </div>
            </div>
          } @else {
            <div class="actions">
              <button type="button" class="btn btn-ghost" [disabled]="!valid() || busy()" (click)="save(false)">Save Draft</button>
              <button type="button" class="btn btn-primary" [disabled]="!valid() || busy()" (click)="confirmOpen.set(true)">
                Submit for Approval
              </button>
            </div>
          }
        </aside>
      </div>
    </section>

    <app-confirm-dialog
      [(open)]="confirmOpen"
      [heading]="editId ? 'Save and resubmit this order?' : 'Create and submit this order?'"
      message="The draft is saved, stock is reserved for every line, and it enters the approval queue."
      confirmLabel="Submit"
      [busy]="busy()"
      (confirmed)="confirmSubmit()"
    />
  `,
  styles: `
    .qty { width: 88px; min-height: 44px; padding: 0 10px; border: 1px solid var(--line); border-radius: 10px; font: inherit; text-align: right; }
    .qty:focus-visible { outline-offset: -2px; border-color: var(--primary); }
    .line-value { display: flex; align-items: center; justify-content: flex-end; min-height: 44px; font-variant-numeric: tabular-nums; }
    @container (max-width: 940px) {
      .order-item-editor > .table tr { padding: 16px 0; gap: 16px; }
      .order-item-editor > .table td.main { padding-right: 56px; font-weight: 400; }
      .order-item-editor > .table td.act { top: 16px; }
      .order-item-editor > .table td[data-label]::before { margin-bottom: 8px; }
      .qty { width: 100%; }
      .line-value { justify-content: flex-start; padding: 0 12px; border-radius: 10px; background: var(--tint); }
    }
    .icon-btn { min-width: 44px; min-height: 44px; border: 1px solid var(--line); background: var(--surface); color: var(--ink); cursor: pointer; border-radius: 10px; transition: transform 160ms var(--ease-out); }
    .icon-btn:active:not(:disabled) { transform: scale(0.97); }
    .icon-btn:disabled { opacity: 0.35; cursor: default; }
    .add { margin-top: 14px; }
    aside .actions > .btn { flex: 1 1 100%; }
    .summary-row { display: flex; justify-content: space-between; margin: 0 0 10px; }
    .total { font-size: 1.125rem; padding-top: 10px; border-top: 1px solid var(--line); margin-bottom: 16px; }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
  `,
})
export class OrderNew {
  protected readonly ref = inject(Reference);
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly session = inject(Session);
  protected readonly editId: string | null = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly loading = signal(false);
  private readonly editing = signal<SalesOrder | null>(null);

  constructor() {
    if (this.editId) void this.loadDraft();
  }

  protected async loadDraft(): Promise<void> {
    if (!this.editId) return;
    this.loading.set(true);
    this.editing.set(null);
    this.error.set('');
    try {
      const order = await firstValueFrom(this.api.order(this.editId));
      const user = this.session.user();
      if (order.status !== 0 || !user || (user.role !== 'Administrator' && order.createdByUserId !== user.id)) {
        this.error.set('Only the owner or an administrator can edit a Draft order.');
        return;
      }
      this.customerId.set(order.customerId);
      this.lines.set(order.items.map((item, index) => ({ key: index + 1, productId: item.productId, quantity: item.quantity })));
      this.nextKey = order.items.length + 1;
      this.editing.set(order);
    } catch (err) {
      this.error.set(problemMessage(err));
    } finally {
      this.loading.set(false);
    }
  }

  private nextKey = 2;

  protected readonly customerId = signal('');
  protected readonly lines = signal<Line[]>([{ key: 1, productId: '', quantity: 1 }]);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly draft = signal<SalesOrder | null>(null);

  protected readonly locked = computed(() => this.busy() || this.loading() || this.draft() !== null || (!!this.editId && !this.editing()));
  protected readonly filled = computed(() => this.lines().filter((l) => l.productId));
  protected readonly quantitiesOk = computed(() =>
    this.filled().every((l) => Number.isInteger(l.quantity) && l.quantity > 0),
  );
  protected readonly valid = computed(
    () => !this.locked() && !!this.customerId() && this.filled().length > 0 && this.quantitiesOk(),
  );
  protected readonly total = computed(() => this.filled().reduce((sum, l) => sum + this.lineTotal(l), 0));

  protected readonly customerOptions = computed<SelectOption[]>(() =>
    this.ref.customers().map((c) => ({ value: c.id, label: `${c.name} (${c.email})` })),
  );
  protected readonly productOptions = computed(() => {
    const lines = this.lines();
    const products = this.ref.products();
    return new Map(
      lines.map((l) => [
        l.key,
        products.map((p) => ({
          value: p.id,
          label: `${p.name} (${p.sku})`,
          disabled: lines.some((o) => o.key !== l.key && o.productId === p.id),
        })),
      ]),
    );
  });

  protected lineTotal(line: Line): number {
    return (this.ref.product(line.productId)?.unitPrice ?? 0) * line.quantity;
  }

  protected setProduct(key: number, productId: string): void {
    this.lines.update((ls) => ls.map((l) => (l.key === key ? { ...l, productId } : l)));
  }

  protected setQuantity(key: number, value: string): void {
    this.lines.update((ls) => ls.map((l) => (l.key === key ? { ...l, quantity: Number(value) } : l)));
  }

  protected add(): void {
    this.lines.update((ls) => [...ls, { key: this.nextKey++, productId: '', quantity: 1 }]);
  }

  protected remove(key: number): void {
    this.lines.update((ls) => ls.filter((l) => l.key !== key));
  }

  protected readonly confirmOpen = signal(false);

  protected async confirmSubmit(): Promise<void> {
    await this.save(true);
    this.confirmOpen.set(false);
  }

  protected async save(thenSubmit: boolean): Promise<void> {
    if (!this.valid() || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const items = this.filled().map((l) => ({ productId: l.productId, quantity: l.quantity }));
      const editing = this.editing();
      const created = await firstValueFrom(editing
        ? this.api.updateDraft(editing.id, this.customerId(), items, editing.rowVersion)
        : this.api.create(this.customerId(), items));
      if (!thenSubmit) {
        await this.router.navigate(['/orders', created.id]);
        return;
      }
      this.draft.set(created);
      await this.submit(created);
    } catch (err) {
      this.error.set(problemMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  protected async submitDraft(): Promise<void> {
    const d = this.draft();
    if (!d) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.submit(d);
    } catch (err) {
      this.error.set(problemMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  private async submit(order: SalesOrder): Promise<void> {
    const submitted = await firstValueFrom(this.api.submit(order.id));
    this.ref.reload();
    await this.router.navigate(['/orders', submitted.id]);
  }
}
