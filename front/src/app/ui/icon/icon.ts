import { Component, computed, input } from '@angular/core';

const PATHS = {
  orders: 'M7 3h8l4 4v14H7z M15 3v4h4 M10 12h6 M10 16h6',
  create: 'M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0 M12 8v8 M8 12h8',
  reference:
    'M4 6c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3z M4 6v6c0 1.7 3.6 3 8 3s8-1.3 8-3V6 M4 12v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6',
  approvals: 'M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0 M8.5 12.5l2.5 2.5 4.5-5',
  assets: 'M4 6a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2H6a2 2 0 0 1 -2 -2z M9 10h6 M9 14h6 M9 6v2 M15 6v2',
  service: 'M14 6l4 4l-8 8H6v-4l8-8z M18 4l2 2l-2 2l-2-2z',
  logout: 'M9 4H5v16h4 M16 8l4 4-4 4 M20 12H9',
  eye: 'M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z',
  'eye-off':
    'M3 3l18 18 M10.6 5.1A10 10 0 0 1 12 5c6.4 0 10 7 10 7a17 17 0 0 1-3.2 4 M6.6 6.6C3.7 8.4 2 12 2 12s3.6 7 10 7c1.6 0 3-.4 4.3-1 M9.9 9.9a3 3 0 0 0 4.2 4.2',
  'arrow-right': 'M5 12h14 M13 6l6 6-6 6',
  check: 'M5 12.5l4.5 4.5L19 7.5',
  'arrow-left': 'M19 12H5 M11 6l-6 6 6 6',
  search: 'M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14z M20 20l-4-4',
  plus: 'M12 5v14 M5 12h14',
  trash: 'M4 7h16 M9 7V4h6v3 M6 7l1 13h10l1-13 M10 11v5 M14 11v5',
  alert: 'M12 3l10 18H2z M12 10v5 M12 18v.5',
  x: 'M6 6l12 12 M18 6L6 18',
  menu: 'M4 7h16 M4 12h16 M4 17h16',
  more: 'M11 5.5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0 M11 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0 M11 18.5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0',
} as const;

export type IconName = keyof typeof PATHS;

@Component({
  selector: 'app-icon',
  template: `
    <svg
      viewBox="0 0 24 24"
      width="20"
      height="20"
      fill="none"
      stroke="currentColor"
      stroke-width="1.75"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <path [attr.d]="path()" />
    </svg>
  `,
  styles: ':host { display: inline-flex; flex: none; }',
})
export class Icon {
  readonly name = input.required<IconName>();
  protected readonly path = computed(() => PATHS[this.name()]);
}
