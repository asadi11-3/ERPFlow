import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Icon } from '../../ui/icon/icon';
import { Logo } from '../../ui/logo/logo';

@Component({
  selector: 'app-landing',
  imports: [RouterLink, Icon, Logo],
  template: `
    <header class="bar">
      <app-logo />
      <a routerLink="/login" class="btn btn-ghost">Sign in</a>
    </header>

    <main>
      <section class="hero">
        <div class="copy">
          <p class="eyebrow">Sales order approvals</p>
          <h1>Orders go in. Decisions come out. Stock stays honest.</h1>
          <p class="lead">
            ERPFlow takes a sales order from draft to approved or rejected, reserves stock the moment it is
            submitted, and keeps a full audit trail of who did what.
          </p>
          <div class="cta">
            <a routerLink="/login" class="btn btn-primary">
              Sign in
              <app-icon name="arrow-right" />
            </a>
          </div>
        </div>

        <figure class="board" aria-label="Example order moving through approval">
          <div class="board-head">
            <span class="no">Order 3F9A21C4</span>
            <span class="chip" data-s="pending">Pending Approval</span>
          </div>
          <ul class="lines">
            <li><span>Steel Bolt M8</span><span>120 x 2.50</span></li>
            <li><span>Copper Pipe 15mm</span><span>40 x 18.00</span></li>
            <li class="sum"><span>Total</span><span>1,020.00</span></li>
          </ul>
          <div class="reserve">
            <app-icon name="check" />
            Stock reserved on submit
          </div>
        </figure>
      </section>

      <section class="flow" aria-labelledby="flow-title">
        <h2 id="flow-title">One path for every order</h2>
        <ol>
          <li>
            <span class="dot" data-s="draft"></span>
            <h3>Draft</h3>
            <p>Sales picks a customer and products. Prices come from the catalog, never from the form.</p>
          </li>
          <li>
            <span class="dot" data-s="pending"></span>
            <h3>Pending approval</h3>
            <p>Submitting reserves the stock. If any line is short, nothing is reserved and nothing is sent.</p>
          </li>
          <li>
            <span class="dot" data-s="done"></span>
            <h3>Approved or rejected</h3>
            <p>An approver decides. A rejection needs a reason and releases the reserved stock.</p>
          </li>
        </ol>
      </section>

      <section class="roles" aria-labelledby="roles-title">
        <h2 id="roles-title">Built around who you are</h2>
        <dl>
          <div>
            <dt>Sales</dt>
            <dd>Create orders, submit them, follow the status of your own.</dd>
          </div>
          <div>
            <dt>Approver</dt>
            <dd>Work the pending queue, approve with a note or reject with a reason.</dd>
          </div>
          <div>
            <dt>Administrator</dt>
            <dd>Everything above, across every order.</dd>
          </div>
        </dl>
      </section>
    </main>

    <footer class="foot">
      <app-logo />
      <span>Sales order and approval workflow</span>
    </footer>
  `,
  styles: `
    :host {
      display: block;
      min-height: 100dvh;
      background: var(--bg);
      color: var(--ink);
    }

    .bar,
    main,
    .foot {
      width: min(1120px, 100% - 48px);
      margin-inline: auto;
    }

    .bar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding-block: 20px;
    }

    .hero {
      display: grid;
      grid-template-columns: minmax(0, 1.1fr) minmax(0, 0.9fr);
      gap: 56px;
      align-items: center;
      padding-block: 56px 72px;
    }

    .eyebrow {
      margin: 0 0 12px;
      color: var(--primary);
      font-weight: 600;
      font-size: 0.875rem;
    }

    h1 {
      margin: 0;
      font-size: clamp(2rem, 4.2vw, 3.1rem);
      line-height: 1.08;
      letter-spacing: -0.03em;
      max-width: 16ch;
    }

    .lead {
      margin: 20px 0 0;
      max-width: 52ch;
      color: var(--muted);
      font-size: 1.0625rem;
      line-height: 1.6;
    }

    .cta {
      margin-top: 28px;
    }

    .cta .btn {
      gap: 8px;
    }

    .board {
      margin: 0;
      padding: 24px;
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: 16px;
      display: grid;
      gap: 18px;
      animation: rise 300ms var(--ease-out) 160ms both;
    }

    .copy > * {
      animation: rise 300ms var(--ease-out) both;
    }
    .copy > :nth-child(2) { animation-delay: 50ms; }
    .copy > :nth-child(3) { animation-delay: 100ms; }
    .copy > :nth-child(4) { animation-delay: 150ms; }

    .board-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
    }

    .no {
      font-family: var(--mono);
      font-weight: 600;
      font-variant-numeric: tabular-nums;
    }

    .chip {
      padding: 4px 12px;
      border-radius: 999px;
      font-size: 0.8125rem;
      font-weight: 600;
      background: var(--warn-soft);
      color: var(--warn-ink);
    }

    .lines {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
    }

    .lines li {
      display: flex;
      justify-content: space-between;
      gap: 16px;
      padding: 12px 0;
      border-bottom: 1px solid var(--line);
      font-variant-numeric: tabular-nums;
    }

    .lines li span:last-child {
      color: var(--muted);
    }

    .lines .sum {
      border-bottom: 0;
      font-weight: 700;
    }

    .lines .sum span:last-child {
      color: var(--ink);
    }

    .reserve {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px 14px;
      border-radius: 12px;
      background: var(--success-soft);
      color: var(--success);
      font-weight: 600;
      font-size: 0.9375rem;
    }

    .flow,
    .roles {
      padding-block: 56px;
      border-top: 1px solid var(--line);
    }

    h2 {
      margin: 0 0 28px;
      font-size: 1.5rem;
      letter-spacing: -0.02em;
    }

    ol {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 0;
      position: relative;
    }

    ol li {
      padding: 0 28px 0 0;
      position: relative;
    }

    ol li::before {
      content: '';
      position: absolute;
      top: 6px;
      left: 20px;
      right: 8px;
      height: 2px;
      background: var(--line);
    }

    ol li:last-child::before {
      display: none;
    }

    .dot {
      position: relative;
      z-index: 1;
      display: block;
      width: 14px;
      height: 14px;
      border-radius: 50%;
      background: var(--muted);
      box-shadow: 0 0 0 4px var(--bg);
    }

    .dot[data-s='pending'] {
      background: oklch(0.72 0.14 80);
    }

    .dot[data-s='done'] {
      background: var(--success);
    }

    h3 {
      margin: 18px 0 6px;
      font-size: 1.0625rem;
    }

    ol p {
      margin: 0;
      color: var(--muted);
      line-height: 1.55;
      max-width: 34ch;
    }

    dl {
      margin: 0;
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 32px;
    }

    dt {
      font-weight: 700;
      margin-bottom: 6px;
    }

    dd {
      margin: 0;
      color: var(--muted);
      line-height: 1.55;
      max-width: 34ch;
    }

    .foot {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      padding-block: 28px 40px;
      border-top: 1px solid var(--line);
      color: var(--muted);
      font-size: 0.875rem;
    }

    @keyframes rise {
      from {
        opacity: 0;
        transform: translateY(8px);
      }
    }

    @media (max-width: 860px) {
      .hero {
        grid-template-columns: minmax(0, 1fr);
        gap: 32px;
        padding-block: 32px 48px;
      }

      ol,
      dl {
        grid-template-columns: minmax(0, 1fr);
        gap: 28px;
      }

      ol li::before {
        display: none;
      }

      .foot {
        flex-direction: column;
        align-items: flex-start;
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .board,
      .copy > * {
        animation: none;
      }
    }
  `,
})
export class Landing {}
