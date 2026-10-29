# ERPFlow

ERPFlow is a portfolio ERP workflow application built with a .NET 10 Minimal API, Entity Framework Core, SQL Server, and an Angular 22 client. Its implemented scope connects sales approvals, warehouse inventory, procurement and receiving, equipment dispatch, service jobcards, and role-scoped analytics.

## Implemented Scope

### Sales Order Management

- Sales and Administrator users create draft orders from active customers and catalog products. Prices come from the server.
- Drafts, including returned orders, can be edited by their Sales owner or an Administrator using `PUT /api/sales-orders/{id}`. Send `customerId`, `items`, and the latest base64 `rowVersion` returned with the order. Edits reprice lines from the server catalog, retain approval history and closed reservations, and reserve no stock until submission. Missing or invalid versions return `400`; stale versions and edits outside Draft return `409`. Existing status values and create/submit request contracts are unchanged; `rowVersion` is an additive response field.
- A submitted order reserves its full requested quantity and enters `PendingApproval`.
- Approver and Administrator users can approve, reject, or return a pending order. Approval keeps its reservations. Rejection releases them. Return releases them and moves the order back to `Draft` for editing and resubmission.
- Approval history records submission and decisions. Sales users see only their own orders. Approvers and Administrators can read all orders.

```text
Draft -> PendingApproval -> Approved
   ^          |              |
   |          +-> Rejected   +-> Dispatch
   +---------- Return
```

### Inventory Reservation

- `AvailableStock = StockOnHand - ReservedQuantity`.
- Submission reserves stock without reducing `StockOnHand`.
- Rejection or return closes the reservation and frees only the reserved quantity.
- Dispatch is the shipment event. It reduces `StockOnHand` and `ReservedQuantity` by the same quantity, then marks the reservation released.
- `IsReleased` means the reservation is closed. It doesn't mean stock was necessarily returned: rejection and return free reserved stock, while dispatch consumes physical stock.
- Row versions, database transactions, and uniqueness constraints protect concurrent stock changes and duplicate records.

### Equipment Dispatch And Delivery Notes

- A Technician or Administrator can dispatch one approved order in full, once.
- Dispatch validates order state before serial generation and accepts at most 1,000 total units and 1,000 supplied serials per order. Checked, bounded quantity accumulation prevents allocation overflow. Approved orders exceeding this limit return `400` without writes; partial dispatch remains outside scope.
- Dispatch creates one delivery note and registers machine assets for the shipped order lines in the same transaction.
- The order keeps its existing `Approved` status; the delivery note and closed reservations record shipment, preserving the existing order-status contract.
- A repeat dispatch returns `409 Conflict`. For concurrent requests, one can succeed and the other returns `409`; neither creates duplicate stock movement or records. The database enforces one delivery note per `SalesOrderId`.
- Dispatch records shipment, not customer delivery confirmation. `DeliveredAt` remains unset, and there is no delivery-confirmation endpoint or workflow.
- `InstalledAt` is the existing asset registration timestamp. It doesn't verify that physical installation happened at the customer site.

### Service Jobcards And Parts

- Technician and Administrator users can create jobcards for customers and optionally link them to an asset owned by that customer.
- Parts requests require positive quantities, unique product IDs, valid products, and enough available stock.
- A `JobcardItem` stores the requested part and remains the reservation owner while the jobcard is `Open`, `PartsRequested`, or `InProgress`. Parts can also be requested while work is in progress.
- Completing a jobcard consumes all requested parts, reduces on-hand and reserved quantities equally, records completion details, and can update the linked asset meter reading.
- Meter readings cannot decrease. The persisted meter value is concurrency-checked so competing jobcards cannot silently overwrite a newer reading; a conflict rolls back the whole completion.
- Completion is accepted from `Open`, `PartsRequested`, or `InProgress`. There is currently no start-work endpoint, even though `InProgress` exists in the domain.
- `Completed` and `Cancelled` are terminal for parts requests and completion. Repeated completion returns `409 Conflict`.
- There is no unused-part, partial-consumption, cancellation, or explicit part-release endpoint or workflow.

### Procurement And Warehouse Operations

- Administrators manage warehouses; Purchasing users manage suppliers and their own purchase orders. Approvers decide purchase orders submitted by other users. Warehouse operators see cost-free operational purchase-order views.
- Warehouse operators and Administrators create and post goods receipts against purchase orders, including partial receipts, and transfer available stock between warehouses. Receiving and transfer posting require completed Warehouse inventory authority; neither falls back to Product balances.
- Authorized users can query bounded stock and movement history. Purchase orders do not change stock or catalog selling prices; posted receipts add stock and transfers move it between warehouses.

### Product Catalog And Analytics

- Administrators can create, edit, activate, and deactivate catalog products and manage their images. Authenticated users can read product details and images.
- Eight analytics endpoints cover sales, approvals, procurement, receiving, transfers, inventory, dispatch, and service. Reports are role-scoped and support time ranges and day/week/month grouping; inventory analytics require Warehouse authority.

## Authentication And Authorization

Login returns a signed JWT containing the persisted user ID and role. Passwords are hashed, signing keys and demo passwords aren't committed, and mutation workflows verify the current persisted user and role in addition to endpoint policy checks.

| Capability | Sales | Approver | Technician | Administrator |
| --- | :---: | :---: | :---: | :---: |
| Read reference data, assets, delivery notes, and jobcards | Yes | Yes | Yes | Yes |
| Read sales orders | Own | All | Authenticated view | All |
| Create and submit sales orders | Yes | No | No | Yes |
| Approve, reject, or return orders | No | Yes | No | Yes |
| Dispatch approved orders | No | No | Yes | Yes |
| Create jobcards, request parts, and complete jobcards | No | No | Yes | Yes |

Purchasing users manage suppliers and their own purchase orders. WarehouseOperator users post goods receipts and stock transfers and see cost-free operational data. Approvers can decide purchase orders; Administrators manage warehouses and products. Inventory queries are limited to Administrator, WarehouseOperator, Purchasing, and Approver roles. All listed reads require authentication. This sample doesn't provide refresh tokens, token revocation, account administration, or SQL Server row-level security.

## API Surface

| Area | Operations |
| --- | --- |
| Authentication | `POST /api/auth/login` |
| Reference data | `GET /api/reference/customers`, `GET /api/reference/products` |
| Product catalog and images | product detail and Administrator create/edit/activation under `/api/products`; authenticated image read and Administrator upload/delete under `/api/products/{id}/image` |
| Sales orders | list, detail, create, submit, pending queue, history, approve, reject, return under `/api/sales-orders` |
| Equipment | dispatch under `/api/equipment/sales-orders/{id}/dispatch`; list delivery notes; list and view machine assets |
| Service | list, view, and create jobcards; request parts; complete jobcards under `/api/service/jobcards` |
| Warehouses | list, view, create, rename, activate and commitment-safe deactivate under `/api/warehouses` |
| Suppliers | list/search, view, create, edit, activate and deactivate under `/api/suppliers` |
| Purchase orders | list, detail, Draft create/edit, submit, approve, reject, cancel and history under `/api/purchase-orders`; separately authorized `/operational` projections exclude commercial costs |
| Goods receipts | Draft create, list/detail, post and Draft cancellation under `/api/goods-receipts`; durable create/post replay and partial/full PO receiving |
| Stock transfers | create/list/detail, atomic post and Draft cancellation under `/api/stock-transfers`; `Idempotency-Key` headers for create/post |
| Operational inventory | filtered, bounded `/api/inventory/stocks` and `/api/inventory/movements`; source document references without purchasing costs |
| Analytics | eight role-scoped reports under `/api/analytics`: sales, approvals, procurement, receiving, transfers, inventory, dispatch, service |

Errors use RFC 7807 Problem Details where a body is available. Invalid input returns `400`, authentication failures return `401`, authorization failures return `403`, missing records return `404`, and invalid or duplicate workflow transitions and concurrency conflicts return `409`.

## Angular Frontend

The Angular client in [`front/`](front) uses standalone components, lazy routes, signals, and authenticated API clients. The implemented pages are:

- `/` and `/login`
- `/orders`, `/orders/new`, and `/orders/:id`
- `/orders/:id/edit` for owner-authorized Draft correction
- `/approvals` and `/approvals/:id`
- `/assets` and `/assets/:id`, including delivery-note and asset views
- `/service`, `/service/new`, and `/service/:id`, including parts requests and completion
- `/reference`, including Administrator product and image management
- `/warehouses`, `/suppliers`, `/purchase-orders`, `/receiving`, `/goods-receipts`, `/stock-transfers`, and `/inventory`, with relevant detail and create/edit routes
- `/analytics/sales-service`, `/analytics/purchasing`, and `/analytics/warehouse`

Navigation is filtered by role. Backend authorization remains authoritative. The client is an installable PWA; its offline cache covers the application shell and static assets, not API data or offline writes. The client has no delivery-confirmation, jobcard-start, jobcard-cancel, partial-part, or unused-part return page because those API workflows don't exist.

## Solution Layout

```text
ERPFlow.Api             HTTP endpoints, JWT policies, OpenAPI, Problem Details
ERPFlow.Application     workflow orchestration and authorization
ERPFlow.Domain          entities, states, and business invariants
ERPFlow.Infrastructure  EF Core persistence, migrations, and development seeding
ERPFlow.Operations      separate production migration, verification, first-admin, and cutover CLI
ERPFlow.Tests           domain and SQL Server integration tests
front                   Angular client
```

Dependencies point inward from API and Infrastructure through Application to Domain.

## Local Setup

Prerequisites are the .NET 10 SDK, Node.js for the Angular client, and a reachable SQL Server instance. Set `ConnectionStrings:ERPFlowDb` for your SQL Server rather than relying on a machine-specific host name. Keep the JWT signing key and optional demo-user password in User Secrets or another secret provider.

```powershell
dotnet restore ERPFlow.slnx
dotnet user-secrets set --project ERPFlow.Api "Jwt:SigningKey" "<at-least-32-random-characters>"
dotnet user-secrets set --project ERPFlow.Api "DemoUsers:Password" "<development-only-password>"
dotnet user-secrets set --project ERPFlow.Api "DemoUsers:EnableAutomaticSeed" "true"
dotnet run --project ERPFlow.Api --launch-profile https
```

Development startup applies pending migrations. When `DemoUsers:EnableAutomaticSeed` is true and `DemoUsers:Password` is set, an empty development database is seeded transactionally with six users: three Sales, one Approver, one Technician, and one Administrator. It also seeds customers, products, 15 sample orders, 23 sales-order reservation rows, approval history, three delivery notes for approved orders, one machine asset per dispatched ordered unit, and two service jobcards. One jobcard has a properly reserved part and the other is completed; active reservation accounting includes both sales-order reservations and incomplete jobcard items.

Those figures describe a fresh database only. Existing references and business records are preserved rather than overwritten, automatically reconciled, or destructively repaired, so a previously seeded or modified database can have different counts and may retain historical inconsistencies that require manual review.

Warehouse posting requires a completed inventory cutover. For an existing Development database, stop other writers and run `dotnet run --project ERPFlow.Api --launch-profile https -- --activate-warehouse-inventory`, then start the API normally. See [Development warehouse activation](docs/development-inventory.md) for prerequisites and reconciliation behavior. This command is Development-only; Production uses the separate operator CLI.

Start the frontend separately:

```powershell
cd front
npm.cmd install
npm.cmd start
```

The default client expects the API at `https://localhost:7165` and runs at `http://localhost:4200`. Change `API_BASE_URL` in `front/src/app/core/config.ts` if needed.

## Migrations And Existing Data

The additive KDT Phase 1–2C migrations introduced WarehouseStock, inventory ownership, source preparation, and a reconciled Product-to-Warehouse authority cutover. Databases start in Product mode until explicitly activated; Warehouse posting never silently falls back to Product balances. See [Phase 1](docs/inventory-foundation-phase1.md), [Phase 2A/2B](docs/inventory-source-preparation-phase2ab.md), and [Phase 2C](docs/inventory-authority-cutover-phase2c.md) for migration history and cutover rules. Old Product balances are not removed, and destructive downgrade is prohibited.

KDT Phase 3 added warehouse, supplier, and purchase-order workflows; Phase 4 added partial goods receiving and replay-safe posting; Phase 5 added conserved stock transfers. Their API operations and Angular pages are available, subject to role checks and completed Warehouse authority for inventory writes.

Production startup validates schema, inventory authority, and private image storage; it does not apply migrations, seed demo data, or perform cutover. `ERPFlow.Operations` provides explicit `migrate`, `verify`, `first-admin`, `prepare-cutover`, and `complete-cutover` commands for offline administration. A Product-authority database cannot post Warehouse inventory and reports `/health/ready` as `503` until cutover and verification succeed. See [production deployment and operator procedure](docs/monsterasp-deployment.md). Local release artifacts have been verified; hosted API setup and public release still require the prerequisites in that guide.

Migrations add database constraints for workflow invariants, including a unique delivery-note `SalesOrderId`, unique jobcard product rows, row-version concurrency on products, and non-negative stock rules. Jobcards use a concurrency-checked `Revision` that advances on every parts request, work start, and completion, protecting completion from racing parts requests even for different products.

If an existing database already contains duplicate delivery notes for one sales order, the uniqueness migration can fail. Review and reconcile those records manually before applying it. ERPFlow doesn't run automated destructive cleanup or choose which duplicate business record to delete.

## Verification Commands

Run these from the repository root when validating a change:

```powershell
dotnet restore ERPFlow.slnx
dotnet build ERPFlow.slnx --no-restore
$env:ERPFLOW_TEST_SQLSERVER = '(localdb)\MSSQLLocalDB'
dotnet test ERPFlow.slnx --no-build
```

The migration contract test compares the current EF model with its snapshot without opening a database connection or starting the API. Development API startup applies migrations; do not use application startup or an unqualified EF startup-project command for an isolated cutover rehearsal.

Frontend checks run from `front/`:

```powershell
npm.cmd install
npm.cmd run build
npm.cmd test -- --watch=false
npm.cmd run lint
```

SQL integration tests generate uniquely named test databases, apply migrations, exercise real persistence, and drop only the databases they created. They never reset or reuse the configured application database. Tests default to `.\SQLEXPRESS`; `ERPFLOW_TEST_SQLSERVER` overrides the server name. Windows integrated authentication needs permission to create and drop test databases.

## Limits

ERPFlow is a workflow-focused portfolio application, not a full ERP suite. Posted receipt/transfer reversals, stock taking/adjustment/reorder, supplier returns, invoicing, accounting, user administration, delivery confirmation, partial dispatch, and service-part return/cancellation remain outside the implemented scope.

See [`front/README.md`](front/README.md), [`docs/analytics.md`](docs/analytics.md), and [`docs/openapi/erpflow-v1.json`](docs/openapi/erpflow-v1.json) for supporting detail. The generated OpenAPI snapshot is checked against the running API in integration tests.
