import { Component } from '@angular/core';

@Component({
  selector: 'app-logo',
  template: `
    <svg viewBox="0 0 32 32" width="30" height="30" fill="none" aria-hidden="true">
      <rect width="32" height="32" rx="9" fill="oklch(0.49 0.1 183)" />
      <path
        d="M9 12.5c3-3.2 6.2-3.2 9 0s6 3.2 5-1M9 20.5c3-3.2 6.2-3.2 9 0s6 3.2 5-1"
        stroke="oklch(0.95 0.05 95)"
        stroke-width="2.4"
        stroke-linecap="round"
      />
    </svg>
    <span class="word"><span class="erp">ERP</span><span class="flow">Flow</span></span>
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 10px;
    }
    .word {
      font-size: 1.25rem;
      font-weight: 700;
      letter-spacing: -0.02em;
    }
    .erp {
      color: var(--ink);
    }
    .flow {
      color: var(--primary);
    }
    :host(.on-dark) .erp {
      color: var(--on-primary);
    }
    :host(.on-dark) .flow {
      color: oklch(0.88 0.08 183);
    }
  `,
})
export class Logo {}
