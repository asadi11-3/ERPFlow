# KDT Phase 2A/2B: warehouse provenance and legacy import preparation

## Authority and deployment status

**Product balances remain authoritative.** Sales, Service and Equipment continue to reserve, release and consume Product quantities. The Phase 1 `ProductAuthorityWriteGate` is unchanged and still refuses WarehouseStock posting in application DI. Preparation never calls `IInventoryPosting`, creates an InventoryReservation, writes WarehouseStock balances or appends inventory movements. There is no apply/import/cutover endpoint, activation flag or automatic preparation at startup.

This increment does not implement PO, GRN, transfers, warehouse master CRUD or new Angular screens. Existing role policies, statuses, prices, ownership checks, dispatch limits and meter concurrency remain in place.

The configured application database was not used for this work. Migration generation/model checks use a non-Development design environment with an overridden unused connection. All executed database tests use uniquely named isolated SQL databases.

## Phase 2A source compatibility

`SalesOrderItem`, legacy `StockReservation` and `JobcardItem` have nullable `WarehouseId` foreign keys with restrictive deletion. Existing rows remain null after migration: null is retained legacy provenance, not proof of a physical warehouse. Existing IDs and original quantity/time fields are not rewritten.

`LegacyDefaultWarehouse` defines stable ID `0f96d350-19ce-4d10-92d9-b1f06be839f1` and code `LEGACY-DEFAULT`. The migration inserts this one compatibility master record only. It creates no stock or opening balances. This represents the existing global accounting bucket; physical-location attribution still needs business sign-off for cutover.

The source policy is intentionally narrow while Product authority is global:

| Request/source | Behavior |
| --- | --- |
| Create Sales line or request Jobcard part without WarehouseId | Persist the legacy default ID. |
| Explicit default ID | Accept if that master is active. |
| Edit a retained Sales line without WarehouseId | Retain its stored origin, including a legacy null. |
| Explicit default on an edited legacy-null line | Store the explicit default relationship. |
| New product in a Draft edit | Resolve omission to the default. |
| Existing null line/reservation during submit/release/dispatch/completion | Treat as inferred default for validation; do not silently backfill old rows. |
| Guid.Empty | Validation error, HTTP 400. |
| Any non-default source | Conflict, HTTP 409, before inventory mutation. No selection substitution or fallback. |
| Missing/inactive default | Conflict; do not invent another default or take stock elsewhere. |

New Sales reservations inherit their line's recorded origin; a legacy null remains explicitly inferable rather than being retroactively asserted as historical fact. Jobcard origins are captured at parts request and are not editable by the workflow. Completion and dispatch accept no warehouse override. Explicit line/reservation mismatches are rejected before consumption.

Existing request shapes still work. WarehouseId is an optional request field and an additive nullable result field. Old Angular clients need no new field to operate against the existing bucket. Requests selecting other warehouses, warehouse-specific balances and warehouse selectors cannot honestly become operational until the separately approved authority transition; those are postponed rather than simulated using global Product stock.

The existing unique ProductId-per-document rules are unchanged: one product line per Sales order and one per Jobcard, even if a caller attempts different warehouse IDs.

## Historical Sales reservation retention

Legacy StockReservation gains:

- `IsHistorical`, default false for existing rows.
- Nullable `ClosureKind`: Released or Consumed for new closures; null preserves unknown old closure semantics.

The unique current-cycle index remains `(SalesOrderId, ProductId)`, now filtered by `IsHistorical = 0`. Historical rows must be closed. Existing unfiltered uniqueness means all rows can safely enter this migration as current without record deletion.

Resubmission archives closed current reservations instead of clearing/deleting them. Archival flags are saved before inserting replacement current rows in the **same outer workflow transaction**, avoiding filtered-index insertion ordering conflicts. Failure rolls back the entire transition. Current reservation getters and API list/detail projections exclude historical cycles; `ReservationHistory` and import preparation retain every surviving row. Approval history is unchanged.

Release, return and dispatch operate only on current reservations. They never re-release or consume archived history. Closed records for products removed during Draft editing are not required to match replacement lines. The retained source ID, product, quantity and closure timestamps remain intact.

History already deleted by a pre-upgrade application cannot be reconstructed. No synthetic closed reservation or historic movement is fabricated.

## Phase 2B preparation services

`ILegacyInventoryImportPreparation` provides internal application operations:

```text
PreflightAsync(optional preparationId)
PrepareAsync(preparationId)
```

Both require a current persisted Administrator. Neither is exposed through a new HTTP endpoint. They use a clean DbContext and their own Serializable transaction; they refuse pending caller changes or a caller-owned transaction. They must not be invoked automatically by application startup or the normal seeder.

Preflight reads one consistent source snapshot: Product balances/versions, order statuses/versions, Sales lines and all reservation cycles, Jobcard statuses/revisions/parts, deliveries, warehouse metadata and any pre-existing warehouse-stock targets. It computes a versioned SHA-256 fingerprint with deterministic row ordering. No passwords or authentication secrets enter this snapshot.

Preflight detects and refuses:

- Invalid balances, quantities, owners, unsupported statuses and non-default source attribution.
- An active Sales reservation without an exact current product/quantity/source line.
- An active obligation on a Draft, Rejected or already delivered order.
- A pending/approved undispatched line without exactly one matching active reservation.
- Aggregate reserved totals that differ from active Sales plus active Jobcard ownership.
- Existing WarehouseStock posting/import activity.
- A missing/inactive fixed default warehouse.
- A prepared manifest whose source fingerprint no longer matches current data.

For every Product:

```text
Product.StockOnHand >= Product.ReservedQuantity >= 0
Product.ReservedQuantity
    = SUM(all active legacy Sales reservations)
    + SUM(JobcardItem quantities where Jobcard is Open, PartsRequested or InProgress)
```

Wide sums prevent integer overflow from concealing mismatches. Aggregate equality alone is insufficient: exact owner/line checks must also pass. Mismatches produce a report and abort Prepare without repairing quantities, dropping obligations or inflating stock.

Preparation claims its requested batch ID with a SQL Server `UPDLOCK/HOLDLOCK` point lookup within the transaction, serializing simultaneous requests for that ID. Same ID plus unchanged valid source replays the original manifest/counts. The same ID with changed source conflicts and preserves the original snapshot. A new ID may prepare a replacement snapshot; old preparations remain immutable.

Preparation is a point-in-time plan, not a durable write freeze. Normal Product activity may make it stale immediately afterward. Phase 2C must freeze/drain writers and revalidate the fingerprint **before** applying or backfilling anything; merely having a prepared batch is not permission to cut over.

## Staging tables, not inventory balances

| Table | Meaning |
| --- | --- |
| `LegacyInventoryImportBatches` | Requested preparation ID, actor, time, format version, default warehouse and source fingerprint. |
| `LegacyInventoryOpenings` | Proposed target stock IDs, source Product row versions and conserved on-hand/reserved values. These rows are not WarehouseStock or movements. |
| `LegacyInventoryReservationImports` | Explicit proposed SalesOrder/JobcardItem ownership, original source IDs, target reservation IDs, quantity, disposition, source timestamps and inferred-origin marker. |

Staged Sales reservation target IDs equal the original legacy StockReservation IDs. Jobcard targets use deterministic IDs derived from the original JobcardItem ID. Proposed stock IDs derive from warehouse/product identity unless an unused zero stock target already exists. No target row is created or written by preparation.

Active obligations have Active disposition. New explicit Sales closure kinds remain Released/Consumed. Closed old Sales reservations and terminal Jobcard items without reliable closure evidence become `LegacyClosedUnknown`, not guessed consumption/release events. Jobcard parts have no original request timestamp; their staged creation time remains null. A known document completion time is preserved only as source completion information, not invented reservation history.

Every staged active quantity is already included in the staged reserved opening. A future importer must not apply that reserved opening and also post new reservation increments. Historical consumption/release must not be replayed against today's stock.

The DbContext permits only additions through the preparation boundary. It rejects modifying/deleting preparations and any attempt to write legacy/business/inventory records during preparation saves. Source IDs in reservation manifests are snapshots, not new foreign keys that constrain subsequent legacy line history. Actual prepared balances are protected by bounds and ownership/disposition constraints.

Privileged raw SQL can bypass EF immutability guards. No database credential/permission changes were made. Later application modules must not bypass these boundaries.

## Migration and independently deployable parts

`20261025193004_WarehouseSourcePreparation`:

1. Adds nullable WarehouseId relationships to the three legacy tables.
2. Adds safe archival/closure metadata to legacy reservations.
3. Replaces the legacy reservation unique index with current-cycle uniqueness.
4. Creates three staging tables, their constraints and indexes.
5. Inserts only the fixed default warehouse master metadata.

There is no legacy data backfill, Product quantity update, WarehouseStock writer activation or removal of existing columns. A reserved default ID/code collision fails the migration and needs operator review; the migration does not adopt or rename an existing warehouse silently.

Schema must be deployed before the new binary. The schema expansion is compatible with old rows, but mixed old/new application writers are not an approved deployment strategy once historical cycles exist: an old binary expects unfiltered reservation collections and may delete or misinterpret retained history. Stop/drain old application instances for this application-version upgrade. This is distinct from the still-unapproved inventory-authority cutover.

Automatic destructive downgrade is explicitly blocked by the migration's Down method. Returning to the old unfiltered unique index or dropping provenance would lose data or fail once history has multiple cycles. Use a reviewed compatible forward change or an approved restore under a write freeze, not an old binary against new history.

Existing Development auto-migration behavior is unchanged. No Development host was started against the configured application database during this task.

## Changed implementation areas

- Domain: SalesOrderItem, StockReservation, JobcardItem, Product reservation construction, SalesOrder history/current-cycle operations, Jobcard part construction.
- Inventory Domain: fixed legacy-default identity and three immutable preparation entities.
- Application: source-policy/preparation ports; optional/additive warehouse fields; Sales/Service/Equipment source validation while retaining Product writes.
- Infrastructure: Product-authority source resolver; snapshot reader, preflight validator and preparation service; EF mappings/guards/DI; current-cycle Sales query projections.
- Migration: WarehouseSourcePreparation migration/designer and model snapshot.
- Tests: warehouse-origin API compatibility, repeated correction history, preparation ownership/reconciliation/staleness/idempotence/atomicity, and upgrades from old schemas using old-column SQL fixtures.
- OpenAPI: regenerated from an isolated host for additive WarehouseId contract fields.

## Verification approach

Only fixture-generated SQL databases are used, with `ERPFLOW_TEST_SQLSERVER='(localdb)\MSSQLLocalDB'`. `ERPFLOW_EXPORT_OPENAPI` is enabled only for isolated snapshot regeneration and disabled for the full suite. Design-time model checks use a non-Development environment, an overridden unused connection and process-only test JWT settings. No database update command targets the application's configured connection.

The migration tests preserve old rows with null origins, active Sales and service commitments, removed-line closed reservations, terminal Jobcard history, original IDs, approval comments, delivery and Product rowversion. They verify a second migration run is harmless and inspect Up operations for no destructive legacy/data changes. Preparation tests assert that WarehouseStock, InventoryReservations, InventoryPostings and StockMovements remain untouched, including after failure injected after staging SQL.

Existing Phase 1 safety tests now expect the new default metadata master after the latest migration; the original InventoryFoundation migration still creates only its original five empty tables. Old-schema fixture seeding uses only columns present at that historical migration version rather than using a newer EF model to insert future columns.

## Remaining Phase 2C work and decision

### Recorded verification

| Check | Result |
| --- | --- |
| Solution restore | PASS |
| Full .NET build | PASS, zero warnings/errors on final build |
| Full SQL-backed backend suite | PASS, 179/179, including all 155 prior cases and 24 new cases |
| Initial focused source/preparation subset | PASS, 14/14; subsequent full run includes the additional API and atomicity cases |
| Angular unit suite, unchanged frontend | PASS, 24/24 |
| EF pending-model check in non-Development InventoryDesign environment | PASS, no pending model changes |
| OpenAPI generation/snapshot comparison against an isolated host | PASS |
| Independent final code review | PASS, no actionable correctness/security findings |
| Test cleanup | No ERPFlow-prefixed catalogs remain on the isolated LocalDB instance |

The pre-existing SQL hostname edit in `ERPFlow.Api/appsettings.Development.json` was preserved exactly. No files were staged, committed or pushed. No configured application database was modified or reset. Fresh LSP diagnostics timed out; the compiler and executed tests provide verification. EF CLI 10.0.11 emitted its existing version warning against packages 10.0.13. One intermediate parallel design-time check briefly locked build outputs; the subsequent clean final build passed without warnings.

**GO to implement and rehearse a separately scoped Phase 2C in isolated databases after Phase 2A/2B verification. NO-GO for live cutover.**

Phase 2C still needs:

1. Business approval of actual default-warehouse attribution and every reconciliation exception.
2. A coordinated writer freeze, fingerprint revalidation and all-reader/all-writer authority transition.
3. Atomic application of nonzero reserved openings and explicit imported ownership, exactly once, without duplicate reservation deltas.
4. Target support for `LegacyClosedUnknown` history; never coerce it to Released/Consumed just to satisfy the current Phase 1 target enum.
5. Approved backfill of legacy nullable origins with retained inferred provenance; apply original-snapshot validation before these intentional source changes.
6. WarehouseStock-only Domain operations and exact-source Sales/Service/dispatch integration; remove live Product mutation only at that coordinated switch.
7. Multi-row posting/rollback and request idempotency where whole workflows require multiple stock effects.
8. A tested recovery boundary: after acknowledged warehouse-authority writes, an old Product-writing binary is unsafe.

Preparing a manifest does not implement these steps. This increment stops at metadata/source compatibility and immutable, validated preparation.
