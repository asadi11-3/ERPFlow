import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';

import { Api } from '../../core/api/api';
import { shortId } from '../../core/api/models';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { Avatar } from '../../ui/avatar/avatar';
import { Icon } from '../../ui/icon/icon';
import { Pill } from '../../ui/pill/pill';

@Component({
  selector: 'app-asset-detail',
  imports: [RouterLink, DatePipe, DecimalPipe, Avatar, Icon, Pill],
  template: `
    <section class="page">
      <a class="back" routerLink="/assets"><app-icon name="arrow-left" /> Machine Assets</a>

      @if (data.status() === 'error') {
        <div class="alert" role="alert">
          <app-icon name="alert" />
          <p>{{ error() }} <button type="button" class="btn btn-ghost" (click)="data.reload()">Retry</button></p>
        </div>
      } @else if (asset(); as a) {
        <header class="page-head">
          <div>
            <h1>{{ a.tagNumber }}</h1>
            <p>
              <span class="serial">{{ a.serialNumber }}</span>
              <span class="muted"> · Registered {{ a.installedAt | date: 'd MMM y' }}</span>
            </p>
          </div>
          <div class="actions">
            <a class="btn btn-primary" [routerLink]="['/service/new']" [queryParams]="{ assetId: a.id, customerId: a.customerId }">
              <app-icon name="plus" /> Open Service Ticket
            </a>
          </div>
        </header>

        <div class="split">
          <section class="card">
            <h2>Machine record</h2>
            <dl class="record-list">
              <div><dt>Serial number</dt><dd class="serial">{{ a.serialNumber }}</dd></div>
              <div><dt>Equipment model</dt><dd>{{ ref.productName(a.productId) }}</dd></div>
              <div><dt>Registered</dt><dd>{{ a.installedAt | date: 'd MMM y, h:mm a' }}</dd></div>
              <div><dt>Warranty expires</dt><dd>{{ a.warrantyExpiresAt | date: 'd MMM y' }}</dd></div>
              <div>
                <dt>Warranty</dt>
                <dd><app-pill [label]="isWarrantyActive(a.warrantyExpiresAt) ? 'Covered' : 'Expired'" [tone]="isWarrantyActive(a.warrantyExpiresAt) ? 'green' : 'amber'" /></dd>
              </div>
              <div><dt>Meter reading</dt><dd>{{ a.meterReading | number }}</dd></div>
              @if (a.salesOrderId) {
                <div><dt>Source order</dt><dd><a class="row-link" [routerLink]="['/orders', a.salesOrderId]">{{ shortOrderNo(a.salesOrderId) }}</a></dd></div>
              }
            </dl>
          </section>

          <aside class="card">
            <h2>Customer</h2>
            <div class="customer">
              <app-avatar [name]="ref.customerName(a.customerId)" />
              <div>
                <strong>{{ ref.customerName(a.customerId) }}</strong>
                @if (ref.customer(a.customerId)?.email; as email) {
                  <span class="muted">{{ email }}</span>
                } @else {
                  <span class="muted serial">{{ a.customerId }}</span>
                }
              </div>
            </div>
          </aside>
        </div>
      } @else {
        <div class="stack" role="status" aria-label="Loading machine asset">
          <span class="skeleton" style="height: 32px; width: 280px"></span>
          <div class="split">
            <span class="skeleton" style="height: 280px"></span>
            <span class="skeleton" style="height: 180px"></span>
          </div>
        </div>
      }
    </section>
  `,
  styles: `
    .serial { font-family: var(--mono); overflow-wrap: anywhere; }
    .customer { display: flex; align-items: center; gap: 12px; min-width: 0; }
    .customer > div { display: grid; min-width: 0; overflow-wrap: anywhere; }
    .customer strong { font-size: 1.0625rem; }
  `,
})
export class AssetDetail {
  private readonly api = inject(Api);
  protected readonly ref = inject(Reference);

  readonly id = input.required<string>();

  protected readonly data = rxResource({
    params: () => ({ id: this.id() }),
    stream: ({ params }) => this.api.machineAsset(params.id),
  });

  protected readonly asset = computed(() => this.data.value() ?? null);
  protected readonly error = computed(() => problemMessage(this.data.error()));

  protected isWarrantyActive(expiresAt: string): boolean {
    return new Date(expiresAt) > new Date();
  }

  protected shortOrderNo(id: string): string {
    return shortId(id);
  }
}
