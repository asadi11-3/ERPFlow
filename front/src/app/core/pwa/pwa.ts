import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SwUpdate } from '@angular/service-worker';

interface InstallPrompt extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

@Injectable({ providedIn: 'root' })
export class Pwa {
  private readonly updates = inject(SwUpdate);
  private readonly destroyRef = inject(DestroyRef);
  private installPrompt: InstallPrompt | null = null;

  readonly online = signal(navigator.onLine);
  readonly updateReady = signal(false);
  readonly recoveryRequired = signal(false);
  readonly updateFailed = signal(false);
  readonly installAvailable = signal(false);
  readonly installing = signal(false);
  readonly installFailed = signal(false);
  readonly pendingWrites = signal(0);

  constructor() {
    const connectionChanged = () => {
      this.online.set(navigator.onLine);
      if (navigator.onLine) void this.checkForUpdate();
    };
    const resume = () => {
      if (document.visibilityState === 'visible') connectionChanged();
    };
    const offerInstall = (event: Event) => {
      event.preventDefault();
      this.installPrompt = event as InstallPrompt;
      this.installAvailable.set(true);
    };
    const installed = () => {
      this.installPrompt = null;
      this.installAvailable.set(false);
    };
    window.addEventListener('online', connectionChanged);
    window.addEventListener('offline', connectionChanged);
    window.addEventListener('beforeinstallprompt', offerInstall);
    window.addEventListener('appinstalled', installed);
    document.addEventListener('visibilitychange', resume);
    this.destroyRef.onDestroy(() => {
      window.removeEventListener('online', connectionChanged);
      window.removeEventListener('offline', connectionChanged);
      window.removeEventListener('beforeinstallprompt', offerInstall);
      window.removeEventListener('appinstalled', installed);
      document.removeEventListener('visibilitychange', resume);
    });

    if (this.updates.isEnabled) {
      this.updates.versionUpdates.pipe(takeUntilDestroyed()).subscribe((event) => {
        if (event.type === 'VERSION_READY') {
          this.updateReady.set(true);
          this.updateFailed.set(false);
        } else if (event.type === 'VERSION_INSTALLATION_FAILED') {
          this.updateFailed.set(true);
        }
      });
      this.updates.unrecoverable.pipe(takeUntilDestroyed()).subscribe(() => this.recoveryRequired.set(true));
    }
  }

  async checkForUpdate(): Promise<void> {
    if (!this.updates.isEnabled || !navigator.onLine) return;
    try {
      await this.updates.checkForUpdate();
      this.updateFailed.set(false);
    } catch {
      this.updateFailed.set(true);
    }
  }

  async install(): Promise<void> {
    const prompt = this.installPrompt;
    if (!prompt || this.installing()) return;
    this.installing.set(true);
    try {
      await prompt.prompt();
      await prompt.userChoice;
    } catch {
      this.installFailed.set(true);
    } finally {
      this.installPrompt = null;
      this.installAvailable.set(false);
      this.installing.set(false);
    }
  }

  /** The caller confirms loss of unsaved work. Reload switches the whole app version. */
  reload(): void {
    if (navigator.onLine && this.pendingWrites() === 0) window.location.reload();
  }
}
