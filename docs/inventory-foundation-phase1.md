# KDT Phase 1: additive inventory foundation

## Authority and scope

**Before Phase 1: Product balances are authoritative. After Phase 1: Product balances are still authoritative.**

Sales, Service, Equipment, legacy StockReservation rows, API contracts, Angular, JWT roles, and development seeding retain their existing behavior. No existing workflow calls the new warehouse posting service. No warehouse, receiving, purchasing or transfer API was added. No default warehouse or opening stock is automatically created, and no existing quantities or reservation IDs are copied, changed or reconciled by this migration.

WarehouseStock is the eventual balance authority, not a second live balance today. `ProductAuthorityWriteGate` is the registered implementation of `IInventoryWriteGate` and always rejects `IInventoryPosting.PostAsync` before database access. There is deliberately no environment/configuration switch to activate it. Only the isolated SQL tests replace that gate by constructing the poster with a test-local implementation. Enabling it in the application requires a separately reviewed authority cutover; changing the gate alone is not a valid cutover.

ADR-001 (Warehouse per SalesOrderItem) and ADR-002 (Warehouse per JobcardItem) remain approved target behavior. Those legacy line contracts and tables are unchanged in this additive phase. Their warehouse fields, exact-source workflow integration and backfill belong to Phase 2/cutover work.

## New model

| Table | Purpose and protections |
| --- | --- |
| `Warehouses` | Stable IDs, normalized unique code, required name, active flag, creation time and SQL rowversion. No master-data UI/API in Phase 1. |
| `WarehouseStocks` | Unique warehouse/product, physical and reserved integer quantities, computed availability, SQL rowversion and monotonic movement sequence. Restrictive warehouse/product FKs. |
| `InventoryReservations` | Immutable stock identity, product, quantity and exactly one SalesOrder or JobcardItem owner. Active/Released/Consumed state, timestamps and SQL rowversion. This does not replace or backfill legacy StockReservations yet. |
| `InventoryPostings` | Append-only durable operation envelope: actor, case-sensitive idempotency key, versioned SHA-256 request hash, kind, reservation reference and creation time. Unique actor/key. |
| `StockMovements` | Append-only posting effects: stock identity, posting kind, sequence, before/after balances, physical/reserved deltas and resulting rowversion for deterministic replay. |

`WarehouseStockId` permanently identifies the warehouse/product pair. A reservation references `(WarehouseStockId, ProductId)` through a composite FK, so it cannot claim a different product from its stock row. The warehouse is derived through that immutable stock identity. Warehouse/product identities cannot be edited through normal EF saves.

The Sales active-reservation unique index is `(SalesOrderId, ProductId) WHERE Status = Active`, deliberately excluding warehouse identity. Therefore one Sales product cannot be split across multiple warehouses through the foundation. Closed Sales cycles remain retained. Jobcard ownership has a unique JobcardItemId, and the existing `(JobcardId, ProductId)` constraint remains intact.

There is no polymorphic owner string, supplier delivery-reference uniqueness, general document workflow engine, stored procedure, PO, GRN or transfer implementation. New modules can add posting kinds and extend their constraints in later migrations; the present enum contains only Opening, Reserve, Release and Consume.

## Invariants

For each warehouse stock row, after every committed posting through this boundary:

```text
StockOnHand >= ReservedQuantity >= 0
AvailableStock = StockOnHand - ReservedQuantity
StockOnHand = SUM(StockMovements.PhysicalDelta)
ReservedQuantity = SUM(StockMovements.ReservedDelta)
ReservedQuantity = SUM(active InventoryReservations.Quantity)
```

The owner sum above uses the new inventory reservations only; do not add legacy JobcardItem or StockReservation quantities to it again. Legacy Product reconciliation remains its existing, separate invariant until cutover.

New stock rows are provisioned explicitly at zero, sequence zero. They cannot reserve before a single opening is posted. A zero opening is allowed. Opening has physical delta `q` and reserved delta zero; Reserve has `(0,+q)`, Release `(0,-q)`, and Consume `(-q,-q)`. Non-opening quantities must be positive. Arithmetic and sequence increments are checked. Consumption/release must identify the exact active owner reservation, source stock and full quantity; aggregate reserved stock is not permission to consume someone else's obligation.

SQL CHECK constraints enforce local bounds, sequence validity, ownership XOR, closure timestamps, before/after delta arithmetic and movement-kind signs. Unique indexes enforce stock keys, movement order, one opening, posting effects and replay keys. Restrictive FKs preserve referenced history. Cross-table SUM equalities are not SQL CHECK constraints: they are maintained by the shared posting transaction and tested through reconciliation.

## Posting and transaction contract

`IInventoryPosting` is an internal application port, not a public API or authorization policy. Its actor comes from the server's `ICurrentUser` and must exist in AppUsers. Future business workflows must apply their endpoint and persisted-role/ownership authorization before invoking it, including before replay. No new role grants are introduced by Phase 1.

`EfInventoryPosting`:

1. Checks the fail-closed authority gate before database access.
2. Validates quantity, IDs, owner shape, bounded idempotency key and eight-byte expected stock version.
3. Rejects pre-existing tracked inventory/master mutations and pending legacy Product balance writes, preventing unrelated changes or dual authority from being smuggled into a posting save.
4. Confirms the current persisted actor and checks for an actor/key replay before checking today's stock version.
5. Loads the explicitly selected active warehouse and pre-provisioned stock row. Missing stock is a conflict; there is no get-or-create or warehouse fallback.
6. Validates the reservation owner against a persisted Sales or Jobcard line, or verifies the exact reservation being closed.
7. Updates the domain stock/reservation state and saves using SQL rowversion predicates.
8. Appends a posting and movement with the persisted resulting stock version.
9. Commits once when it owns the transaction.

The two SaveChanges calls are enclosed by the same transaction. Faults before stock/reservation SQL, before movement insertion and after movement SQL insertion all roll back balances, reservations, movements and replay claims. The caller may supply an existing transaction; in that case the poster neither commits nor independently rolls it back. Its returned result is tentative until the caller commits. Any exception requires the caller to roll back the **entire** transaction and discard its DbContext. Never catch and continue a partially failed transaction, particularly with SQL Server MARS/savepoint limitations.

The context guard blocks ordinary EF balance modifications outside the posting scope, history updates/deletes, stock deletion and reservation deletion. Zero stock provisioning is allowed without a posting but has no balance effect. These are EF write-boundary protections, not protection against privileged SQL, ExecuteUpdate/ExecuteDelete or other raw SQL bypasses. Such bypasses must not be used by later modules. Production database-principal permissions and operational reconciliation remain deployment responsibilities; no credentials or permissions were changed in Phase 1.

## Idempotency and concurrency

The posting envelope is the idempotency store; a separate generic idempotency framework is not needed yet. The unique key is `(ActorId, IdempotencyKey)`. Keys are bounded to 128 non-whitespace characters. The binary SHA-256 hash includes an explicit `inventory-v1` format marker, kind, warehouse, product, quantity, expected stock version, owner IDs and reservation ID.

- Same actor/key and same payload: return the original posting/movement/reservation IDs, resulting balances and original result rowversion, even if stock has since changed.
- Same actor/key and different payload: conflict with no mutation.
- Different actors do not receive each other's replay results.
- Stock and reservation rowversions prevent lost updates and double consumption.
- Concurrent identical standalone requests: one commits; the losing transaction rolls back, clears tracked state and replays the committed result.
- A failed transaction leaves no successful key claim. Its unchanged command/key can be retried with a fresh context.
- An ambient-transaction caller must roll back before retry/replay; the poster does not override that caller's transaction ownership.
- There is no unbounded automatic retry or silent token refresh. A genuinely stale version is a concurrency conflict.

The future PO parent fence, multi-row transfer protocol and request-level API idempotency remain later-module work. Phase 1's single-stock posting result is intentionally narrow; future batch postings must persist all effects and their replay result within one transaction rather than independently calling this as multiple committed operations.

## Additive migration and safety

`20261025184342_InventoryFoundation` creates exactly the five new tables and their indexes/constraints. Its Up operations contain no updates, inserts/backfill, ALTERs or DROPs against legacy tables. It creates no default warehouse and no stock data. The generated model snapshot/designer include the same constraints.

An isolated upgrade test starts at `20261024110336_CriticalWorkflowConsistency`, inserts a pending Sales order and active reservation, a dispatched order, a delivery, an asset, approval history and an active Jobcard part. After applying the new migration twice, it checks original IDs and history, unchanged Product rowversion and quantities (18 physical/7 reserved), and that every new inventory table is empty.

The generated Down drops only the new tables. It is not an approved production rollback procedure after inventory data exists. No Down operation or schema upgrade was executed against the configured application database. Existing Development startup still applies pending migrations when an operator starts it; that behavior was not invoked against the configured database during this work. Deployment operators must review pending migrations before starting that host.

## Separately approved cutover prerequisites

Phase 2 may integrate/test warehouse-aware workflows in isolated databases while production Product authority remains unchanged. Before switching live authority:

1. Approve default warehouse identity, attribution provenance and opening-balance sign-off.
2. Verify `Product.ReservedQuantity = active legacy Sales reservations + active legacy Jobcard part quantities` and physical stock covers all obligations. Mismatches block cutover; no automatic correction.
3. Add/backfill per-line source warehouse fields, preserve original record IDs/timestamps and explicitly represent unknown legacy closure history.
4. Define a carry-forward opening/import operation for nonzero reserved balances. Phase 1's opening cannot import an arbitrary reserved total; do not combine an imported reserved opening with fresh reservation increments and double-count it.
5. Reconcile archived closed reservation cycles without guessing source history or replaying historical consumption.
6. Retain closed Sales reservation cycles; the current legacy resubmission-clearing behavior must be replaced atomically with the warehouse-aware implementation.
7. Freeze/drain all inventory writers, including seeders/importers, and switch every reader/writer together. WarehouseStock then becomes the sole mutable authority; legacy Product columns become frozen archival evidence, not independently synchronized balances.
8. Preserve existing owner/status/dispatch idempotency and meter checks, including aggregate fences for line-only changes. Approval enums and API compatibility must remain stable.
9. Rehearse migrations, invariants and recovery on a restored isolated copy. No live enablement by changing a feature flag or replacing only the gate.

Before the first post-cutover write, rollback can restore the untouched legacy authority under the freeze. After any new reservation, release or consumption commits, an old Product-writing binary is unsafe: use a compatible build, halt posting or repair forward. Destructive column removal or flattening stock back into Product totals is not an approved rollback.

## Verification

Use a uniquely named isolated SQL Server database through `ERPFLOW_TEST_SQLSERVER`; tests never reuse the application connection string. The new fixture defaults to `.\SQLEXPRESS` like existing fixtures and was run with `(localdb)\MSSQLLocalDB`.

```powershell
dotnet restore ERPFlow.slnx
dotnet build ERPFlow.slnx --no-restore
$env:ERPFLOW_TEST_SQLSERVER='(localdb)\MSSQLLocalDB'
$env:ERPFLOW_EXPORT_OPENAPI='0'
dotnet test ERPFlow.slnx --no-build
```

EF generation/model checks must use a non-Development environment and an overridden unused design-only connection so startup migration/seeding cannot touch the application database. The model-check command is:

```powershell
dotnet ef migrations has-pending-model-changes --project ERPFlow.Infrastructure --startup-project ERPFlow.Api --no-build -- --environment InventoryDesign
```

Supply issuer/audience and a test-only signing key through process environment variables for design-time validation; do not change application secrets. No database update command is needed for that check.

The tests cover domain arithmetic, closure ownership, SQL stock constraints and indexes, migration preservation/shape, fail-closed application DI, exact source/owner matching, idempotent replay/key conflicts, simultaneous requests, oversell prevention, stale versions, rollback before/after movement SQL, ambient rollback and ordinary EF bypass/history protection. Existing Sales, Equipment, Service, authentication and OpenAPI tests remain the regression gate.

## Phase 2 decision

### Recorded Phase 1 verification (2026-10-25)

| Check | Result |
| --- | --- |
| Solution restore | PASS |
| Solution build | PASS, zero warnings and errors |
| Full backend suite on isolated LocalDB | PASS, 155/155, including 36 new foundation cases and all 119 existing regressions |
| Angular unit suite (frontend unchanged) | PASS, 24/24 |
| EF pending-model check in InventoryDesign environment | PASS, no model changes pending |
| Additive migration and legacy preservation tests | PASS; five new tables only, legacy quantities, rowversion, IDs and history preserved |
| Concurrent requests and replay | PASS; no oversell or duplicate effects |
| Fault injection | PASS before initial stock save, before movement save and after movement SQL, for openings and reservations |
| Final independent review | PASS after dirty-master guard and pre-stock-save fault coverage were added |
| Test database cleanup | No ERPFlow-prefixed catalogs remain on the isolated LocalDB instance |

The initial focused run exposed a test-only culture-dependent table ordering assertion, corrected to ordinal comparison. The final full suite above passed after the subsequent guard and coverage improvements. C# LSP diagnostics timed out; the compiler and executable tests provide verification. EF CLI 10.0.11 emitted its existing older-tool warning against EF packages 10.0.13; tools and package versions were not changed.

The pre-existing development SQL hostname edit in `ERPFlow.Api/appsettings.Development.json` was preserved exactly. No configured database migration, application startup against that database, secrets change, commit or push was performed.

**GO for separately scoped Phase 2 implementation and isolated verification after Phase 1 tests pass. NO-GO for live WarehouseStock authority or data cutover.** The authority decision, historical reconciliation and all-reader/writer transition remain separate approvals. This foundation does not certify legacy data, a production migration, or production inventory balances.
