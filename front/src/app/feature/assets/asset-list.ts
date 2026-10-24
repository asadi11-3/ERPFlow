import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';

import { Api } from '../../core/api/api';
import { DeliveryNote, MachineAsset, shortId } from '../../core/api/models';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { Avatar } from '../../ui/avatar/avatar';
import { Icon } from '../../ui/icon/icon';
import { Pill } from '../../ui/pill/pill';

type AssetTab = 'assets' | 'deliveries';

@Component({
  selector: 'app-asset-list',
  imports: [RouterLink, DatePipe, DecimalPipe, Avatar, Icon, Pill],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Machine Assets</h1>
          <p>Trace machines registered on dispatch and their delivery notes.</p>
        </div>
      </header>

      <section class="card" aria-label="Equipment records">
        <div class="tabs" role="group" aria-label="Record type">
          <button
            type="button"
            class="tab"
            [attr.aria-pressed]="activeTab() === 'assets'"
            (click)="activeTab.set('assets')"
          >
            Machine Assets @if (!data.isLoading() && data.status() !== 'error') { ({{ assets().length }}) }
          </button>
          <button
            type="button"
            class="tab"
            [attr.aria-pressed]="activeTab() === 'deliveries'"
            (click)="activeTab.set('deliveries')"
          >
            Delivery Notes @if (!data.isLoading() && data.status() !== 'error') { ({{ deliveries().length }}) }
          </button>
        </div>

        @if (data.status() === 'error') {
          <div class="alert" role="alert">
            <app-icon name="alert" />
            <p>{{ error() }} <button type="button" class="btn btn-ghost" (click)="data.reload()">Retry</button></p>
          </div>
        } @else if (data.isLoading()) {
          <div role="status" aria-label="Loading machine assets and deliveries">
            @for (n of skeleton; track n) {
              <div class="skel-row" aria-hidden="true">
                <span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span>
              </div>
            }
          </div>
        } @else if (activeTab() === 'assets') {
          <p class="list-note" aria-live="polite">{{ warrantyActiveCount() }} covered · {{ warrantyExpiredCount() }} expired</p>
          @if (assets().length === 0) {
            <p class="empty">No machine assets registered yet. Dispatch an approved order to generate assets.</p>
          } @else {
            <div class="table-wrap wide">
              <table class="table dotted">
                <thead>
                  <tr>
                    <th>Tag #</th>
                    <th>Serial Number</th>
                    <th>Equipment Model</th>
                    <th>Customer</th>
                    <th class="num">Meter Reading</th>
                    <th>Warranty Until</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  @for (a of assets(); track a.id) {
                    <tr>
                      <td class="main">
                        <a class="row-link" [routerLink]="['/assets', a.id]">{{ a.tagNumber }}</a>
                      </td>
                      <td class="serial" data-label="Serial number">{{ a.serialNumber }}</td>
                      <td data-label="Equipment model">{{ ref.productName(a.productId) }}</td>
                      <td data-label="Customer">
                        <span class="cell-customer">
                          <app-avatar [name]="ref.customerName(a.customerId)" />
                          <span class="clip" [title]="ref.customerName(a.customerId)">{{ ref.customerName(a.customerId) }}</span>
                        </span>
                      </td>
                      <td class="num" data-label="Meter reading">{{ a.meterReading | number }}</td>
                      <td class="muted" data-label="Warranty until">{{ a.warrantyExpiresAt | date: 'd MMM y' }}</td>
                      <td data-label="Warranty">
                        <app-pill
                          [label]="isWarrantyActive(a.warrantyExpiresAt) ? 'Covered' : 'Expired'"
                          [tone]="isWarrantyActive(a.warrantyExpiresAt) ? 'green' : 'gray'"
                        />
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        } @else {
          @if (deliveries().length === 0) {
            <p class="empty">No delivery notes generated.</p>
          } @else {
            <div class="table-wrap wide">
              <table class="table dotted">
                <thead>
                  <tr>
                    <th>Delivery #</th>
                    <th>Sales Order</th>
                    <th>Customer</th>
                    <th>Carrier</th>
                    <th>Tracking #</th>
                    <th>Dispatched Date</th>
                  </tr>
                </thead>
                <tbody>
                  @for (d of deliveries(); track d.id) {
                    <tr>
                      <td class="main serial">{{ d.deliveryNumber }}</td>
                      <td data-label="Sales order">
                        <a class="row-link" [routerLink]="['/orders', d.salesOrderId]">{{ shortOrderNo(d.salesOrderId) }}</a>
                      </td>
                      <td data-label="Customer">{{ ref.customerName(d.customerId) }}</td>
                      <td data-label="Carrier">{{ d.carrier }}</td>
                      <td class="serial" data-label="Tracking number">{{ d.trackingNumber ?? '-' }}</td>
                      <td class="muted" data-label="Dispatched">{{ d.dispatchedAt | date: 'd MMM y, h:mm a' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
      </section>
    </section>
  `,
  styles: `
    .serial { font-family: var(--mono); font-size: 0.875rem; overflow-wrap: anywhere; }
    .tab[aria-pressed='true'] { color: var(--primary); border-bottom-color: var(--primary); }
    .list-note { margin: 0 0 12px; color: var(--muted); font-size: 0.875rem; }
    .table-wrap { overflow-x: auto; }
  `,
})
export class AssetList {
  private readonly api = inject(Api);
  protected readonly ref = inject(Reference);

  protected readonly activeTab = signal<AssetTab>('assets');
  protected readonly skeleton = [1, 2, 3, 4, 5];

  protected readonly data = rxResource({
    stream: () =>
      forkJoin({
        assets: this.api.machineAssets(),
        deliveries: this.api.deliveryNotes(),
      }),
  });

  protected readonly assets = computed<MachineAsset[]>(() => this.data.value()?.assets ?? []);
  protected readonly deliveries = computed<DeliveryNote[]>(() => this.data.value()?.deliveries ?? []);
  protected readonly error = computed(() => problemMessage(this.data.error()));

  protected isWarrantyActive(expiresAt: string): boolean {
    return new Date(expiresAt) > new Date();
  }

  protected shortOrderNo(id: string): string {
    return shortId(id);
  }

  protected readonly warrantyActiveCount = computed(
    () => this.assets().filter((a: MachineAsset) => this.isWarrantyActive(a.warrantyExpiresAt)).length,
  );

  protected readonly warrantyExpiredCount = computed(
    () => this.assets().filter((a: MachineAsset) => !this.isWarrantyActive(a.warrantyExpiresAt)).length,
  );
}
