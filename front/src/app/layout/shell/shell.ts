import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { PendingQueue } from '../../core/api/pending';
import { ANALYTICS_PAGES, ANALYTICS_VIEWS } from '../../core/analytics/models';
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

const NAV: { label: string; items: NavItem[] }[] = [
  { label: 'Sales', items: [
    { label: 'Sales Orders', path: '/orders', icon: 'orders', roles: ['Sales', 'Approver', 'Technician', 'Administrator'] },
    { label: 'Create Order', path: '/orders/new', icon: 'create', roles: ['Sales', 'Administrator'] },
    { label: 'Pending Approvals', path: '/approvals', icon: 'approvals', roles: ['Approver', 'Administrator'] },
  ] },
  { label: 'Purchasing', items: [
    { label: 'Purchase Orders', path: '/purchase-orders', icon: 'orders', roles: ['Purchasing', 'Approver', 'Administrator'] },
    { label: 'Suppliers', path: '/suppliers', icon: 'reference', roles: ['Purchasing', 'Approver', 'Administrator'] },
  ] },
  { label: 'Warehouse', items: [
    { label: 'Receiving', path: '/receiving', icon: 'create', roles: ['WarehouseOperator', 'Purchasing', 'Approver', 'Administrator'] },
    { label: 'Goods Receipts', path: '/goods-receipts', icon: 'reference', roles: ['WarehouseOperator', 'Administrator'] },
    { label: 'Inventory', path: '/inventory', icon: 'assets', roles: ['WarehouseOperator', 'Purchasing', 'Approver', 'Administrator'] },
    { label: 'Stock Transfers', path: '/stock-transfers', icon: 'service', roles: ['WarehouseOperator', 'Administrator'] },
    { label: 'Warehouses', path: '/warehouses', icon: 'assets' },
  ] },
  { label: 'Service', items: [
    { label: 'Machine Assets', path: '/assets', icon: 'assets' },
    { label: 'Service Jobcards', path: '/service', icon: 'service', roles: ['Technician', 'Administrator'] },
  ] },
  { label: 'Reference Data', items: [
    { label: 'Customers & Products', path: '/reference', icon: 'reference' },
  ] },
  { label: 'Insights', items: ANALYTICS_PAGES.map(page => ({ label: page.label, path: `/analytics/${page.key}`, icon: 'reference', roles: [...new Set(ANALYTICS_VIEWS.filter(view => (page.kinds as readonly string[]).includes(view.key)).flatMap(view => [...view.roles]))] })) },
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
  private readonly router = inject(Router);
  private readonly initialPath = (this.router.currentNavigation()?.finalUrl?.toString() ?? this.router.url).split(/[?#]/)[0];
  protected readonly category = signal(NAV.find(group =>
    group.items.some(item => this.initialPath === item.path || this.initialPath.startsWith(item.path + '/')))?.label ?? '');

  protected readonly nav = computed(() => {
    const role = this.session.user()?.role;
    return NAV.map(group => ({
      ...group,
      items: group.items.filter(item => !item.roles || (role !== undefined && item.roles.includes(role))),
    })).filter(group => group.items.length);
  });

  protected toggleCategory(label: string, event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open) this.category.set(label);
    else if (this.category() === label) this.category.set('');
  }

  protected readonly initials = computed(() =>
    (this.session.user()?.name ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0].toUpperCase())
      .join(''),
  );
}
