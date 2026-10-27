import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../config';
import { localTimestamp, validLines } from './models';
import { ProcurementApi } from './procurement-api';

describe('Procurement contracts', () => {
  it('rejects duplicate products, fractional quantities and excess cost precision', () => {
    const line = { productId: 'product-1', quantity: 2, unitCost: 12.3456 };
    expect(validLines([line])).toBe(true);
    expect(validLines([])).toBe(false);
    expect(validLines([line, line])).toBe(false);
    expect(validLines([{ ...line, quantity: 1.5 }])).toBe(false);
    expect(validLines([{ ...line, unitCost: 12.34567 }])).toBe(false);
    expect(validLines([{ ...line, quantity: 2147483648 }])).toBe(false);
  });

  it('treats zone-less API timestamps as UTC without changing explicit offsets', () => {
    expect(localTimestamp('2026-10-27T12:00:00')).toBe('2026-10-27T12:00:00Z');
    expect(localTimestamp('2026-10-27T12:00:00+03:00')).toBe('2026-10-27T12:00:00+03:00');
    expect(localTimestamp(null)).toBeNull();
  });

  it('keeps transfer keys in headers and receipt keys in bodies; never retries failed writes', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(ProcurementApi);
    const http = TestBed.inject(HttpTestingController);
    const transfer = api.post('stock-transfers/t1/post', { rowVersion: 'version' }, 'transfer-key');
    const failed = expect(transfer).rejects.toMatchObject({ status: 503 });
    const request = http.expectOne(`${API_BASE_URL}/api/stock-transfers/t1/post`);
    expect(request.request.headers.get('Idempotency-Key')).toBe('transfer-key');
    expect(request.request.body).toEqual({ rowVersion: 'version' });
    request.flush({}, { status: 503, statusText: 'Unavailable' });
    await failed;
    const receipt = api.post('goods-receipts/r1/post', { rowVersion: 'version', idempotencyKey: 'receipt-key' });
    const second = http.expectOne(`${API_BASE_URL}/api/goods-receipts/r1/post`);
    expect(second.request.headers.has('Idempotency-Key')).toBe(false);
    expect(second.request.body.idempotencyKey).toBe('receipt-key');
    second.flush({});
    await receipt;
    http.verify();
  });
});
