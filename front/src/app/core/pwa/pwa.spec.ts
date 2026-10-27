import { TestBed } from '@angular/core/testing';
import { SwUpdate, VersionEvent } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { vi } from 'vitest';

import { Pwa } from './pwa';

describe('Pwa', () => {
  let versions: Subject<VersionEvent>;
  let recovery: Subject<{ reason: string }>;
  let check: ReturnType<typeof vi.fn>;

  function setup(enabled = true): Pwa {
    versions = new Subject();
    recovery = new Subject();
    check = vi.fn().mockResolvedValue(false);
    TestBed.configureTestingModule({
      providers: [{ provide: SwUpdate, useValue: {
        isEnabled: enabled, versionUpdates: versions, unrecoverable: recovery, checkForUpdate: check,
      } }],
    });
    return TestBed.inject(Pwa);
  }

  afterEach(() => vi.restoreAllMocks());

  it('offers ready updates and recovery without activating or reloading automatically', () => {
    const pwa = setup();
    versions.next({ type: 'VERSION_READY', currentVersion: { hash: 'a' }, latestVersion: { hash: 'b' } });
    expect(pwa.updateReady()).toBe(true);
    expect(check).not.toHaveBeenCalled();
    recovery.next({ reason: 'missing chunk' });
    expect(pwa.recoveryRequired()).toBe(true);
  });

  it('keeps the web app usable when the service worker is disabled', async () => {
    const pwa = setup(false);
    await pwa.checkForUpdate();
    window.dispatchEvent(new Event('online'));
    expect(check).not.toHaveBeenCalled();
    expect(pwa.updateReady()).toBe(false);
  });

  it('reports failed update checks and allows a later retry', async () => {
    const pwa = setup();
    check.mockRejectedValueOnce(new Error('offline'));
    await pwa.checkForUpdate();
    expect(pwa.updateFailed()).toBe(true);
    await pwa.checkForUpdate();
    expect(pwa.updateFailed()).toBe(false);
  });

  it('tracks connection changes and removes browser listeners on destruction', () => {
    const pwa = setup(false);
    const online = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    window.dispatchEvent(new Event('offline'));
    expect(pwa.online()).toBe(false);
    online.mockReturnValue(true);
    window.dispatchEvent(new Event('online'));
    expect(pwa.online()).toBe(true);
    TestBed.resetTestingModule();
    online.mockReturnValue(false);
    window.dispatchEvent(new Event('offline'));
    expect(pwa.online()).toBe(true);
  });

  it('only offers an install action after the browser supplies a prompt', async () => {
    const pwa = setup(false);
    expect(pwa.installAvailable()).toBe(false);
    const prompt = vi.fn().mockResolvedValue(undefined);
    const event = Object.assign(new Event('beforeinstallprompt', { cancelable: true }), {
      prompt, userChoice: Promise.resolve({ outcome: 'dismissed' }),
    });
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
    expect(pwa.installAvailable()).toBe(true);
    await pwa.install();
    expect(prompt).toHaveBeenCalledOnce();
    expect(pwa.installAvailable()).toBe(false);
    expect(pwa.installing()).toBe(false);
  });
});
