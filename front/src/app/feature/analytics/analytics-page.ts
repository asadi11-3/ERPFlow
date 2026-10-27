import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AnalyticsApi } from '../../core/analytics/analytics-api';
import { ANALYTICS_VIEWS, AnalyticsKind, AnalyticsWindow, defaultWindow, GroupBy, permittedAnalytics, windowError } from '../../core/analytics/models';
import { problemMessage } from '../../core/api/problem';
import { Session } from '../../core/auth/session';
import { Chart } from '../../ui/chart/chart';
import { Select } from '../../ui/select/select';
import { AnalyticsView, analyticsView } from './analytics-view';

@Component({
  selector: 'app-analytics-page',
  imports: [Chart, DatePipe, DecimalPipe, RouterLink, Select],
  template: `
    <div class="page">
      <header class="page-head"><div><span class="eyebrow">Insights</span><h1>{{ title() }}</h1><p>Operational activity, with exact values behind every chart.</p></div>
      </header>
      <form class="card" (submit)="$event.preventDefault(); apply()">
        <div class="toolbar filter-bar">
          <div class="field"><label for="analytics-from">From (UTC)</label><input id="analytics-from" type="date" required [value]="from()" (input)="from.set($any($event.target).value)" aria-describedby="window-help" /></div>
          <div class="field"><label for="analytics-to">Until (UTC, exclusive)</label><input id="analytics-to" type="date" required [value]="to()" (input)="to.set($any($event.target).value)" aria-describedby="window-help" /></div>
          <div class="field field-compact"><label for="analytics-group">Group by</label><app-select inputId="analytics-group" ariaLabel="Group by" [options]="groups" [(value)]="group" /></div>
          <button type="submit" class="btn btn-primary">Apply</button><button type="button" class="btn btn-ghost" (click)="refresh()" [disabled]="busy() || !applied()">Refresh</button>
        </div>
        <div class="presets"><span id="window-help" class="muted">UTC dates, up to 366 days. Until is not included.</span>
          <button type="button" class="btn btn-ghost" (click)="preset(7)">Last 7 days</button><button type="button" class="btn btn-ghost" (click)="preset(30)">Last 30 days</button><button type="button" class="btn btn-ghost" (click)="preset(0)">This month</button>
        </div>
        @if (validation()) { <p role="alert" class="alert">{{ validation() }}</p> }
      </form>
      <div role="status" class="muted" aria-live="polite">@if (busy()) { Loading sections for the selected dates… }</div>
      <nav class="section-links" aria-label="Analytics sections">@for (section of sections(); track section.kind) { <a [attr.href]="'#insight-' + section.kind">{{ section.label }}</a> }</nav>
      @for (section of sections(); track section.kind) {
        <section class="insight-section" [id]="'insight-' + section.kind">
        <header class="section-heading"><h2>{{ section.label }}</h2>@if (section.data?.link; as link) { <a class="btn btn-ghost" [routerLink]="link.path">{{ link.label }}</a> }</header>
        @if (section.error) { <div class="alert" role="alert"><p>{{ section.error }}</p><button type="button" class="btn btn-ghost" (click)="retry(section.kind)" [disabled]="section.loading">Retry {{ section.label }}</button></div> }
        @if (section.loading) { <p role="status" class="muted">Loading {{ section.label }}…</p> }
      @if (section.data; as data) {
        <section [attr.aria-busy]="busy()">
          <p class="window-label">{{ data.window.from }} ≤ UTC date &lt; {{ data.window.to }} · {{ data.window.groupBy }} buckets</p>
          @if (data.window.groupBy !== 'day') { <p class="muted">Weeks start Monday; months start on day 1. Boundary buckets may be partial and can start before From.</p> }
          <p class="scope-note">{{ data.note }}</p>
          <dl class="summary-strip">@for (item of data.summary; track item.label) { <div><dt>{{ item.label }}</dt><dd>{{ item.value | number: item.amount ? '1.2-2' : '1.0-0' }}</dd></div> }</dl>
          <p class="muted fetched">@if (data.snapshot) { Current snapshot: {{ timestamp(data.snapshot) | date:'medium' }} · }Fetched at {{ section.fetchedAt | date:'mediumTime' }}. Operational links open lists without this date filter.</p>
          <div class="charts">@for (chart of data.charts; track chart.title) {
            <app-chart [heading]="chart.title" [description]="chart.description" [rows]="chart.rows" [series]="chart.series" [horizontal]="chart.horizontal ?? false" [switchMetric]="chart.switchMetric ?? false" [type]="chart.type ?? 'bar'" />
          }</div>
        </section>
      } @else if (section.loading) { <div class="card skeleton-chart skeleton" aria-hidden="true"></div> }
        </section>
      }
    </div>
  `,
  styles: `
    .presets { display:flex; flex-wrap:wrap; align-items:center; gap:6px; margin-top:12px; } .presets>span { flex:1 1 280px; font-size:.8rem; }
    .page { gap:12px; } [role=status]:empty { display:none; }
    .charts { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:12px; align-items:start; }
    .scope-note { max-width:90ch; margin:6px 0 12px; font-size:.8125rem; line-height:1.5; color:var(--muted); } .window-label { font-size:.8125rem; font-weight:600; margin:0; }
    .summary-strip { display:flex; flex-wrap:wrap; gap:12px 24px; padding:12px 0; margin:0; border-top:1px solid var(--line); border-bottom:1px solid var(--line); }
    .summary-strip>div { flex:1 1 130px; } dt { font-size:.8rem; color:var(--muted); } dd { margin:4px 0 0; font-size:1.25rem; font-family:var(--mono,monospace); font-variant-numeric:tabular-nums; }
    .fetched { font-size:.75rem; margin:8px 0 12px; line-height:1.5; } .skeleton-chart { height:260px; }
    .section-links { display:flex; flex-wrap:wrap; gap:8px 20px; } .section-links a { min-height:44px; display:inline-flex; align-items:center; color:var(--primary-strong); }
    .insight-section { scroll-margin-top:16px; border-top:1px solid var(--line); padding-top:12px; } .section-heading { margin-bottom:8px; } .section-heading h2 { margin:0; font-size:1.2rem; }
    @media(max-width:1100px) { .charts { grid-template-columns:1fr; } } @media(max-width:640px) { .summary-strip { gap:12px; } }
  `,
})
export class AnalyticsPage {
  private readonly api = inject(AnalyticsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly session = inject(Session);
  private readonly destroyRef = inject(DestroyRef);
  private sequence = 0;
  protected readonly sections = signal<{ kind: AnalyticsKind; label: string; data: AnalyticsView | null; loading: boolean; error: string; fetchedAt?: Date }[]>([]);
  protected readonly title = signal<string>(this.route.snapshot.data['title']);
  protected readonly from = signal(defaultWindow().from);
  protected readonly to = signal(defaultWindow().to);
  protected readonly group = signal('day');
  protected readonly groups = [{ value: 'day', label: 'Day' }, { value: 'week', label: 'Week' }, { value: 'month', label: 'Month' }];
  protected readonly applied = signal<AnalyticsWindow | null>(null);
  protected readonly busy = signal(false);
  protected readonly validation = signal('');

  constructor() {
    this.destroyRef.onDestroy(() => this.sequence++);
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe(params => {
      const defaults = defaultWindow();
      this.from.set(params.get('from') ?? defaults.from);
      this.to.set(params.get('to') ?? defaults.to);
      this.group.set(params.get('groupBy') ?? 'day');
      const window = this.draft();
      this.validation.set(windowError(window));
      if (!this.validation()) { this.applied.set(window); void this.load(window); }
      else { this.sequence++; this.busy.set(false); this.applied.set(null); this.sections.set([]); }
    });
  }

  private draft(): AnalyticsWindow { return { from: this.from(), to: this.to(), groupBy: this.group() as GroupBy }; }

  protected apply(): void {
    const window = this.draft();
    this.validation.set(windowError(window));
    if (this.validation()) return;
    if (JSON.stringify(window) === JSON.stringify(this.applied())) { void this.load(window); return; }
    void this.router.navigate([], { relativeTo: this.route, queryParams: { ...window } });
  }

  protected refresh(): void { const window = this.applied(); if (window) void this.load(window); }

  protected preset(days: number): void {
    const window = defaultWindow();
    const start = new Date(`${window.to}T00:00:00Z`);
    if (days) start.setUTCDate(start.getUTCDate() - days);
    else { start.setUTCDate(start.getUTCDate() - 1); start.setUTCDate(1); }
    this.from.set(start.toISOString().slice(0, 10)); this.to.set(window.to); this.apply();
  }

  protected timestamp(value: string): string { return /(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : `${value}Z`; }

  protected retry(kind: AnalyticsKind): void {
    const window = this.applied();
    if (window) { this.sections.update(sections => sections.map(section => section.kind === kind ? { ...section, loading: true, error: '' } : section)); void this.loadSection(kind, window, this.sequence); }
  }

  private async load(window: AnalyticsWindow): Promise<void> {
    const request = ++this.sequence;
    const kinds = permittedAnalytics(this.route.snapshot.data['kinds'], this.session.user()!.role);
    this.busy.set(true);
    this.sections.set(kinds.map(kind => ({ kind, label: ANALYTICS_VIEWS.find(view => view.key === kind)!.label, data: null, loading: true, error: '' })));
    await Promise.all(kinds.map(kind => this.loadSection(kind, window, request)));
    if (request === this.sequence) this.busy.set(false);
  }

  private async loadSection(kind: AnalyticsKind, window: AnalyticsWindow, request: number): Promise<void> {
    try {
      const result = await this.api.read(kind, window);
      if (request !== this.sequence) return;
      const data = analyticsView(kind, result, this.session.user()!.role);
      this.sections.update(sections => sections.map(section => section.kind === kind ? { ...section, data, loading: false, fetchedAt: new Date() } : section));
    } catch (error) {
      if (request !== this.sequence) return;
      const message = error instanceof HttpErrorResponse && error.status === 404 ? 'Analytics are not available on the running API. Start the updated API and retry.' : problemMessage(error);
      this.sections.update(sections => sections.map(section => section.kind === kind ? { ...section, data: null, loading: false, error: message } : section));
    }
  }
}
