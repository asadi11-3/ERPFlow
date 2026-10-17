# ERPFlow

ERPFlow is a focused sales-order and approval API built with .NET 10, ASP.NET Core Minimal APIs, Entity Framework Core, and SQL Server. It demonstrates how domain rules, application orchestration, persistence, authentication, and HTTP concerns can be separated without hiding the business workflow behind framework code.

## 1. Project Overview

The implemented MVP supports this lifecycle:

```text
Draft -> PendingApproval -> Approved
                         -> Rejected
```

A Sales user creates a draft and submits it. Submission reserves stock and records an audit entry. An Approver decides the pending order; approval retains the reservation, while rejection releases it exactly once. Administrators can perform both sets of operations.

## 2. Why This Project Exists

ERP systems are shaped by state transitions, authorization, consistency, and auditability rather than CRUD alone. ERPFlow keeps the scope intentionally small so those concerns are visible and testable:

- State changes are enforced by the domain model.
- Prices and actor identities come from trusted server-side data.
- Multi-record changes are committed transactionally.
- Conflicting writes are detected instead of silently overwriting data.
- Authentication, role policies, ownership rules, and HTTP errors have distinct responsibilities.

## 3. Implemented Capabilities

- JWT login with hashed development-user passwords.
- Sales, Approver, and Administrator roles.
- Active-customer and product reference endpoints.
- Draft order creation using persisted product prices.
- Submission with stock validation and reservation.
- Pending-approval queue.
- Approval and rejection with immutable audit records.
- Chronological approval-history retrieval with actor and remarks.
- Stock release on rejection.
- Sales-user ownership enforcement.
- RFC 7807-style Problem Details responses.
- OpenAPI document and Swagger UI in Development.
- EF Core migrations, optimistic concurrency, constraints, and indexes.
- Domain, SQL Server integration, migration-upgrade, and HTTP integration tests.

## 4. Architecture

The solution follows Clean Architecture dependency direction:

```text
ERPFlow.Api -> ERPFlow.Application -> ERPFlow.Domain
     |                 ^
     +-> ERPFlow.Infrastructure ---+
```

| Project | Responsibility |
| --- | --- |
| `ERPFlow.Domain` | Entities, value-preserving behavior, order states, stock rules, and role/action enums |
| `ERPFlow.Application` | Use-case orchestration, ports, authorization checks, transactions, and result contracts |
| `ERPFlow.Infrastructure` | EF Core SQL Server persistence, migrations, queries, password verification, and development seeding |
| `ERPFlow.Api` | Minimal API endpoints, JWT validation, policies, Swagger, and Problem Details mapping |
| `ERPFlow.Tests` | Domain tests plus real SQL Server and in-process HTTP integration tests |

The Domain has no dependency on EF Core or ASP.NET Core. Application depends on abstractions, and Infrastructure supplies their implementations.

## 5. Domain Model

The main aggregate is `SalesOrder`, with `SalesOrderItem` and `StockReservation` children. Supporting entities are `Customer`, `Product`, `AppUser`, and `ApprovalHistory`.

Important invariants include:

- New orders start in `Draft`.
- An order requires a valid customer, creator, and at least one positive-quantity item before submission.
- Only draft orders can be edited or submitted.
- Submission succeeds only when all requested products exist and stock is available.
- Only pending orders can be approved or rejected.
- Rejection requires a reason and releases each active reservation once.
- Product prices are copied from persistence into order lines; clients cannot set prices.

## 6. Workflow And Consistency

Create, submit, approve, and reject each run inside an explicit database transaction. EF Core row-version concurrency tokens protect mutable order and product records. Database constraints reinforce key assumptions, including positive quantities, non-negative prices and stock, unique product lines per order, and unique product reservations per order.

Application exceptions distinguish validation, missing resources, forbidden access, workflow conflicts, concurrency conflicts, and duplicate reservations. The API translates those outcomes into stable HTTP responses.

## 7. Security Model

JWTs are signed with HMAC SHA-256 and validated for issuer, audience, lifetime, and signing key. Tokens contain the user ID in `sub` and the current role in the `role` claim. The signing key is intentionally absent from committed configuration and must contain at least 32 characters.

Authorization is defense in depth:

- Endpoint policies reject callers without an accepted role.
- Application workflow checks repeat role requirements independently of HTTP.
- Sales users can read and submit only orders they created.
- Actor IDs and roles are resolved from validated claims and persisted users, never request bodies.
- Passwords are stored with ASP.NET Core `PasswordHasher`, not as plaintext.

This sample does not implement refresh tokens, token revocation, account administration, or SQL Server row-level security.

## 8. API Surface

| Method | Route | Access | Success |
| --- | --- | --- | --- |
| `POST` | `/api/auth/login` | Anonymous | `200 OK` |
| `GET` | `/api/reference/customers` | Authenticated | `200 OK` |
| `GET` | `/api/reference/products` | Authenticated | `200 OK` |
| `POST` | `/api/sales-orders` | Sales, Administrator | `201 Created` |
| `GET` | `/api/sales-orders/{id}` | Authenticated; Sales ownership applies | `200 OK` |
| `GET` | `/api/sales-orders/{id}/history` | Authenticated; Sales ownership applies | `200 OK` |
| `POST` | `/api/sales-orders/{id}/submit` | Sales, Administrator; Sales ownership applies | `200 OK` |
| `GET` | `/api/sales-orders/pending` | Approver, Administrator | `200 OK` |
| `POST` | `/api/sales-orders/{id}/approve` | Approver, Administrator | `200 OK` |
| `POST` | `/api/sales-orders/{id}/reject` | Approver, Administrator | `200 OK` |

Approval history is returned oldest first. Approver and Administrator users can read any existing order's history; Sales users can read only history for orders they created. Missing authentication returns `401`, an ownership violation returns `403`, and an unknown order returns `404`.

```json
[
  {
    "action": 2,
    "actorId": "11111111-1111-1111-1111-111111111111",
    "timestampUtc": "2026-10-17T12:00:00Z",
    "remarks": "Submitted for approval."
  },
  {
    "action": 0,
    "actorId": "22222222-2222-2222-2222-222222222222",
    "timestampUtc": "2026-10-17T12:05:00Z",
    "remarks": "Approved after review."
  }
]
```

`action` uses the existing numeric `ApprovalAction` values: Approved `0`, Rejected `1`, and Submitted `2`. `remarks` is nullable and contains the persisted submission remark, approval comments, or rejection reason.

## 9. Error Contract

Errors use `application/problem+json` where a response body is available.

| Status | Meaning |
| --- | --- |
| `400` | Invalid input or malformed request |
| `401` | Missing, invalid, expired, or rejected credentials |
| `403` | Authenticated caller lacks the required role or ownership |
| `404` | Requested entity was not found |
| `409` | Invalid workflow transition, stock/concurrency conflict, or duplicate reservation |
| `500` | Unexpected server failure with internal details suppressed |

## 10. Technology Stack

- .NET SDK 10.0 / C# / ASP.NET Core Minimal APIs
- Entity Framework Core 10 with SQL Server
- ASP.NET Core JWT bearer authentication and Identity password hashing
- Built-in OpenAPI generation with Swashbuckle Swagger UI
- xUnit and `Microsoft.AspNetCore.Mvc.Testing`
- SQL Server-backed integration tests

## 11. Getting Started

Prerequisites are the .NET 10 SDK and a reachable SQL Server instance. The committed Development connection string targets `DESKTOP-P8A4TV4\SQLEXPRESS`; change `ConnectionStrings:ERPFlowDb` for your environment.

```powershell
dotnet restore ERPFlow.slnx
dotnet user-secrets set --project ERPFlow.Api "Jwt:SigningKey" "replace-with-at-least-32-random-characters"
dotnet user-secrets set --project ERPFlow.Api "DemoUsers:Password" "replace-with-a-development-only-password"
dotnet run --project ERPFlow.Api --launch-profile https
```

In Development, startup applies pending migrations. Supplying the `DemoUsers:Password` secret enables the demo dataset below. Open `https://localhost:7165/swagger` after trusting the local development certificate if necessary.

### Development Demo Dataset

| Entity | Initial empty-database count |
| --- | ---: |
| Users | 5 (3 Sales, 1 Approver, 1 Administrator) |
| Customers | 10 (8 active, 2 inactive) |
| Products | 20 |
| Sales orders | 15 (4 Draft, 5 PendingApproval, 3 Approved, 3 Rejected) |
| Order items | 30 |
| Stock reservations | 23 (17 active, 6 released) |
| Approval history | 17 (11 Submitted, 3 Approved, 3 Rejected) |

Accounts are `sales@erpflow.local`, `sales2@erpflow.local`, and `sales3@erpflow.local` (Sales), `approver@erpflow.local` (Approver), and `admin@erpflow.local` (Administrator). New accounts use the configured development secret through the existing password hasher; existing hashes are never replaced.

The catalog includes warehouse, packing, and safety supplies, an out-of-stock scanner battery (`BATT-100`), and a low-stock dispenser (`DISP-002`, stock 2). Orders are spread equally across Sales users and use Domain transitions with persisted price snapshots. Pending and approved orders retain reservations; rejected orders release them.

Seeding is Development-only and transactional. Customer email, product SKU, and user email identify reference records; deterministic namespaced GUIDs identify the 15 orders. Repeated startup skips existing orders, including ones subsequently changed by users. Existing references are reused without overwriting fields. Incompatible roles, inactive customers, insufficient stock, or inconsistent reservation totals cause affected missing scenarios to be skipped with a warning, not repaired destructively. Counts therefore describe a fresh database, not a guarantee for previously modified databases. Concurrent changes are protected by transactions, unique constraints, and rowversions; a conflict rolls back the seed transaction rather than overwriting data.

Use Swagger reference endpoints for dropdown data, sign in as Approver for the pending queue, and read order detail/history by ID. There is no all-orders endpoint; see the integration guide for a read-only SQL query to discover Draft, Approved, and Rejected IDs.

## 12. Configuration

| Key | Required | Default or behavior |
| --- | --- | --- |
| `ConnectionStrings:ERPFlowDb` | Yes | Development file targets local SQL Server Express |
| `Jwt:Issuer` | Yes | `ERPFlow` |
| `Jwt:Audience` | Yes | `ERPFlow.Api` |
| `Jwt:SigningKey` | Yes | No committed value; minimum 32 characters |
| `Jwt:ExpirationMinutes` | No | `60`; accepted range is 1-1440 |
| `DemoUsers:Password` | No | Enables Development demo-data seeding when non-empty |
| `ERPFLOW_TEST_SQLSERVER` | No | Overrides the SQL Server used by integration tests |

Keep signing keys and passwords out of source control. User Secrets are appropriate for local development; production should use a managed secret provider and environment-specific configuration.

## 13. Testing And Verification

```powershell
dotnet build ERPFlow.slnx
dotnet test ERPFlow.slnx --no-restore
dotnet ef migrations has-pending-model-changes --project ERPFlow.Infrastructure --startup-project ERPFlow.Api
dotnet list ERPFlow.slnx package --vulnerable --include-transitive
```

The automated suite covers workflow/API behavior and development-seed counts, consistency, preservation, idempotency, environment gating, and demo-user permissions. Integration tests create isolated SQL Server databases, apply real migrations, exercise the workflow or HTTP API, and remove the databases afterward.

## 14. Scope And Trade-Offs

ERPFlow is a backend portfolio MVP, not a production ERP suite. It deliberately favors explicit use-case code over generic repositories, mediator pipelines, or premature abstractions. Out of scope are a frontend, refresh-token lifecycle, user administration, pagination, observability infrastructure, deployment automation, and other ERP modules such as purchasing, invoicing, and accounting.

Natural next steps would be permission-based authorization, refresh-token rotation, structured telemetry, pagination/filtering, containerized infrastructure, CI, and a client application.

## 15. Further Documentation

- [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) records completed phases, acceptance evidence, and future scope.
- [`docs/phase4-integration.md`](docs/phase4-integration.md) provides complete local setup, Swagger usage, PowerShell requests, role checks, and troubleshooting.
