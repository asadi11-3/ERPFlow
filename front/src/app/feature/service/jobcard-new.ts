import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { Api } from '../../core/api/api';
import { shortId } from '../../core/api/models';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { Icon } from '../../ui/icon/icon';
import { Select, SelectOption } from '../../ui/select/select';

const PRIORITY_OPTIONS: SelectOption[] = [
  { value: '0', label: 'Low' },
  { value: '1', label: 'Medium' },
  { value: '2', label: 'High' },
  { value: '3', label: 'Urgent' },
];

@Component({
  selector: 'app-jobcard-new',
  imports: [RouterLink, Icon, Select],
  template: `
    <section class="page">
      <a class="back" routerLink="/service"><app-icon name="arrow-left" /> Service Jobcards</a>

      <header class="page-head">
        <div>
          <h1>Open a service jobcard</h1>
          <p>Record the customer's reported fault. Parts and completion are managed on the jobcard.</p>
        </div>
      </header>

      @if (actionError()) {
        <div class="alert" role="alert">
          <app-icon name="alert" />
          <p>{{ actionError() }}</p>
        </div>
      }

      @if (ref.failed()) {
        <div class="alert" role="alert">
          <app-icon name="alert" />
          <p>Could not load customers. <button type="button" class="btn btn-ghost" (click)="ref.reload()">Retry</button></p>
        </div>
      }

      <div class="split ticket-layout">
        <form class="card ticket-form" (submit)="submit($event)">
          <div class="form-section">
            <h2>Customer & equipment</h2>
            <div class="field">
              <label for="customer">Customer</label>
              <app-select
                inputId="customer"
                [options]="customerOptions()"
                [value]="customerId()"
                [disabled]="busy() || ref.loading() || ref.failed()"
                (valueChange)="setCustomer($event)"
              />
              @if (ref.loading()) { <span class="muted" role="status">Loading customers...</span> }
            </div>
            @if (machineAssetId(); as assetId) {
              <p class="linked-asset">Linked machine <a class="row-link" [routerLink]="['/assets', assetId]">{{ shortId(assetId) }}</a></p>
            }
          </div>

          <div class="form-section">
            <h2>Service call</h2>
            <div class="field">
              <label for="fault">Reported fault</label>
              <textarea
                id="fault"
                rows="5"
                required
                placeholder="Describe the symptoms reported by the customer"
                [value]="fault()"
                [disabled]="busy()"
                (input)="fault.set($any($event.target).value)"
              ></textarea>
            </div>
            <div class="field">
              <label for="priority">Priority</label>
              <app-select
                inputId="priority"
                [options]="priorityOptions"
                [value]="priority()"
                [disabled]="busy()"
                (valueChange)="priority.set($event)"
              />
            </div>
          </div>

          <div class="form-section">
            <h2>Assignment</h2>
            <div class="field">
              <label for="technician">Technician (optional)</label>
              <input
                id="technician"
                type="text"
                autocomplete="off"
                placeholder="Leave blank if unassigned"
                [value]="technician()"
                [disabled]="busy()"
                (input)="technician.set($any($event.target).value)"
              />
            </div>
          </div>

          <div class="actions form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="busy() || ref.loading() || ref.failed() || !customerId() || !fault().trim()">
              {{ busy() ? 'Creating jobcard...' : 'Create jobcard' }}
            </button>
            <a class="btn btn-ghost" routerLink="/service">Cancel</a>
          </div>
        </form>

        <aside class="flow-guide" aria-label="Next steps">
          <h2>After creation</h2>
          <ol>
            <li><strong>Review the call</strong><span>Customer, fault and priority appear on the new jobcard.</span></li>
            <li><strong>Request parts if needed</strong><span>Add products to the jobcard while work is open.</span></li>
            <li><strong>Complete the jobcard</strong><span>Record optional notes and a final meter reading.</span></li>
          </ol>
        </aside>
      </div>
    </section>
  `,
  styles: `
    .ticket-layout { grid-template-columns: minmax(0, 2fr) minmax(220px, 1fr); }
    .ticket-form { display: grid; }
    .form-section { display: grid; gap: 16px; padding: 24px 0; border-bottom: 1px dotted var(--line); }
    .form-section:first-child { padding-top: 0; }
    .form-section h2 { margin: 0; }
    .form-actions { padding-top: 24px; }
    .linked-asset { margin: 0; color: var(--muted); overflow-wrap: anywhere; }
    .flow-guide { padding: 20px 8px; }
    .flow-guide h2 { margin: 0 0 20px; font-size: 1.0625rem; }
    .flow-guide ol { margin: 0; padding: 0; list-style: none; counter-reset: step; }
    .flow-guide li { display: grid; gap: 4px; padding: 16px 0; border-top: 1px dotted var(--line); counter-increment: step; }
    .flow-guide li::before { content: '0' counter(step); color: var(--primary); font-family: var(--mono); font-size: 0.875rem; }
    .flow-guide span { color: var(--muted); font-size: 0.875rem; line-height: 1.5; }
    @media (max-width: 960px) { .ticket-layout { grid-template-columns: minmax(0, 1fr); } .flow-guide { padding: 4px 8px; } }
  `,
})
export class JobcardNew {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly ref = inject(Reference);

  protected readonly customerId = signal(this.route.snapshot.queryParamMap.get('customerId') ?? '');
  protected readonly technician = signal('');
  protected readonly priority = signal('1');
  protected readonly fault = signal('');
  protected readonly machineAssetId = signal(this.route.snapshot.queryParamMap.get('assetId'));

  protected readonly priorityOptions = PRIORITY_OPTIONS;
  protected readonly busy = signal(false);
  protected readonly actionError = signal('');

  protected readonly shortId = shortId;

  protected readonly customerOptions = computed<SelectOption[]>(() => {
    const list = this.ref.customers();
    return [
      { value: '', label: 'Select customer...' },
      ...list.map((c) => ({ value: c.id, label: c.name })),
    ];
  });

  protected setCustomer(id: string): void {
    if (id !== this.customerId()) this.machineAssetId.set(null);
    this.customerId.set(id);
  }

  protected async submit(e: Event): Promise<void> {
    e.preventDefault();
    if (!this.customerId() || !this.fault().trim()) return;

    this.busy.set(true);
    this.actionError.set('');
    try {
      const result = await firstValueFrom(
        this.api.createJobcard({
          customerId: this.customerId(),
          machineAssetId: this.machineAssetId(),
          technicianName: this.technician().trim(),
          reportedFault: this.fault().trim(),
          priority: parseInt(this.priority(), 10),
        }),
      );
      await this.router.navigate(['/service', result.id]);
    } catch (err) {
      this.actionError.set(problemMessage(err));
    } finally {
      this.busy.set(false);
    }
  }
}
