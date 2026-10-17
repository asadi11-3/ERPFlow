export type Tone = 'green' | 'amber' | 'gray' | 'red' | 'blue';

export type OrderStatus = 0 | 1 | 2 | 3;

export const STATUSES: readonly OrderStatus[] = [0, 1, 2, 3];

export const STATUS: Record<OrderStatus, { label: string; tone: Tone }> = {
  0: { label: 'Draft', tone: 'gray' },
  1: { label: 'Pending Approval', tone: 'amber' },
  2: { label: 'Approved', tone: 'green' },
  3: { label: 'Rejected', tone: 'red' },
};

export const ACTION: Record<number, { label: string; tone: Tone }> = {
  0: { label: 'Approved', tone: 'green' },
  1: { label: 'Rejected', tone: 'red' },
  2: { label: 'Submitted', tone: 'blue' },
};

export const HIGH_VALUE = 10000;

export interface OrderItem {
  id: string;
  productId: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface Reservation {
  id: string;
  productId: string;
  quantity: number;
  isReleased: boolean;
  createdAt: string;
  releasedAt: string | null;
}

export interface SalesOrder {
  id: string;
  customerId: string;
  createdByUserId: string;
  status: OrderStatus;
  createdAt: string;
  submittedAt: string | null;
  decidedAt: string | null;
  totalAmount: number;
  items: OrderItem[];
  reservations: Reservation[];
}

export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface Customer {
  id: string;
  name: string;
  email: string;
}

export interface Product {
  id: string;
  name: string;
  sku: string;
  unitPrice: number;
  availableStock: number;
}

export interface HistoryEntry {
  action: number;
  actorId: string;
  timestampUtc: string;
  remarks: string | null;
}

export interface NewItem {
  productId: string;
  quantity: number;
}

export function shortId(id: string): string {
  return id.slice(0, 8).toUpperCase();
}

export function reservationState(order: SalesOrder): { label: string; tone: Tone } {
  if (order.reservations.length === 0) return { label: 'Not reserved', tone: 'gray' };
  if (order.reservations.every((r) => r.isReleased)) return { label: 'Released', tone: 'gray' };
  return { label: 'Reserved', tone: 'green' };
}
