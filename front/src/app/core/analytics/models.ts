import { Role } from '../auth/session';

export const ANALYTICS_VIEWS = [
  { key: 'sales', label: 'Sales', roles: ['Administrator', 'Approver', 'Sales', 'Technician'] },
  { key: 'approvals', label: 'Approvals', roles: ['Administrator', 'Approver'] },
  { key: 'procurement', label: 'Procurement', roles: ['Administrator', 'Approver', 'Purchasing'] },
  { key: 'receiving', label: 'Receiving', roles: ['Administrator', 'Approver', 'Purchasing', 'WarehouseOperator'] },
  { key: 'transfers', label: 'Transfers', roles: ['Administrator', 'WarehouseOperator'] },
  { key: 'inventory', label: 'Inventory', roles: ['Administrator', 'Approver', 'Purchasing', 'WarehouseOperator'] },
  { key: 'dispatch', label: 'Dispatch', roles: ['Administrator', 'Approver', 'Sales', 'Technician'] },
  { key: 'service', label: 'Service', roles: ['Administrator', 'Approver', 'Sales', 'Technician'] },
] as const satisfies readonly { key: string; label: string; roles: readonly Role[] }[];

export type AnalyticsKind = typeof ANALYTICS_VIEWS[number]['key'];
export const ANALYTICS_PAGES = [
  { key: 'sales-service', label: 'Sales & Service', kinds: ['sales', 'dispatch', 'service'] },
  { key: 'purchasing', label: 'Purchasing & Approvals', kinds: ['procurement', 'approvals'] },
  { key: 'warehouse', label: 'Warehouse & Inventory', kinds: ['inventory', 'receiving', 'transfers'] },
] as const satisfies readonly { key: string; label: string; kinds: readonly AnalyticsKind[] }[];
export function permittedAnalytics(kinds: readonly AnalyticsKind[], role: Role): AnalyticsKind[] {
  return kinds.filter(kind => (ANALYTICS_VIEWS.find(view => view.key === kind)!.roles as readonly Role[]).includes(role));
}
export type GroupBy = 'day' | 'week' | 'month';
export interface AnalyticsWindow { from: string; to: string; groupBy: GroupBy }
export interface StatusCount { status: number; label: string; count: number }
export interface CountPoint { date: string; count: number }
export interface ValuePoint extends CountPoint { amount: number }
export interface QuantityPoint extends CountPoint { quantity: number }
export interface ApprovalPoint { date: string; submitted: number; approved: number; rejected: number; returned: number }
export interface SalesAnalytics {
  window: AnalyticsWindow; byStatus: StatusCount[]; orderCount: number; approvedOrderValue: number;
  created: ValuePoint[]; topProducts: { productId: string; name: string; sku: string; quantity: number; amount: number }[];
}
export interface ApprovalsAnalytics {
  window: AnalyticsWindow; snapshotAtUtc: string; pendingSalesOrders: number; pendingPurchaseOrders: number;
  salesActivity: ApprovalPoint[]; purchaseActivity: ApprovalPoint[];
}
export interface ProcurementAnalytics {
  window: AnalyticsWindow; byStatus: StatusCount[]; orderCount: number; committedOrderValue: number;
  orderedQuantity: number; receivedQuantity: number; created: ValuePoint[];
  topSuppliers: { supplierId: string; name: string; orderCount: number; amount: number }[];
}
export interface PostingAnalytics { window: AnalyticsWindow; byStatus: StatusCount[]; posted: QuantityPoint[] }
export interface InventoryAnalytics {
  window: AnalyticsWindow; snapshotAtUtc: string;
  byWarehouse: { warehouseId: string; code: string; name: string; stockOnHand: number; reservedQuantity: number; availableQuantity: number; outOfStockPositions: number }[];
  movementsByKind: StatusCount[];
  movements: { date: string; physicalIn: number; physicalOut: number; reservedDelta: number }[];
}
export interface DispatchAnalytics {
  window: AnalyticsWindow; delivered: number; awaitingDelivery: number; dispatched: CountPoint[];
  byCarrier: { carrier: string; count: number }[]; installedAssetsByStatus: { status: string; count: number }[];
}
export interface ServiceAnalytics {
  window: AnalyticsWindow; byStatus: StatusCount[]; byPriority: StatusCount[];
  created: CountPoint[]; completed: QuantityPoint[];
}
export interface AnalyticsResults {
  sales: SalesAnalytics; approvals: ApprovalsAnalytics; procurement: ProcurementAnalytics;
  receiving: PostingAnalytics; transfers: PostingAnalytics; inventory: InventoryAnalytics;
  dispatch: DispatchAnalytics; service: ServiceAnalytics;
}

export function defaultWindow(now = new Date()): AnalyticsWindow {
  const to = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1));
  const from = new Date(to);
  from.setUTCDate(from.getUTCDate() - 30);
  return { from: from.toISOString().slice(0, 10), to: to.toISOString().slice(0, 10), groupBy: 'day' };
}

export function windowError(window: AnalyticsWindow): string {
  const dates = [window.from, window.to].map(value => {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000')) return NaN;
    const time = Date.parse(`${value}T00:00:00Z`);
    return Number.isFinite(time) && new Date(time).toISOString().slice(0, 10) === value ? time : NaN;
  });
  const days = (dates[1] - dates[0]) / 86400000;
  if (!Number.isFinite(days) || days <= 0 || days > 366) return 'Choose valid dates: From must precede Until by at most 366 days. Until is exclusive.';
  return ['day', 'week', 'month'].includes(window.groupBy) ? '' : 'Choose Day, Week or Month.';
}
