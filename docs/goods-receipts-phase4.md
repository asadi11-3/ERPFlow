# KDT Phase 4 — Goods Receipt Notes and partial receiving

## Implemented scope and activation boundary

Baseline: `de07159` on `feature/kdt-inventory-procurement`. This phase implements GRN backend code, partial/full receiving, exact-warehouse physical postings, durable command replay, receiving-state/audit transitions, endpoints and SQL tests. It does not add transfers, Angular screens, valuation/accounting, serial/lot tracking or posted-receipt reversals.

Production DI continues to register `ProductAuthorityWriteGate` and `DisabledInventoryCutoverPermission` unchanged. The configured application database was not accessed or migrated. No production permission, cutover or inventory-authority configuration switch was added. `Product` inventory and selling prices are never mutated by receiving. The pre-existing development settings and Phase 2C documentation modifications are preserved. No commit or push is included.

Both GRN **creation and posting** deliberately require the existing warehouse-write permission and a completed durable Warehouse authority under the transaction fence. Thus production composition fails closed even for paperwork creation; a permissive test gate alone cannot admit a Product-authority database. Only the test assembly supplies an override, and it verifies exact current fixture ownership and completed cutover. Draft cancellation has no stock effect and still requires authorized actor, version and fenced warehouse-mode document scope.

## Domain and command contracts

New aggregates: `GoodsReceipt` and immutable `GoodsReceiptItem`. A GRN has an immutable generated number, one original PO, its exact supplier/destination, creator/time, optional supplier delivery reference/notes, rowversion and attributable post/cancel metadata. Items retain original PO line ID, PO ID, product ID and positive Int32 quantity; there are 1–100 distinct lines/products per GRN. No purchase costs are exposed in GRN DTOs.

```text
Draft --post--> Posted
  |
  +--cancel with reason--> Cancelled
```

Posted and Cancelled are terminal. Posting cannot be undone by cancelling: stock reversal requires a separately designed compensating workflow, not modification/deletion of a posted movement. Draft lines are immutable; incorrect paperwork is cancelled and recreated with a new command key. Optional supplier references are normalized and unique per supplier among Draft/Posted records; cancelling an unposted record allows corrected paperwork using that reference while retaining the cancelled record.

`PurchaseOrderItem.ReceivedQuantity` starts at zero, is bounded by ordered quantity and changes only through receiving SQL. Remaining quantity is calculated as ordered minus received. A PO can have multiple GRNs and multiple Drafts; Draft quantities are not counted as received or reserved. Posting always rechecks the current remaining ordered quantities. For example, two Drafts of 7 against an order of 10 are permitted individually, but only one can post; the other remains Draft and its post conflicts without effects.

PO receiving state is derived across **all** original PO lines:

- Approved: nothing posted yet, including admitted Draft GRNs.
- PartiallyReceived: some posted quantities and at least one outstanding quantity.
- Received: every line fully received.

The first Draft admission atomically establishes permanent `ReceivingStartedAt` and a receiving-start audit event. Even cancelled paperwork does not erase admission or make the Approved PO cancellable again. Subsequent posts append attributable GRN-linked PO receiving events, including Partial-to-Partial. Existing approval/rejection/cancellation semantics remain separate.

## API and authorization

All `/api/goods-receipts` routes require both the endpoint policy and a current persisted **WarehouseOperator (5)** or **Administrator (0)**. Operators act on shared receiving documents rather than their own Purchasing documents. Purchasing, Approver, Sales and Technician cannot call these routes. Role revocation is checked before new commands and replay. Existing role numbers and production authentication behavior remain unchanged.

| Endpoint | Request/result |
| --- | --- |
| `GET /api/goods-receipts?page=1&pageSize=20&purchaseOrderId=...&status=...` | Bounded page, optional PO/status filters |
| `GET /api/goods-receipts/{id}` | Current GRN and line posting/movement IDs |
| `POST /api/goods-receipts` | `{ idempotencyKey, purchaseOrderId, purchaseOrderRowVersion, items: [{ purchaseOrderItemId, quantity }], supplierDeliveryReference?, notes? }` -> 201 |
| `POST /api/goods-receipts/{id}/post` | `{ idempotencyKey, rowVersion }` -> 200 |
| `POST /api/goods-receipts/{id}/cancel` | `{ rowVersion, reason }` -> 200; Draft only |

Tokens are eight-byte SQL rowversions encoded as base64. Keys are 1–128 non-whitespace characters. References are at most 100 characters; notes/reasons at most 1000, with no control characters. Cancellation requires a reason. Quantities are positive Int32 values, checked against the exact original PO line. Pages are positive and pageSize is 1–100. Invalid payload -> 400, unauthenticated -> 401, unauthorized/revoked role -> 403, missing resource -> 404, source/state/version/idempotency/over-receipt conflict -> 409. Stock arithmetic overflow is rejected without mutation.

Existing commercial and operational PO DTOs gain received/remaining quantities. The operational projection also provides original PO item IDs and parent rowversion so WarehouseOperators can create GRNs without commercial access. Unit costs, totals and commercial notes remain excluded from that projection. All routes and additive contracts are in the updated OpenAPI snapshot.

## Durable idempotency

`GoodsReceiptCommands` stores the actor/key, operation, SHA-256 canonical request hash, receipt ID and serialized successful response in the **same transaction** as the effects. There is a case-sensitive unique actor/key index. Create and post require different keys; attempting to reuse a key for another payload or operation returns 409.

- Same actor/key/payload returns the original persisted response, including its rowversions and state at that command's commit.
- Replaying Create after the GRN was later posted still returns the original Draft creation response. GET provides current state.
- Request tokens are part of the hash: retry the identical command after a transport failure, rather than refreshing its token under the same key.
- An actor/key transaction-owned application lock serializes concurrent identical commands. SQL uniqueness is the final durable defense.
- Failed commands roll back their effect and successful-key claim, permitting a clean retry.
- A different actor never receives another actor's replay record. An already posted GRN cannot be posted again with a new key or different actor.

## Atomic receiving, concurrency and inventory

Every command uses one explicitly opened connection, one DbContext and one outer application transaction. Cleanup rolls back failures, clears the receiving session scope and releases the owned connection. No line commits independently.

Lock order is command key, PO parent, receipt, active source masters, then sorted product stock locks. The PO uses `UPDLOCK,HOLDLOCK` across validation, all stock effects, counters, GRN state, PO state/history and replay persistence. Same-PO posts cannot both validate the same remaining quantity. Warehouse master changes retain their exclusive authority fence; receiving participates in the shared fence and checks active supplier and exact warehouse through commit.

`AdmitGoodsReceipt` establishes PO admission under its version/parent lock. `PostGoodsReceipt` validates original quantities using bigint comparisons and checks stored received counters against already posted GRN sums **before** adding the new receipt. It updates the GRN, line counters and derived PO state with GRN-linked immutable audit in the caller transaction. A mismatch is an error, never a repair. The additive SQL protocol extends the existing PO guards without editing their earlier migration definitions.

`IInventoryPosting.PostReceiptAsync` is the dedicated inventory boundary. Generic posting rejects Receipt kind, preventing a Sales/Service owner from masquerading as an inbound receipt. The dedicated method independently checks permission, durable Warehouse authority, workflow scope, persisted receiving role and original GRN line. It uses the exact PO destination/product with no Product/default-warehouse fallback.

`InventoryPostingKind.Receipt = 4` leaves existing values unchanged. A receipt creates physical delta `+quantity`, reservation delta `0`, and a monotonic stock sequence. A missing destination stock row is explicitly provisioned in the same transaction. Its first physical event is a Receipt at sequence 1; there is **no fabricated zero opening**. Existing rows use rowversion and product-specific transaction locks. All arithmetic is checked; receipt movement/posting links are unique and immutable.

Receipt-linked postings are not inventory reservations. Reconciliation now verifies receipt ownership/source/actor, one movement per posted receipt line, no movements for unposted/cancelled GRNs, exact PO-line linkage, posted GRN sums versus received counters, and Partial/Received document state, in addition to the existing physical/reserved/owner invariants.

Draft PO supplier/warehouse edits remain supported. GRN headers have restrictive PO/supplier/warehouse FKs plus a SQL source-equality guard rather than an EF alternate key on those editable Draft PO fields. A regression test verifies Draft source changes; a second rejects wrong-warehouse receipts even when the individual IDs are valid.

## Additive migration

`20261026203843_GoodsReceiptPartialReceiving` adds:

- GoodsReceipts, GoodsReceiptItems and GoodsReceiptCommands.
- Default-zero PO received quantities, nullable GRN audit ownership and nullable inventory receipt-item ownership.
- Positive quantity, bounded received quantity, valid lifecycle/audit/kind and JSON-result checks.
- Restrictive FKs, exact original PO-item/product identity, unique document/line/posting/actor-key/source-reference indexes and rowversions.
- GRN document/command/line and receipt-ledger SQL guards, receiving procedures and additive receiving-aware replacements for the three PO protocol triggers.

Existing table/column data is retained. Existing enum values remain accepted; their check constraints are expanded. Destructive Down is refused. Earlier migrations, inventory authority data and production gate registrations are unchanged. Model/snapshot and an upgrade from the committed Phase 3 schema are checked in isolated SQL databases. Runtime SQL execution grants remain a separately reviewed deployment concern; no credentials/grants were changed.

## Source-file inventory

- Domain: new `ERPFlow.Domain/Receiving/GoodsReceipt.cs`; receiving fields/validation in `Procurement/PurchaseOrder.cs`; Receipt kind/effects in `Inventory/WarehouseStock.cs` and receipt ownership in `Inventory/InventoryPosting.cs`.
- Application: new `Receiving/GoodsReceiptContracts.cs`, `Receiving/GoodsReceiptWorkflow.cs`, `Abstractions/IGoodsReceiptPersistence.cs`; dedicated receipt posting port and additive PO progress DTOs.
- Infrastructure: new `EfGoodsReceiptPersistence.cs` and `.Reads.cs`, `ERPFlowDbContext.Receiving.cs`, `Configurations/GoodsReceiptConfiguration.cs`, three `ReceivingSqlGuards` files, `Inventory/EfInventoryPosting.Receipts.cs` and `EfInventoryReconciliation.Receipts.cs`; context/mapping/DI hooks, GRN commitment checks, migration/designer and updated snapshot.
- API: new `ERPFlow.Api/GoodsReceiptEndpoints.cs` and policy/route registration.
- Tests: `GoodsReceiptDomainTests`, `GoodsReceiptIntegrationTests`, `GoodsReceiptSafetyTests`, `GoodsReceiptConcurrencyTests`, `GoodsReceiptRollbackTests`, `GoodsReceiptAtomicityTests`, `GoodsReceiptApiTests`, `GoodsReceiptMigrationTests`, `GoodsReceiptCompatibilityTests` and fixture support.
- Documentation: OpenAPI snapshot, README and this report. The development settings and earlier Phase 2C report modifications were pre-existing and are not part of Phase 4.

## Executed receiving evidence

- Against an approved quantity-10 PO, GRNs of **3 and 7** post successfully: PO transitions Approved -> PartiallyReceived -> Received, received/remaining becomes 10/0, destination WarehouseStock becomes **10 physical / 0 reserved / sequence 2**, and the two Receipt movements reconcile. Archived Product balances, rowversion and selling price remain unchanged.
- Two simultaneous 7-unit posts against quantity 10 produce one success and one conflict, leaving exactly 7 received and one posted receipt. Simultaneous 3- and 4-unit posts both succeed with total 7. Concurrent identical create/post keys produce one document/effect set and the same durable response.
- Different POs receiving the same destination/product share one sequenced stock ledger. New destination stock begins with Receipt rather than Opening.
- Before cutover, even a deliberately permissive test gate cannot create/post; no receipt, command, balance or ledger effects occur. Default production composition also rejects post against an otherwise valid isolated completed-cutover receipt.
- Faults after stock save, movement save and final replay save roll back stock, receipt state, PO counters/state and the post claim; clean retries succeed. A real SQL fault after receiving counters/state updates likewise rolls back the whole command. Multi-SKU failure rolls back every SKU, including newly provisioned stock rows. Create failure rolls back receiving admission/audit and its key claim.
- Positive Int32 capacity is tested end-to-end; an additional receipt exceeding stock capacity fails with unchanged GRN/counters/ledger. Posted documents/movements cannot be reversed or modified. Cancelled Draft paperwork has no stock effect and can be corrected without erasing its history.
- Real ASP.NET API tests exercise policies, revocation, partial/full post, cancellation, response replay, pagination, missing resources, malformed commands, default-gate rejection and cost-free PO progress.

## Phase 5 decision boundary

### Final backend verification

| Check | Actual result |
| --- | --- |
| Solution restore | PASS |
| Final .NET build | PASS, 0 warnings / 0 errors |
| Complete isolated backend regression suite | **PASS, 330/330**, 0 failed / 0 skipped, 4m30s |
| Baseline compatibility | All 281 existing tests retained; 49 new Phase 4 cases |
| Partial/full receiving and ledger reconciliation | PASS |
| GRN SQL concurrency and durable replay | PASS |
| Stock/movement/PO/receipt/idempotency rollback and clean retry | PASS |
| Migration upgrade and offline model/snapshot checks | PASS |
| EF CLI pending-model check | PASS, no pending changes, non-Development unreachable design-only connection |
| OpenAPI regeneration and snapshot comparison | PASS; comparison also included in final full suite |
| Angular unit regressions, unchanged frontend source | **PASS, 24/24** across four files |
| Development settings preservation | Unchanged Git blob hash `4134a04f650bef6e25fdad549896014d333bb402` |

Final result artifacts: `C:\Users\User\AppData\Local\Temp\opencode\ERPFlow-Phase4-final.trx` and adjacent `.stdout.log` / `.stderr.log`; Angular output is `ERPFlow-Phase4-angular.stdout.log` in the same directory. The first Angular transport call timed out; it exited and the captured rerun passed. The earlier 328-test run predates the final Draft PO source-edit and SQL source-mismatch coverage. All fixture databases were uniquely named and fixture cleanup completed. No configured database was inspected. The EF CLI emits its existing 10.0.11-versus-10.0.13 version warning; no tool or dependency upgrade was made.

**GO for separately scoped Phase 5 development and isolated verification: Phase 4 implementation and backend regression gates passed.** Production receiving remains **NO-GO**: the production gate is intentionally closed and all-host cutover/deployment, SQL grants, physical attribution and operational recovery approvals remain separate prerequisites. Posted receipt reversals, stock transfers and Angular screens are not implemented or authorized by this phase.
