import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { Api } from '../../core/api/api';
import { JOBCARD_PRIORITY, JOBCARD_STATUS, shortId } from '../../core/api/models';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { Session } from '../../core/auth/session';
import { Icon } from '../../ui/icon/icon';
import { Pill } from '../../ui/pill/pill';
import { Select, SelectOption } from '../../ui/select/select';

@Component({
  selector: 'app-jobcard-detail',
  imports: [RouterLink, DatePipe, DecimalPipe, CurrencyPipe, Icon, Pill, Select],
  template: `
    <section class="page">
      <a class="back" routerLink="/service"><app-icon name="arrow-left" /> Service Jobcards</a>

      @if (data.status() === 'error') {
        <div class="alert" role="alert">
          <app-icon name="alert" />
          <p>{{ error() }} <button type="button" class="btn btn-ghost" (click)="data.reload()">Retry</button></p>
        </div>
      } @else if (jobcard(); as j) {
        <header class="page-head">
          <div>
            <h1>{{ j.ticketNumber }}</h1>
            <p class="detail-meta">
              <app-pill [label]="statusMap[j.status]?.label ?? 'Unknown'" [tone]="statusMap[j.status]?.tone ?? 'gray'" />
              <app-pill [label]="priorityMap[j.priority]?.label ?? 'Unknown'" [tone]="priorityMap[j.priority]?.tone ?? 'gray'" />
              <span class="muted">Opened {{ j.createdAt | date: 'd MMM y, h:mm a' }}</span>
            </p>
          </div>
          @if (j.status !== 3 && j.status !== 4 && canSignOff()) {
            <button type="button" class="btn btn-primary" [attr.aria-expanded]="completionFormOpen()" [attr.aria-controls]="completionFormOpen() ? 'completion-form' : null" (click)="openCompletion()">
              <app-icon name="check" /> Complete jobcard
            </button>
          }
        </header>

        <div class="split">
          <div class="stack">
            <section class="card">
              <h2>Reported fault</h2>
              <p class="prose fault">{{ j.reportedFault }}</p>
            </section>

            <section class="card">
              <div class="section-head">
                <h2>Spare parts <span class="muted">({{ j.items.length }})</span></h2>
                @if (j.status !== 3 && j.status !== 4 && canSignOff()) {
                  <button type="button" class="btn btn-ghost" [attr.aria-expanded]="partFormOpen()" [attr.aria-controls]="partFormOpen() ? 'part-form' : null" (click)="openPart()">
                    <app-icon name="plus" /> Request Part
                  </button>
                }
              </div>

              @if (j.items.length === 0) {
                <p class="empty">No parts requested for this jobcard.</p>
              } @else {
                <div class="table-wrap wide">
                  <table class="table dotted">
                    <thead>
                      <tr>
                        <th>Part / Item Name</th>
                        <th class="num">Qty</th>
                        <th class="num">Unit Price</th>
                        <th class="num">Line Total</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (item of j.items; track item.id) {
                        <tr>
                          <td class="main">{{ ref.productName(item.productId) }}</td>
                          <td class="num" data-label="Qty">{{ item.quantity }}</td>
                          <td class="num" data-label="Unit price">{{ item.unitPrice | currency }}</td>
                          <td class="num" data-label="Line total"><strong>{{ item.lineTotal | currency }}</strong></td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }

              @if (partFormOpen()) {
                <form id="part-form" class="inline-form" (submit)="addPart($event)">
                  <h3>Request a spare part</h3>
                  <p class="muted form-hint">Stock is reserved when the request succeeds.</p>
                  <div class="field">
                    <label for="partSelect">Product</label>
                    <app-select
                      inputId="partSelect"
                      [options]="productOptions()"
                      [value]="selectedProductId()"
                      [disabled]="busy() || ref.loading() || ref.failed()"
                      (valueChange)="selectedProductId.set($event)"
                    />
                  </div>
                  <div class="field quantity-field">
                    <label for="partQty">Quantity</label>
                    <input id="partQty" type="number" min="1" step="1" required [value]="partQuantity()" [disabled]="busy()" (input)="partQuantity.set(+$any($event.target).value)" />
                  </div>
                  @if (ref.failed()) {
                    <div class="alert" role="alert"><app-icon name="alert" /><p>Could not load products. <button type="button" class="btn btn-ghost" (click)="ref.reload()">Retry</button></p></div>
                  }
                  @if (actionError()) {
                    <div class="alert" role="alert"><app-icon name="alert" /><p>{{ actionError() }}</p></div>
                  }
                  <div class="actions">
                    <button type="submit" class="btn btn-primary" [disabled]="busy() || ref.loading() || ref.failed() || !selectedProductId() || invalidPartQuantity()">
                      {{ busy() ? 'Requesting...' : 'Request part' }}
                    </button>
                    <button type="button" class="btn btn-ghost" [disabled]="busy()" (click)="partFormOpen.set(false)">Cancel</button>
                  </div>
                </form>
              }
            </section>

            @if (completionFormOpen() && j.status !== 3 && j.status !== 4) {
              <section class="card" id="completion-form">
                <h2>Complete jobcard</h2>
                <p class="muted form-hint">Notes and a final meter reading are optional.</p>
                <form class="completion-form" (submit)="complete($event)">
                  <div class="field">
                    <label for="notes">Resolution notes</label>
                    <textarea id="notes" rows="4" placeholder="Work performed (optional)" [value]="completionNotes()" [disabled]="busy()" (input)="completionNotes.set($any($event.target).value)"></textarea>
                  </div>
                  <div class="field quantity-field">
                    <label for="meter">Final meter reading</label>
                    <input id="meter" type="number" min="0" step="1" placeholder="Optional" [value]="completionMeter()" [disabled]="busy()" (input)="completionMeter.set($any($event.target).value)" />
                  </div>
                  @if (actionError()) {
                    <div class="alert" role="alert"><app-icon name="alert" /><p>{{ actionError() }}</p></div>
                  }
                  <div class="actions">
                    <button type="submit" class="btn btn-primary" [disabled]="busy()">{{ busy() ? 'Completing...' : 'Complete jobcard' }}</button>
                    <button type="button" class="btn btn-ghost" [disabled]="busy()" (click)="completionFormOpen.set(false)">Cancel</button>
                  </div>
                </form>
              </section>
            }

            @if (j.status === 3) {
              <section class="card">
                <h2>Resolution</h2>
                <dl class="record-list">
                  <div><dt>Completed</dt><dd>{{ j.completedAt | date: 'd MMM y, h:mm a' }}</dd></div>
                  @if (j.finalMeterReading !== null) {
                    <div><dt>Final meter reading</dt><dd>{{ j.finalMeterReading | number }}</dd></div>
                  }
                  <div><dt>Technician notes</dt><dd class="prose">{{ j.technicianNotes || 'No notes recorded.' }}</dd></div>
                </dl>
              </section>
            }
          </div>

          <aside class="stack">
            <section class="card">
              <h2>Call details</h2>
              <dl class="record-list compact">
                <div><dt>Customer</dt><dd>{{ ref.customerName(j.customerId) }}</dd></div>
                <div><dt>Technician</dt><dd>{{ j.technicianName }}</dd></div>
                <div><dt>Opened</dt><dd>{{ j.createdAt | date: 'd MMM y, h:mm a' }}</dd></div>
              </dl>
            </section>
            <section class="card">
              <h2>Linked machine</h2>
              @if (j.machineAssetId; as assetId) {
                <a class="row-link" [routerLink]="['/assets', assetId]">{{ shortId(assetId) }} · View machine</a>
              } @else {
                <p class="muted">No machine linked to this jobcard.</p>
              }
            </section>
          </aside>
        </div>
      } @else {
        <div class="stack" role="status" aria-label="Loading service jobcard">
          <span class="skeleton" style="height: 32px; width: 280px"></span>
          <div class="split">
            <span class="skeleton" style="height: 320px"></span>
            <span class="skeleton" style="height: 320px"></span>
          </div>
        </div>
      }
    </section>

  `,
  styles: `
    .prose { white-space: pre-wrap; line-height: 1.5; overflow-wrap: anywhere; }
    .fault { margin: 0; max-width: 72ch; }
    .detail-meta { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .section-head { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .section-head h2 { margin: 0; }
    .form-hint { margin: 0 0 16px; font-size: 0.875rem; }
    .inline-form { display: grid; gap: 16px; margin-top: 20px; padding-top: 20px; border-top: 1px dotted var(--line); }
    .inline-form h3 { margin: 0; font-size: 1rem; }
    .inline-form .form-hint { margin: -12px 0 0; }
    .completion-form { display: grid; gap: 16px; }
    .quantity-field { max-width: 220px; }
    .compact > div { grid-template-columns: minmax(0, 1fr); gap: 4px; }
  `,
})
export class JobcardDetail {
  private readonly api = inject(Api);
  private readonly session = inject(Session);
  protected readonly ref = inject(Reference);

  readonly id = input.required<string>();

  protected readonly canSignOff = computed(() => {
    const role = this.session.user()?.role;
    return role === 'Technician' || role === 'Administrator';
  });

  protected readonly priorityMap = JOBCARD_PRIORITY;
  protected readonly statusMap = JOBCARD_STATUS;
  protected readonly shortId = shortId;

  protected readonly busy = signal(false);
  protected readonly actionError = signal('');

  protected readonly partFormOpen = signal(false);
  protected readonly selectedProductId = signal('');
  protected readonly partQuantity = signal(1);
  protected readonly invalidPartQuantity = computed(
    () => !Number.isInteger(this.partQuantity()) || this.partQuantity() <= 0,
  );

  protected readonly completionFormOpen = signal(false);
  protected readonly completionNotes = signal('');
  protected readonly completionMeter = signal('');

  protected readonly data = rxResource({
    params: () => ({ id: this.id() }),
    stream: ({ params }) => this.api.jobcard(params.id),
  });

  protected readonly jobcard = computed(() => this.data.value() ?? null);
  protected readonly error = computed(() => problemMessage(this.data.error()));

  protected readonly productOptions = computed<SelectOption[]>(() => {
    return [
      { value: '', label: 'Select product/spare...' },
      ...this.ref.products().map((p) => ({
        value: p.id,
        label: `${p.name} (${p.sku}) — Available: ${p.availableStock}`,
      })),
    ];
  });

  protected openPart(): void {
    this.actionError.set('');
    this.completionFormOpen.set(false);
    this.partFormOpen.update((open) => !open);
  }

  protected openCompletion(): void {
    this.actionError.set('');
    this.partFormOpen.set(false);
    this.completionFormOpen.update((open) => !open);
  }

  protected async addPart(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.selectedProductId() || this.invalidPartQuantity()) {
      this.actionError.set('Select a part and enter a positive whole quantity.');
      return;
    }
    if (this.ref.failed()) {
      this.actionError.set('Could not load products. Retry before requesting a part.');
      return;
    }

    this.busy.set(true);
    this.actionError.set('');
    try {
      await firstValueFrom(
        this.api.requestJobcardParts(this.id(), [
          { productId: this.selectedProductId(), quantity: this.partQuantity() },
        ]),
      );
      this.ref.reload();
      this.data.reload();
      this.partFormOpen.set(false);
      this.selectedProductId.set('');
      this.partQuantity.set(1);
    } catch (err) {
      this.actionError.set(problemMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  protected async complete(event: Event): Promise<void> {
    event.preventDefault();
    const meter = this.completionMeter().trim();
    if (meter && (!Number.isInteger(Number(meter)) || Number(meter) < 0)) {
      this.actionError.set('Enter a non-negative whole number for the meter reading.');
      return;
    }
    this.busy.set(true);
    this.actionError.set('');
    try {
      await firstValueFrom(
        this.api.completeJobcard(
          this.id(),
          this.completionNotes().trim() || null,
          meter === '' ? null : Number(meter),
        ),
      );
      this.data.reload();
      this.completionFormOpen.set(false);
    } catch (err) {
      this.actionError.set(problemMessage(err));
    } finally {
      this.busy.set(false);
    }
  }
}
