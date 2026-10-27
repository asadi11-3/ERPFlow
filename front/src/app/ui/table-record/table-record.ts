import { Component, Directive, ElementRef, computed, inject, input, signal } from '@angular/core';

@Component({
  selector: 'app-middle-text',
  host: {
    '[attr.title]': 'text()',
    '[attr.aria-label]': 'text()',
    '[attr.tabindex]': 'focusable() ? 0 : null',
  },
  template: `<span class="start" aria-hidden="true">{{ start() }}</span><span class="end" aria-hidden="true">{{ end() }}</span><span class="full">{{ text() }}</span>`,
  styles: `
    :host { display: inline-flex; max-width: 100%; min-width: 0; vertical-align: bottom; }
    .start { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; min-width: 0; }
    .end { flex-shrink: 0; white-space: nowrap; }
    .full { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
    :host(:focus), :host-context(a:focus-visible), :host-context(button:focus-visible) { display: inline-block; white-space: normal; overflow-wrap: anywhere; }
    :host(:focus) .start, :host(:focus) .end,
    :host-context(a:focus-visible) .start, :host-context(a:focus-visible) .end,
    :host-context(button:focus-visible) .start, :host-context(button:focus-visible) .end { display: none; }
    :host(:focus) .full, :host-context(a:focus-visible) .full, :host-context(button:focus-visible) .full { position: static; width: auto; height: auto; overflow: visible; clip-path: none; white-space: normal; }
  `,
})
export class MiddleText {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly value = input<string | null | undefined>('');
  protected readonly text = computed(() => this.value() || '—');
  protected readonly end = computed(() => this.text().length > 12 ? this.text().slice(-Math.min(12, Math.floor(this.text().length / 3))) : '');
  protected readonly start = computed(() => this.end() ? this.text().slice(0, -this.end().length) : this.text());
  protected focusable(): boolean { return this.text().length > 12 && !this.element.nativeElement.closest('a, button'); }
}

@Directive({
  selector: 'tr[appRecordRow]',
  exportAs: 'recordRow',
  host: { class: 'record-row', '[class.record-expanded]': 'expanded()' },
})
export class RecordRow {
  readonly expanded = signal(false);
}

@Component({
  selector: 'app-record-toggle',
  template: `<button type="button" class="record-toggle" [attr.aria-expanded]="row.expanded()" (click)="row.expanded.set(!row.expanded())">{{ row.expanded() ? 'Hide details' : 'Show details' }}</button>`,
})
export class RecordToggle {
  protected readonly row = inject(RecordRow);
}
