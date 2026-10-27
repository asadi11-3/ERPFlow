# Analytics charts implementation plan

## Implemented consolidation

The navigation proposal below is superseded by three combined pages:

- `/analytics/sales-service`: Sales, Dispatch and Service endpoints.
- `/analytics/purchasing`: Procurement and Approvals endpoints.
- `/analytics/warehouse`: Inventory, Receiving and Transfers endpoints.

Each page requests only endpoints permitted for the signed-in role. One UTC date/grouping filter controls all its sections; each section has independent loading, errors and retry. Original endpoint-specific URLs redirect to the combined page and preserve query parameters.

Chart.js renders line charts for time trends, pie charts for status breakdowns and bar charts for comparisons. Exact-value tables remain available for every chart. Chart.js is lazy-loaded with the analytics page and chart instances are destroyed when replaced or unmounted.

## Scope and evidence

Design eight working, read-only analytics views using the existing warm-paper surfaces, teal accent, Geist/Geist Mono typography, shared filters, cards, tables and responsive shell. This document plans implementation; it does not add charts or change backend behavior.

Sources inspected:
- `ERPFlow.Api/AnalyticsEndpoints.cs`, mounted by `ApiEndpoints.cs`.
- `ERPFlow.Application/Analytics/AnalyticsContracts.cs`.
- `ERPFlow.Infrastructure/Persistence/EfAnalyticsQueries.cs`, `.Commercial.cs`, `.Operations.cs`.
- `ERPFlow.Tests/AnalyticsApiTests.cs` and `AnalyticsIntegrationTests.cs`.
- Existing frontend dependencies and UI/UX skill chart guidance.

There are eight new GET operations. `docs/openapi/erpflow-v1.json` currently contains none of them. Generate/update that contract in a separately authorized backend documentation change before relying on generated frontend types. The actual query implementations govern the semantics below.

## 1. Navigation and access

Add one collapsible **Insights** sidebar category, following the existing category interaction. Each link loads one analytics view; only render authorized links. Keep current home routes and operational pages.

| Endpoint | Proposed page | Allowed roles | Scope |
| --- | --- | --- | --- |
| `GET /api/analytics/sales` | `/analytics/sales` | Administrator, Approver, Sales, Technician | Sales sees own orders; other allowed roles see all |
| `GET /api/analytics/approvals` | `/analytics/approvals` | Administrator, Approver | Organization-wide sales and purchase activity |
| `GET /api/analytics/procurement` | `/analytics/procurement` | Administrator, Approver, Purchasing | Purchasing sees own purchase orders |
| `GET /api/analytics/receiving` | `/analytics/receiving` | Administrator, Approver, Purchasing, WarehouseOperator | Purchasing sees receipts belonging to own purchase orders |
| `GET /api/analytics/transfers` | `/analytics/transfers` | Administrator, WarehouseOperator | All transfers |
| `GET /api/analytics/inventory` | `/analytics/inventory` | Administrator, Approver, Purchasing, WarehouseOperator | All warehouse balances and movements; warehouse authority required |
| `GET /api/analytics/dispatch` | `/analytics/dispatch` | Administrator, Approver, Sales, Technician | All dispatches/assets, including for Sales |
| `GET /api/analytics/service` | `/analytics/service` | Administrator, Approver, Sales, Technician | All jobcards, including for Sales |

Guards and visibility mirror this matrix; the server remains authoritative. Analytics access does not imply access to operational details. In particular, Sales/Approver service analytics must not link into Technician/Administrator-only jobcard pages. Approver/Purchasing receiving analytics must not link into Administrator/WarehouseOperator-only receipt lists. Show appropriate operational links only after checking their separate permissions.

Bare `/analytics` redirects to the first permitted view in the table. No all-endpoint dashboard request or unauthorized background prefetch.

## 2. Shared page design and filters

Page order:
1. Existing page heading, short scope explanation and permitted operational link.
2. Existing `.toolbar.filter-bar`: **From (UTC)**, **Until (UTC, exclusive)** native date inputs; **Group by** existing `app-select` with Day, Week, Month; Apply and Refresh buttons.
3. Compact textual summary strip with labelled counts/amounts, without oversized KPI tiles.
4. Primary full-width activity chart, followed by supporting comparison charts and exact-value tables.
5. Current-snapshot sections where applicable, explicitly separated from date-filtered activity.

Use date-only query strings `from=YYYY-MM-DD&to=YYYY-MM-DD&groupBy=day|week|month`. The interval is `[from, to)` in UTC, not local-midnight timestamps. Require from < to and at most 366 days. Default matches the API: UTC tomorrow as exclusive end and 30 days before that end as start. The response `window` is authoritative.

Suggested presets: Last 7 days, Last 30 days, This month. Each includes the current UTC day through tomorrow's exclusive boundary. Apply commits draft filters, resets chart selection and updates URL query parameters; Refresh repeats the applied query. Invalid input stays beside the fields and does not send a request. Preserve applied filters across view changes. Use explicit forms rather than fetching on every date keystroke.

Week buckets begin Monday; month buckets begin on the first. The first bucket can precede From, and first/last buckets may be partial. Label them as partial periods and never imply they contain a complete week/month. Render date-only labels without timezone conversion that shifts their day.

No warehouse, product, supplier, customer, user or status server filters exist on these endpoints. Do not add them to requests or present a local slice as a server-wide filter.

## 3. Endpoint-specific charts and meaning

### A01. Sales

**Task:** understand order volume and approved value for orders created during the selected window.

- Summary: `orderCount`, `approvedOrderValue`; Sales scope label **Your orders**.
- Primary: `created[].count` as columns by bucket. Separate metric switch to `created[].amount`, labelled **Approved value by order creation date**. Use a single axis per metric.
- Supporting: horizontal bars for `byStatus` and top ten `topProducts` ranked by amount, with name/SKU, quantity and amount in an accessible table.
- Crucial: status is the current status of the created-order cohort. Amount includes only currently Approved orders from that cohort. It is not recognized revenue or approval-date activity. Top products use the same cohort and approved-only rule.
- Link to `/orders`; a status shortcut may use the existing supported server status filter once the list consumes its URL parameter. Do not imply date-filter drill-down when the orders list cannot honor dates.

### A02. Approvals

**Task:** compare recorded submissions and decisions, and inspect the current pending workload.

- Snapshot summary: `pendingSalesOrders`, `pendingPurchaseOrders`, labelled **Current queue** with `snapshotAtUtc` displayed in local time.
- Date-window sections: Sales / Purchase tabs, each plotting `salesActivity` or `purchaseActivity`. Default approved/rejected grouped columns; a labelled metric selector exposes submitted and returned counts without four crowded series.
- Exact table includes every response series. Purchase Returned is always zero; omit that chart choice rather than advertising an unsupported purchase return workflow.
- Current queues are not date-filtered. Events are counted by history timestamp, so resubmissions and repeated actions are events, not distinct orders. Never derive approval rate, SLA, average decision time or conversion funnel from these counts.
- Link sales queue to `/approvals`, purchase queue to the supported purchase-order list with Pending Approval filtering when URL support is implemented.

### A03. Procurement

**Task:** understand purchasing commitments and receipt progress for purchase orders created in the window.

- Summary: `orderCount`, `committedOrderValue`, `orderedQuantity`, `receivedQuantity`; Purchasing scope **Your purchase orders**.
- Primary: `created[].count` columns, switch to amount labelled **Committed value by order creation date**.
- Supporting: `byStatus` horizontal bars; top ten `topSuppliers` ranked by committed amount, with order count and amount in a table.
- Committed means current Approved, PartiallyReceived, Received or ShortClosed. Ordered/received quantities and supplier totals cover only these committed orders from the created cohort. Received quantity is the current accumulated quantity, not receipts posted during the selected window.
- ShortClosed may appear as a status even though there is no short-close action endpoint. Display it read-only.
- Link to `/purchase-orders`; no fabricated supplier analytics detail route.

### A04. Receiving

**Task:** distinguish receipt creation status from actual posted receiving activity.

- Summary: sum of `posted[].count` labelled **Receipts posted**, sum of `posted[].quantity` labelled **Units received**.
- Primary: `posted` columns with a Receipts / Units metric switch.
- Supporting: `byStatus` horizontal bars labelled **Current status of receipts created in this window**.
- These cohorts differ: posted series uses `postedAt`, status distribution uses `createdAt`. A posted receipt created before From can appear only in the posting chart. Do not reconcile these sections as matching totals.
- Administrator/WarehouseOperator link to `/goods-receipts`; Purchasing/Approver may link to `/receiving` but not restricted receipt detail pages.

### A05. Transfers

**Task:** track transfer posting volume, not inventory growth.

- Summary: sum of `posted[].count` (**Transfers posted**) and quantity (**Units transferred**).
- Primary: posting columns with Transfers / Units switch; supporting current-status bars for transfers created in the window.
- Posting timestamps and creation-status cohorts differ, as in receiving. Quantities count each transferred unit once here; the inventory ledger contains both source and destination legs.
- Link to `/stock-transfers`. No origin/destination split is returned, so do not invent a flow map or warehouse pair chart.

### A06. Inventory

**Task:** inspect balances now and physical/reservation activity during the window.

- Current section: `byWarehouse` table plus grouped horizontal bars for Available / Reserved. Physical on-hand is a labelled total, not a third stacked segment: available + reserved = on-hand.
- Include warehouse code/name and `outOfStockPositions`, labelled **Tracked positions with no available stock**. This counts existing warehouse/product positions with available <= 0, not every catalog product absent from a warehouse. Warehouses without tracked stock rows may be absent.
- Show `snapshotAtUtc` as the balance snapshot time. Date filters do not reconstruct historical balances.
- Activity: `movements` columns for physical in and physical out; switch to a separate signed reserved-delta chart with a zero baseline. Use absolute out quantities as returned, not negative inventory balances.
- `movementsByKind` horizontal bars show ledger-row counts, not units or posting counts. Transfer in/out and opening entries are legitimate activity; do not label gross inbound movement as purchases or revenue.
- 409 authority unavailable: show the server explanation and Retry, with no zero-balance substitute.
- Warehouse links may open `/inventory?warehouseId=...`; movement links use only filters supported by the existing inventory screen. No implied product-level analytics data.

### A07. Dispatch

**Task:** inspect dispatch activity and the current state of the selected dispatch/install cohorts.

- Summary: sum `dispatched[].count`, `delivered`, `awaitingDelivery`.
- Primary: `dispatched` count columns, based on dispatch timestamps.
- Supporting: `byCarrier` horizontal ranking with table; blank carrier labelled **Not specified** while preserving its actual value. For long lists, show a clearly labelled top-ten chart and retain all carriers in the table, without claiming those ten are the total.
- `installedAssetsByStatus`: compact horizontal bars/table, labelled **Current status of assets installed in this window**. This is a separate installation cohort, not a subset of delivery counts.
- Delivered means the selected dispatched cohort currently has DeliveredAt set; it does not mean deliveries completed during the selected dates. There is no delivery-time trend or mark-delivered UI action to add.
- Link to the existing assets/delivery-notes screen using its supported tab navigation. No carrier-detail route.

### A08. Service

**Task:** compare jobcard intake and completion activity.

- Primary Created / Completed tabs: `created[].count` and `completed[].count` respectively. Completion table additionally shows `completed[].quantity`, labelled **Parts consumed on completed jobcards**.
- Supporting horizontal bars: `byStatus` and `byPriority`, both current attributes of jobcards created within the date window.
- Completions use `completedAt` and may include older jobcards; intake/status and completion totals need not match. Parts quantity is not hours worked, technician productivity or repair duration.
- Technician/Administrator get `/service` link. Sales/Approver retain analytics access without a link to restricted jobcard records.

## 4. Chart craft, accessibility and responsive behavior

- Use columns for bucketed event/quantity totals and horizontal bars for status/rank comparisons. Avoid donuts, gauges, decorative area fills and dual-axis amount/count plots.
- Teal is the main series; secondary series use existing neutral/semantic tokens with visible labels, patterns or distinct outlines. Color alone never identifies a series.
- Count/quantity axes use integer formatting and a zero baseline. Signed deltas include zero and negative space. Amounts use the existing number formatter without inventing a currency code or symbol absent from the contract.
- Keep server zero-filled buckets, including leading/trailing periods. Do not interpolate missing values, smooth lines, estimate trends, or compare partial periods without saying so.
- Native SVG/CSS is sufficient for these bounded bars and a maximum 366 daily buckets. Start with two small presentational primitives, time columns and horizontal comparisons. No chart dependency is currently installed. Reassess a library only if confirmed requirements outgrow these plots, not for speculative zoom or export.
- Chart figures have concise text summaries, axis/unit labels, and adjacent **View data** tables containing exact values. Pointer tooltips are supplementary. Do not create 366 keyboard tab stops: offer one keyboard-operable selected bucket control or the data table.
- At 320–640px, charts stack in one column; labels wrap/middle-truncate with full accessible text. Retain a readable chart height and thin bars for long periods, reduce tick-label density, and offer Week/Month grouping without silently changing the applied selection. Exact values remain available in the table.
- Reuse existing mobile accordion records for dense tables. Keep essential date/name/value visible when collapsed. Reuse established buttons, selects and middle text components.
- No count-up, drawing, or replay-on-refresh animation. Existing page entrance and short selection feedback are sufficient; reduced-motion removes transforms. Keyboard changes update immediately.

## 5. Request and state behavior

- Fetch only the active authorized endpoint. Use cancellable `httpResource` where suitable or the existing request-sequence pattern to prevent stale responses replacing a newer applied window.
- First load: layout-matched skeleton plus accessible busy state. Refetch: keep prior chart only with an explicit updating/stale label; never relabel old data with newly requested dates.
- Empty valid response: **No activity in this period**, preserved zero axes and a usable filter action. An inventory snapshot may still contain balances while its date-window movement section is empty.
- 400: inline filter/validation message. 401: existing session clear/login return URL. 403: inline no access. 404: analytics unavailable on this running API. 409 inventory: authority explanation. 5xx/network: recoverable inline error and Retry. Never replace failed requests with zero data.
- Snapshot timestamps exist only for approvals/inventory. Other pages may show **Fetched at** using client receipt time, never label it a backend transactional snapshot.
- Aggregate endpoints run multiple queries; do not promise a cross-endpoint atomic dashboard snapshot.
- Operational links indicate that they open the operational list, rather than claiming to reproduce a date-window cohort that the destination cannot filter.

## 6. Minimal implementation structure

- `core/analytics/analytics-api.ts`: eight typed reads; no chart or route logic.
- `core/analytics/models.ts`: exact DTOs, explicit date-only values and role capability map where shared.
- `feature/analytics/analytics.routes.ts`: lazy route configuration and allowed-role data/guards.
- One analytics page composition with domain-specific template sections or small local view components as complexity warrants. Keep each endpoint's labels and cohort semantics explicit rather than building a configurable dashboard framework.
- `ui/chart/`: generic input-only column and horizontal-bar renderers, no core imports or injected business state. Exact-value table uses existing UI/table patterns.
- Extend the shell's Insights links and existing boundary configuration only as required by the established feature/core/ui policies.

Before structural implementation, read the relevant Trajan feature/UI chapters and Shvets composition guidance as required by repository instructions. No base components, chart plugin registry, global analytics store, polling loop or chart export subsystem.

## 7. Delivery order and acceptance

1. Typed contracts, role routing, shared UTC filter form and one accessible chart primitive. Verify exclusive dates, 366-day limit, Monday/month buckets, partial periods, URL restore and no timezone drift.
2. Sales and procurement vertical slices. Verify own-record scope, current-status cohort wording, approved/committed value and top-ten rankings against real responses.
3. Approvals and service. Verify event-vs-order counts, current queue snapshot, separate created/completed cohorts and record-link permissions.
4. Receiving, transfers and dispatch. Verify posting-vs-creation windows, units-vs-documents and separate installation cohort.
5. Inventory. Verify current available + reserved = on-hand, signed reservation movement, transfer double-leg interpretation and 409 state.
6. Lint, typecheck/build, focused Vitest coverage and Playwright at 320, 375, 768 and 1440px. Verify keyboard controls, full-value tables, contrast, reduced motion and zero/long/failed responses. Verify all eight live endpoints with authorized users; isolated fixtures supplement edge cases but do not replace live verification.

Minimum meaningful automated checks: date/filter serialization including DST boundaries; role matrix and forbidden links; chart zero/signed values and accessible data alternative; stale-response protection. Retain backend tests for authentication/role restrictions and accounting semantics. Planning did not execute or claim a fresh backend test run.

Success: all eight endpoints have one discoverable, authorized view; every chart label matches its actual cohort/time semantics; no unsupported filters, financial claims, drill-downs or fabricated data; existing visual system remains coherent.
