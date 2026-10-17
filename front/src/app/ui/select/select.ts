import {
  afterNextRender,
  Component,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  model,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import TomSelect from 'tom-select';

export interface SelectOption {
  value: string;
  label: string;
  disabled?: boolean;
}

@Component({
  selector: 'app-select',
  template: `<select #el [attr.id]="inputId() || null"></select>`,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
  `,
})
export class Select {
  readonly options = input.required<SelectOption[]>();
  readonly value = model('');
  readonly placeholder = input('');
  readonly ariaLabel = input('');
  readonly inputId = input('');
  readonly disabled = input(false);
  readonly searchable = input(false);

  private readonly el = viewChild.required<ElementRef<HTMLSelectElement>>('el');
  private readonly destroyRef = inject(DestroyRef);
  private readonly ts = signal<TomSelect | null>(null);

  constructor() {
    afterNextRender(() => {
      const ts = new TomSelect(this.el().nativeElement, {
        valueField: 'value',
        labelField: 'label',
        searchField: ['label'],
        disabledField: 'disabled',
        maxOptions: null,
        placeholder: this.placeholder(),
        controlInput: this.searchable() ? undefined : null,
        dropdownParent: 'body',
        onChange: (v: string) => {
          if (this.isDisabledOption(ts, v)) {
            ts.setValue(this.value(), true);
          } else {
            this.value.set(v);
          }
        },
      });
      if (this.ariaLabel()) {
        ts.focus_node.setAttribute('aria-label', this.ariaLabel());
      }
      ts.control.dataset['placeholder'] = this.placeholder();
      this.ts.set(ts);
      this.destroyRef.onDestroy(() => ts.destroy());
    });

    effect(() => {
      const ts = this.ts();
      const opts = this.options();
      if (!ts) return;
      ts.clear(true);
      ts.clearOptions();
      ts.addOptions(opts);
      ts.setValue(untracked(this.value), true);
    });

    effect(() => {
      const ts = this.ts();
      const v = this.value();
      if (ts && ts.getValue() !== v) ts.setValue(v, true);
    });

    effect(() => {
      const ts = this.ts();
      if (!ts) return;
      if (this.disabled()) ts.disable();
      else ts.enable();
    });
  }

  private isDisabledOption(ts: TomSelect, v: string): boolean {
    return !!(ts.options[v] as SelectOption | undefined)?.disabled;
  }
}
