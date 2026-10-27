# KDT Phase 5 — stock transfers and operational inventory

## Baseline and scope

Inspected HEAD: `ed300d9` (`feat(receiving): implement partial goods receipt and inventory posting`) on `feature/kdt-inventory-procurement`. Phase 4 source, migration and recorded 330/24 test results are present. The only initial uncommitted work was `ERPFlow.Api/appsettings.Development.json`; its blob hash `4134a04f650bef6e25fdad549896014d333bb402` is preserved.

Phase 5 adds backend StockTransfer lifecycle, atomic inter-warehouse movements, durable replay and cost-free inventory queries. No Angular code/screens, Stock Taking, Adjustment, Reorder, Supplier Returns or Finance is introduced. No configured application database access/migration/reset, production cutover, production gate replacement, Product inventory write, commit or push occurs.

## Domain and state machine

`StockTransfer` owns immutable lines, generated unique `TRF-yyyyMMdd-<full GUID>` number, distinct source/destination, creator/time, post/cancel actors/times/reason and SQL rowversion. There are 1–100 distinct products with positive Int32 quantities. Draft lines are immutable; corrections use cancellation plus a new command. Posted and Cancelled are terminal.

```text
Draft --post--> Posted
  |
  +--cancel with reason--> Cancelled
```

Draft create/cancel changes only document metadata and command history. It is allowed before cutover; stock is neither reserved nor read as fulfillment authority. Posting requires the existing warehouse-write permission and a completed Warehouse authority under its shared fence. Production DI retains `ProductAuthorityWriteGate` and `DisabledInventoryCutoverPermission` unchanged, so production posting fails closed.

Source stock must exist. Both warehouses and every catalog product must exist; warehouses must remain active. Source AvailableStock must cover each quantity and destination capacity must fit Int32. The entire batch is validated before the first mutation. Reserved quantities never change or move: their original owner/warehouse links remain intact.

## Posting and global SQL lock order

All work uses the existing scoped DbContext, explicitly owned connection and one outer application transaction. There is no second inventory engine or independently committed transfer leg.

Transaction sequence:

1. Shared durable authority fence; persisted role verification; actor/key application lock and replay check.
2. Transfer `UPDLOCK,HOLDLOCK`; expected document rowversion and Draft-state validation.
3. Lock both warehouse rows in ordinal `Guid.ToString("N")` order, retaining active eligibility through commit.
4. Acquire the **complete distinct stock resource set** before stock reads/mutations. The stable global key is `(warehouse N, product N)` with ordinal string ordering, not SQL GUID ordering or client line order.
5. Read exact stock keys using `UPDLOCK,HOLDLOCK`, backed by UNIQUE warehouse/product. Existing application-lock resource names remain `ERPFlow.Inventory.Stock.{warehouse:N}.{product:N}`.
6. Validate availability/capacity for every item; apply both sides; save stock with rowversion predicates.
7. Append two existing-engine `InventoryPosting`/`StockMovement` pairs per item, finalize Posted state, persist original successful response, commit once.

`InventoryStockLocks` is shared by transfers, GRN and Warehouse-authority Sales/Service posting. Sales/Service prelock the complete affected set before owner saves/stock reads; GRN prelocks the complete receipt set before its posting loop. Singleton generic posts also join the protocol in Warehouse mode. Product-authority foundation component fixtures retain their old optimistic path. Stale rowversions and lock/deadlock conflicts return 409; no business command is silently refreshed or automatically retried.

The conservative warehouse update locks intentionally serialize same-warehouse stock mutations. Opposite directions acquire the same warehouse order, then the same global stock order. A transfer never holds one stock leg while requesting an earlier warehouse lock. GRN source admission remains warehouse-before-stock; its finalization only reacquires held resources.

## Stock ledger and destination provisioning

Existing kinds 0–4 retain their values. New kinds:

- `TransferOut = 5`: physical `-Quantity`, reserved `0`.
- `TransferIn = 6`: physical `+Quantity`, reserved `0`.

Each transfer item owns exactly two envelopes and two movements, preserving the existing **one movement per posting** contract. UNIQUE `(StockTransferItemId, Kind)` prevents repeated sides across actors/keys. Original reservation/GRN/transfer posting owners are mutually exclusive through SQL checks/FKs.

Missing destination stock is instantiated at zero only after the complete lock set is held, then receives TransferIn at sequence 1 in the same transaction. There is no fabricated zero opening or historical stock. Any failed validation/persistence/finalization removes the tentative destination row with all other effects. Missing source stock never falls back to Product or another warehouse.

For the required example, A `10 on hand / 3 reserved` and B `2/0`, transferring 5 produces A `5/3` (available 2), B `7/0`. The physical pair is `(-5,+5)`, total zero, with zero reservation deltas. Transferring 8 fails without any document-posting/balance/ledger effect.

SQL guards protect document identity/state, lines, replay records, actor/scope and movement associations. Posted finalization requires every exact conserved pair, correct product/warehouses/quantity and posting actor. Posted documents and transfer movements cannot be updated/deleted. Reconciliation verifies pair completeness/conservation/ownership/status in addition to physical ledger, reserved ledger, active owners and sequence continuity. Historical imported owners and frozen Product evidence are unchanged.

As with the prior inventory protocol, session markers are not cryptographic application identity and guards do not attest a hostile SQL administrator. Runtime least-privilege permissions and prohibition of direct DML bypass remain deployment prerequisites; no credentials/grants changed here.

## Durable idempotency and API contracts

Create and post require the **`Idempotency-Key` HTTP header**, 1–128 non-whitespace/control characters. A case-sensitive unique actor/key stores operation (create 0/post 1), canonical SHA-256 request hash and serialized original response in `StockTransferCommands`. Create canonicalizes product order; post includes original transfer ID and rowversion. Same actor/key/request returns the original response after reauthorization. Different payload/operation returns 409. Replay precedes stale document/state checks. Failed commands claim no successful key. Reposting with a different key cannot produce additional pairs.

| Route | Contract |
| --- | --- |
| `GET /api/stock-transfers?page&pageSize&warehouseId&productId&status` | Cost-free bounded list; warehouse matches either side |
| `GET /api/stock-transfers/{id}` | Current document with exact source/destination posting and movement references |
| `POST /api/stock-transfers` | Header key; `{ sourceWarehouseId, destinationWarehouseId, items: [{ productId, quantity }] }` -> 201 |
| `POST /api/stock-transfers/{id}/post` | Header key; `{ rowVersion }` -> 200 |
| `POST /api/stock-transfers/{id}/cancel` | `{ rowVersion, reason }` -> 200; Draft only |
| `GET /api/inventory/stocks?page&pageSize&warehouseId&productId` | Current WarehouseStock quantities, availability, sequence, rowversion and public product/warehouse identity |
| `GET /api/inventory/movements?page&pageSize&warehouseId&productId&from&to` | Physical/reserved deltas and before/after, sequence, actor/time and source document/item references |

Tokens are eight-byte rowversions encoded as base64. Page defaults to 1, size 20; size is bounded 1–100 and offset arithmetic is wide. Ordering is stable. Movement time is UTC, From inclusive and To exclusive; From >= To is invalid. ISO offset query strings must URL-encode `+`. Source references cover transfers, GRNs, Sales reservations, Jobcards and openings. Quantities/IDs/stock history are exposed, never purchasing unit costs/totals/contact details. Inventory queries return 409 before Warehouse authority rather than presenting dormant warehouse quantities or Product fallback as authority.

ProblemDetails: 400 invalid input/header/token/filter; 401 unauthenticated; 403 unauthorized/revoked role; 404 missing resource; 409 state, inventory, stale version, idempotency or lock conflict.

## Authorization matrix

| Capability | Administrator 0 | WarehouseOperator 5 | Purchasing 4 | Approver 2 | Sales 1 / Technician 3 |
| --- | --- | --- | --- | --- | --- |
| Transfer create/view/post/cancel | Yes | Yes | No | No | No |
| Operational stock/movement queries | Yes | Yes | Yes | Yes | No |

Both endpoint policies and application workflows independently verify roles persisted in AppUsers. Valid old JWT claims do not retain rights after role revocation. Warehouses are organizational resources; operators need not own the document personally. Existing numeric values, Sales approval/dispatch, Service, Procurement and GRN permissions are unchanged.

## Additive database migration

`20261027133632_StockTransfersPhase5` adds StockTransfers, StockTransferItems, StockTransferCommands, nullable transfer posting ownership, restrictive FKs, document/line/key/side indexes, rowversion and positive/different-warehouse/lifecycle/JSON constraints. Existing posting/movement kind checks are extended. Five new SQL guards protect transfer lifecycle, lines, replay, postings and movements. Earlier migrations and inventory/GRN guards are not rewritten. No legacy table/column/data is dropped or backfilled; destructive Down is refused.

Phase 4 schema upgrade/repeated application tests preserve Product 50/6, rowversions and original Sales ownership. Imported owner IDs/status/quantities/timestamps and archived Product are independently verified after a transfer of imported stock. Draft transfers join warehouse-deactivation commitment checks on both sides.

## Verification evidence

Real isolated SQL tests cover reserved availability, exact paired deltas, unprovisioned destinations, immutable records, durable restart replay, changed-payload conflicts, concurrent duplicate/different-key posts, same-source depletion, opposite-direction and multi-SKU reversed-line-order transfers. An explicit start barrier overlaps transactions without sleeping or placing the barrier behind locks.

Cross-workflow races verify Transfer versus Sales reservation, Service parts reservation and GRN. Transfer/GRN also race to provision the same missing destination. Every successful scenario reconciles. Save and SQL-trigger faults target stock persistence, each movement side, document finalization, replay persistence and multi-SKU partial progress. All effects/status/key claims roll back and clean retries succeed.

The initial API query run exposed record-constructor projection ordering that EF could not translate; the retained API tests now exercise ordered/paged relational projections before DTO construction. The date-boundary test was corrected to URL-encode an offset so it actually validates inclusive/exclusive semantics rather than a malformed timestamp.

Hands-on real Kestrel HTTP evidence is retained at `C:\Users\User\AppData\Local\Temp\opencode\ERPFlow-Phase5-http-qa.log`. The temporary unshipped host used an exact generated LocalDB database with an explicitly authorized completed cutover and owned permission override. It performed real WarehouseOperator/Purchasing login, missing-key400, create201, post200, identical lost-response replay200, A10R3/B2 -> A5R3/B7, cost-free source-referenced movement query and complete reconciliation with unchanged archived Product. The owned host/database were cleaned up. Production DI was not modified to run this demo.

## Complete changed-file inventory

The pre-existing `ERPFlow.Api/appsettings.Development.json` modification is excluded. Source paths below are relative to the repository root.

```text
ERPFlow.Api/ApiEndpoints.cs
ERPFlow.Api/Program.cs
ERPFlow.Api/InventoryQueryEndpoints.cs
ERPFlow.Api/StockTransferEndpoints.cs
ERPFlow.Application/Abstractions/IGoodsReceiptPersistence.cs
ERPFlow.Application/Abstractions/IInventoryQueryPersistence.cs
ERPFlow.Application/Abstractions/IStockTransferPersistence.cs
ERPFlow.Application/Inventory/IInventoryPosting.cs
ERPFlow.Application/Inventory/InventoryQueryContracts.cs
ERPFlow.Application/Inventory/InventoryQueryWorkflow.cs
ERPFlow.Application/Receiving/GoodsReceiptWorkflow.cs
ERPFlow.Application/Transfers/StockTransferContracts.cs
ERPFlow.Application/Transfers/StockTransferWorkflow.cs
ERPFlow.Domain/Inventory/InventoryPosting.cs
ERPFlow.Domain/Inventory/WarehouseStock.cs
ERPFlow.Domain/Transfers/StockTransfer.cs
ERPFlow.Infrastructure/DependencyInjection.cs
ERPFlow.Infrastructure/Persistence/Configurations/InventoryPostingConfiguration.cs
ERPFlow.Infrastructure/Persistence/Configurations/StockMovementConfiguration.cs
ERPFlow.Infrastructure/Persistence/Configurations/StockTransferConfiguration.cs
ERPFlow.Infrastructure/Persistence/ERPFlowDbContext.Inventory.cs
ERPFlow.Infrastructure/Persistence/ERPFlowDbContext.Transfers.cs
ERPFlow.Infrastructure/Persistence/EfGoodsReceiptPersistence.cs
ERPFlow.Infrastructure/Persistence/EfInventoryQueryPersistence.cs
ERPFlow.Infrastructure/Persistence/EfProcurementPersistence.cs
ERPFlow.Infrastructure/Persistence/EfStockTransferPersistence.cs
ERPFlow.Infrastructure/Persistence/EfStockTransferPersistence.Reads.cs
ERPFlow.Infrastructure/Persistence/Inventory/EfInventoryPosting.cs
ERPFlow.Infrastructure/Persistence/Inventory/EfInventoryPosting.Receipts.cs
ERPFlow.Infrastructure/Persistence/Inventory/EfInventoryPosting.Transfers.cs
ERPFlow.Infrastructure/Persistence/Inventory/EfInventoryReconciliation.cs
ERPFlow.Infrastructure/Persistence/Inventory/EfInventoryReconciliation.Transfers.cs
ERPFlow.Infrastructure/Persistence/Inventory/EfWorkflowInventory.cs
ERPFlow.Infrastructure/Persistence/Inventory/InventoryStockLocks.cs
ERPFlow.Infrastructure/Persistence/TransferSqlGuards.cs
ERPFlow.Infrastructure/Persistence/Migrations/20261027133632_StockTransfersPhase5.cs
ERPFlow.Infrastructure/Persistence/Migrations/20261027133632_StockTransfersPhase5.Designer.cs
ERPFlow.Infrastructure/Persistence/Migrations/ERPFlowDbContextModelSnapshot.cs
ERPFlow.Tests/InventoryTransactionBarrier.cs
ERPFlow.Tests/StockTransferApiTests.cs
ERPFlow.Tests/StockTransferAtomicityTests.cs
ERPFlow.Tests/StockTransferConcurrencyTests.cs
ERPFlow.Tests/StockTransferDomainTests.cs
ERPFlow.Tests/StockTransferIntegrationTests.cs
ERPFlow.Tests/StockTransferMigrationTests.cs
ERPFlow.Tests/StockTransferRollbackTests.cs
ERPFlow.Tests/StockTransferSafetyTests.cs
ERPFlow.Tests/StockTransferTestSupport.cs
ERPFlow.Tests/StockTransferWorkflowRaceTests.cs
README.md
docs/openapi/erpflow-v1.json
docs/stock-transfers-phase5.md
```

## Deployment and Phase 6 boundary

### Recorded final verification

| Check | Actual result |
| --- | --- |
| `dotnet restore ERPFlow.slnx` | PASS |
| Backend build | PASS, 0 warnings / 0 errors |
| Complete isolated backend suite | **PASS, 385/385**, 0 failed / 0 skipped, 7m39s |
| Baseline compatibility | All 330 existing tests retained; 55 new Phase 5 cases |
| Opposite direction / reversed multi-SKU order | PASS |
| Sales / Service / GRN / shared destination races | PASS |
| Stock side / paired movement / status / key-claim fault rollback | PASS |
| Migration upgrade and model/snapshot checks | PASS |
| EF pending-model CLI check | PASS, no pending changes; non-Development unused design connection |
| OpenAPI snapshot | Regenerated in isolated host; comparison passes in full suite |
| Hands-on isolated HTTP QA | PASS; host stopped and exact generated database deleted |
| Shared frontend | Unchanged; conditional Angular build/tests/lint not rerun in Phase 5 |

Full artifacts: `C:\Users\User\AppData\Local\Temp\opencode\ERPFlow-Phase5-full.trx` and adjacent `.stdout.log` / `.stderr.log`; HTTP artifact is `ERPFlow-Phase5-http-qa.log`. The early focused run recorded four query failures before the projection-order fix; all are retained and pass in the final full run. One initial uncaptured test tool call exceeded its response window; the durable TRX and later full output provide the complete results. Fresh LSP diagnostics timed out for shared lock/posting files; compiler and executed SQL tests provide verification. EF CLI emits its pre-existing 10.0.11/10.0.13 version warning; packages/tools were not changed.

### Independent final review

The independent reviewer inspected the dedicated locked 52-file review overlay and final artifacts, returning **APPROVE — High confidence**, with no goal-linked blockers. Manual HTTP QA and the 385-test full suite support the final gate. The temporary review and test-host source workspaces are removed after review; logs/TRX remain available.

One nonblocking Phase 6 deployment prerequisite was identified: development CORS currently permits Authorization/Content-Type but omits `Idempotency-Key`. The cross-origin browser client increment must add that allowed header and its regression coverage before introducing transfer create/post UI. No browser client is included in Phase 5, and no production authority gate should be changed to address CORS.

**GO for separately scoped Phase 6 — Angular Inventory & Procurement Frontend development and isolated API verification.** The backend contracts and regression/review gates pass; include the CORS header prerequisite above. Live WarehouseStock activation remains a separate **NO-GO**: production deny gates, all-reader/writer version admission, physical attribution, least-privilege permissions, backup/forward recovery and representative load readiness remain separate approvals. The shared lock protocol requires a coordinated compatible writer deployment. No UI or production activation is part of this increment.
