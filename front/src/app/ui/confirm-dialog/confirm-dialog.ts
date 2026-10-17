import { Component, effect, ElementRef, input, model, output, viewChild } from '@angular/core';

@Component({
  selector: 'app-confirm-dialog',
  template: `
    <dialog #dlg aria-labelledby="cd-title" aria-describedby="cd-msg" (close)="open.set(false)">
      <h2 id="cd-title">{{ heading() }}</h2>
      <p id="cd-msg">{{ message() }}</p>
      <div class="actions">
        <button type="button" class="btn btn-ghost" [disabled]="busy()" (click)="open.set(false)">Cancel</button>
        <button type="button" class="btn" [class]="'btn btn-' + tone()" [disabled]="busy()" (click)="confirmed.emit()">
          {{ confirmLabel() }}
        </button>
      </div>
    </dialog>
  `,
  host: { '(click)': 'onBackdrop($event)' },
  styles: `
    dialog {
      width: min(440px, calc(100vw - 32px));
      padding: 24px;
      border: 1px solid var(--line);
      border-radius: var(--radius);
      background: var(--surface);
      color: var(--ink);
      opacity: 0;
      transform: scale(0.95);
      /* exit is faster than enter; centered origin since modal is not anchored */
      transition:
        opacity 120ms ease,
        transform 120ms var(--ease-out),
        overlay 120ms allow-discrete,
        display 120ms allow-discrete;
    }
    dialog[open] {
      opacity: 1;
      transform: none;
      transition-duration: 200ms;
    }
    @starting-style {
      dialog[open] { opacity: 0; transform: scale(0.95); }
    }
    dialog::backdrop {
      background: oklch(0.2 0.02 180 / 0.45);
      opacity: 0;
      transition: opacity 120ms ease, overlay 120ms allow-discrete, display 120ms allow-discrete;
    }
    dialog[open]::backdrop { opacity: 1; transition-duration: 200ms; }
    @starting-style {
      dialog[open]::backdrop { opacity: 0; }
    }
    h2 { margin: 0 0 8px; font-size: 1.125rem; }
    p { margin: 0 0 20px; color: var(--muted); max-width: 60ch; }
    .actions { display: flex; justify-content: flex-end; gap: 12px; flex-wrap: wrap; }
    @media (prefers-reduced-motion: reduce) {
      dialog, dialog[open] { transform: none; }
    }
  `,
})
export class ConfirmDialog {
  readonly open = model(false);
  readonly heading = input.required<string>();
  readonly message = input.required<string>();
  readonly confirmLabel = input('Confirm');
  readonly tone = input<'primary' | 'success' | 'danger'>('primary');
  readonly busy = input(false);
  readonly confirmed = output<void>();

  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');

  constructor() {
    effect(() => {
      const el = this.dlg().nativeElement;
      if (this.open() && !el.open) el.showModal();
      else if (!this.open() && el.open) el.close();
    });
  }

  protected onBackdrop(e: MouseEvent): void {
    if (e.target === this.dlg().nativeElement && !this.busy()) this.open.set(false);
  }
}
