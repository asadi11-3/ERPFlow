# KDT Phase 3 — warehouse/supplier masters and purchase orders

## Scope and inventory authority

Baseline: `cccb824` on `feature/kdt-inventory-procurement`. Phase 3 adds backend master management and purchase-order approval only. It does not add receiving, GRNs, transfers, adjustments or Angular screens.

Product remains the configured application's inventory authority. `ProductAuthorityWriteGate` and `DisabledInventoryCutoverPermission` remain unchanged in production DI. Procurement has no `IInventoryPosting` dependency, creates no WarehouseStock rows, reservations, openings or movements, and never changes Product balances or selling prices. New warehouses are master data only. The configured database was not accessed or migrated. The pre-existing development settings modification is preserved (Git blob hash `4134a04f650bef6e25fdad549896014d333bb402`). No commit or push was performed.

## Domain model and state machine

- Reused `Warehouse`: immutable normalized code, editable name, active flag, creation time and SQL rowversion. This phase supports name as its editable metadata. No deletion endpoint is provided. EF forbids ordinary deletion; restrictive FKs protect referenced rows. The fixed legacy default ID/code is protected from deactivation, code changes and deletion by domain/application/SQL protections.
- New `Supplier`: ID, immutable normalized SupplierCode, name, optional contact/email/phone/address, active flag, creation time and SQL rowversion. Code immutability is enforced in EF and SQL while metadata edits remain supported. History is retained by soft deactivation.
- New `PurchaseOrder`: immutable ID/number/creator/time, one supplier and destination warehouse, notes, calculated total, state/timestamps, rowversion and monotonic revision.
- New `PurchaseOrderItem`: unique product per parent, positive Int32 quantity, nonnegative four-decimal purchase cost. It never changes `Product.UnitPrice`.
- New `PurchaseOrderHistory`: immutable actor/action/from-state/to-state/comments/revision/time, with restrictive actor and parent FKs and append-only EF/SQL protections.

```text
Draft --submit--> PendingApproval --approve--> Approved
                       |                         |
                       +--reject--> Rejected     +--cancel before receiving--> Cancelled
Draft / PendingApproval --cancel-------------------------------------------> Cancelled
```

Rejected and Cancelled are terminal in Phase 3. There is no resubmission or return workflow for POs. Sales approval semantics remain separate.

`PartiallyReceived=5`, `Received=6`, `ShortClosed=7` are represented for future compatibility; there is no Phase 3 operation that enters them. `ReceivingStartedAt` is a future receiving-admission marker, not a receipt implementation. Phase 3's ordinary EF paths cannot set it. Cancellation rejects any receiving marker or receiving-state PO. Phase 4 must atomically fence/touch the PO parent and establish that marker before creating any dependent receiving operation, including operations later voided; it must never implement a check-then-insert receipt outside the parent transaction.

### Bounds and calculations

- All lists: page >= 1, pageSize 1–100, default 20; deterministic ordering and wide offset arithmetic.
- Warehouse: code 1–32, name 1–200; trim and uppercase code. Identity is absent from the update contract.
- Supplier: ASCII letter/digit/hyphen/underscore code 1–32, name 1–200, contact 150, email 320, phone 50, address 1000. Optional blank values normalize to null; email is parsed and display-name forms are rejected. Text fields reject control characters.
- PO: 1–100 distinct products, positive Int32 quantities, unit cost `0..999999999999.9999` with at most four fractional digits, notes/comments/reasons <= 1000. Rejection/cancellation require a nonblank reason.
- Checked decimal multiplication and accumulation calculate totals on the server. UnitCost uses `decimal(16,4)` and total uses `decimal(28,4)`, covering the maximum permitted 100-line document without Int32 multiplication overflow or SQL rounding differences.
- PONumber uses `PO-yyyyMMdd-<full GUID>` with a database unique index. Numbers are immutable and non-sequential; no gap-free sequencing promise is made.

## Authorization and ownership

Existing roles remain Administrator=0, Sales=1, Approver=2, Technician=3. New roles are Purchasing=4 and WarehouseOperator=5. JWT role claims retain their existing string-name behavior. Angular's role union recognizes the two additions; no procurement UI is introduced.

| Operation | Administrator | Purchasing | Approver | WarehouseOperator | Sales / Technician |
| --- | --- | --- | --- | --- | --- |
| Read warehouse master | Yes | Yes | Yes | Yes | Yes |
| Change warehouse master | Yes | No | No | No | No |
| Read suppliers | Yes | Yes | Yes | No | No |
| Change suppliers | Yes | Yes | No | No | No |
| Read commercial POs/history | All | Own | All | No | No |
| Create/edit/submit PO | Yes | Own | No | No | No |
| Approve/reject PO | Other users' | No | Other users' | No | No |
| Cancel Draft/Pending PO | Yes | Own | No | No | No |
| Cancel unreceived Approved PO | Yes | No | No | No | No |
| Read operational projection | All | Own | All | Approved/receiving-state only | No |

Policies enforce JWT roles before handlers. Every application workflow independently checks the current persisted user/role, so a role changed after token issuance cannot retain mutation rights. Purchasing ownership applies to detail, list, history and operational reads as well as mutations. New roles do not inherit Sales detail/history access; the existing four Sales readers keep their permissions.

WarehouseOperator cannot call commercial list/detail/history or supplier endpoints. Operational DTOs contain only PO ID/number, supplier ID, warehouse ID, status, created time and product quantities. They contain no costs, totals, notes, approval comments or supplier contact information.

## API contracts

All routes are authenticated and documented in `docs/openapi/erpflow-v1.json`.

| Routes | Contracts |
| --- | --- |
| `GET /api/warehouses[?page&pageSize&active]`, `GET /api/warehouses/{id}` | WarehouseResult / PageResult |
| `POST /api/warehouses` | `{ code, name }` -> 201 WarehouseResult |
| `PUT /api/warehouses/{id}` | `{ name, rowVersion }` |
| `POST /api/warehouses/{id}/activate` or `/deactivate` | `{ rowVersion }` |
| `GET /api/suppliers[?page&pageSize&search&active]`, `GET /api/suppliers/{id}` | SupplierResult / PageResult |
| `POST /api/suppliers` | `{ supplierCode, name, contactName?, email?, phone?, address? }` -> 201 |
| `PUT /api/suppliers/{id}` | `{ name, contactName, email, phone, address, rowVersion }` |
| `POST /api/suppliers/{id}/activate` or `/deactivate` | `{ rowVersion }` |
| `GET /api/purchase-orders[?page&pageSize&status]`, `GET /api/purchase-orders/{id}` | Commercial PurchaseOrderResult / PageResult |
| `POST /api/purchase-orders` | `{ supplierId, warehouseId, items: [{ productId, quantity, unitCost }], notes? }` -> 201 |
| `PUT /api/purchase-orders/{id}` | Same commercial fields plus `rowVersion`; Draft only |
| `POST /api/purchase-orders/{id}/submit` | `{ rowVersion }` |
| `POST /api/purchase-orders/{id}/approve` | `{ rowVersion, comments? }` |
| `POST /api/purchase-orders/{id}/reject` or `/cancel` | `{ rowVersion, reason }` |
| `GET /api/purchase-orders/{id}/history` | Ordered immutable commercial audit projection |
| `GET /api/purchase-orders/operational[?page&pageSize&status]`, `GET /api/purchase-orders/operational/{id}` | Cost-free operational DTOs, separately authorized |

RowVersion is an eight-byte SQL token encoded as base64 in JSON. Edits and transitions require the last returned token; state changes and child-only edits advance the parent revision/rowversion. Invalid input/token shape -> 400; unauthenticated -> 401; forbidden role/ownership/self-decision -> 403; missing resource -> 404; stale token/invalid state/inactive source/duplicate identity/stranded commitments -> 409. Errors use the existing ProblemDetails handler. There are no master/PO DELETE routes.

## Transactions and concurrency

All writes use the same scoped DbContext and existing `IApplicationTransaction`; no side connection bypasses the unit of work. Procurement rejects pre-existing pending caller changes and refuses saving unrelated/inventory entities. It clears unchanged tracked state when beginning a new operation so repeated use of a context cannot reuse a stale active master.

PO operations take the shared inventory-authority fence. Active warehouse and supplier reads use `UPDLOCK,HOLDLOCK` on that same connection/transaction. Create, edit, submit and approval retain these source locks through commit; inactive suppliers/warehouses cannot be admitted on stale reads. Cancellation/rejection can resolve commitments even when a supplier has since become inactive.

Warehouse changes take the **exclusive** authority fence in a Serializable transaction. Deactivation checks nonzero stock, active explicit/legacy reservations, Draft/Pending/undispatched Approved Sales lines, active Service parts, and Draft/Pending/Approved/PartiallyReceived POs. Checks and active-state update share the transaction; serialized predicate reads and the fence prevent a participating writer from inserting a commitment between validation and commit. The legacy default remains protected even if its quantities are zero. Zero stock rows and closed historical documents may remain attached to a deactivated master; their FKs still prevent deletion.

Optimistic parent versions arbitrate edits/submissions/decisions. One contender commits; stale contenders return 409. Supplier/warehouse locks prevent source admission/deactivation races. Ordinary direct line changes require a Draft parent revision update; submitted commercial lines cannot be changed through workflows, EF saves or direct line DML. SQL deadlock/lock-admission conflicts are translated into workflow conflicts at the persistence boundary; callers retry the complete operation with a fresh context/token.

State transitions additionally use `dbo.TransitionPurchaseOrder` on the **same** DbContext connection and caller-owned workflow transaction. The procedure locks/version-checks the parent, verifies persisted actor permissions/ownership/non-self decision, permitted state edge, source eligibility and receiving admission, then inserts the event and updates state/revision/timestamps together. Audit insertion requires the scoped event marker and an actual transaction-owned authority fence, and its trigger independently checks persisted actor role/ownership/non-self decision and parent revision. The procedure clears the marker on success and failure. The PO trigger requires a matching legal audit event, revision progression and event timestamps; direct Draft-to-Approved, unaudited Pending cancellation and unaudited submitted metadata changes are rejected. EF cannot add audit events outside this transition boundary. A failed transition must roll back the entire outer transaction; no catch-and-continue is supported. This is ordinary application/database integrity enforcement, not cryptographic application identity or protection against a privileged principal deliberately impersonating an authorized actor or disabling guards.

Draft product replacement explicitly deletes removed Draft lines before saving the revised aggregate, retaining restrictive database FKs and the parent concurrency fence. It does not depend on cascade deletion or permit submitted-line changes.

## Additive migration

`20261026154834_ProcurementPhase3` creates only Suppliers, PurchaseOrders, PurchaseOrderItems and PurchaseOrderHistories, indexes, restrictive FKs/checks, five protective triggers and one atomic transition procedure. It creates no stock data, backfill, users or role assignments. Existing role numbers require no data rewrite. Warehouse's existing unique code index is reused.

Database protections include unique SupplierCode/PONumber/PO-product keys, positive quantities/revisions, bounded nonnegative costs/totals, valid status/action pairs and restrictive history/master/product/user relationships. New triggers protect supplier codes, warehouse identity/default activity, PO identity/submitted terms, submitted lines and append-only history. OUTPUT is disabled in EF mappings for trigger-bearing tables, including Suppliers. Destructive Down is refused. Earlier migrations and inventory guard definitions are unchanged. This Phase 3 migration is unpublished and has been exercised only on disposable fixtures; after publication its guard definitions must be frozen and extended through new migrations.

An isolated upgrade from the Phase 2C schema verifies two migration applications preserve Product 50/6 and rowversion, existing warehouse rowversions and active Sales ownership while adding empty procurement tables. An offline check compares the model/snapshot and inspects additive operations without opening a database connection.

## Changed-file inventory

- Domain: `Enums/UserRole.cs`, `Inventory/Warehouse.cs`, new `Procurement/Supplier.cs`, `Procurement/PurchaseOrder.cs`.
- Application: new `Abstractions/IProcurementPersistence.cs`; new `Procurement/ProcurementContracts.cs`, `ProcurementAccess.cs`, `ProcurementMasters.cs`, `PurchaseOrderWorkflow.cs`; SalesOrderWorkflow's existing-role reader admission.
- Infrastructure: `DependencyInjection.cs`, `ERPFlowDbContext.cs`, two additional procurement-validation calls in `ERPFlowDbContext.Inventory.cs`; new `ERPFlowDbContext.Procurement.cs`, `EfProcurementPersistence.cs`, `ProcurementSqlGuards.cs`, `Configurations/ProcurementConfiguration.cs`; existing WarehouseConfiguration OUTPUT mapping; new migration/designer and updated model snapshot.
- API: `ApiEndpoints.cs`, `Program.cs`, new `ProcurementEndpoints.cs`.
- Tests: new `ProcurementDomainTests.cs`, `ProcurementIntegrationTests.cs`, `ProcurementConcurrencyTests.cs`, `ProcurementApiTests.cs`, `ProcurementApiTestSupport.cs`, `ProcurementMigrationTests.cs`, `ProcurementDraftEditingTests.cs`, `ProcurementTestSupport.cs`.
- Compatibility/docs: Angular `core/auth/session.ts` role union, OpenAPI snapshot, README and this document. The development settings edit is pre-existing and excluded.

## Recorded verification and real HTTP QA

The first focused run passed 59 new domain/API/SQL/concurrency cases; six isolated migration/constraint cases passed separately. Subsequent tests add edit-versus-submit, HTTP rejection/cancellation, direct SQL state/audit rejection, forged unauthorized audit rejection, transition-failure rollback and removed-product Draft editing coverage. The SQL state bypass and Draft orphan deletion were each reproduced with failing tests before their fixes. All database tests use unique fixture-created catalogs on `(localdb)\MSSQLLocalDB`, never the configured application database.

Recorded checks: `dotnet restore ERPFlow.slnx` PASS; `dotnet build ERPFlow.slnx` PASS with 0 warnings/errors; explicit non-Development EF pending-model check PASS; OpenAPI regeneration/comparison from an isolated host PASS; Angular build, 24/24 tests and lint PASS. The EF CLI reports its existing 10.0.11/10.0.13 version warning; no tool/package upgrade was performed.

Direct HTTP evidence: `C:\Users\User\AppData\Local\Temp\opencode\ERPFlow-Phase3-http-qa.log`. The final re-run after SQL transition and Draft replacement hardening used a temporary Kestrel process with an explicit Production-environment override pointing only at owned catalog `ERPFlow_Phase3_HttpQA_bdca2173de4d44a9945c07184a030803`, not the configured database. The driver performed actual logins for all four procurement roles, master creation, PO creation/Draft product replacement/submission/approval, malformed-token rejection, cost-redacted operational reads, commercial access rejection, warehouse commitment rejection, cancellation and successful deactivation. It verified original Product 25/0 and selling price 100 plus replacement Product 10/0 and selling price 77 remained unchanged and no WarehouseStocks/postings appeared. Host and owned catalog were removed afterward.

| QA scenario | Expected/observed | Evidence |
| --- | --- | --- |
| Anonymous procurement read | 401 | HTTP log |
| Admin warehouse / Purchasing supplier and PO creation | 201 | HTTP log |
| Server commercial calculation | 3 x 2.5 = 7.5 | HTTP log |
| Draft product replacement | PUT 200; exact replacement product and total 4 x 1.875 = 7.5 | HTTP log |
| Bad concurrency token / valid submit and approval | 400 / 200 / 200 | HTTP log |
| WarehouseOperator commercial / operational reads | 403 / no cost fields | HTTP log |
| Warehouse deactivation with Approved PO | 409 | HTTP log |
| Cancel before receipt, then deactivate | 200 / 200 | HTTP log |
| Inventory neutrality and cleanup | Unchanged Product/pricing; no stock creation; cleanup completed | HTTP log |

### Final regression results

| Check | Result |
| --- | --- |
| `dotnet restore ERPFlow.slnx` | PASS |
| `dotnet build ERPFlow.slnx` | PASS, 0 warnings / 0 errors |
| Full isolated `dotnet test ERPFlow.slnx --no-build` | **PASS, 281/281**, 0 failed / 0 skipped, 5m31s |
| Baseline compatibility | All 209 existing tests retained and passing; 72 new Phase 3 cases |
| EF CLI pending-model check | PASS, no pending changes; non-Development, unreachable design-only connection |
| OpenAPI snapshot | Regenerated from isolated host; comparison passes in full suite |
| Angular build / unit tests / lint | PASS / **24/24** / PASS |
| Final real HTTP QA | PASS, including Draft line replacement and atomic SQL approval/cancellation |
| Whitespace / development-settings preservation | PASS; expected LF/CRLF notices only; settings hash unchanged |

Final backend artifacts: `C:\Users\User\AppData\Local\Temp\opencode\ERPFlow-Phase3-approved.trx` and adjacent `.stdout.log` / `.stderr.log`. Earlier 276/277/279/280-test runs are intermediate evidence, not the final result. A transient build collision with a running testhost was resolved by waiting for that run and using isolated build artifacts for the edge reproduction; final build has no warnings/errors. Fresh LSP diagnostics timed out; compiler and executable API/SQL tests provide verification. No failing regression was removed or weakened.

The SQL fault-injection case throws after audit insertion during the parent update, verifies unchanged Draft state/rowversion and zero audit rows after rollback, then successfully retries. The direct-DML cases reject skipped submission, unaudited cancellation, unaudited revision/timestamp mutation and an unauthorized forged event even with an event marker/fence. The Draft replacement case first reproduced EF's required-FK orphan error, then passed with explicit Draft-line deletion and preserved document identity.

An additional independent-review finding reproduced a direct SQL supplier-code change. The new supplier identity trigger rejects it with 51010. Trigger-compatible Supplier mapping and a matching snapshot/designer preserve normal metadata edits and rowversion advancement; the focused code/metadata/lifecycle/model checks pass. A separate real HTTP recheck (`C:\Users\User\AppData\Local\Temp\opencode\ERPFlow-Phase3-supplier-http-qa.log`) verifies Purchasing login, Supplier create201/edit200, unchanged code with advanced version, SQL rename rejection and owned host/catalog cleanup. It supplements the unchanged broader HTTP matrix above.

### Independent gate review

Final re-review of the dedicated locked review worktree returned **APPROVE — High confidence**, with **no remaining goal-linked blockers**. The reviewer independently inspected the supplier trigger, trigger-compatible mapping, designer/snapshot, migration hookup, raw-SQL regression and completed 281-test/HTTP artifacts. Earlier SQL transition/audit findings and the SupplierCode finding were fixed before this verdict; the Draft replacement defect was independently reproduced and fixed during implementation.

| Review lane | Final verdict |
| --- | --- |
| Hands-on isolated HTTP QA, including affected Supplier recheck | PASS |
| Independent code/security/constraint/delta review | APPROVE, High confidence |
| Clean Architecture dependency review | PASS; Domain stays independent, Application uses Domain/ports, Infrastructure implements ports, API delegates to workflows |

No dependency/package changes, SQL credential/grant changes or production activation were introduced. Temporary review/QA workspaces are removed after the reviewer finishes; verification logs/TRX are retained in the approved temporary evidence directory.

## Phase 4 boundary

Phase 3 is a procurement commitment/approval foundation, not permission to receive goods. Phase 4 must implement receiving idempotency, receipt/parent concurrency, exact warehouse sources, supplier-document rules and partial/complete receipt state transitions in one transaction. It must additively extend the SQL transition/audit protocol for receiving actions while preserving cancellation fencing and commercial cost visibility. Production runtime grants for the new procedure/tables require deployment review; no SQL credentials/grants were changed here.

**GO for separately scoped Phase 4 development and isolated rehearsal: the Phase 3 regression and independent-review gates passed. NO-GO for production GRN posting or live WarehouseStock activation.** The Phase 2C deployment, physical attribution and all-reader/all-writer authority prerequisites still apply; a PO approval is never an inventory-authority switch. No Phase 4 implementation is included in this increment.
