import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { ANALYTICS_PAGES, ANALYTICS_VIEWS, permittedAnalytics } from '../../core/analytics/models';
import { roleGuard } from '../../core/auth/guards';
import { Session } from '../../core/auth/session';

export default [
  { path: '', pathMatch: 'full', redirectTo: () => {
    const role = inject(Session).user()?.role;
    return ANALYTICS_PAGES.find(page => role && permittedAnalytics(page.kinds, role).length)?.key ?? 'sales-service';
  } },
  ...ANALYTICS_PAGES.map(page => ({
    path: page.key, canActivate: [roleGuard(...new Set(ANALYTICS_VIEWS.filter(view => (page.kinds as readonly string[]).includes(view.key)).flatMap(view => [...view.roles])))], data: { kinds: page.kinds, title: page.label },
    loadComponent: () => import('./analytics-page').then(module => module.AnalyticsPage),
  })),
  ...ANALYTICS_VIEWS.map(view => ({ path: view.key, redirectTo: ANALYTICS_PAGES.find(page => (page.kinds as readonly string[]).includes(view.key))!.key, pathMatch: 'full' as const })),
] satisfies Routes;
