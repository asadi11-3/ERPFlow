import { Component, computed, input, model } from '@angular/core';

import { Icon } from '../icon/icon';

@Component({
  selector: 'app-pagination',
  imports: [Icon],
  template: `
    <nav aria-label="Pagination">
      <button type="button" aria-label="Previous page" [disabled]="page() <= 1" (click)="go(page() - 1)">
        <app-icon name="arrow-left" />
      </button>
      @for (p of window(); track $index) {
        @if (p === 0) {
          <span class="gap" aria-hidden="true">...</span>
        } @else {
          <button type="button" [class.on]="p === page()" [attr.aria-current]="p === page() ? 'page' : null" (click)="go(p)">
            {{ p }}
          </button>
        }
      }
      <button type="button" aria-label="Next page" [disabled]="page() >= pages()" (click)="go(page() + 1)">
        <app-icon name="arrow-right" />
      </button>
    </nav>
  `,
  styles: `
    nav { display: flex; gap: 6px; align-items: center; }
    button {
      min-width: 44px;
      min-height: 44px;
      border: 1px solid var(--line);
      border-radius: 10px;
      background: var(--surface);
      color: var(--ink);
      font: inherit;
      font-family: var(--mono);
      font-weight: 600;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      transition: transform 160ms var(--ease-out), background-color 160ms var(--ease-out);
    }
    button:active:not(:disabled) { transform: scale(0.97); }
    button.on { background: var(--primary); border-color: var(--primary); color: var(--on-primary); }
    button:disabled { opacity: 0.4; cursor: default; }
    .gap { padding: 0 4px; color: var(--muted); }
  `,
})
export class Pagination {
  readonly page = model.required<number>();
  readonly pages = input.required<number>();

  protected readonly window = computed(() => {
    const total = this.pages();
    const current = this.page();
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const out: number[] = [1];
    const from = Math.max(2, current - 1);
    const to = Math.min(total - 1, current + 1);
    if (from > 2) out.push(0);
    for (let p = from; p <= to; p++) out.push(p);
    if (to < total - 1) out.push(0);
    out.push(total);
    return out;
  });

  protected go(p: number) {
    if (p >= 1 && p <= this.pages()) this.page.set(p);
  }
}
