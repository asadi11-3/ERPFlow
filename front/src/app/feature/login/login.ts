import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

import { homeForRole, Session } from '../../core/auth/session';
import { Icon } from '../../ui/icon/icon';
import { Logo } from '../../ui/logo/logo';

function messageFor(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 400) return 'Enter your email and password.';
    if (error.status === 401) return 'Email or password is incorrect.';
    if (error.status === 0) return navigator.onLine
      ? 'Cannot reach the server. Try again.'
      : 'You are offline. Reconnect to sign in.';
  }
  return 'Something went wrong. Try again.';
}

@Component({
  selector: 'app-login',
  imports: [Icon, Logo],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly showPassword = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canSubmit = computed(
    () => this.email().trim() !== '' && this.password() !== '' && !this.busy(),
  );

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.canSubmit()) return;

    this.busy.set(true);
    this.error.set(null);
    try {
      await this.session.login(this.email().trim(), this.password());
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      const defaultHome = homeForRole(this.session.user()?.role);
      const target =
        returnUrl && returnUrl.startsWith('/') && !returnUrl.startsWith('//') && returnUrl !== '/'
          ? returnUrl
          : defaultHome;
      await this.router.navigateByUrl(target);
    } catch (error) {
      this.error.set(messageFor(error));
    } finally {
      this.busy.set(false);
    }
  }
}
