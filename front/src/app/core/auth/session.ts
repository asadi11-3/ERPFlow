import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from '../config';

export type Role = 'Sales' | 'Approver' | 'Administrator';

export interface SessionUser {
  id: string;
  name: string;
  email: string;
  role: Role;
}

interface Claims {
  sub?: string;
  name?: string;
  email?: string;
  role?: string;
  exp?: number;
}

const STORAGE_KEY = 'erpflow.token';

function decode(token: string): Claims | null {
  try {
    const payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const bytes = Uint8Array.from(atob(payload), (c) => c.charCodeAt(0));
    return JSON.parse(new TextDecoder().decode(bytes)) as Claims;
  } catch {
    return null;
  }
}

function toUser(token: string): SessionUser | null {
  const claims = decode(token);
  if (!claims?.sub) return null;
  if (claims.exp !== undefined && claims.exp * 1000 <= Date.now()) return null;
  return {
    id: claims.sub,
    name: claims.name ?? claims.email ?? 'User',
    email: claims.email ?? '',
    role: claims.role as Role,
  };
}

@Injectable({ providedIn: 'root' })
export class Session {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly raw = signal(sessionStorage.getItem(STORAGE_KEY));

  readonly token = this.raw.asReadonly();
  readonly user = computed(() => {
    const token = this.raw();
    return token ? toUser(token) : null;
  });

  /** Expiry is time based, so guards recheck it on every run. */
  isActive(): boolean {
    const token = this.raw();
    if (!token) return false;
    if (toUser(token)) return true;
    this.clear();
    return false;
  }

  async login(email: string, password: string): Promise<void> {
    const { accessToken } = await firstValueFrom(
      this.http.post<{ accessToken: string }>(`${API_BASE_URL}/api/auth/login`, { email, password }),
    );
    sessionStorage.setItem(STORAGE_KEY, accessToken);
    this.raw.set(accessToken);
  }

  logout(): void {
    this.clear();
    void this.router.navigate(['/login']);
  }

  /** Called on a 401: drop the session and come back to the same page after login. */
  expire(): void {
    const returnUrl = this.router.url;
    this.clear();
    void this.router.navigate(['/login'], { queryParams: { returnUrl } });
  }

  private clear(): void {
    sessionStorage.removeItem(STORAGE_KEY);
    this.raw.set(null);
  }
}
