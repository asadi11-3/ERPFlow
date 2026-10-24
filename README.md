# ERPFlow

ERPFlow is a portfolio ERP workflow application built with a .NET 10 Minimal API, Entity Framework Core, SQL Server, and an Angular 22 client. Its implemented scope connects sales approval, warehouse reservation and dispatch, customer equipment, and service jobcards.

## Implemented Scope

### Sales Order Management

- Sales and Administrator users create draft orders from active customers and catalog products. Prices come from the server.
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
- Product row versions, database transactions, and uniqueness constraints protect concurrent stock changes and duplicate records.

### Equipment Dispatch And Delivery Notes

- A Technician or Administrator can dispatch one approved order in full, once.
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

All listed reads require authentication. This sample doesn't provide refresh tokens, token revocation, account administration, or SQL Server row-level security.

## API Surface

| Area | Operations |
| --- | --- |
| Authentication | `POST /api/auth/login` |
| Reference data | `GET /api/reference/customers`, `GET /api/reference/products` |
| Sales orders | list, detail, create, submit, pending queue, history, approve, reject, return under `/api/sales-orders` |
| Equipment | dispatch under `/api/equipment/sales-orders/{id}/dispatch`; list delivery notes; list and view machine assets |
| Service | list, view, and create jobcards; request parts; complete jobcards under `/api/service/jobcards` |

Errors use RFC 7807 Problem Details where a body is available. Invalid input returns `400`, authentication failures return `401`, authorization failures return `403`, missing records return `404`, and invalid or duplicate workflow transitions and concurrency conflicts return `409`.

## Angular Frontend

The Angular client in [`front/`](front) uses standalone components, lazy routes, signals, and authenticated API clients. The implemented pages are:

- `/` and `/login`
- `/orders`, `/orders/new`, and `/orders/:id`
- `/approvals` and `/approvals/:id`
- `/assets` and `/assets/:id`, including delivery-note and asset views
- `/service`, `/service/new`, and `/service/:id`, including parts requests and completion
- `/reference`

Navigation is filtered by role. Backend authorization remains authoritative. The client has no delivery-confirmation, jobcard-start, jobcard-cancel, partial-part, or unused-part return page because those API workflows don't exist.

## Solution Layout

```text
ERPFlow.Api             HTTP endpoints, JWT policies, OpenAPI, Problem Details
ERPFlow.Application     workflow orchestration and authorization
ERPFlow.Domain          entities, states, and business invariants
ERPFlow.Infrastructure  EF Core persistence, migrations, and development seeding
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
dotnet run --project ERPFlow.Api --launch-profile https
```

Development startup applies pending migrations. When `DemoUsers:Password` is set, an empty development database is seeded transactionally with six users: three Sales, one Approver, one Technician, and one Administrator. It also seeds customers, products, 15 sample orders, 23 sales-order reservation rows, approval history, three delivery notes for approved orders, one machine asset per dispatched ordered unit, and two service jobcards. One jobcard has a properly reserved part and the other is completed; active reservation accounting includes both sales-order reservations and incomplete jobcard items.

Those figures describe a fresh database only. Existing references and business records are preserved rather than overwritten, automatically reconciled, or destructively repaired, so a previously seeded or modified database can have different counts and may retain historical inconsistencies that require manual review.

Start the frontend separately:

```powershell
cd front
npm.cmd install
npm.cmd start
```

The default client expects the API at `https://localhost:7165` and runs at `http://localhost:4200`. Change `API_BASE_URL` in `front/src/app/core/config.ts` if needed.

## Migrations And Existing Data

Migrations add database constraints for workflow invariants, including a unique delivery-note `SalesOrderId`, unique jobcard product rows, row-version concurrency on products, and non-negative stock rules. Jobcards use a concurrency-checked `Revision` that advances on every parts request, work start, and completion, protecting completion from racing parts requests even for different products.

If an existing database already contains duplicate delivery notes for one sales order, the uniqueness migration can fail. Review and reconcile those records manually before applying it. ERPFlow doesn't run automated destructive cleanup or choose which duplicate business record to delete.

## Verification Commands

Run these from the repository root when validating a change:

```powershell
dotnet restore ERPFlow.slnx
dotnet build ERPFlow.slnx --no-restore
dotnet test ERPFlow.slnx --no-build
dotnet ef migrations has-pending-model-changes --project ERPFlow.Infrastructure --startup-project ERPFlow.Api
```

Frontend checks run from `front/`:

```powershell
npm.cmd install
npm.cmd run build
npm.cmd test -- --watch=false
npm.cmd run lint
```

SQL integration tests generate uniquely named test databases, apply migrations, exercise real persistence, and drop only the databases they created. They never reset or reuse the configured application database. Tests default to `.\SQLEXPRESS`; `ERPFLOW_TEST_SQLSERVER` overrides the server name. Windows integrated authentication needs permission to create and drop test databases.

## Limits

ERPFlow is a workflow-focused portfolio application, not a full ERP suite. Purchasing, invoicing, accounting, user administration, production deployment automation, delivery confirmation, partial dispatch, and the service-part return and cancellation workflows described above are outside the implemented scope.

See [`docs/FRONTEND_HANDOFF.md`](docs/FRONTEND_HANDOFF.md), [`docs/openapi/erpflow-v1.json`](docs/openapi/erpflow-v1.json), and [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) for supporting detail. The generated OpenAPI snapshot is checked against the running API in integration tests.
