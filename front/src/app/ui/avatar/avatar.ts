import { Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-avatar',
  template: '{{ initials() }}',
  host: { '[style.background]': 'background()' },
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex: none;
      width: 32px;
      height: 32px;
      border-radius: 50%;
      font-size: 0.75rem;
      font-weight: 700;
      color: oklch(0.32 0.05 200);
    }
  `,
})
export class Avatar {
  readonly name = input.required<string>();

  protected readonly initials = computed(() => {
    const words = this.name().trim().split(/\s+/).filter(Boolean);
    return ((words[0]?.[0] ?? '?') + (words.length > 1 ? (words[words.length - 1][0] ?? '') : '')).toUpperCase();
  });

  protected readonly background = computed(() => {
    let hash = 0;
    for (const ch of this.name()) hash = (hash * 31 + ch.charCodeAt(0)) % 360;
    return `oklch(0.91 0.04 ${hash})`;
  });
}
