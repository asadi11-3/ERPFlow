# Analytics API

Eight authenticated, read-only GET endpoints under `/api/analytics`. Responses are chart-ready aggregates, not paginated document lists. No schema changes or migrations.

## Common query

`GET /api/analytics/sales?from=2026-01-01&to=2026-02-01&groupBy=week`

- `from`: inclusive UTC calendar date (`yyyy-MM-dd`).
- `to`: exclusive UTC calendar date. Defaults to tomorrow UTC, including today.
- Omitted `from`: 30 days before `to` (clamped at the earliest calendar date).
- Positive range, at most 366 days. Invalid ranges or grouping return 400.
- `groupBy`: `day` (default), `week` (Monday), or `month` (first day).
- `window` echoes resolved dates/grouping and UTC boundaries. Series include zero-valued empty buckets. First bucket can start before `from`; only events inside the requested window contribute.
- Amounts use stored decimal prices/costs. No currency exists in the domain; clients must not infer one. These values are neither recognized revenue nor paid cash.
- Status/priority distributions include zero counts for every defined enum value; numeric `status` and readable `label` are both returned.
- Authorization uses the persisted user. Sales sees only their own sales orders; Purchasing sees only their own purchase orders and associated receipts.

## Endpoints and chart semantics

| Endpoint | Roles | Measures |
| --- | --- | --- |
| `/sales` | Administrator, Approver, Sales, Technician | Orders created in window, current status distribution, created count/value series, top 10 products by approved value. Value and product quantities include only currently Approved orders from this created cohort. |
| `/approvals` | Administrator, Approver | Sales/purchase history event counts by event time. Repeated submissions count separately; sales returns are separate. Pending queue counts are current, independent of date window, with `snapshotAtUtc`. |
| `/procurement` | Administrator, Approver, Purchasing | Purchase orders created in window, current status distribution, created count/committed value series, quantities and top 10 suppliers by committed value. Committed statuses: Approved, PartiallyReceived, Received, ShortClosed. Received quantity is cumulative against this created cohort, not receipts during the window. |
| `/receiving` | Administrator, Approver, Purchasing, WarehouseOperator | Status distribution of receipts created in window; posted document counts and units by actual `postedAt`. Drafts/cancelled receipts contribute no posted quantity. |
| `/transfers` | Administrator, WarehouseOperator | Status distribution of transfers created in window; posted counts and units by `postedAt`. Each transfer counts once, not once per warehouse movement. |
| `/inventory` | Administrator, Approver, Purchasing, WarehouseOperator | Current warehouse balances (`stockOnHand`, `reservedQuantity`, `availableQuantity`, `outOfStockPositions`), movement-kind counts and physical-in/out/reservation-delta series by posting time. Requires warehouse inventory authority; otherwise 409. |
| `/dispatch` | Administrator, Approver, Sales, Technician | Dispatch count series and carrier distribution by dispatch time; delivered/awaiting counts for that cohort using current delivery state. Installed assets grouped by current status, filtered by installation time. Operational scope is global. |
| `/service` | Administrator, Approver, Sales, Technician | Created jobcards by current status/priority and creation-time series; completed count/part units by completion time. Operational scope is global. |

Current state can change historical cohort totals. These endpoints do not reconstruct an as-of ledger of past order/asset/jobcard states. Several aggregate queries form each response; `snapshotAtUtc` identifies observation time, not a transactionally frozen cross-workflow snapshot.

Inventory sums existing warehouse/product stock positions. An out-of-stock position has available quantity <= 0; absent warehouse/product combinations are not synthesized. Reserve/release affects reservation deltas, not physical flow. Transfers produce one physical-out and one physical-in movement, so summing inflow alone is not net growth. Zero net physical change can coexist with warehouse redistribution.

Sales/product and procurement/supplier rankings are capped at 10 with stable ID tie-breaking. Other categorical distributions include all matching stored categories. Aggregation/filtering occurs in SQL; at most 366 daily aggregates are folded into calendar buckets in memory.

Errors follow existing Problem Details handling: 400 invalid query, 401 missing/invalid session, 403 role denied, 409 inventory authority unavailable. Endpoints expose no customer/user identity rankings, payments, profit, tax, or invented financial measures.

## Verification

`dotnet test ERPFlow.Tests/ERPFlow.Tests.csproj --filter "FullyQualifiedName~Analytics"`

Tests exercise authenticated HTTP routes against isolated SQL Server databases, permissions, owner scoping, UTC/exclusive boundaries, zero filling, decimal value semantics, actual posted receipt/transfer quantities, inventory reconciliation and calendar limits.
