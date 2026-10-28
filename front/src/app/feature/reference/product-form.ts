import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { ProductCatalog, catalogValidation } from '../../core/api/product-catalog';
import type { CatalogDraft, CatalogProduct } from '../../core/api/product-catalog';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { Session } from '../../core/auth/session';
import { ProductImage } from '../../pattern/product-image/product-image';
import { ConfirmDialog } from '../../ui/confirm-dialog/confirm-dialog';
import { Pill } from '../../ui/pill/pill';

@Component({
  selector: 'app-product-form',
  imports: [RouterLink, CurrencyPipe, DatePipe, ProductImage, ConfirmDialog, Pill],
  templateUrl: './product-form.html',
  styles: `
    .catalog-form { max-width: 880px; }
    fieldset { border: 0; padding: 0; margin: 0; min-width: 0; }
    .catalog-history { display: grid; gap: 16px; padding: 0; list-style: none; }
    .catalog-history li { padding-bottom: 16px; border-bottom: 1px solid var(--line); overflow-wrap: anywhere; }
    .catalog-image { display: flex; gap: 16px; align-items: center; }
    .catalog-image p { color: var(--muted); }
  `,
})
export class ProductForm {
  private readonly api = inject(ProductCatalog);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly session = inject(Session);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly ref = inject(Reference);
  protected readonly id = this.route.snapshot.paramMap.get('id');
  protected readonly readOnly = this.route.snapshot.data['readOnly'] === true;
  protected readonly canEdit = computed(() => !this.readOnly && this.session.user()?.role === 'Administrator');
  protected readonly isAdmin = computed(() => this.session.user()?.role === 'Administrator');
  protected readonly product = signal<CatalogProduct | null>(null);
  protected readonly draft = signal<CatalogDraft>({ name: '', sku: '', unitPrice: 0 });
  protected readonly priceText = signal('0');
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly loadError = signal('');
  protected readonly conflict = signal(false);
  protected readonly notice = signal('');
  protected readonly confirming = signal(false);
  protected readonly imageProduct = computed(() => {
    const product = this.product();
    return product ? { ...product, availableStock: 0 } : null;
  });
  private sequence = 0;

  constructor() { if (this.id) this.load(); }

  protected setName(value: string): void { this.draft.update(draft => ({ ...draft, name: value })); }
  protected setSku(value: string): void { this.draft.update(draft => ({ ...draft, sku: value })); }
  protected setPrice(value: string): void {
    this.priceText.set(value);
    this.draft.update(draft => ({ ...draft, unitPrice: value.trim() ? Number(value) : NaN }));
  }

  protected load(): void {
    if (!this.id || this.busy()) return;
    const sequence = ++this.sequence;
    this.loading.set(true);
    this.loadError.set('');
    this.api.get(this.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: product => {
        if (sequence !== this.sequence) return;
        this.product.set(product);
        this.draft.set({ name: product.name, sku: product.sku, unitPrice: product.unitPrice });
        this.priceText.set(String(product.unitPrice));
        this.error.set(''); this.conflict.set(false); this.loading.set(false);
      },
      error: (error: unknown) => {
        if (sequence !== this.sequence) return;
        this.loadError.set(problemMessage(error)); this.loading.set(false);
      },
    });
  }

  protected save(): void {
    if (!this.canEdit() || this.busy() || this.conflict()) return;
    const validation = catalogValidation(this.draft());
    this.error.set(validation ?? '');
    if (validation) return;
    this.busy.set(true); this.notice.set('');
    const product = this.product();
    const request = product ? this.api.update(product, this.draft()) : this.api.create(this.draft());
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: saved => {
        this.product.set(saved); this.busy.set(false); this.ref.reload();
        this.draft.set({ name: saved.name, sku: saved.sku, unitPrice: saved.unitPrice });
        this.priceText.set(String(saved.unitPrice));
        this.notice.set('Product saved. Existing transaction prices and inventory balances are unchanged.');
        if (!this.id) void this.router.navigate(['/reference/products', saved.id, 'edit']);
      },
      error: (error: unknown) => this.failed(error),
    });
  }

  protected changeStatus(): void {
    const product = this.product();
    if (!product || !this.canEdit() || this.busy() || this.conflict()) return;
    this.busy.set(true); this.error.set('');
    this.api.setActive(product).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: saved => {
        this.product.set(saved); this.busy.set(false); this.confirming.set(false);
        this.ref.reload(); this.notice.set(saved.isActive ? 'Product activated.' : 'Product deactivated. Existing commitments remain valid.');
      },
      error: (error: unknown) => { this.confirming.set(false); this.failed(error); },
    });
  }

  protected imageChanged(change: { readonly version: string | null }): void {
    this.product.update(product => product ? { ...product, hasImage: change.version !== null, imageVersion: change.version } : null);
    const product = this.product();
    if (product) this.ref.updateProductImage(product.id, change.version);
  }

  protected timestamp(value: string): string { return /(?:Z|[+-]\d\d:\d\d)$/.test(value) ? value : `${value}Z`; }

  private failed(error: unknown): void {
    this.busy.set(false);
    const conflict = this.product() !== null && error instanceof HttpErrorResponse && error.status === 409;
    this.conflict.set(conflict);
    this.error.set(problemMessage(error) + (conflict ? ' Your entries are preserved. Reload the current product before trying again.' : ''));
  }
}
