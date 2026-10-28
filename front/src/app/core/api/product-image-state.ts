import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { Subject, Subscription } from 'rxjs';

import { Session } from '../auth/session';
import type { Product } from './models';
import { imageFileError, productImageError } from './product-image-errors';
import { ProductImages, strongImageVersion } from './product-images';

@Injectable()
export class ProductImageState {
  private readonly api = inject(ProductImages);
  private readonly session = inject(Session);
  private readRequest: Subscription | undefined;
  private writeRequest: Subscription | undefined;
  private generation = 0;
  private id = '';
  private referenceKey = '';
  private file: File | null = null;
  readonly url = signal<string | null>(null);
  readonly selectionUrl = signal<string | null>(null);
  readonly selectedName = signal('');
  readonly version = signal<string | null>(null);
  readonly exists = signal(false);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly changed = new Subject<{ readonly id: string; readonly version: string | null }>();

  constructor() {
    effect(() => {
      this.session.token();
      untracked(() => this.reset());
    });
    inject(DestroyRef).onDestroy(() => { this.reset(); this.changed.complete(); });
  }

  connect(product: Product): void {
    const key = `${product.id}:${product.hasImage}:${product.imageVersion}`;
    if (this.referenceKey === key) return;
    this.referenceKey = key;
    if (this.id === product.id && this.exists() === !!product.hasImage &&
        this.version() === (product.imageVersion ?? null)) return;
    this.generation++;
    this.readRequest?.unsubscribe();
    this.writeRequest?.unsubscribe();
    this.busy.set(false);
    this.loading.set(false);
    this.id = product.id;
    this.exists.set(!!product.hasImage);
    this.version.set(strongImageVersion(product.imageVersion) ? product.imageVersion : null);
    this.clearSelection();
    this.clearUrl();
    if (product.hasImage) this.refresh();
  }

  refresh(preserveError = false): void {
    if (!this.id || this.busy() || !this.session.user()) return;
    this.readRequest?.unsubscribe();
    const generation = ++this.generation;
    const token = this.session.token();
    this.clearUrl();
    this.loading.set(true);
    if (!preserveError) this.error.set('');
    this.readRequest = this.api.read(this.id).subscribe({
      next: (image) => {
        if (generation !== this.generation || token !== this.session.token()) return;
        this.url.set(URL.createObjectURL(image.blob));
        this.version.set(image.version);
        this.exists.set(true);
        this.loading.set(false);
        if (image.version) this.publish(image.version);
        else this.error.set('The image version is unavailable. Refresh before replacing or deleting it.');
      },
      error: (error: unknown) => {
        if (generation !== this.generation || token !== this.session.token()) return;
        this.loading.set(false);
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.exists.set(false);
          this.version.set(null);
          this.publish(null);
        } else this.error.set(productImageError(error));
      },
    });
  }

  select(file: File): void {
    if (this.busy()) return;
    this.clearSelection();
    this.notice.set('');
    const error = imageFileError(file);
    this.error.set(error ?? '');
    if (error) return;
    this.file = file;
    this.selectedName.set(file.name);
    this.selectionUrl.set(URL.createObjectURL(file));
  }

  selectionFailed(): void {
    this.clearSelection();
    this.error.set('This file could not be displayed as an image. Choose a different file.');
  }

  displayFailed(): void {
    this.clearUrl();
    this.error.set('The image could not be displayed. Refresh to try again.');
  }

  clearSelection(): void {
    const url = this.selectionUrl();
    if (url) URL.revokeObjectURL(url);
    this.selectionUrl.set(null);
    this.selectedName.set('');
    this.file = null;
  }

  upload(): void {
    const file = this.file;
    if (!file || !this.canMutate()) return;
    this.beginWrite();
    const token = this.session.token();
    this.writeRequest = this.api.upload(this.id, file, this.version()).subscribe({
      next: (version) => {
        if (token !== this.session.token()) return;
        this.busy.set(false);
        this.clearSelection();
        this.exists.set(true);
        this.version.set(version);
        this.publish(version);
        this.notice.set('Product image saved.');
        this.refresh();
      },
      error: (error: unknown) => this.writeFailed(error, token),
    });
  }

  delete(): void {
    const version = this.version();
    if (!version || !this.canMutate()) return;
    this.beginWrite();
    const token = this.session.token();
    this.writeRequest = this.api.delete(this.id, version).subscribe({
      next: () => {
        if (token !== this.session.token()) return;
        this.busy.set(false);
        this.clearSelection();
        this.clearUrl();
        this.exists.set(false);
        this.version.set(null);
        this.publish(null);
        this.notice.set('Product image deleted.');
      },
      error: (error: unknown) => this.writeFailed(error, token),
    });
  }

  private canMutate(): boolean {
    return !this.busy() && !this.loading() && this.session.user()?.role === 'Administrator' &&
      (!this.exists() || this.version() !== null);
  }

  private beginWrite(): void {
    this.readRequest?.unsubscribe();
    this.generation++;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
  }

  private writeFailed(error: unknown, token: string | null): void {
    if (token !== this.session.token()) return;
    this.busy.set(false);
    this.error.set(productImageError(error));
    if (error instanceof HttpErrorResponse && [400, 413, 415].includes(error.status)) return;
    this.clearSelection();
    this.refresh(true);
  }

  private publish(version: string | null): void { this.changed.next({ id: this.id, version }); }

  private clearUrl(): void {
    const url = this.url();
    if (url) URL.revokeObjectURL(url);
    this.url.set(null);
  }

  private reset(): void {
    this.generation++;
    this.readRequest?.unsubscribe();
    this.writeRequest?.unsubscribe();
    this.clearUrl();
    this.clearSelection();
    this.loading.set(false);
    this.busy.set(false);
    this.version.set(null);
    this.exists.set(false);
    this.referenceKey = '';
  }
}
