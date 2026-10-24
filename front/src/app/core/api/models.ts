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
  3: { label: 'Returned', tone: 'amber' },
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

export interface DeliveryNote {
  id: string;
  salesOrderId: string;
  customerId: string;
  deliveryNumber: string;
  carrier: string;
  trackingNumber: string | null;
  dispatchedAt: string;
  deliveredAt: string | null;
}

export interface MachineAsset {
  id: string;
  tagNumber: string;
  serialNumber: string;
  productId: string;
  customerId: string;
  salesOrderId: string | null;
  installedAt: string;
  warrantyExpiresAt: string;
  status: string;
  meterReading: number;
}

export interface JobcardItem {
  id: string;
  productId: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface Jobcard {
  id: string;
  ticketNumber: string;
  customerId: string;
  machineAssetId: string | null;
  technicianName: string;
  reportedFault: string;
  priority: number; // 0=Low, 1=Medium, 2=High, 3=Urgent
  status: number; // 0=Open, 1=PartsRequested, 2=InProgress, 3=Completed, 4=Cancelled
  createdAt: string;
  completedAt: string | null;
  technicianNotes: string | null;
  finalMeterReading: number | null;
  items: JobcardItem[];
}

export const JOBCARD_STATUS: Record<number, { label: string; tone: Tone }> = {
  0: { label: 'Open', tone: 'amber' },
  1: { label: 'Parts Requested', tone: 'blue' },
  2: { label: 'In Progress', tone: 'amber' },
  3: { label: 'Completed', tone: 'green' },
  4: { label: 'Cancelled', tone: 'gray' },
};

export const JOBCARD_PRIORITY: Record<number, { label: string; tone: Tone }> = {
  0: { label: 'Low', tone: 'gray' },
  1: { label: 'Medium', tone: 'blue' },
  2: { label: 'High', tone: 'amber' },
  3: { label: 'Urgent', tone: 'red' },
};

export function shortId(id: string): string {
  return id.slice(0, 8).toUpperCase();
}

export function reservationState(order: SalesOrder): { label: string; tone: Tone } {
  if (order.reservations.length === 0) return { label: 'Not reserved', tone: 'gray' };
  if (order.reservations.every((r) => r.isReleased)) return { label: 'Released', tone: 'gray' };
  return { label: 'Reserved', tone: 'green' };
}
