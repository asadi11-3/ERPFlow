import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { PendingQueue } from '../../core/api/pending';
import { Role, Session } from '../../core/auth/session';
import { ConfirmDialog } from '../../ui/confirm-dialog/confirm-dialog';
import { Icon, IconName } from '../../ui/icon/icon';
import { Logo } from '../../ui/logo/logo';

interface NavItem {
  label: string;
  path: string;
  icon: IconName;
  roles?: Role[];
}

const NAV: NavItem[] = [
  { label: 'Sales Orders', path: '/orders', icon: 'orders' },
  { label: 'Create Order', path: '/orders/new', icon: 'create', roles: ['Sales', 'Administrator'] },
  { label: 'Customers & Products', path: '/reference', icon: 'reference' },
  { label: 'Pending Approvals', path: '/approvals', icon: 'approvals', roles: ['Approver', 'Administrator'] },
];

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ConfirmDialog, Icon, Logo],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  host: { '(document:keydown.escape)': 'drawerOpen.set(false)' },
})
export class Shell {
  protected readonly session = inject(Session);
  protected readonly pending = inject(PendingQueue);
  protected readonly signOutOpen = signal(false);
  protected readonly drawerOpen = signal(false);

  protected readonly nav = computed(() => {
    const role = this.session.user()?.role;
    return NAV.filter((item) => !item.roles || (role !== undefined && item.roles.includes(role)));
  });

  protected readonly initials = computed(() =>
    (this.session.user()?.name ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0].toUpperCase())
      .join(''),
  );
}
