import { AnalyticsKind, AnalyticsResults, AnalyticsWindow, StatusCount } from '../../core/analytics/models';
import { Role } from '../../core/auth/session';
import { ChartRow, ChartSeries } from '../../ui/chart/chart';

export interface AnalyticsChart {
  title: string; description: string; rows: ChartRow[]; series: ChartSeries[]; horizontal?: boolean; switchMetric?: boolean; type?: 'bar' | 'line' | 'pie';
}
export interface AnalyticsView {
  window: AnalyticsWindow; note: string; summary: { label: string; value: number; amount?: boolean }[];
  charts: AnalyticsChart[]; snapshot?: string; link?: { label: string; path: string };
}
const count: ChartSeries[] = [{ label: 'Count' }];
const statuses = (title: string, rows: StatusCount[], description: string): AnalyticsChart => ({
  title, description, type: 'pie', horizontal: true, rows: rows.map(row => ({ label: row.label.replace(/([a-z])([A-Z])/g, '$1 $2'), values: [row.count] })), series: count,
});
const time = (title: string, description: string, rows: ChartRow[], series: ChartSeries[], switchMetric = false): AnalyticsChart => ({ title, description, rows, series, switchMetric, type: 'line' });
const total = <T>(rows: T[], value: (row: T) => number) => rows.reduce((sum, row) => sum + value(row), 0);

export function analyticsView(kind: AnalyticsKind, result: AnalyticsResults[AnalyticsKind], role: Role): AnalyticsView {
  const base = { window: result.window };
  switch (kind) {
    case 'sales': {
      const data = result as AnalyticsResults['sales'];
      return { ...base, note: `${role === 'Sales' ? 'Your orders' : 'All sales orders'} created in this UTC window. Status and approved values reflect their current state, not approval-date revenue.`,
        summary: [{ label: 'Orders created', value: data.orderCount }, { label: 'Approved order value', value: data.approvedOrderValue, amount: true }],
        link: { label: 'Open sales orders', path: '/orders' }, charts: [
          time('Order intake', 'Approved value is grouped by order creation date. Amounts have no currency specified by the API.', data.created.map(row => ({ label: row.date, values: [row.count, row.amount] })), [{ label: 'Orders' }, { label: 'Approved value', amount: true }], true),
          statuses('Current order status', data.byStatus, 'Only orders created in the selected window.'),
          { title: 'Top products by approved value', description: 'Up to ten products from currently approved orders in this creation cohort.', horizontal: true, switchMetric: true,
            rows: data.topProducts.map(row => ({ label: `${row.name} · ${row.sku}`, values: [row.amount, row.quantity] })), series: [{ label: 'Approved value', amount: true }, { label: 'Units' }] },
        ] };
    }
    case 'procurement': {
      const data = result as AnalyticsResults['procurement'];
      return { ...base, note: `${role === 'Purchasing' ? 'Your purchase orders' : 'All purchase orders'} created in this window. Commitments include Approved, Partially Received, Received and Short Closed. Received units are accumulated now, not posting activity within these dates.`,
        summary: [{ label: 'Orders created', value: data.orderCount }, { label: 'Committed value', value: data.committedOrderValue, amount: true }, { label: 'Committed units ordered', value: data.orderedQuantity }, { label: 'Committed units received', value: data.receivedQuantity }],
        link: { label: 'Open purchase orders', path: '/purchase-orders' }, charts: [
          time('Purchase order intake', 'Committed value by order creation date, using current status. No currency is specified by the API.', data.created.map(row => ({ label: row.date, values: [row.count, row.amount] })), [{ label: 'Orders' }, { label: 'Committed value', amount: true }], true),
          statuses('Current purchase order status', data.byStatus, 'Only purchase orders created in the selected window.'),
          { title: 'Top suppliers by commitment', description: 'Up to ten suppliers, ranked by committed order value in this creation cohort.', horizontal: true, switchMetric: true,
            rows: data.topSuppliers.map(row => ({ label: row.name, values: [row.amount, row.orderCount] })), series: [{ label: 'Committed value', amount: true }, { label: 'Orders' }] },
        ] };
    }
    case 'approvals': {
      const data = result as AnalyticsResults['approvals'];
      return { ...base, snapshot: data.snapshotAtUtc, note: 'Current queues are independent of the date filter. Activity counts history events, including repeated submissions, rather than distinct orders.',
        summary: [{ label: 'Sales pending now', value: data.pendingSalesOrders }, { label: 'Purchases pending now', value: data.pendingPurchaseOrders }],
        link: { label: 'Open sales approval queue', path: '/approvals' }, charts: [
          time('Sales approval activity', 'Recorded event dates. Select an action to inspect its counts.', data.salesActivity.map(row => ({ label: row.date, values: [row.approved, row.rejected, row.submitted, row.returned] })), [{ label: 'Approved' }, { label: 'Rejected' }, { label: 'Submitted' }, { label: 'Returned' }], true),
          time('Purchase approval activity', 'Recorded event dates. Purchase returns are not a supported workflow.', data.purchaseActivity.map(row => ({ label: row.date, values: [row.approved, row.rejected, row.submitted] })), [{ label: 'Approved' }, { label: 'Rejected' }, { label: 'Submitted' }], true),
        ] };
    }
    case 'receiving':
    case 'transfers': {
      const data = result as AnalyticsResults['receiving'];
      const receipt = kind === 'receiving';
      return { ...base, note: `${receipt && role === 'Purchasing' ? 'Receipts for your purchase orders. ' : ''}Posting activity uses posting dates; status counts use creation dates. These are different cohorts.${receipt ? '' : ' Transferred units are counted once here, not as stock growth.'}`,
        summary: [{ label: receipt ? 'Receipts posted' : 'Transfers posted', value: total(data.posted, row => row.count) }, { label: receipt ? 'Units received' : 'Units transferred', value: total(data.posted, row => row.quantity) }],
        link: receipt ? { label: role === 'Administrator' || role === 'WarehouseOperator' ? 'Open goods receipts' : 'Open receiving', path: role === 'Administrator' || role === 'WarehouseOperator' ? '/goods-receipts' : '/receiving' } : { label: 'Open stock transfers', path: '/stock-transfers' },
        charts: [time('Posted activity', 'Only documents posted within the selected UTC window.', data.posted.map(row => ({ label: row.date, values: [row.count, row.quantity] })), [{ label: receipt ? 'Receipts' : 'Transfers' }, { label: 'Units' }], true), statuses('Current document status', data.byStatus, 'Documents created in the window, not necessarily posted in it.')] };
    }
    case 'inventory': {
      const data = result as AnalyticsResults['inventory'];
      return { ...base, snapshot: data.snapshotAtUtc, note: 'Balances are current, not reconstructed for the selected dates. Movement activity includes opening stock and both legs of transfers. Positions with no available stock count tracked warehouse/product positions only.',
        summary: [{ label: 'On hand now', value: total(data.byWarehouse, row => row.stockOnHand) }, { label: 'Reserved now', value: total(data.byWarehouse, row => row.reservedQuantity) }, { label: 'Available now', value: total(data.byWarehouse, row => row.availableQuantity) }, { label: 'Tracked positions with no available stock', value: total(data.byWarehouse, row => row.outOfStockPositions) }],
        link: { label: 'Open inventory ledger', path: '/inventory' }, charts: [
          time('Physical movement activity', 'Gross units in/out by posting date. Transfers contribute an inbound and an outbound leg.', data.movements.map(row => ({ label: row.date, values: [row.physicalIn, row.physicalOut] })), [{ label: 'Physical in' }, { label: 'Physical out' }]),
          time('Reservation changes', 'Net reserved-unit change by posting date. Negative values release reservations.', data.movements.map(row => ({ label: row.date, values: [row.reservedDelta] })), [{ label: 'Reserved delta' }]),
          { title: 'Current warehouse balances', description: 'Available plus reserved equals on hand. Warehouses without tracked positions may be absent. Exact on-hand and out-of-stock positions are in View data.', horizontal: true,
            rows: data.byWarehouse.map(row => ({ label: `${row.code} · ${row.name}`, values: [row.availableQuantity, row.reservedQuantity, row.stockOnHand, row.outOfStockPositions] })), series: [{ label: 'Available' }, { label: 'Reserved' }, { label: 'On hand' }, { label: 'Positions with no available stock' }], switchMetric: true },
          statuses('Movement kinds', data.movementsByKind, 'Ledger-row counts in the selected window, not units or distinct postings.'),
        ] };
    }
    case 'dispatch': {
      const data = result as AnalyticsResults['dispatch'];
      return { ...base, note: 'All dispatches, including when viewed by Sales. Delivered and awaiting delivery describe the current state of dispatches sent during this window. Installed assets form a separate installation-date cohort.',
        summary: [{ label: 'Dispatched', value: total(data.dispatched, row => row.count) }, { label: 'Now delivered', value: data.delivered }, { label: 'Awaiting delivery', value: data.awaitingDelivery }],
        link: { label: 'Open machine assets', path: '/assets' }, charts: [time('Dispatch activity', 'Delivery notes grouped by dispatch date.', data.dispatched.map(row => ({ label: row.date, values: [row.count] })), [{ label: 'Dispatches' }]),
          { title: 'Dispatches by carrier', description: 'Dispatch cohort ranked by carrier; blank values are Not specified.', horizontal: true, rows: data.byCarrier.map(row => ({ label: row.carrier.trim() || 'Not specified', values: [row.count] })), series: count },
          { title: 'Installed assets by current status', description: 'Assets installed in the selected window, not the dispatched delivery cohort.', horizontal: true, rows: data.installedAssetsByStatus.map(row => ({ label: row.status, values: [row.count] })), series: count },
        ] };
    }
    case 'service': {
      const data = result as AnalyticsResults['service'];
      return { ...base, note: 'All jobcards. Status and priority cover jobcards created in this window. Completions use completion dates and can include older jobcards. Parts consumed are units, not hours worked.',
        summary: [{ label: 'Jobcards created', value: total(data.created, row => row.count) }, { label: 'Jobcards completed', value: total(data.completed, row => row.count) }, { label: 'Parts on completed jobcards', value: total(data.completed, row => row.quantity) }],
        link: role === 'Administrator' || role === 'Technician' ? { label: 'Open service jobcards', path: '/service' } : undefined,
        charts: [time('Jobcard intake', 'Grouped by creation date.', data.created.map(row => ({ label: row.date, values: [row.count] })), [{ label: 'Created' }]),
          time('Jobcard completions', 'Grouped by completion date. Parts quantity covers these completed jobcards.', data.completed.map(row => ({ label: row.date, values: [row.count, row.quantity] })), [{ label: 'Completed' }, { label: 'Parts consumed' }], true),
          statuses('Current jobcard status', data.byStatus, 'Jobcards created in the selected window.'), statuses('Current jobcard priority', data.byPriority, 'Jobcards created in the selected window.'),
        ] };
    }
  }
}
