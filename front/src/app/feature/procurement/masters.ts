import { DatePipe } from '@angular/common';
import { Component, computed, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Page } from '../../core/api/models';
import { problemMessage } from '../../core/api/problem';
import { Session } from '../../core/auth/session';
import { Supplier, Warehouse, localTimestamp } from '../../core/procurement/models';
import { ProcurementApi, Query } from '../../core/procurement/procurement-api';
import { Pagination } from '../../ui/pagination/pagination';
import { Select } from '../../ui/select/select';
import { RowMenu } from '../../ui/row-menu/row-menu';
import { MiddleText } from '../../ui/table-record/table-record';

@Component({
  selector: 'app-masters',
  imports: [DatePipe, Pagination, Select, RowMenu, MiddleText],
  template: `
    <section class="page">
      <header class="page-head"><div><span class="eyebrow">Master data</span><h1>{{ supplier ? 'Suppliers' : 'Warehouses' }}</h1><p>{{ supplier ? 'Trading partners and purchasing contacts.' : 'Locations used by purchasing, stock and fulfilment.' }}</p></div>
        @if (canWrite()) { <button class="btn btn-primary" (click)="create()">New {{ supplier ? 'supplier' : 'warehouse' }}</button> }
      </header>
      @if (notice()) { <p class="alert success-note" role="status">{{ notice() }}</p> }
      <div>
        <div class="card">
          <form class="toolbar filter-bar" (submit)="$event.preventDefault(); page.set(1); load()">
            @if (supplier) { <div class="field"><label for="master-search">Find supplier</label><input id="master-search" type="search" maxlength="200" [value]="search()" (input)="search.set($any($event.target).value)" placeholder="Name or supplier code"></div> }
            <div class="field field-compact"><label for="master-status">Availability</label><app-select inputId="master-status" [options]="activeOptions" [value]="active()" (valueChange)="active.set($event); page.set(1); load()" /></div>
            <div class="field field-size"><label for="master-size">Show</label><app-select inputId="master-size" [options]="sizeOptions" [value]="'' + pageSize()" (valueChange)="pageSize.set(+$event); page.set(1); load()" /></div>
            @if (supplier) { <button class="btn btn-ghost" type="submit">Search</button> }
          </form>
          @if (error()) { <div class="alert" role="alert"><p>{{ error() }}</p><button class="btn btn-ghost" (click)="load()">Retry</button></div> }
          @if (loading()) { <div class="loading-lines" role="status" aria-label="Loading records"><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span></div> }
          @else if (result(); as data) {
            @if (!data.items.length) { <div class="empty"><h2>No matching records</h2><p>Change the availability filter{{ supplier ? ' or search terms' : '' }} to see more records.</p></div> }
            @else { <div class="table-wrap"><table class="table"><thead><tr><th>Name / code</th><th>Status</th><th>Created</th><th><span class="sr-only">Open</span></th></tr></thead><tbody>
              @for (row of data.items; track row.id) { <tr><td class="main"><button class="text-link row-link" (click)="edit(row)"><app-middle-text [value]="row.name" /></button><small class="cell-sub"><app-middle-text [value]="code(row)" /></small></td><td data-label="Status"><span class="status-chip" [class.positive]="row.isActive">{{ row.isActive ? 'Active' : 'Inactive' }}</span></td><td class="muted span" data-label="Created" [title]="utc(row.createdAt) | date:'d MMM y, h:mm a'">{{ utc(row.createdAt) | date:'d MMM y' }}</td><td class="act"><app-row-menu [label]="'Actions for ' + row.name"><button class="menu-item" role="menuitem" (click)="edit(row)">View record</button></app-row-menu></td></tr> }
            </tbody></table></div><div class="table-foot"><span>Showing {{ data.totalCount ? (page() - 1) * pageSize() + 1 : 0 }} to {{ Math.min(page() * pageSize(), data.totalCount) }} of {{ data.totalCount }} records</span><app-pagination [page]="page()" (pageChange)="page.set($event); load()" [pages]="data.totalPages" /></div> }
          }
        </div>
        <dialog #editor class="master-dialog" aria-labelledby="master-heading" (cancel)="busy() ? $event.preventDefault() : open.set(false)" (close)="open.set(false)"><div class="stack"><div class="section-heading"><h2 id="master-heading">{{ selected() ? 'Record details' : 'New ' + (supplier ? 'supplier' : 'warehouse') }}</h2><button type="button" class="btn btn-ghost" [disabled]="busy()" (click)="open.set(false)">Close</button></div>
          <form class="stack" (submit)="$event.preventDefault(); save()">
            <div class="field"><label for="master-code">{{ supplier ? 'Supplier code' : 'Warehouse code' }}</label><input id="master-code" required maxlength="32" [pattern]="supplier ? '[A-Za-z0-9_-]+' : '.+'" [value]="draft().code" [disabled]="!!selected() || !canWrite() || busy()" (input)="set('code', $any($event.target).value)"><small class="muted">Code cannot be changed after creation.</small></div>
            <div class="field"><label for="master-name">Name</label><input id="master-name" required maxlength="200" [value]="draft().name" [disabled]="!canWrite() || busy()" (input)="set('name', $any($event.target).value)"></div>
            @if (supplier) {
              <div class="field"><label for="contact">Contact name <span class="muted">(optional)</span></label><input id="contact" maxlength="150" [value]="draft().contactName" [disabled]="!canWrite() || busy()" (input)="set('contactName', $any($event.target).value)"></div>
              <div class="field"><label for="email">Email <span class="muted">(optional)</span></label><input id="email" type="email" maxlength="320" [value]="draft().email" [disabled]="!canWrite() || busy()" (input)="set('email', $any($event.target).value)"></div>
              <div class="field"><label for="phone">Phone <span class="muted">(optional)</span></label><input id="phone" type="tel" maxlength="50" [value]="draft().phone" [disabled]="!canWrite() || busy()" (input)="set('phone', $any($event.target).value)"></div>
              <div class="field"><label for="address">Address <span class="muted">(optional)</span></label><textarea id="address" rows="3" maxlength="1000" [value]="draft().address" [disabled]="!canWrite() || busy()" (input)="set('address', $any($event.target).value)"></textarea></div>
            }
            @if (formError()) { <p class="alert" role="alert">{{ formError() }}</p> }
            @if (canWrite()) { <button class="btn btn-primary" [disabled]="busy()">{{ busy() ? 'Saving…' : selected() ? 'Save changes' : 'Create record' }}</button> }
          </form>
          @if (selected(); as row) { @if (canWrite()) { <div class="action-divider"><h3>{{ row.isActive ? 'Deactivate record' : 'Activate record' }}</h3><p class="muted">Historical documents retain this record. {{ row.isActive ? 'Deactivation removes it from new selections.' : 'Activation makes it available for new selections.' }}</p><button class="btn btn-ghost" [disabled]="busy()" (click)="confirmState.set(!confirmState())">{{ row.isActive ? 'Deactivate' : 'Activate' }}</button>
              @if (confirmState()) { <div class="stack"><p>Confirm this availability change?</p><button class="btn btn-primary" [disabled]="busy()" (click)="changeState()">Confirm {{ row.isActive ? 'deactivation' : 'activation' }}</button></div> }
            </div> } }
        </div></dialog>
      </div>
    </section>
  `,
  styles: `
    .master-dialog {
      width: min(560px, calc(100vw - 32px));
      max-height: calc(100dvh - 32px);
      overflow-y: auto;
      padding: 24px;
      border: 1px solid var(--line);
      border-radius: var(--radius);
      background: var(--surface);
      color: var(--ink);
    }
    .master-dialog::backdrop { background: oklch(0.2 0.02 180 / 0.45); }
    @media (max-width: 640px) { .master-dialog { padding: 20px; } }
  `,
})
export class Masters {
  private readonly api = inject(ProcurementApi);
  private readonly session = inject(Session);
  protected readonly supplier = inject(ActivatedRoute).snapshot.data['kind'] === 'suppliers';
  private readonly path = this.supplier ? 'suppliers' : 'warehouses';
  protected readonly canWrite = computed(() => this.session.user()?.role === 'Administrator' || (this.supplier && this.session.user()?.role === 'Purchasing'));
  protected readonly result = signal<Page<Supplier | Warehouse> | null>(null);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly Math = Math;
  protected readonly sizeOptions = [10, 25, 50].map(n => ({ value: '' + n, label: '' + n }));
  protected readonly activeOptions = [{ value: '', label: 'All records' }, { value: 'true', label: 'Active' }, { value: 'false', label: 'Inactive' }];
  protected readonly search = signal('');
  protected readonly active = signal('');
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly formError = signal('');
  protected readonly notice = signal('');
  protected readonly open = signal(false);
  protected readonly confirmState = signal(false);
  protected readonly selected = signal<Supplier | Warehouse | null>(null);
  protected readonly draft = signal({ code: '', name: '', contactName: '', email: '', phone: '', address: '' });
  protected readonly utc = localTimestamp;
  private request = 0;
  private readonly editor = viewChild.required<ElementRef<HTMLDialogElement>>('editor');

  constructor() {
    void this.load();
    effect(() => {
      const dialog = this.editor().nativeElement;
      if (this.open() && !dialog.open) dialog.showModal();
      else if (!this.open() && dialog.open) dialog.close();
    });
  }
  protected code(row: Supplier | Warehouse): string { return 'code' in row ? row.code : row.supplierCode; }
  protected set(key: keyof ReturnType<typeof this.draft>, value: string): void { this.draft.update(d => ({ ...d, [key]: value })); }
  protected create(): void { this.selected.set(null); this.draft.set({ code: '', name: '', contactName: '', email: '', phone: '', address: '' }); this.formError.set(''); this.confirmState.set(false); this.open.set(true); }
  protected async edit(row: Supplier | Warehouse): Promise<void> {
    this.formError.set(''); this.confirmState.set(false);
    try {
      const fresh = await this.api.get<Supplier | Warehouse>(`${this.path}/${row.id}`);
      this.selected.set(fresh);
      const s = 'supplierCode' in fresh ? fresh : null;
      this.draft.set({ code: this.code(fresh), name: fresh.name, contactName: s?.contactName ?? '', email: s?.email ?? '', phone: s?.phone ?? '', address: s?.address ?? '' });
      this.open.set(true);
    } catch (e) { this.error.set(problemMessage(e)); }
  }
  protected async load(): Promise<void> {
    const request = ++this.request; this.loading.set(true); this.error.set('');
    const query: Query = { page: this.page(), pageSize: this.pageSize() };
    if (this.active()) query['active'] = this.active();
    if (this.supplier && this.search().trim()) query['search'] = this.search().trim();
    try { const result = await this.api.get<Page<Supplier | Warehouse>>(this.path, query); if (request === this.request) this.result.set(result); }
    catch (e) { if (request === this.request) this.error.set(problemMessage(e)); }
    finally { if (request === this.request) this.loading.set(false); }
  }
  protected async save(): Promise<void> {
    if (this.busy() || !this.canWrite()) return;
    const d = this.draft(); if (!d.name.trim() || !d.code.trim()) { this.formError.set('Enter a code and a name.'); return; }
    this.busy.set(true); this.formError.set('');
    const row = this.selected();
    const details = this.supplier ? { name: d.name.trim(), contactName: d.contactName.trim() || null, email: d.email.trim() || null, phone: d.phone.trim() || null, address: d.address.trim() || null } : { name: d.name.trim() };
    try {
      const saved = row ? await this.api.put<Supplier | Warehouse>(`${this.path}/${row.id}`, { ...details, rowVersion: row.rowVersion }) : await this.api.post<Supplier | Warehouse>(this.path, { ...details, ...(this.supplier ? { supplierCode: d.code.trim() } : { code: d.code.trim() }) });
      this.selected.set(saved); this.notice.set('Record saved.'); await this.load();
    } catch (e) { this.formError.set(problemMessage(e) + ' Your entries are preserved. Reopen the record to load its latest version.'); }
    finally { this.busy.set(false); }
  }
  protected async changeState(): Promise<void> {
    const row = this.selected(); if (!row || this.busy()) return;
    this.busy.set(true); this.formError.set('');
    try { this.selected.set(await this.api.post<Supplier | Warehouse>(`${this.path}/${row.id}/${row.isActive ? 'deactivate' : 'activate'}`, { rowVersion: row.rowVersion })); this.confirmState.set(false); this.notice.set('Availability updated.'); await this.load(); }
    catch (e) { this.formError.set(problemMessage(e)); }
    finally { this.busy.set(false); }
  }
}
