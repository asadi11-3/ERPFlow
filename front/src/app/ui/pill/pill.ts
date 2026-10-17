import { Component, input } from '@angular/core';

export type PillTone = 'green' | 'amber' | 'gray' | 'red' | 'blue';

@Component({
  selector: 'app-pill',
  template: '{{ label() }}',
  host: { '[attr.data-tone]': 'tone()' },
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      min-height: 26px;
      padding: 0 10px;
      border-radius: 999px;
      font-size: 0.8125rem;
      font-weight: 600;
      white-space: nowrap;
      background: var(--tint);
      color: var(--muted);
    }
    :host([data-tone='green']) { background: var(--success-soft); color: var(--success); }
    :host([data-tone='amber']) { background: var(--warn-soft); color: var(--warn-ink); }
    :host([data-tone='red']) { background: var(--danger-soft); color: var(--danger-ink); }
    :host([data-tone='blue']) { background: var(--primary-soft); color: var(--primary-strong); }
  `,
})
export class Pill {
  readonly label = input.required<string>();
  readonly tone = input<PillTone>('gray');
}
