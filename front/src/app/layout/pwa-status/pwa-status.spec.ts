import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';

import { Pwa } from '../../core/pwa/pwa';
import { PwaStatus } from './pwa-status';

describe('PwaStatus', () => {
  it('requires confirmation and blocks reload while offline or saving', async () => {
    const pwa = {
      online: signal(true), updateReady: signal(true), recoveryRequired: signal(false),
      updateFailed: signal(false), pendingWrites: signal(0), installAvailable: signal(false),
      installing: signal(false), installFailed: signal(false), reload: vi.fn(),
    };
    TestBed.configureTestingModule({ providers: [{ provide: Pwa, useValue: pwa }] });
    const fixture = TestBed.createComponent(PwaStatus);
    const root = fixture.nativeElement as HTMLElement;
    const button = (text: string) => [...root.querySelectorAll('button')].find((node) => node.textContent?.trim() === text)!;
    await fixture.whenStable();
    button('Review reload').click();
    await fixture.whenStable();
    expect(root.textContent).toContain('Reloading loses unsaved form entries');
    button('Cancel').click();
    await fixture.whenStable();
    expect(pwa.reload).not.toHaveBeenCalled();
    button('Review reload').click();
    await fixture.whenStable();
    pwa.pendingWrites.set(1);
    await fixture.whenStable();
    expect(button('Reload now').disabled).toBe(true);
    button('Reload now').click();
    expect(pwa.reload).not.toHaveBeenCalled();
    pwa.pendingWrites.set(0);
    pwa.online.set(false);
    await fixture.whenStable();
    expect(button('Reload now').disabled).toBe(true);
    expect(root.textContent).toContain('Nothing is queued');
    pwa.online.set(true);
    await fixture.whenStable();
    button('Reload now').click();
    expect(pwa.reload).toHaveBeenCalledExactlyOnceWith();
  });
});
