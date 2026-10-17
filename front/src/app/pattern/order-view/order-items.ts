import { CurrencyPipe } from '@angular/common';
import { Component, inject, input } from '@angular/core';

import { SalesOrder } from '../../core/api/models';
import { Reference } from '../../core/api/reference';

@Component({
  selector: 'app-order-items',
  imports: [CurrencyPipe],
  template: `
    <div class="table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>#</th>
            <th>Product</th>
            <th>SKU</th>
            <th class="num">Quantity</th>
            <th class="num">Unit price</th>
            <th class="num">Line total</th>
          </tr>
        </thead>
        <tbody>
          @for (item of order().items; track item.id; let i = $index) {
            <tr>
              <td class="muted idx">{{ i + 1 }}</td>
              <td class="main"><span class="clip" [title]="ref.productName(item.productId)">{{ ref.productName(item.productId) }}</span></td>
              <td class="muted" data-label="SKU">{{ ref.product(item.productId)?.sku ?? '-' }}</td>
              <td class="num" data-label="Quantity">{{ item.quantity }}</td>
              <td class="num" data-label="Unit price">{{ item.unitPrice | currency }}</td>
              <td class="num" data-label="Line total">{{ item.lineTotal | currency }}</td>
            </tr>
          }
        </tbody>
        <tfoot>
          <tr>
            <td colspan="5" class="num total-label">Total amount</td>
            <td class="num total-value">{{ order().totalAmount | currency }}</td>
          </tr>
        </tfoot>
      </table>
    </div>
  `,
})
export class OrderItems {
  protected readonly ref = inject(Reference);
  readonly order = input.required<SalesOrder>();
}
