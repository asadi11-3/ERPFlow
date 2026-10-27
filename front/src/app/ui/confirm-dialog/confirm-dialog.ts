import { Component, effect, ElementRef, input, model, output, viewChild } from '@angular/core';

@Component({
  selector: 'app-confirm-dialog',
  template: `
    <dialog #dlg aria-labelledby="cd-title" aria-describedby="cd-msg" (cancel)="busy() ? $event.preventDefault() : open.set(false)" (close)="open.set(false)">
      <h2 id="cd-title">{{ heading() }}</h2>
      <p id="cd-msg">{{ message() }}</p>
      <ng-content />
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
    }
    h2 { margin: 0 0 8px; font-size: 1.125rem; }
    p { margin: 0 0 20px; color: var(--muted); max-width: 60ch; }
    .actions { display: flex; justify-content: flex-end; gap: 12px; flex-wrap: wrap; margin-top: 16px; }
  `,
})
export class ConfirmDialog {
  readonly open = model(false);
  readonly heading = input.required<string>();
  readonly message = input.required<string>();
  readonly confirmLabel = input('Confirm');
  readonly tone = input<'primary' | 'success' | 'danger' | 'amber'>('primary');
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
