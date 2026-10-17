import { DatePipe } from '@angular/common';
import { Component, computed, inject, input } from '@angular/core';

import { ACTION, HistoryEntry, shortId } from '../../core/api/models';
import { Session } from '../../core/auth/session';
import { Pill } from '../../ui/pill/pill';

@Component({
  selector: 'app-order-history',
  imports: [DatePipe, Pill],
  template: `
    @if (rows().length === 0) {
      <p class="empty">No activity yet.</p>
    } @else {
      <ol class="timeline">
        @for (row of rows(); track $index) {
          <li>
            <div class="timeline-head">
              <app-pill [label]="row.label" [tone]="row.tone" />
              <span class="muted">{{ row.entry.timestampUtc | date: 'd MMM y, h:mm a' }}</span>
            </div>
            <p class="timeline-actor">by {{ row.actor }}</p>
            @if (row.entry.remarks) {
              <blockquote>{{ row.entry.remarks }}</blockquote>
            }
          </li>
        }
      </ol>
    }
  `,
})
export class OrderHistory {
  private readonly session = inject(Session);
  readonly entries = input.required<HistoryEntry[]>();

  protected readonly rows = computed(() => {
    const me = this.session.user()?.id;
    return this.entries().map((entry) => ({
      entry,
      label: ACTION[entry.action]?.label ?? 'Event',
      tone: ACTION[entry.action]?.tone ?? 'gray',
      actor: entry.actorId === me ? 'You' : shortId(entry.actorId),
    }));
  });
}
