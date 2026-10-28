# Phase 6B: Professional Demo Seeder

## Current implementation status

Phase 6B now has an explicit, isolated seeding command. Normal application startup no
longer invokes `DevelopmentDataSeeder` by default; the legacy seeder remains available only
through the Development-only `DemoUsers:EnableAutomaticSeed` opt-in. No startup path passes
`allowProduction: true`. The pre-existing Development inventory registration is unchanged;
professional-demo permissions are private to the explicit command and add no startup permissions.

The current increment creates the deterministic Jordan Office Technology Trading & Services
master-data baseline in a new manifest-owned database:

- 6 demo users covering Administrator, Sales, Approver, Purchasing, Warehouse Operator, and Technician
- 20 customers, including one inactive customer
- 38 products, including 35 active and 3 inactive catalog products
- 10 suppliers
- 3 active warehouses including the migration-provided `LEGACY-DEFAULT` compatibility warehouse
- zero Product stock and zero Product reservations

Phase 6B.2B now creates the operational history through the supported procurement, receiving,
transfer, sales, equipment, and service workflows. Inventory effects remain behind the existing
authority and posting boundaries; the seeder never inserts business documents, reservations,
postings, movements, or balances directly.

## Existing database read-only audit

The configured target was verified without writes:

- SQL Server: `DESKTOP-P8A4TV4\SQLEXPRESS`
- Database: `ERPFlowDb`
- State: `ONLINE`, `MULTI_USER`, not read-only
- Highest migration: `20261028114503_ProductCatalogPhase6A2`
- Inventory authority: `Product` mode

Baseline counts at audit time:

| Entity | Count |
| --- | ---: |
| App users | 8 |
| Products | 20 |
| Customers | 10 |
| Suppliers | 3 |
| Warehouses | 4 |
| Purchase orders | 10 |
| Goods receipts | 0 |
| Sales orders | 19 |
| Stock reservations | 30 |
| Delivery notes | 0 |
| Machine assets | 0 |
| Service jobcards | 0 |
| Stock transfers | 3 |
| Inventory postings | 0 |
| Stock movements | 0 |
| Product images | 0 |
| Product catalog audits | 0 |

Status distributions were also captured read-only: all 20 existing products were active;
purchase orders were split evenly across five implemented statuses (2 each); sales orders
were 4 Draft, 7 Pending Approval, 4 Approved, and 4 Rejected; stock transfers were 2 Draft
and 1 Cancelled. These records were not classified as disposable because names/SKUs/email
patterns are insufficient evidence of ownership. No existing record was changed.

## Safety and invocation

The command is Development-only and accepts only a local integrated SQL Express target whose
name matches `ERPFlow_Demo_<identifier>`:

```powershell
$env:ERPFLOW_DEMO_PASSWORD = "<local-demo-password>"
dotnet run --project ERPFlow.Api --environment Development -- --seed-professional-demo --demo-database ERPFlow_Demo_2026
```

Dry-run performs target validation without creating or migrating a database:

```powershell
dotnet run --project ERPFlow.Api --environment Development -- --seed-professional-demo --demo-database ERPFlow_Demo_2026 --dry-run
```

The command verifies the local `.`\`SQLEXPRESS` instance and checks `master` before any
migration. An existing database without the matching manifest is rejected. The manifest is
unique by scenario/version, and an application lock prevents concurrent seed runs. Re-running
an owned scenario is a no-op and does not replace passwords or business records.

`ERPFlow.Api/appsettings.Development.json` is not used as the demo target and must not be
overwritten. The configured application connection remains untouched.

## Phase 6B.1 blocker resolution

### Historical business time

The direct `DateTime.UtcNow` audit covered domain entities, application document-number
generation, inventory envelopes, SQL receipt procedures, migrations, analytics snapshots,
JWT expiry, and Product Image storage retry deadlines.

Business records now read `BusinessClock.UtcNow`. Its default is `TimeProvider.System`, so
ordinary application behavior remains real UTC. The override scope is internal to the Domain
assembly and available only to the Infrastructure assembly. `ProfessionalDemoSeeder.RunAtAsync`
is the sole production-code entry to that scope; it is not registered as an API service and no
request contract accepts a timestamp. `AsyncLocal` confines the provider to the current async
seed flow and disposal restores the previous provider.

This covers users, customers, catalog records/audits, Sales orders/approvals/reservations,
delivery notes/assets, Jobcards, procurement masters/orders/history, GRNs, stock transfers,
inventory authority/import/posting/reservation records, and their generated document numbers.
Analytics/JWT use real query/request time. Product Image workflows already use injected
`TimeProvider`; file retry deadlines intentionally remain real elapsed wall time. Historical
migrations remain unchanged.

GRN SQL was the one active database-generated business-time path. Migration
`20261028143605_HistoricalBusinessClockPhase6B1` changes only the two receipt procedures to
accept the workflow business time. The procedures reject future time, enforce PO <= GRN <=
posting order, retain transaction/session/role/version checks, and write the same timestamp to
the GRN and PO history. No trigger or constraint is disabled.

### Receipt bootstrap

The production registrations remain `DisabledInventoryCutoverPermission` and
`ProductAuthorityWriteGate`. This increment does not activate warehouse authority, bypass SQL
guards, initialize stock by direct balance mutation, or post fake receipts/transfers.

The earlier inventory blocker was incorrect. `EfInventoryPosting.PostReceiptAsync` acquires the
exact warehouse/product lock and, when no row exists, creates a zero `WarehouseStock` through
the approved posting boundary before applying `InventoryPostingKind.Receipt`. The stock row,
posting, movement, GRN status, and PO received quantities commit in the same fenced transaction.

An isolated real-SQL proof started with a zero-stock Product, completed the supported cutover,
and posted separate approved PO/GRN workflows into Central and Service warehouses. Results:

| Destination | Received | Final physical | Reserved | Receipt movements |
| --- | ---: | ---: | ---: | ---: |
| Central | 7 | 7 | 0 | 1 |
| Service | 4 | 4 | 0 | 1 |

The archived Product remained 0 physical/0 reserved. Each movement started at zero, had one
positive Receipt delta, and linked to exactly one original GRN item. PO status became Received
and remaining quantity became zero. No opening balance, direct `WarehouseStock` write, or gate
bypass was used.

### Procurement race

The immutable-lines error was a legitimate guard response but the wrong externally observed
classification for a stale draft command. `FindOrderAsync(..., tracking: true)` previously read
without an update lock. Submission could commit between the draft read and line save, causing
the SQL immutability trigger to report a workflow conflict. Tracking reads now use
`UPDLOCK,HOLDLOCK` within the existing transaction. Competing edit/submission commands serialize;
the loser observes the changed rowversion and receives `ConcurrencyConflictException`. The SQL
immutable-line guard and test assertion remain unchanged.

## Verification completed

- Solution build: passed with 0 warnings and 0 errors.
- Dedicated seeder integration tests: 3 passed.
- Legacy development-seeder integration tests: 9 passed.
- Production inventory-gate API test: passed.
- EF model verification: no pending model changes.
- Tests cover dry-run non-creation, deterministic counts, idempotent rerun, and rejection of
  an existing unowned database.

Phase 6B.1 focused verification covered historical scope restoration, PO/GRN/transfer
document-date consistency, real two-warehouse zero-stock receipt provisioning, exact
GRN-to-ledger links, and the repaired procurement race: 4 passed, 0 failed. The exact-final
backend regression then passed 511 of 511 tests with 0 failed or skipped in 11 minutes 24 seconds.
The repaired procurement race also passed three additional consecutive isolated repetitions.

## Phase 6B.2A safe execution infrastructure

The production dependency chain remains unchanged: normal API startup registers
`ProductAuthorityWriteGate` and `DisabledInventoryCutoverPermission`. Warehouse writes therefore
remain unavailable until the durable `InventoryAuthority` row is in Warehouse mode. The demo
command does not replace those registrations. It constructs private command-only actor, cutover
permission, and warehouse gate implementations after the existing Development-only CLI, local
`\.\SQLEXPRESS`, `ERPFlow_Demo_<identifier>`, exact connection, and ownership checks pass.

Migration `20261028154045_ProfessionalDemoSeedOperationsPhase6B2` adds the durable operation
ledger. Each row has a scenario and stable operation key, operation type, SHA-256 input
fingerprint, persisted actor ID, resulting document ID, start time, and completion time. A unique
scenario/operation index rejects duplicates. GRN create/post also retain their existing durable
idempotency commands. PO Notes and supplier delivery references are deterministic workflow-owned
business identities. If the process stops after a workflow commit but before ledger completion,
replay finds and validates those committed documents, records the existing result ID, and does
not create another PO, GRN, posting, or movement. A changed fingerprint is rejected. A
database-specific session application lock serializes concurrent command invocations.

The small proof uses persisted separation-of-duty actors: Purchasing creates/submits each PO,
Approver approves it, WarehouseOperator creates/posts each GRN, and Administrator authorizes the
supported cutover. All PO, receipt, posting, movement, and stock mutations pass through the normal
workflows and SQL fences.

### Real command proof

- Database: `ERPFlow_Demo_Phase6B2A_20261028A` on local `\.\SQLEXPRESS`.
- Authority: Warehouse mode, writer version 2, 38 zero-balance stocks imported, 0 reservations.
- Operations: 3 completed (`cutover`, `receipt-central`, `receipt-service`).
- Documents: 2 Received POs and 2 Posted GRNs.
- Central: physical 7, reserved 0, one Receipt posting/movement.
- Service: physical 7, reserved 0, one Receipt posting/movement.
- Reconciliation: 14 physical, 0 reserved, 2 unique Receipt postings, 2 Receipt movements.
- Replay: second CLI execution retained exactly 2 POs, 2 GRNs, 2 postings, 2 movements, and 3
  completed operations.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ERPFLOW_DEMO_PASSWORD = '<local-demo-password>'
dotnet run --project ERPFlow.Api --no-build -- --seed-professional-demo --demo-database ERPFlow_Demo_Phase6B2A_20261028A
```

Focused real-SQL verification passed 6 of 6 tests. It covers dry-run non-creation, unowned
database rejection, exact two-warehouse reconciliation, repeat execution, recovery from a missing
external checkpoint after a committed receipt, changed-fingerprint rejection, and concurrent
command serialization.

The exact-final backend regression passed 514 of 514 tests with 0 failed or skipped in 8 minutes
51 seconds. The solution build passed with 0 warnings and 0 errors, EF reported no pending model
changes, and `git diff --check` reported no whitespace errors. The protected Development settings
SHA-256 remained `FD0E460E8B05A8ADFC8ED87E32DE0D29110B095277C8BFA408B8043E18532FFD`.

## Phase 6B.2B readiness

The historical clock, command-only composition, durable replay ledger, and legitimate receipt
bootstrap prerequisites are resolved. Phase 6B.2B
may create the operational history through normal workflows, provided it remains inside a
manifest-owned demo database and advances business time monotonically. It must not directly
edit audit timestamps or inventory balances.

The original configured database was not opened or modified by this increment. No commit or
push was performed.

## Phase 6B.2B Stage C: sales and dispatch

Stage C creates 45 deterministic SalesOrderWorkflow orders dated 27 September through 19 October
2026 for active customers, owned by the persisted Sales user: 10 Draft, 12 PendingApproval, 10
Approved, and 13 Rejected. Eight orders are returned by the persisted Approver, edited as Drafts,
and resubmitted; five finish PendingApproval and three finish Approved. Item prices are the normal
workflow snapshots and are never repriced by the seeder.

All lines use explicit Central or LEGACY-DEFAULT sources already provisioned by Stages A/B; the
21 Service units remain for Stage D. Three Approved orders are dispatched only through
EquipmentWorkflow by the persisted Technician with exact deterministic serials. This creates 3
DeliveryNotes, 3 MachineAssets, and 3 Consume postings/movements, leaving physical stock Central
16, Service 21, LEGACY-DEFAULT 17 (54 total) and 19 active sales reservations.

Sales entries use stable `so-##` ledger keys and SHA-256 fingerprints. Because sales creation has
no native idempotency key, replay identifies the unique BusinessClock timestamp plus owner and
validates the persisted customer/product/source identity before advancing each status/history
step. Dispatches use distinct `dispatch-so-##` ledger keys and reconcile an existing DeliveryNote
by sales order before creating one. Missing operation completions therefore do not duplicate
orders, assets, serials, postings, or movements. Correction scenarios begin with an alternate
active customer, then use the supported Draft edit to select the intended customer before
resubmission, making the correction a genuine persisted business change rather than a no-op.

## Phase 6B.2B Stage D: service jobcards

Stage D creates 15 deterministic ServiceWorkflow jobcards dated 20-27 October 2026 using the
persisted Technician: 5 Open without parts, 5 PartsRequested with active reservations, and 5
Completed after supported parts reservation and consumption. Unsupported InProgress and Cancelled
states are not fabricated. Three completed cases reuse the exact MachineAssets created by Stage C;
their customer ownership is validated and their meter readings advance monotonically.

Each case has a stable `srv-##` ledger key and fingerprint. Because service creation generates a
random ticket number, recovery uses the exact historical CreatedAt, customer, technician, and
reported-fault marker as a durable business identity, fails closed on ambiguity, and validates the
asset, priority, parts, source warehouse, and target state before continuing. A committed parts or
completion effect is reconciled from the jobcard and reservation state before the external ledger
checkpoint is completed.

All ten parts cases source one unit from AMM-SERVICE through EfWorkflowInventory. Five open parts
reservations remain active and five completed reservations close as Consumed through exactly five
Consume postings/movements. Stage D leaves physical stock Central 16, Service 16, and
LEGACY-DEFAULT 17 (49 total), with 24 active warehouse reservations: 19 sales and 5 service.

Initial Stage D focused verification on 28 October 2026 passed all 8
`ProfessionalDemoSeederIntegrationTests` in 2 minutes 18 seconds. It proves 122 completed durable
operations, exact workflow status distributions, replay stability, missing checkpoints after a
posted transfer, dispatched sale, and completed service case, changed-fingerprint rejection, and
concurrent-command serialization. The backend build passed with 0 warnings and 0 errors; EF found
no pending model changes. Angular verification passed 111 tests, lint, the production build
(371.94 kB initial, 100.71 kB estimated transfer), and both hosting-worker tests.

The completion pass strengthened recovery before final acceptance. Existing purchase orders
resume the supported create/submit/approve/cancel sequence rather than completing an external
checkpoint from presence alone. Sales validate their declared product, exact quantity/source,
corrected customer and terminal state; dispatch recovery validates the original delivery and
normalized asset serial set; service recovery validates completion notes, time and asset meter.
An internal test-only interruption probe exercises committed PO create/submit/approve boundaries
and the committed return-before-edit sales boundary. The expanded real-SQL recovery suite passed
10/10 before the final chronology pass. Direct namespace-aware XML inspection also verified the
preceding full backend baseline as 516/516 passed, not an incomplete run.

### Final isolated acceptance

The final dataset is retained for review in local SQL Express database
`ERPFlow_Demo_Phase6B2B_20261028Final`. The earlier
`ERPFlow_Demo_Phase6B2A_20261028A` proof database was not used or modified.

The final chronology implementation schedules workflow events by historical UTC timestamp and
stable operation key. Dispatch runs as a separate event two hours after its sales order;
service assets are resolved when their later event executes. New scenario master data is created
at 1 September 2026 07:00 UTC, before the business documents. The migration-provided legacy
warehouse retains its existing migration metadata. Existing durable fingerprint formats are
preserved. The final focused real-SQL class passed 10/10 in 2 minutes 11 seconds, including
reconciliation after seed and replay and a SQL `LAG` proof that posting time never decreases
within a stock movement sequence. Before the scheduler fix, that assertion reproduced one
historical ordering violation.

The real CLI dry-run left the target absent, then the real command created the isolated dataset:

| Final document | Count |
| --- | ---: |
| Purchase orders | 22 |
| Goods receipts | 24 |
| Stock transfers | 12 |
| Sales orders | 45 |
| Delivery notes / machine assets | 3 / 3 |
| Service jobcards | 15 |
| Completed operation checkpoints | 122 |

Physical stock is Central 16, Service 16, and LEGACY-DEFAULT 17: 49 units total. Active reserved
stock is 10, 5, and 9 respectively: 24 total, leaving 25 available. The ledger has 57 receipt
units, 15 units transferred in each direction (net zero), and 8 consumed units. There are zero
balance, posting-sequence chronology, duplicate serial, or incomplete-operation violations.
Archived Product balances remain zero. All 154 postings have their normal workflow provenance.

A second CLI process, supplied a different password argument, preserved every table's canonical
primary-key-ordered JSON SHA-256 snapshot. This proves IDs, passwords, documents, operation
checkpoints, inventory reservations, postings, movements, and row versions were unchanged.

Read-only acceptance used process-scoped database, JWT, CORS and image-root settings; no project
configuration was edited. SQL session metadata confirmed the API was connected to the final
isolated target. All six demo roles authenticated. API list/detail and eight historical analytics
checks matched SQL, with anonymous reads returning 401, forbidden Sales inventory reads 403, and
an invalid analytics date range 400. The three dispatches correctly remain awaiting delivery;
no delivery-completion state was fabricated.

Angular read QA covered sales, procurement, posted GRNs/transfers, approvals, assets, completed
service details, inventory and all three analytics pages. The explicit analytics window was
`2026-09-01 <= UTC date < 2026-10-28`, grouped weekly. Desktop and 390px mobile checks rendered
the expected data without alerts or page-level overflow. Only authentication POSTs were allowed
by the browser guard; business writes were blocked. Whole-table SHA-256 snapshots remained
identical after the API/browser reads.

Local evidence artifacts (intentionally not committed):

- `ERPFlow.Tests/phase6b2b-before.log`, `phase6b2b-replay.log`, and `phase6b2b-after-qa.log`
- `ERPFlow.Tests/phase6b2b-api-qa.log`
- `.playwright-mcp/phase6b2b-warehouse-desktop.png`
- `.playwright-mcp/phase6b2b-sales-service-mobile.png`
- `.playwright-mcp/phase6b2b-completed-service-desktop.png`
- `.playwright-mcp/phase6b2b-requests.log` and `phase6b2b-console.log`
- `ERPFlow.Tests/TestResults/phase6b2b-final.trx`

The final full backend regression completed on 28 October 2026 in 11 minutes 39 seconds:
518 executed, 518 passed, zero failures, errors, timeouts or skipped tests. The result was
verified directly from the namespaced TRX counters and all 518 recorded test outcomes. The final
solution build has zero warnings/errors, all five completion-pass C# files are LSP-clean, and EF
reports no pending model changes. The older EF CLI/runtime version warning is pre-existing and
did not block the model check. `git diff --check` has no whitespace errors.

The protected Development settings retain SHA-256
`FD0E460E8B05A8ADFC8ED87E32DE0D29110B095277C8BFA408B8043E18532FFD`.
Temporary QA API/Angular processes were stopped after evidence capture. No commits or pushes
were made; the configured application database and the earlier proof target were not modified.

The independent read-only completion gate returned **APPROVE**, confidence **HIGH**, with no
blocking issues. It verified workflow provenance, exact totals, chronology, recovery and safety
boundaries, identical before/replay/post-QA snapshots, all 25 API checks, and the desktop/mobile
browser evidence. The final 518/518 full regression was separately confirmed before handoff.

To review the retained target without editing appsettings, set the connection for that process:

```powershell
$env:ConnectionStrings__ERPFlowDb = 'Server=.\SQLEXPRESS;Database=ERPFlow_Demo_Phase6B2B_20261028Final;Integrated Security=True;TrustServerCertificate=True'
$env:DemoUsers__EnableAutomaticSeed = 'false'
dotnet run --project ERPFlow.Api --launch-profile https
```

Use the password chosen when the database was first seeded, not a different password supplied on
replay. Role accounts are `admin`, `sales`, `approver`, `purchasing`, `warehouse`, and `technician`
at `jot.example`. Select the full historical analytics window above; the default recent window
does not include all September activity.

## Phase 6B.2B Stage B: stock transfers

Stage B adds 12 deterministic StockTransferWorkflow operations using the persisted
WarehouseOperator: 6 Posted, 3 Draft, and 3 Cancelled. The plan dates run from 16 September to
11 October 2026. It uses only stock provisioned by Stage A and retains nonnegative available
stock; no sales or service workflow is seeded.

Each transfer has a durable `trf-##` operation-ledger key, SHA-256 plan fingerprint, result
document ID, and native create/post keys (`professional-demo-trf-##` and
`professional-demo-trf-##-post`). A create replay is required to return the original transfer;
the six post replays return their original post result. If an external checkpoint is missing,
the native create replay locates the document, the current document state is re-read, a Draft is
continued to its requested Posted/Cancelled state, and an existing terminal state must match the
plan. Changed operation fingerprints and concurrent seed commands remain rejected/serialized.

Focused real-SQL assertions cover the six Posted lines as exactly one TransferOut and one
TransferIn posting/movement each, linked to the transfer's source and destination warehouse.
They also prove zero net transfer physical delta. Stage B contributes 12 posting records and 12
movement records; Stage A's 57 physical units remain conserved. Final warehouse physical totals
are Central 17, Service 21, and LEGACY-DEFAULT 19 (57 total), with zero reservations.

Actual focused verification on 28 October 2026 passed all 6
`ProfessionalDemoSeederIntegrationTests` in 47 seconds. The test creates a fresh isolated SQL
database, proves the 59 completed operation checkpoints, 12 transfer documents, 18 native
transfer command records, 12 transfer postings/movements, paired source/destination ownership,
zero net transfer delta, replay stability, missing-checkpoint recovery, fingerprint mismatch
rejection, and concurrent-command serialization. `ERPFlow.Tests` built with 0 warnings and 0
errors before the run.

## Phase 6B.2B Stage A

Stage A extends only the isolated, manifest-owned scenario through the normal purchase-order and
goods-receipt workflows. The deterministic operation ledger now contains the cutover plus 22 PO
operations and 24 GRN operations. PO Notes (`Professional demo 6B.2B po-##`) and GRN supplier
references (`PD-6B2B-GRN-##`) are durable reconciliation identities; replay validates the stable
SHA-256 operation fingerprint, finds a business commit before completing its external checkpoint,
and relies on native GRN create/post idempotency for workflow retries.

The plan spans 1 September through 27 October 2026 and uses the persisted Purchasing, Approver,
WarehouseOperator, and Administrator actors. It has 22 POs: 2 Draft, 2 PendingApproval, 3
Approved, 3 Rejected, 3 Cancelled, 4 PartiallyReceived, and 5 Received. It has 24 GRNs: 1 Draft,
22 Posted, and 1 Cancelled. Posted receipts total 57 units across all warehouses: Central 20,
Service 22, and LEGACY-DEFAULT 15. No status, timestamp, stock, posting, or movement is mutated
outside its normal workflow and SQL fence.

Focused real-SQL verification passed 6 of 6 `ProfessionalDemoSeederIntegrationTests` on 28 October
2026 (42 seconds): exact PO/GRN status distributions, chronology, quantities and three-warehouse
stock/movement reconciliation, idempotent replay, durable fingerprint mismatch rejection,
concurrent command serialization, and recovery after a committed GRN loses its external operation
checkpoint. The Stage A solution build passed with 0 warnings and 0 errors.
