import { Component, input } from '@angular/core';

@Component({
  selector: 'app-kpi',
  template: `
    <span class="label">{{ label() }}</span>
    <strong class="value">{{ value() }}</strong>
    @if (hint()) {
      <span class="hint">{{ hint() }}</span>
    }
  `,
  host: { '[attr.data-tone]': 'tone()' },
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 4px;
      padding: 16px 18px;
      border-radius: var(--radius);
      background: var(--surface);
      border: 1px solid var(--line);
    }
    :host([data-tone='blue']) { background: var(--primary-soft); }
    :host([data-tone='amber']) { background: var(--warn-soft); }
    :host([data-tone='green']) { background: var(--success-soft); }
    :host([data-tone='red']) { background: var(--danger-soft); }
    .label { font-size: 0.8125rem; color: color-mix(in oklch, var(--ink) 78%, transparent); font-weight: 600; }
    .value { font-family: var(--mono); font-size: 1.5rem; line-height: 1.2; color: var(--ink); font-variant-numeric: tabular-nums; }
    .hint { font-size: 0.8125rem; color: var(--muted); }
  `,
})
export class Kpi {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly hint = input<string>('');
  readonly tone = input<'blue' | 'amber' | 'green' | 'red' | 'gray'>('gray');
}
