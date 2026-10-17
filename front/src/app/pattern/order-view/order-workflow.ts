import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';

import { SalesOrder } from '../../core/api/models';

interface Step {
  label: string;
  note: string;
  date: string | null | undefined;
  state: 'done' | 'current' | 'todo' | 'rejected';
}

@Component({
  selector: 'app-order-workflow',
  imports: [DatePipe],
  template: `
    <ol class="steps">
      @for (step of steps(); track step.label) {
        <li [attr.data-state]="step.state">
          <span class="dot" aria-hidden="true"></span>
          <div>
            <strong>{{ step.label }}</strong>
            <span class="muted">{{ step.note }}</span>
            @if (step.date) {
              <span class="muted">{{ step.date | date: 'd MMM y, h:mm a' }}</span>
            }
          </div>
        </li>
      }
    </ol>
  `,
  styles: `
    .steps { list-style: none; margin: 0; padding: 0; display: grid; gap: 0; }
    li { display: grid; grid-template-columns: 20px 1fr; gap: 12px; padding-bottom: 20px; position: relative; }
    li:last-child { padding-bottom: 0; }
    li:not(:last-child)::before {
      content: '';
      position: absolute;
      left: 9px;
      top: 20px;
      bottom: 0;
      width: 2px;
      background: var(--line);
    }
    .dot { width: 20px; height: 20px; border-radius: 50%; border: 2px solid var(--line); background: var(--surface); margin-top: 2px; }
    li[data-state='done'] .dot { background: var(--success); border-color: var(--success); }
    li[data-state='current'] .dot { border-color: var(--primary); box-shadow: 0 0 0 4px var(--primary-soft); }
    li[data-state='rejected'] .dot { background: var(--danger); border-color: var(--danger); }
    div { display: flex; flex-direction: column; gap: 2px; }
    strong { color: var(--ink); }
    .muted { font-size: 0.8125rem; color: var(--muted); }
  `,
})
export class OrderWorkflow {
  readonly order = input.required<SalesOrder>();

  protected readonly steps = computed<Step[]>(() => {
    const o = this.order();
    const created: Step = { label: 'Order created', note: 'Draft saved', date: o.createdAt, state: 'done' };
    const submitted: Step = {
      label: 'Submitted for approval',
      note: o.status === 0 ? 'Not submitted yet' : 'Stock reserved',
      date: o.submittedAt,
      state: o.status === 0 ? 'current' : 'done',
    };
    const decision: Step =
      o.status === 3
        ? { label: 'Rejected', note: 'Reservations released', date: o.decidedAt, state: 'rejected' }
        : {
            label: 'Approved',
            note: o.status === 2 ? 'Reservations kept' : 'Awaiting a decision',
            date: o.decidedAt,
            state: o.status === 2 ? 'done' : o.status === 1 ? 'current' : 'todo',
          };
    return [created, submitted, decision];
  });
}
