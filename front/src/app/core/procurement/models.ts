export interface Warehouse {
  id: string; code: string; name: string; isActive: boolean; createdAt: string; rowVersion: string;
}
export interface Supplier {
  id: string; supplierCode: string; name: string; contactName: string | null; email: string | null;
  phone: string | null; address: string | null; isActive: boolean; createdAt: string; rowVersion: string;
}
export interface PurchaseLine {
  productId: string; quantity: number; unitCost: number;
}
export interface PurchaseOrder {
  id: string; poNumber: string; supplierId: string; warehouseId: string; createdByUserId: string;
  status: number; createdAt: string; submittedAt: string | null; decidedAt: string | null;
  notes: string | null; totalAmount: number; rowVersion: string;
  items: (PurchaseLine & { id: string; lineTotal: number; receivedQuantity: number; remainingQuantity: number })[];
}
export interface OperationalOrder {
  id: string; poNumber: string; supplierId: string; warehouseId: string; status: number;
  createdAt: string; rowVersion: string;
  items: { purchaseOrderItemId: string; productId: string; quantity: number; receivedQuantity: number; remainingQuantity: number }[];
}
export interface PurchaseHistory {
  id: string; actorId: string; action: number; fromStatus: number; toStatus: number;
  comments: string | null; revision: number; createdAt: string;
}
export interface Receipt {
  id: string; number: string; purchaseOrderId: string; supplierId: string; warehouseId: string;
  status: number; createdByUserId: string; createdAt: string; supplierDeliveryReference: string | null;
  notes: string | null; postedByUserId: string | null; postedAt: string | null;
  cancelledByUserId: string | null; cancelledAt: string | null; cancellationReason: string | null;
  rowVersion: string; purchaseOrderStatus: number; purchaseOrderRowVersion: string;
  items: { id: string; purchaseOrderItemId: string; productId: string; quantity: number; inventoryPostingId: string | null; stockMovementId: string | null }[];
}
export interface Transfer {
  id: string; number: string; sourceWarehouseId: string; destinationWarehouseId: string; status: number;
  createdByUserId: string; createdAt: string; postedByUserId: string | null; postedAt: string | null;
  cancelledByUserId: string | null; cancelledAt: string | null; cancellationReason: string | null; rowVersion: string;
  items: { id: string; productId: string; quantity: number; sourceMovementId: string | null; destinationMovementId: string | null }[];
}
export interface Stock {
  id: string; warehouseId: string; warehouseCode: string; productId: string; productSku: string;
  productName: string; stockOnHand: number; reservedQuantity: number; availableStock: number; sequence: number; rowVersion: string;
}
export interface Movement {
  id: string; warehouseId: string; productId: string; sequence: number; kind: number;
  physicalDelta: number; reservedDelta: number; beforeOnHand: number; beforeReserved: number;
  afterOnHand: number; afterReserved: number; actorId: string; createdAt: string;
  sourceType: string; sourceDocumentId: string | null; sourceNumber: string | null;
}
export const PO_STATUS = ['Draft', 'Pending approval', 'Approved', 'Rejected', 'Cancelled', 'Partially received', 'Received', 'Short closed'];
export const DOCUMENT_STATUS = ['Draft', 'Posted', 'Cancelled'];

export function localTimestamp(value: string | null): string | null {
  return value && !/(Z|[+-]\d\d:?\d\d)$/.test(value) ? `${value}Z` : value;
}

export function validLines(lines: PurchaseLine[]): boolean {
  return lines.length > 0 && lines.length <= 100 && new Set(lines.map(l => l.productId)).size === lines.length &&
    lines.every(l => !!l.productId && Number.isInteger(l.quantity) && l.quantity > 0 && l.quantity <= 2147483647 &&
      Number.isFinite(l.unitCost) && l.unitCost >= 0 && /^\d+(\.\d{1,4})?$/.test(String(l.unitCost)));
}
