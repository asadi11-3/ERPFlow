import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AnalyticsApi } from '../../core/analytics/analytics-api';
import { ANALYTICS_PAGES, ANALYTICS_VIEWS, defaultWindow, permittedAnalytics, SalesAnalytics, windowError } from '../../core/analytics/models';
import { API_BASE_URL } from '../../core/config';
import { fakeToken } from '../../core/auth/fake-token';
import { Chart } from '../../ui/chart/chart';
import routes from './analytics.routes';
import { analyticsView } from './analytics-view';

const window = { from: '2026-03-01', to: '2026-04-01', groupBy: 'day' as const };
const chartRender = vi.hoisted(() => vi.fn<(canvas: unknown, config: { type: string; data: { datasets: { data: number[] }[] } }) => void>());
vi.mock('chart.js/auto', () => ({ default: class {
  constructor(canvas: unknown, config: Parameters<typeof chartRender>[1]) { chartRender(canvas, config); }
  destroy() { /* No canvas renderer in jsdom. */ }
} }));
const sales = (count = 1): SalesAnalytics => ({ window, orderCount: count, approvedOrderValue: 125,
  byStatus: [{ status: 2, label: 'Approved', count }], created: [{ date: window.from, count, amount: 125 }], topProducts: [],
});

describe('Analytics contracts and presentation', () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal('ResizeObserver', class { observe() { /* No layout in jsdom. */ } disconnect() { /* No layout in jsdom. */ } });
  });
  afterEach(() => vi.unstubAllGlobals());

  it('uses UTC date-only bounds across DST and rejects invalid or excessive windows', () => {
    expect(defaultWindow(new Date('2026-03-08T23:30:00-05:00'))).toEqual({ from: '2026-02-08', to: '2026-03-10', groupBy: 'day' });
    expect(windowError(window)).toBe('');
    expect(windowError({ ...window, from: '2026-02-30' })).not.toBe('');
    expect(windowError({ ...window, to: window.from })).not.toBe('');
    expect(windowError({ from: '2024-01-01', to: '2025-01-01', groupBy: 'week' })).toBe('');
    expect(windowError({ from: '2024-01-01', to: '2025-01-02', groupBy: 'week' })).not.toBe('');
  });

  it('serializes only supported date filters to the selected endpoint', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const result = TestBed.inject(AnalyticsApi).read('sales', window);
    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne(req => req.url === `${API_BASE_URL}/api/analytics/sales`);
    expect(request.request.params.keys().sort()).toEqual(['from', 'groupBy', 'to']);
    expect(request.request.params.get('from')).toBe('2026-03-01');
    expect(request.request.params.get('to')).toBe('2026-04-01');
    request.flush(sales()); await result; http.verify();
  });

  it('keeps receiving scope and operational links separate from analytics permissions', () => {
    expect(ANALYTICS_PAGES).toHaveLength(3);
    expect(permittedAnalytics(['inventory', 'receiving', 'transfers'], 'Purchasing')).toEqual(['inventory', 'receiving']);
    expect(permittedAnalytics(['procurement', 'approvals'], 'Purchasing')).toEqual(['procurement']);
    expect(ANALYTICS_VIEWS.filter(view => (view.roles as readonly string[]).includes('WarehouseOperator')).map(view => view.key)).toEqual(['receiving', 'transfers', 'inventory']);
    const receiving = analyticsView('receiving', { window, byStatus: [], posted: [] }, 'Purchasing');
    expect(receiving.link?.path).toBe('/receiving');
    expect(receiving.note).toContain('your purchase orders');
    const service = analyticsView('service', { window, byStatus: [], byPriority: [], created: [], completed: [] }, 'Sales');
    expect(service.link).toBeUndefined();
  });

  it('renders signed values and zero buckets with an exact accessible data alternative', async () => {
    const fixture = TestBed.createComponent(Chart);
    fixture.componentRef.setInput('heading', 'Reservations');
    fixture.componentRef.setInput('series', [{ label: 'Reserved delta' }]);
    fixture.componentRef.setInput('rows', [{ label: '2026-03-01', values: [-4] }, { label: '2026-03-02', values: [0] }]);
    await fixture.whenStable(); fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('canvas')?.getAttribute('aria-label')).toContain('Exact values');
    expect(chartRender.mock.calls.at(-1)?.[1].data.datasets[0].data).toEqual([-4, 0]);
    expect(element.querySelector('tbody')?.textContent).toContain('-4');
    expect(element.querySelectorAll('tbody tr')).toHaveLength(2);
  });

  it('does not let an older response overwrite a newly applied URL window', async () => {
    sessionStorage.setItem('erpflow.token', fakeToken({ sub: 'analytics-user', role: 'Administrator' }));
    const resolvers: ((value: SalesAnalytics) => void)[] = [];
    TestBed.configureTestingModule({ providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting(),
      { provide: AnalyticsApi, useValue: { read: (kind: string) => kind === 'sales' ? new Promise<SalesAnalytics>(resolve => resolvers.push(resolve)) : Promise.reject(new Error('Section unavailable')) } },
    ] });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/sales?from=2026-03-01&to=2026-04-01&groupBy=day');
    await harness.navigateByUrl('/sales?from=2026-02-01&to=2026-03-01&groupBy=week');
    expect(resolvers).toHaveLength(2);
    resolvers[1]({ ...sales(7), window: { from: '2026-02-01', to: '2026-03-01', groupBy: 'week' } });
    await harness.fixture.whenStable();
    resolvers[0](sales(999)); await harness.fixture.whenStable(); harness.detectChanges();
    const element = harness.fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.window-label')?.textContent).toContain('2026-02-01');
    expect(element.querySelector('.summary-strip dd')?.textContent?.trim()).toBe('7');
    expect(element.querySelectorAll('.insight-section')).toHaveLength(3);
    expect(element.querySelectorAll('.insight-section [role=alert]')).toHaveLength(2);
    await TestBed.inject(Router).navigateByUrl('/sales?from=invalid&to=2026-03-01');
    await harness.fixture.whenStable(); harness.detectChanges();
    expect(element.querySelector('[role=alert]')?.textContent).toContain('valid dates');
    expect(element.querySelector('app-chart')).toBeNull();
  });

  it('draws signed line trends and proportional pies without losing exact values or dividing by zero', async () => {
    const fixture = TestBed.createComponent(Chart);
    fixture.componentRef.setInput('heading', 'Trend');
    fixture.componentRef.setInput('type', 'line');
    fixture.componentRef.setInput('series', [{ label: 'Units' }]);
    fixture.componentRef.setInput('rows', [{ label: '2026-03-01', values: [-4] }, { label: '2026-03-02', values: [8] }]);
    await fixture.whenStable(); fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(chartRender.mock.calls.at(-1)?.[1].type).toBe('line');
    expect(chartRender.mock.calls.at(-1)?.[1].data.datasets[0].data).toEqual([-4, 8]);
    fixture.componentRef.setInput('type', 'pie');
    fixture.componentRef.setInput('rows', [{ label: 'Approved', values: [3] }, { label: 'Pending', values: [1] }]);
    await fixture.whenStable(); fixture.detectChanges();
    expect(element.querySelector('.pie-key')?.textContent).toContain('75%');
    expect(element.querySelector('.pie-key')?.textContent).toContain('25%');
    fixture.componentRef.setInput('rows', [{ label: 'Approved', values: [0] }]);
    await fixture.whenStable(); fixture.detectChanges();
    expect(element.querySelector('.pie-key')).toBeNull();
    expect(element.querySelector('tbody')?.textContent).toContain('Approved');
  });
});
