# Phase 2C: inactive atomic inventory cutover and isolated rehearsal

## Safety verdict and scope

**GO for further development and isolated rehearsal. NO-GO for live activation.**

The implementation supports a durable atomic Product-to-WarehouseStock transition and warehouse-backed runtime workflows in isolated fixtures. Production DI still registers `DisabledInventoryCutoverPermission` and `ProductAuthorityWriteGate`, which always reject operator execution and warehouse posting respectively. No configuration flag, startup action, HTTP endpoint or production CLI activates either permission. The test-only permission verifies the exact connection against databases actually created and currently owned by the isolated fixture; a matching database-name prefix is insufficient.

No configured application database was accessed, migrated, reset or cut over. No production warehouse writer was enabled, and no secrets, credentials or role assignments were changed. No PO, GRN, transfer or other ERP module was introduced. The pre-existing development SQL hostname modification is unrelated and remains preserved.

This is an inactive operator-service implementation, not a ready-to-run production activation tool. A separately reviewed operator host and deployment authorization are still required.

## Changed-file inventory

### Authority and operator contracts

- `ERPFlow.Domain/Inventory/InventoryAuthority.cs`: singleton mode, minimum writer protocol and immutable completion/approval metadata.
- `ERPFlow.Application/Inventory/IInventoryCutover.cs`: explicit operator request/result, permission and reconciliation ports.
- `ERPFlow.Application/Inventory/IWorkflowInventory.cs`: shared exact-source runtime reservation/release/consumption port.
- `ERPFlow.Infrastructure/Persistence/Inventory/DisabledInventoryCutoverPermission.cs`: no production activation path by default.
- `ERPFlow.Infrastructure/Persistence/Inventory/InventoryAuthoritySession.cs`: transaction-owned shared/exclusive SQL application lock and supported-writer check.

### Import and posting implementation

- `EfInventoryCutover.cs`: frozen-source and manifest validation, approval, atomic import, reconciliation, completion and idempotent replay.
- `EfInventoryReconciliation.cs`: owner/balance/ledger/document consistency and sequence validation.
- `EfWorkflowInventory.cs`: same-transaction Sales and Service stock effects through the posting boundary.
- `EfInventoryPosting.cs`, `IInventoryPosting.cs`: original Sales reservation IDs for new explicit owners, exact-source validation and standalone transaction fencing.
- `InventoryReservation.cs`: carry-forward construction, source timestamp provenance and LegacyClosedUnknown support.
- `WarehouseStock.cs`: one conserved opening with physical and reserved quantities, without reserve increments.
- `EfLegacyInventoryImportPreparation.cs`: shared authority fence and refusal to prepare after warehouse activation.

### Existing runtime integration

- `SalesOrder.cs`, `StockReservation.cs`: warehouse-authority transitions that retain original state/history semantics without Product balance mutation.
- `SalesOrderWorkflow.cs`, `ServiceWorkflow.cs`, `EquipmentWorkflow.cs`: authority-selected inventory branch; existing role, pricing, approval, dispatch, asset and meter rules retained.
- `EfSalesOrderPersistence.cs`, `EfServicePersistence.cs`, `EfEquipmentPersistence.cs`, `EfApplicationTransaction.cs`: shared authority fence for workflow transactions.
- `EfReferenceDataQueries.cs`: fenced quantity reads from the default WarehouseStock after cutover instead of archived Product quantities.
- `ProductAuthorityWarehouseSourcePolicy.cs`: exact active source selection in warehouse mode; legacy default-only behavior before cutover.
- `DevelopmentDataSeeder.cs`: refuses legacy Product seeding after warehouse cutover.
- `ERPFlowDbContext.cs`, `ERPFlowDbContext.Inventory.cs`, `DependencyInjection.cs`: authority mapping, constrained import scope, frozen Product guards and fail-closed registrations.

### Schema

- `InventoryAuthorityConfiguration.cs`: new singleton and completion constraints, approval/preparation/default warehouse FKs and initial Product mode.
- `InventoryReservationConfiguration.cs`, `StockMovementConfiguration.cs`: preserved unknown legacy closures and reserved opening deltas.
- Product, Sales order/item, legacy reservation, Jobcard/item and WarehouseStock configurations: SQL OUTPUT behavior compatible with write-fence triggers.
- `InventorySqlGuards.cs`: version-plus-transaction-lock checks, frozen Product balances and immutable authority completion.
- `20261026140337_AtomicInventoryCutoverRehearsal.cs`, designer and model snapshot: additive authority/provenance schema, guard installation, no inventory-data cutover and no destructive downgrade.

### Tests/documentation

- `InventoryCutoverTestSupport.cs`: fixture-owned permission and rehearsal-only warehouse gate.
- `InventoryCutoverIntegrationTests.cs`: openings, original owners, replay, rollback stages, approval/version/source failures and Product writer rejection.
- `InventoryCutoverRuntimeTests.cs`: concurrent Sales/Service/dispatch, exact non-default sources and unknown historical closures.
- `InventoryCutoverSafetyTests.cs`: fresh initialization, concurrent import, transaction fencing, SQL guard bypass attempts, stale tracked replay and reconciliation failure.
- `InventoryCutoverReaderTests.cs`: current warehouse availability instead of archived Product stock.
- `InventoryCutoverOwnerRaceTests.cs`: coordinated same-Jobcard warehouse requests cannot duplicate owners or leak reservations; parent-save conflicts remain typed workflow conflicts.
- `InventoryTestDatabase.cs`: exact fixture ownership registry; normal fixture cleanup retained.
- Existing Sales/Equipment/Service integration helper wiring: inject the shared runtime port without changing their legacy-mode assertions.
- `README.md` and this document: inactive execution, evidence and deployment prerequisites.

## Operator request and transaction boundary

`IInventoryCutover.RehearseAsync` requires:

```text
PreparationId
ExpectedFingerprint: SHA-256 of the prepared frozen source
DefaultWarehouseId: the stable legacy compatibility warehouse
PhysicalAttributionApproval: explicit nonempty approval reference, at most 200 characters
ApplicationWriterVersion: the supported version, currently 2
```

The permission gate runs before database access. Within an owned Serializable transaction, the service obtains an Exclusive `ERPFlow.Inventory.Authority.v2` application lock. It verifies the persisted Administrator, refreshes the authority entity, validates the preparation/source fingerprint and reconstructs the expected staging rows to detect manifest modification. A fingerprint match alone does not allow tampered opening or owner quantities.

All import effects occur in that one transaction:

1. Revalidate source statuses, exact active owner lines and `Product.Reserved = active Sales + active Jobcard quantities` using wide/checked sums.
2. Import one physical/reserved opening per product into the prepared target stock identity; existing targets must still be unused zero rows.
3. Import explicit owner records with original Sales reservation IDs and deterministic Jobcard targets/references.
4. Backfill approved legacy null warehouse origins to the approved default. This intentional source change occurs only after frozen-source validation, not during normal preparation.
5. Persist target row versions, then append one Opening posting/movement per imported stock row.
6. Reconcile all balances, owners, movements, sequences and document states.
7. Record completed authority metadata last and commit once.

Failure at any stage rolls back stock, owner records, line-origin backfill, postings, movements and completion metadata. The original preparation remains unchanged. Operator session permission is cleared and the connection is closed on exit. Failed contexts are cleared before reuse; callers should still dispose them.

For the same completed request, the service re-authorizes, re-reads completion state, reconciles and returns the stored import counts/time. It does not re-import or compare today's intentionally changed source to the old preparation. A changed request, actor or preparation cannot replace completion metadata. Replay also works when its DbContext tracked the old Product-mode authority before another context completed cutover.

## Legacy quantities and historical evidence

For a product with physical quantity H and reserved quantity R, import:

```text
WarehouseStock: H physical, R reserved, sequence 1
Opening movement: physical delta +H, reserved delta +R
Active imported owners: total quantity R
Additional reserve movements for those owners: NONE
```

There is no second increment to reserved stock. A zero opening is valid. Product balance columns and their row versions remain unchanged as archival evidence.

Imported closed Sales rows retain their IDs and known closure metadata. Rows whose original closure is uncertain remain LegacyClosedUnknown; terminal legacy Jobcard parts likewise retain uncertain history without synthetic release or consumption events. `IsLegacyImport` and nullable `SourceCreatedAt` distinguish a carry-forward record from a known original request timestamp. All old approval histories, deliveries, assets and legacy reservation rows remain in place.

Fresh empty databases may initialize warehouse authority with zero imported stock/owner rows, without fabricating quantities. Catalog and stock initialization after that point must use warehouse-compatible paths; legacy development stock seeding is intentionally rejected.

## Runtime authority enforcement

The additive migration seeds Product mode and writer version 1, not Warehouse mode. The current application protocol is 2. No environment variable selects authority.

Sales, Service and Equipment transactions obtain a Shared transaction-owned application lock before selecting their durable authority. Cutover's Exclusive lock waits for these transactions to finish and blocks new selection until completion. A transaction cannot read Product authority and then commit against a new warehouse authority. Standalone inventory postings and preparation also participate in authority fencing; quantity-bearing reference reads use the same shared fence.

In warehouse mode:

- Sales submission creates the current legacy ownership/history records without reserving Product, then reserves exact WarehouseStock rows and matching explicit owners in the same transaction.
- Rejection/return release original warehouse reservations and mirror legacy closure/history, without changing Product.
- Dispatch consumes those owners, keeps Approved state, and commits its delivery/assets with all inventory effects.
- Jobcard parts capture their source and create explicit reservations. Completion consumes those original owners and retains asset meter concurrency.
- Reference availability reads default WarehouseStock quantities. It does not return the frozen Product balance.
- Warehouse shortage never falls back to another warehouse or to Product.

The normal application still has its warehouse write gate closed. Even if a warehouse-mode database is pointed at an unapproved host, inventory writes fail closed rather than reverting to Product.

## SQL guards and compatibility

After Warehouse authority is completed, guard triggers on Sales orders/items, legacy reservations, Jobcards/items and WarehouseStocks require **both** protocol marker 2 and an actual Shared/Exclusive transaction-owned authority application lock. Merely setting SESSION_CONTEXT to version 2 is insufficient. The authority transition requires the Exclusive lock and explicit operator session permit; completed authority metadata cannot be updated/deleted.

A separate Product trigger prohibits changing archived physical/reserved balances after cutover even with a valid current protocol. The EF context also rejects those mutations. Legacy positive-balance Product insertion/deletion is similarly not a stock initialization mechanism.

These guards reject known incompatible writers. The protocol marker is not cryptographic application attestation: a privileged SQL principal capable of deliberately acquiring the lock and issuing raw SQL remains a trust boundary. A deliberate fenced raw balance corruption is detected by reconciliation and is not auto-repaired. Least-privilege runtime credentials, separate migration/operator permissions and a prohibition on raw DML bypass are operational prerequisites, not credentials changes made here.

Old binaries can still perform stale read-only queries against archived columns. SQL DML guards cannot prove that every old reader has left service. Therefore host drain, routing/version admission and disabling old database access must be rehearsed before any live approval. Mixed old/new application deployment after authority switching is not supported.

The migration preserves legacy inventory columns/data and adds only the authority/provenance capability plus guard constraints. It does not invoke the operator service. Down is explicitly refused: dropping the marker/guards or restoring old closure constraints after import is not a safe rollback.

## Reconciliation

Under an exclusive reconciliation fence:

```text
H >= R >= 0
R = sum(active explicit inventory owner quantities)
H = sum(physical movement deltas including openings)
R = sum(reservation movement deltas including openings)
```

Also validate movement before/after continuity and sequence, posting/movement association, source identities, active legacy Sales mirrors, current pending/approved lines, deliveries' order states and active Jobcard parts. Missing owner rows are rejected even if totals happen to match. Historical closed rows do not have to match products subsequently removed from returned Drafts.

No automatic correction, clamping, stock inflation or rewriting of movement history occurs.

## Rehearsal coverage and operational blockers

SQL tests run only against fixture-generated isolated LocalDB/SQL Server catalogs. Rehearsal activation permission is test-local and verifies exact current fixture ownership; it is not shipped as a production authorization option. Existing foundation component tests retain their explicit isolated posting gates and do not represent activation of application DI.

Coverage includes conserved reserved openings, original IDs, unknown closures, fresh empty initialization, duplicate/concurrent execution, dirty/tampered/stale manifests, missing physical approval, rollback after each of three import saves, blocked/incompatible writers, version-marker-only bypass, stale-tracked replay, actual shared/exclusive fencing, exact non-default consumption, concurrent Sales/Service/dispatch and current quantity reads.

### Original implementation verification

| Check | Result |
| --- | --- |
| `dotnet restore ERPFlow.slnx` | PASS |
| Final .NET build | PASS, zero warnings/errors |
| Full backend/SQL suite | PASS, 199/199: all 179 prior regressions plus 20 cutover cases |
| Initial focused cutover run | PASS, 15/15 before subsequent hardening/coverage additions |
| Final coordinated warehouse owner race | PASS; also included in the full 199-test run |
| Angular unit regressions, frontend unchanged | PASS, 24/24 |
| EF pending-model check in non-Development InventoryDesign environment | PASS, no pending changes |
| Final independent review | PASS; SQL fence, metadata replay and fixture ownership findings resolved |
| Final whitespace check | PASS; normal LF/CRLF working-copy notices only |
| Isolated database cleanup | No ERPFlow-prefixed catalogs remain on LocalDB |

Representative conserved import evidence is 50 physical/6 reserved, one opening with deltas `(50,6)` and two active owner records totalling 6; the old Product row/version is unchanged. After concurrent dispatch, completion and another Sales submission, the warehouse has 44 physical/3 reserved, while archived Product remains 50/6. An additional reader case returns current availability 41 instead of archived Product availability 44 after a warehouse reservation. A non-default-source case consumes only its selected warehouse while the default remains unchanged.

Fault injection runs after target/owner/source-origin SQL, after opening movement SQL and after completion-marker SQL. Each failure leaves Product authority, no imported postings/owners/movements, original null origins and original source balances, then allows a successful retry. Concurrent duplicate import and stale-tracked-context replay produce one opening/owner set.

During hardening, execution exposed a fixture connection-string-option ordering issue and SQL Server autocommit `APPLOCK_MODE` error handling. Fixture authorization now compares registered canonical server/catalog identity, and missing user-transaction lock context is translated into a rejecting guard error rather than inferred from `@@TRANCOUNT`. These cases, including version-marker-only rejection, pass in the final full run. No failing test was removed to achieve the result.

At the original verification the migration was uncommitted and had been applied only to disposable owned fixtures. The implementation is now the existing repository baseline at `221c0ff`; this follow-up leaves that migration unchanged. No live/shared migration was amended. Subsequent released trigger changes require a new migration.

Fresh C# LSP diagnostics timed out; compiler/tests provide verification. EF CLI 10.0.11 emitted its existing older-tool warning against EF packages 10.0.13; tools, packages and production credentials were not changed. The first parallel Angular transport call timed out; the subsequent captured unit run passed. No production database, real host fleet, production permissions or physical counts were inspected.

Before production activation, separately approve and prove:

1. Physical attribution and resolution of every active obligation mismatch on a restored copy of the actual target database.
2. Backup/restore and forward-recovery objectives preserving acknowledged activity; no old Product-writing binary after successful warehouse writes.
3. Complete writer and reader drain, version admission and prevention of old host/database access, including background jobs, seeders, imports and administrative scripts.
4. A production operator host, authenticated approval process, least-privilege permissions and secure audit retention. There is no live operator host in this phase.
5. A deliberate reviewed replacement of both fail-closed permissions, not a configuration switch. The code in this branch never enables those permissions in production.
6. Load/deadlock rehearsal for representative multi-line documents, monitoring and periodic reconciliation.

The application-lock and SQL-protocol guards are technical protection, not proof that the production fleet, permissions or physical counts are ready. Until these operational items are verified, the live verdict remains NO-GO.

## Follow-up implementation and isolated rehearsal — 2026-10-26

The request arrived with Phase 2C already committed in `221c0ff`. This increment preserves that implementation and closes a demonstrated reconciliation defect. The pre-existing modification to `ERPFlow.Api/appsettings.Development.json` is preserved; it is excluded from this increment. No commit or push was performed.

### Changed-file inventory for this increment

| File | Change |
| --- | --- |
| `ERPFlow.Infrastructure/Persistence/Inventory/EfInventoryReconciliation.cs` | Verify posting-to-owner source/quantity/kind, native reserve/terminal lifecycle, approved import provenance and closed document history; clear observed authority when an owned reconciliation fails. |
| `ERPFlow.Infrastructure/Persistence/Inventory/EfInventoryCutover.cs` | Supply the approved preparation ID to pre-completion reconciliation inside the existing atomic transaction. |
| `ERPFlow.Tests/InventoryReconciliationAssociationTests.cs` | Eight isolated cases covering wrong-owner postings, equal-quantity wrong-warehouse associations, closed Sales mirror mismatch, consumed Jobcard state mismatch and valid return/edit/resubmit/dispatch history. |
| `ERPFlow.Tests/InventoryCutoverMigrationTests.cs` | Two cases: real InventoryFoundation-to-latest upgrade followed by atomic import/replay, and offline migration/model-snapshot safety checks. |
| `README.md` | Explicit LocalDB verification selection; replace unqualified EF startup-project verification with the offline model check in the migration test. |
| `docs/inventory-authority-cutover-phase2c.md` | This follow-up inventory, evidence and readiness assessment. |

### Confirmed defect and reconciliation contract

Before the fix, a foreign-key-valid `InventoryPostings.ReservationId` could be changed to another existing reservation while all warehouse totals remained balanced. Reconciliation accepted it. Six corruption cases reproduced the gap (`Assert.Throws` failed because no exception was thrown); the valid historical lifecycle case already passed.

Reconciliation now requires:

- One movement per posting, matching kind, exact reservation stock identity and full quantity deltas.
- One Reserve posting per native owner. An active owner has no terminal posting; a Released/Consumed owner has exactly one corresponding terminal posting, sequenced after Reserve.
- An imported owner must match the **approved preparation**, including original identity, source, quantity and known source timestamp. An originally active import uses its opening instead of a Reserve posting; subsequent release/consumption requires its normal terminal event.
- An originally closed import keeps its prepared disposition and closure timestamp and has **no fabricated historical posting**. `LegacyClosedUnknown` is admitted only with that provenance.
- Closed Sales inventory ownership agrees with the original legacy reservation, including order/product/source/quantity and known closure kind. A consumed Sales owner requires an Approved dispatched order. Closed returned cycles may refer to products/quantities removed by a later Draft edit.
- A consumed Jobcard owner matches the original part and a Completed Jobcard; unknown terminal imports remain unknown.

There is no repair or balance adjustment on rejection. These are relational integrity checks, not tamper-proof audit attestation: perfectly symmetric reassignment of indistinguishable records cannot be disproved without independent protected evidence.

### Legacy upgrade and opening evidence

The upgrade case creates a unique owned LocalDB catalog at `InventoryFoundation`, inserts only columns present at that historical schema, then applies the additive upgrades twice. It verifies Product authority before explicit cutover. Preparation is performed **after** schema upgrade by the current preparation service.

The source contains 20 physical / 5 reserved, an active Sales reservation of 2, an active Jobcard part of 3, two closed legacy Sales reservations, a completed historical Jobcard part, original approval history and a delivery note. After cutover and replay:

- One WarehouseStock and one Opening movement retain `(20,5)` with sequence 1.
- Five ownership records preserve original Sales IDs and Jobcard references; active owners total 5.
- The three ambiguous closed owners remain `LegacyClosedUnknown`, without invented consumption/release movements.
- Original known timestamps, approval text/time, delivery time, Product quantities and Product rowversion are unchanged.

The offline contract case checks `HasPendingModelChanges() == false`, inactive Product-mode authority seeding, provenance columns, all eight SQL guard definitions, absence of destructive table/column/data operations and refusal of destructive downgrade. It uses an unreachable connection string and never opens a connection.

### Direct operator-service rehearsal and rollback evidence

A temporary, unshipped console driver directly invoked preparation/cutover/reconciliation against the unique catalog `ERPFlow_Phase2C_OperatorQA_f24dfad94dea4c80ba76796b374ff897` on `(localdb)\MSSQLLocalDB`. It loaded no application settings and used an exact catalog/server permission scoped to that invocation.

Observed output:

```text
PASS fault injected after opening-ledger persistence
PASS rollback: Product authority; zero stocks/owners/postings/movements
PASS rollback: source50/6, rowversion and null origins preserved
PASS import: stock50/6; one opening delta(50,6); two owners totaling6
PASS original SalesID and Jobcard reference preserved
PASS archived Product unchanged after success
PASS replay: identical completion and one opening
PASS reconciliation
PASS old Product writer rejected SQL51001
CLEANUP owned unique fixture database deleted
```

The focused suite additionally exercises failures after each of the three import saves, exact non-default sources, concurrent Sales/Service/Dispatch and reservation-owner races, incompatible writers, pre-cutover write rejection and stale/tampered preparations. No configured application database was accessed and no application host was launched for this rehearsal.

### Final follow-up verification

| Check | Observed result |
| --- | --- |
| `dotnet build ERPFlow.slnx --no-restore` | PASS, 0 warnings / 0 errors |
| Full backend suite with `ERPFLOW_TEST_SQLSERVER=(localdb)\MSSQLLocalDB` | PASS, **209/209**, 0 failed / 0 skipped, 4m26s |
| Focused `InventoryCutover` and `InventoryReconciliationAssociationTests` filter | PASS, **30/30**, including all ten new cases |
| EF model/snapshot and inactive migration contract | PASS in the offline migration contract case |
| `npm test -- --watch=false` | PASS, **24/24** across four files |
| `npm run build` | PASS |
| `npm run lint` | PASS |
| Direct operator-service fault/retry/replay rehearsal | PASS; owned unique LocalDB catalog deleted |
| `git diff --check` | PASS; LF/CRLF working-copy notices only |

Full backend artifacts are retained at `C:\Users\User\AppData\Local\Temp\opencode\ERPFlow-Phase2C-20261026-verified.trx` and the adjacent `.stdout.log` / `.stderr.log` files. The temporary operator driver was removed after rehearsal; no executable activation host was added to the repository. The first captured full run passed 208/208 before the last exact-warehouse case; the final captured run above includes it. Earlier red corruption tests were retained and now pass. Fresh LSP diagnostics timed out; compiler and executed SQL tests provide verification.

### Remaining activation prerequisites

The operational prerequisites above still apply. The audit also confirms these concrete boundaries for subsequent development:

1. The production operator permission and WarehouseStock gate remain disabled. A separately reviewed operator/deployment admission mechanism is required; an approval payload or configuration flag cannot enable them.
2. Old readers cannot be fenced by DML triggers. Fleet drain, routing admission and database credential isolation must exclude old hosts, scripts and background workers before a live transition.
3. Raw ledger DML is protected by EF boundaries, constraints and reconciliation, but the protocol triggers do not cover every ledger table. Production SQL grants and append-only/audit protection require a separate least-privilege deployment review. Session markers are not cryptographic application identity.
4. Reference availability retains the compatible default-warehouse view. Non-default-source availability presentation and explicit stock-row provisioning must be designed before those user workflows are admitted to production.
5. Rehearse on an approved restored copy with physical attribution evidence, representative load, backup/restore and forward-recovery procedures. A live switch cannot be undone by restoring an old Product-writing binary.

**Further development and isolated rehearsal: GO. Live activation: NO-GO.** The tested mechanism remains inactive in production; the all-host/all-reader deployment transition is not proven by this repository-only rehearsal.
