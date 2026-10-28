import { Component, computed, effect, ElementRef, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import type { Product } from '../../core/api/models';
import { ProductImageState } from '../../core/api/product-image-state';
import { Session } from '../../core/auth/session';
import { Icon } from '../../ui/icon/icon';

@Component({
  selector: 'app-product-image',
  imports: [Icon],
  providers: [ProductImageState],
  host: { '(click)': 'backdrop($event)' },
  templateUrl: './product-image.html',
  styleUrl: './product-image.scss',
})
export class ProductImage {
  readonly product = input.required<Product>();
  readonly imageChanged = output<{ readonly id: string; readonly version: string | null }>();
  protected readonly state = inject(ProductImageState);
  protected readonly administrator = computed(() => this.session.user()?.role === 'Administrator');
  protected readonly open = signal(false);
  protected readonly confirmingDelete = signal(false);
  private readonly session = inject(Session);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  protected readonly canSave = computed(() => !!this.state.selectionUrl() && !this.state.busy() &&
    !this.state.loading() && !this.state.productMissing() && !this.state.imageStateUncertain() &&
    (!this.state.exists() || !!this.state.version()));

  constructor() {
    effect(() => {
      const product = this.product();
      const token = this.session.token();
      if (!token) this.open.set(false);
      untracked(() => this.state.connect(product));
    });
    effect(() => {
      const dialog = this.dialog().nativeElement;
      if (this.open() && !dialog.open) dialog.showModal();
      else if (!this.open() && dialog.open) dialog.close();
    });
    this.state.changed.pipe(takeUntilDestroyed()).subscribe((change) => this.imageChanged.emit(change));
  }

  protected show(): void {
    this.open.set(true);
    this.confirmingDelete.set(false);
    this.state.notice.set('');
    this.state.refresh();
  }

  protected close(): void {
    if (this.state.busy()) return;
    this.open.set(false);
    this.confirmingDelete.set(false);
    this.state.clearSelection();
  }

  protected cancel(event: Event): void {
    if (this.state.busy()) event.preventDefault();
    else this.close();
  }

  protected backdrop(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) this.close();
  }

  protected choose(event: Event): void {
    if (!(event.target instanceof HTMLInputElement)) return;
    const file = event.target.files?.item(0);
    if (file) this.state.select(file);
    event.target.value = '';
    this.confirmingDelete.set(false);
  }

  protected remove(): void {
    this.confirmingDelete.set(false);
    this.state.delete();
  }
}
