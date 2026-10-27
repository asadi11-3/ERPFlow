import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';

import { Api } from '../../core/api/api';
import { Jobcard, JOBCARD_PRIORITY, JOBCARD_STATUS } from '../../core/api/models';
import { problemMessage } from '../../core/api/problem';
import { Reference } from '../../core/api/reference';
import { Session } from '../../core/auth/session';
import { Avatar } from '../../ui/avatar/avatar';
import { Icon } from '../../ui/icon/icon';
import { Pill } from '../../ui/pill/pill';
import { Select, SelectOption } from '../../ui/select/select';
import { MiddleText, RecordRow, RecordToggle } from '../../ui/table-record/table-record';

const STATUS_OPTIONS: SelectOption[] = [
  { value: 'all', label: 'All statuses' },
  { value: '0', label: 'Open' },
  { value: '1', label: 'Parts Requested' },
  { value: '2', label: 'In Progress' },
  { value: '3', label: 'Completed' },
  { value: '4', label: 'Cancelled' },
];

@Component({
  selector: 'app-jobcard-list',
  imports: [RouterLink, DatePipe, Avatar, Icon, Pill, Select, MiddleText, RecordRow, RecordToggle],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Service Jobcards</h1>
          <p>Review reported faults, parts requests and completed work.</p>
        </div>
        @if (canCreate()) {
          <a class="btn btn-primary" routerLink="/service/new">
            <app-icon name="plus" /> New Jobcard
          </a>
        }
      </header>

      <section class="card" aria-label="Service jobcard records">
        <div class="list-head">
          <div>
            <h2>Jobcards</h2>
            @if (!data.isLoading() && data.status() !== 'error') {
              <p class="muted" aria-live="polite">Showing {{ filteredRows().length }} of {{ jobcards().length }}</p>
            }
          </div>
          <label for="status">Status</label>
          <app-select
            inputId="status"
            [options]="statusOptions"
            [value]="filterStatus()"
            (valueChange)="filterStatus.set($event)"
          />
        </div>

        @if (data.status() === 'error') {
          <div class="alert" role="alert">
            <app-icon name="alert" />
            <p>{{ error() }} <button type="button" class="btn btn-ghost" (click)="data.reload()">Retry</button></p>
          </div>
        } @else if (data.isLoading()) {
          <div role="status" aria-label="Loading service jobcards">
            @for (n of skeleton; track n) {
              <div class="skel-row" aria-hidden="true">
                <span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span><span class="skeleton"></span>
              </div>
            }
          </div>
        } @else if (filteredRows().length === 0) {
          <p class="empty">{{ jobcards().length ? 'No jobcards match this status.' : 'No service jobcards yet. Open a jobcard to record a service call.' }}</p>
        } @else {
          <div class="table-wrap wide">
            <table class="table dotted">
              <thead>
                <tr>
                  <th>Ticket #</th>
                  <th>Customer</th>
                  <th>Reported Fault</th>
                  <th>Technician</th>
                  <th>Priority</th>
                  <th>Status</th>
                  <th>Opened</th>
                </tr>
              </thead>
              <tbody>
                @for (j of filteredRows(); track j.id) {
                  <tr appRecordRow>
                    <td class="main">
                      <a class="row-link" [routerLink]="['/service', j.id]"><app-middle-text [value]="j.ticketNumber" /></a>
                    </td>
                    <td data-label="Customer">
                      <span class="cell-customer">
                        <app-avatar [name]="ref.customerName(j.customerId)" />
                        <app-middle-text [value]="ref.customerName(j.customerId)" />
                      </span>
                    </td>
                    <td class="span" data-label="Reported fault"><app-middle-text [value]="j.reportedFault" /></td>
                    <td data-label="Technician"><app-middle-text [value]="j.technicianName" /></td>
                    <td data-label="Priority">
                      <app-pill
                        [label]="priorityMap[j.priority]?.label ?? 'Unknown'"
                        [tone]="priorityMap[j.priority]?.tone ?? 'gray'"
                      />
                    </td>
                    <td data-label="Status">
                      <app-pill
                        [label]="statusMap[j.status]?.label ?? 'Unknown'"
                        [tone]="statusMap[j.status]?.tone ?? 'gray'"
                      />
                    </td>
                    <td class="muted" data-label="Opened">{{ j.createdAt | date: 'd MMM y, h:mm a' }}</td>
                    <td class="record-disclosure"><app-record-toggle /></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    </section>
  `,
  styles: `
    .list-head { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-bottom: 12px; }
    .list-head > div { margin-right: auto; }
    .list-head h2 { margin: 0; }
    .list-head p { margin: 2px 0 0; font-size: 0.875rem; }
    .list-head label { font-size: 0.875rem; font-weight: 600; }
    .list-head app-select { width: min(100%, 220px); }
    .table { table-layout: fixed; }
    .table th:nth-child(1) { width: 13%; }
    .table th:nth-child(2) { width: 17%; }
    .table th:nth-child(3) { width: 20%; }
    .table th:nth-child(4) { width: 13%; }
    .table th:nth-child(5) { width: 9%; }
    .table th:nth-child(6) { width: 17%; }
    .table th:nth-child(7) { width: 11%; }
    .table th { white-space: normal; }
    .table app-pill { max-width: 100%; white-space: normal; }
    .table td[data-label='Opened'] { white-space: normal; }
    @media (max-width: 640px) { .list-head app-select { width: 100%; } }
  `,
})
export class JobcardList {
  private readonly api = inject(Api);
  private readonly session = inject(Session);
  protected readonly canCreate = computed(() => {
    const role = this.session.user()?.role;
    return role === 'Technician' || role === 'Administrator';
  });
  protected readonly ref = inject(Reference);

  protected readonly statusOptions = STATUS_OPTIONS;
  protected readonly filterStatus = signal('all');
  protected readonly skeleton = [1, 2, 3, 4, 5];

  protected readonly priorityMap = JOBCARD_PRIORITY;
  protected readonly statusMap = JOBCARD_STATUS;

  protected readonly data = rxResource({
    stream: () => this.api.jobcards(),
  });

  protected readonly jobcards = computed<Jobcard[]>(() => this.data.value() ?? []);
  protected readonly error = computed(() => problemMessage(this.data.error()));

  protected readonly filteredRows = computed<Jobcard[]>(() => {
    const filter = this.filterStatus();
    if (filter === 'all') return this.jobcards();
    const statusNum = parseInt(filter, 10);
    return this.jobcards().filter((j: Jobcard) => j.status === statusNum);
  });

}
