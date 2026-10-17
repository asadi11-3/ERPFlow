import { computed, inject, Injectable } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';

import { Session } from '../auth/session';
import { Api } from './api';
import { SalesOrder } from './models';

@Injectable({ providedIn: 'root' })
export class PendingQueue {
  private readonly api = inject(Api);
  private readonly session = inject(Session);

  private readonly data = rxResource({
    params: () => {
      const role = this.session.user()?.role;
      return role === 'Approver' || role === 'Administrator' ? {} : undefined;
    },
    stream: () => this.api.pending(),
  });

  readonly orders = computed<SalesOrder[]>(() =>
    this.data.hasValue()
      ? [...this.data.value()].sort((a, b) => (a.submittedAt ?? '').localeCompare(b.submittedAt ?? ''))
      : [],
  );
  readonly count = computed(() => this.orders().length);
  readonly loading = this.data.isLoading;
  readonly failed = computed(() => this.data.status() === 'error');

  reload(): void {
    this.data.reload();
  }
}
