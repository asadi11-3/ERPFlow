import { computed, inject, Injectable } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { forkJoin } from 'rxjs';

import { Session } from '../auth/session';
import { Api } from './api';
import { Customer, Product, shortId } from './models';

@Injectable({ providedIn: 'root' })
export class Reference {
  private readonly api = inject(Api);
  private readonly session = inject(Session);

  private readonly data = rxResource({
    params: () => (this.session.user() ? {} : undefined),
    stream: () => forkJoin({ customers: this.api.customers(), products: this.api.products() }),
  });

  readonly customers = computed<Customer[]>(() => (this.data.hasValue() ? this.data.value().customers : []));
  readonly products = computed<Product[]>(() => (this.data.hasValue() ? this.data.value().products : []));
  readonly loading = this.data.isLoading;
  readonly failed = computed(() => this.data.status() === 'error');

  private readonly customerById = computed(() => new Map(this.customers().map((c) => [c.id, c])));
  private readonly productById = computed(() => new Map(this.products().map((p) => [p.id, p])));

  customer(id: string): Customer | undefined {
    return this.customerById().get(id);
  }

  customerName(id: string): string {
    return this.customerById().get(id)?.name ?? shortId(id);
  }

  product(id: string): Product | undefined {
    return this.productById().get(id);
  }

  productName(id: string): string {
    return this.productById().get(id)?.name ?? shortId(id);
  }

  reload(): void {
    this.data.reload();
  }
}
