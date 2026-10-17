import { Component, ElementRef, inject, input, signal } from '@angular/core';

import { Icon } from '../icon/icon';

/** Kebab menu. Project `<a class="menu-item">` / `<button class="menu-item">` children. */
@Component({
  selector: 'app-row-menu',
  imports: [Icon],
  template: `
    <button
      type="button"
      class="trigger"
      aria-haspopup="menu"
      [attr.aria-expanded]="open()"
      [attr.aria-label]="label()"
      (click)="open.set(!open())"
    >
      <app-icon name="more" />
    </button>
    <div class="menu" role="menu" [class.open]="open()">
      <ng-content />
    </div>
  `,
  host: {
    '(document:click)': 'onDocClick($event)',
    '(click)': 'onClick($event)',
    '(keydown.escape)': 'open.set(false)',
  },
  styles: `
    :host { position: relative; display: inline-block; }
    .trigger {
      display: grid;
      place-items: center;
      width: 44px;
      height: 44px;
      border: 0;
      border-radius: 10px;
      background: transparent;
      color: var(--muted);
      cursor: pointer;
      transition: transform 160ms var(--ease-out), background-color 160ms ease, color 160ms ease;
    }
    .trigger:active { transform: scale(0.97); }
    .trigger[aria-expanded='true'] { background: var(--tint); color: var(--ink); }
    @media (hover: hover) and (pointer: fine) {
      .trigger:hover { background: var(--tint); color: var(--ink); }
    }
    .menu {
      position: absolute;
      top: calc(100% + 4px);
      right: 0;
      z-index: 20;
      display: grid;
      min-width: 168px;
      padding: 6px;
      border: 1px solid var(--line);
      border-radius: var(--radius);
      background: var(--surface);
      transform-origin: top right;
      opacity: 0;
      transform: scale(0.95);
      visibility: hidden;
      pointer-events: none;
      /* exit is faster than enter */
      transition: opacity 100ms ease, transform 100ms var(--ease-out), visibility 0s 100ms;
    }
    .menu.open {
      opacity: 1;
      transform: none;
      visibility: visible;
      pointer-events: auto;
      transition: opacity 160ms ease, transform 160ms var(--ease-out), visibility 0s;
    }
    @media (prefers-reduced-motion: reduce) {
      .menu, .menu.open { transform: none; }
    }
  `,
})
export class RowMenu {
  readonly label = input('Row actions');
  protected readonly open = signal(false);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected onClick(e: Event): void {
    if ((e.target as HTMLElement).closest('.menu')) this.open.set(false);
  }

  protected onDocClick(e: Event): void {
    if (!this.host.nativeElement.contains(e.target as Node)) this.open.set(false);
  }
}
