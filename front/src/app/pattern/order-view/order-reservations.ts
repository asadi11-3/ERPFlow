import { DatePipe } from '@angular/common';
import { Component, inject, input } from '@angular/core';

import { SalesOrder } from '../../core/api/models';
import { Reference } from '../../core/api/reference';
import { Pill } from '../../ui/pill/pill';
import { MiddleText, RecordRow, RecordToggle } from '../../ui/table-record/table-record';

@Component({
  selector: 'app-order-reservations',
  imports: [DatePipe, Pill, MiddleText, RecordRow, RecordToggle],
  template: `
    @if (order().reservations.length === 0) {
      <p class="empty">No stock is reserved. Stock is reserved when the order is submitted.</p>
    } @else {
      <div class="table-wrap">
        <table class="table">
          <thead>
            <tr>
              <th>#</th>
              <th>Product</th>
              <th class="num">Quantity</th>
              <th>Status</th>
              <th>Reserved at</th>
              <th>Released at</th>
            </tr>
          </thead>
          <tbody>
            @for (r of order().reservations; track r.id; let i = $index) {
              <tr appRecordRow>
                <td class="muted idx">{{ i + 1 }}</td>
                <td class="main"><app-middle-text [value]="ref.productName(r.productId)" /></td>
                <td class="num" data-label="Quantity">{{ r.quantity }}</td>
                <td data-label="Status">
                  <app-pill [label]="r.isReleased ? 'Released' : 'Reserved'" [tone]="r.isReleased ? 'gray' : 'green'" />
                </td>
                <td class="muted span" data-label="Reserved">{{ r.createdAt | date: 'd MMM y, h:mm a' }}</td>
                <td class="muted span" data-label="Released">{{ r.releasedAt ? (r.releasedAt | date: 'd MMM y, h:mm a') : '-' }}</td>
                <td class="record-disclosure"><app-record-toggle /></td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class OrderReservations {
  protected readonly ref = inject(Reference);
  readonly order = input.required<SalesOrder>();
}
