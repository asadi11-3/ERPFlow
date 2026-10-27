import { DecimalPipe } from '@angular/common';
import { afterRenderEffect, Component, computed, ElementRef, input, signal, viewChild } from '@angular/core';
import ChartJs from 'chart.js/auto';
import { MiddleText } from '../table-record/table-record';

export interface ChartRow { label: string; values: number[] }
export interface ChartSeries { label: string; amount?: boolean }

@Component({
  selector: 'app-chart',
  imports: [DecimalPipe, MiddleText],
  template: `
    <section class="card chart-card" [attr.aria-label]="heading()">
      <header><h3>{{ heading() }}</h3><p class="muted">{{ description() }}</p></header>
      @if (switchMetric() && series().length > 1) {
        <div class="metric-options" aria-label="Chart metric">
          @for (item of series(); track item.label; let i = $index) {
            <button type="button" class="btn btn-ghost" [class.selected]="metric() === i" [attr.aria-pressed]="metric() === i" (click)="metric.set(i)">{{ item.label }}</button>
          }
        </div>
      }
      @if (!hasActivity()) { <p class="empty">No activity in this period.</p> }
      <div class="chart-visual" [class.pie-layout]="type() === 'pie'">
      <div class="plot" [class.pie-plot]="type() === 'pie'" [class.empty-plot]="!hasActivity()">
        <canvas #canvas role="img" [attr.aria-label]="heading() + '. Exact values in View data.'">{{ heading() }}. Exact values are available in the data table below.</canvas>
      </div>
      @if (type() === 'pie' && pieTotal() > 0) {
        <ul class="pie-key">@for (row of rows(); track $index) { <li><span>{{ row.label }}</span><span class="num">{{ row.values[0] ?? 0 | number:'1.0-0' }} <span class="muted">({{ (row.values[0] ?? 0) / pieTotal() * 100 | number:'1.0-1' }}%)</span></span></li> }</ul>
      }
      </div>
      @if (horizontal() && type() !== 'pie' && rows().length > 10) { <p class="muted">Top 10 shown. All {{ rows().length }} entries in View data.</p> }
      <details class="chart-data"><summary>View data <span class="muted">({{ rows().length }} rows)</span></summary>
        <div class="data-scroll" tabindex="0" [attr.aria-label]="heading() + ' data'"><table class="table">
          <thead><tr><th scope="col">{{ horizontal() ? 'Name' : 'UTC bucket start' }}</th>@for (item of series(); track item.label) { <th scope="col" class="num">{{ item.label }}</th> }</tr></thead>
          <tbody>@for (row of rows(); track $index) { <tr><th scope="row"><app-middle-text [value]="row.label" /></th>@for (item of series(); track item.label; let i = $index) { <td class="num">{{ row.values[i] ?? 0 | number: item.amount ? '1.2-2' : '1.0-0' }}</td> }</tr> }</tbody>
        </table></div>
      </details>
    </section>
  `,
  styles: `
    :host { display:block; min-width:0; container-type:inline-size; } .chart-card { min-width:0; padding:16px; }
    h3 { font-size:1.05rem; margin:0 0 6px; } header p { margin:0; font-size:.875rem; line-height:1.5; max-width:72ch; }
    .plot { position:relative; height:220px; margin:12px 0; min-width:0; } canvas { max-width:100%; } .pie-plot { height:180px; } .empty-plot { height:100px; }
    .pie-layout { display:grid; grid-template-columns:minmax(0,160px) minmax(0,1fr); align-items:center; gap:16px; }
    .metric-options { display:flex; flex-wrap:wrap; gap:4px; margin-top:8px; } .selected { background:var(--primary-soft); color:var(--primary-strong); }
    .pie-key { list-style:none; padding:0; margin:12px 0; display:grid; gap:8px; } .pie-key li { display:flex; flex-wrap:wrap; justify-content:space-between; gap:4px 8px; font-size:.8rem; }
    .chart-data { border-top:1px solid var(--line); padding-top:8px; } summary { cursor:pointer; min-height:44px; padding:12px 0; font-size:.875rem; }
    .data-scroll { overflow:auto; max-height:360px; } .data-scroll .table { min-width:420px; } th[scope=row] { max-width:260px; font-weight:400; text-align:left; }
    @container(max-width:380px) { .pie-layout { grid-template-columns:1fr; gap:0; } .pie-plot { width:100%; max-width:180px; margin-inline:auto; } }
  `,
})
export class Chart {
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  readonly heading = input.required<string>();
  readonly description = input('');
  readonly rows = input.required<ChartRow[]>();
  readonly series = input.required<ChartSeries[]>();
  readonly horizontal = input(false);
  readonly switchMetric = input(false);
  readonly type = input<'bar' | 'line' | 'pie'>('bar');
  protected readonly metric = signal(0);
  protected readonly pieTotal = computed(() => this.rows().reduce((sum, row) => sum + Math.max(0, row.values[0] ?? 0), 0));
  private readonly visibleSeries = computed(() => this.series().map((series, index) => ({ series, index })).filter(item => !this.switchMetric() || item.index === this.metric()));
  protected readonly hasActivity = computed(() => this.rows().some(row => this.visibleSeries().some(item => (row.values[item.index] ?? 0) !== 0)));

  constructor() {
    afterRenderEffect(cleanup => {
      const canvas = this.canvas().nativeElement;
      const style = getComputedStyle(canvas);
      const color = (token: string) => style.getPropertyValue(token).trim() || style.color;
      const palette = ['--primary', '--warn-ink', '--muted', '--danger', '--success', '--primary-strong', '--ink', '--line'].map(color);
      const type = this.type();
      const horizontal = this.horizontal() && type === 'bar';
      const rows = horizontal ? this.rows().slice(0, 10) : this.rows();
      const chart = new ChartJs(canvas, {
        type,
        data: {
          labels: rows.map(row => row.label),
          datasets: this.visibleSeries().map(({ series, index }) => ({
            label: series.label, data: rows.map(row => row.values[index] ?? 0),
            backgroundColor: type === 'pie' ? rows.map((_, i) => palette[i % palette.length]) : palette[index % palette.length],
            borderColor: type === 'pie' ? color('--surface') : palette[index % palette.length],
            borderWidth: 2, borderDash: type === 'line' && index % 2 ? [6, 4] : [],
            pointRadius: rows.length > 60 ? 0 : 3, pointHitRadius: 12, tension: 0,
          })),
        },
        options: {
          responsive: true, maintainAspectRatio: false, animation: false, color: color('--muted'),
          font: { family: style.fontFamily }, indexAxis: horizontal ? 'y' : 'x',
          plugins: { legend: { display: type !== 'pie', position: 'bottom', labels: { boxWidth: 12 } },
            tooltip: { callbacks: { label: context => {
              const series = this.visibleSeries()[context.datasetIndex].series;
              return `${series.label}: ${Number(context.raw).toLocaleString(undefined, { minimumFractionDigits: series.amount ? 2 : 0, maximumFractionDigits: series.amount ? 2 : 0 })}`;
            } } } },
          ...(type === 'pie' ? {} : { scales: {
            [horizontal ? 'x' : 'y']: { beginAtZero: true, grid: { color: color('--line') }, ticks: { precision: 0 } },
            [horizontal ? 'y' : 'x']: { grid: { display: false }, ticks: { maxTicksLimit: horizontal ? 10 : 5, maxRotation: 0, callback: function(value: string | number) { const label = this.getLabelForValue(Number(value)); return label.length > 24 ? `${label.slice(0, 12)}…${label.slice(-8)}` : label; } } },
          } }),
        },
      });
      cleanup(() => chart.destroy());
    });
  }
}
