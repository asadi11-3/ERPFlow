import { Component, inject, signal } from '@angular/core';

import { Pwa } from '../../core/pwa/pwa';

@Component({
  selector: 'app-pwa-status',
  templateUrl: './pwa-status.html',
  styleUrl: './pwa-status.scss',
})
export class PwaStatus {
  protected readonly pwa = inject(Pwa);
  protected readonly confirmReload = signal(false);

  protected reload(): void {
    this.pwa.reload();
  }
}
