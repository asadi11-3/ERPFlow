# Phase 4 Local Integration Guide

## 1. Purpose

This guide runs the completed JWT-secured Sales Order and Approval API locally and exercises its primary workflow. Commands use PowerShell and the HTTPS launch profile.

## 2. Prerequisites

- .NET 10 SDK (`dotnet --version` should report `10.x`).
- SQL Server or SQL Server Express reachable from the API and test process.
- The EF Core CLI for migration inspection: `dotnet tool install --global dotnet-ef` if it is not already available.
- A trusted ASP.NET Core development certificate for the HTTPS examples.

```powershell
dotnet dev-certs https --trust
```

## 3. Restore The Solution

From the repository root:

```powershell
dotnet restore ERPFlow.slnx
```

## 4. Configure SQL Server

The committed Development setting currently targets:

```text
Server=DESKTOP-P8A4TV4\SQLEXPRESS;Database=ERPFlowDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True
```

Override it when that instance is unavailable:

```powershell
dotnet user-secrets set --project ERPFlow.Api "ConnectionStrings:ERPFlowDb" "Server=.\SQLEXPRESS;Database=ERPFlowDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
```

The SQL identity or Windows account must be able to create and update the development database.

## 5. Configure JWT Signing

The API validates JWT configuration at startup and fails closed when the signing key is missing or shorter than 32 characters. Store local secrets outside committed configuration:

```powershell
dotnet user-secrets set --project ERPFlow.Api "Jwt:SigningKey" "replace-with-at-least-32-random-characters"
```

Committed defaults are issuer `ERPFlow`, audience `ERPFlow.Api`, and a 60-minute lifetime. `Jwt:ExpirationMinutes` accepts values from 1 through 1440.

## 6. Enable Demo Data

Set one development-only password to enable idempotent seeding:

```powershell
dotnet user-secrets set --project ERPFlow.Api "DemoUsers:Password" "replace-with-a-development-only-password"
```

When this value is absent or blank, the API applies migrations but does not seed demo data.

## 7. Seeded Records

Development seeding creates missing records by user/customer email, product SKU, and deterministic demo-order ID. It never resets the database, replaces passwords, or overwrites existing business records.

| Email | Role |
| --- | --- |
| `sales@erpflow.local` | Sales |
| `sales2@erpflow.local` | Sales |
| `sales3@erpflow.local` | Sales |
| `approver@erpflow.local` | Approver |
| `admin@erpflow.local` | Administrator |

New users receive the configured secret through `PasswordHasher`. Existing users keep their hashes. On an empty database the complete dataset is:

| Entity | Count |
| --- | --- |
| Users | 5: 3 Sales, 1 Approver, 1 Administrator |
| Customers | 10: 8 active, 2 inactive |
| Products | 20 unique SKUs |
| Orders | 15: 4 Draft, 5 PendingApproval, 3 Approved, 3 Rejected |
| Line items | 30, with 1-3 per order |
| Reservations | 23: 17 active, 6 released |
| History | 17: 11 Submitted, 3 Approved, 3 Rejected |

Customers cover retail, medical supplies, food, logistics, electronics, hardware, and office distribution. The catalog includes the original `SCAN-100` and `LABEL-500`, plus packing, storage, safety, and dispatch supplies. `BATT-100` has zero stock and `DISP-002` has stock 2, useful for insufficient-stock tests. Other products have both reserved and unreserved inventory.

Five orders belong to each Sales account. Submission uses the owning Sales actor; decisions alternate between Approver and Administrator. Domain methods create stock reservations, release rejected reservations, and generate chronological UTC timestamps. Every product's reserved quantity matches its active reservations in the fresh dataset.

Startup runs dependent inserts in a transaction. Existing deterministic order IDs are skipped even if their status/items have changed; incomplete existing orders are not repaired. Existing customer/product values are never reset. Missing scenarios whose references have incompatible roles, inactive customers, insufficient stock, or inconsistent reservation totals are skipped with a warning. Fresh counts can therefore differ on an existing database. Database conflicts roll back rather than overwrite user data; resolve the conflict and restart if necessary.

To verify through Swagger, authenticate as Approver or Administrator, call `GET /api/sales-orders/pending` (initially 5), and use an ID with `GET /api/sales-orders/{id}` and `GET /api/sales-orders/{id}/history`. Authenticate as each Sales user to demonstrate ownership denial on another user's ID. Reference endpoints return 8 active customers and 20 products initially.

There is no all-orders API. Discover other state IDs with this read-only SQL query against your development database, then inspect them in Swagger:

```sql
SELECT o.Id, o.Status, u.Email AS Owner, c.Name AS Customer
FROM SalesOrders AS o
JOIN AppUsers AS u ON u.Id = o.CreatedByUserId
JOIN Customers AS c ON c.Id = o.CustomerId
ORDER BY o.Status, u.Email, o.Id;
```

Status values are Draft `0`, PendingApproval `1`, Approved `2`, Rejected `3`. The existing API intentionally has no inactive-customer listing; inactive records can be inspected through SQL. Repeated startup adds no duplicate demo records and does not restore a user's modified order to its original status.

## 8. Apply Migrations

Development startup calls `Database.MigrateAsync`, so running the API applies pending migrations automatically. To apply them explicitly instead:

```powershell
dotnet ef database update --project ERPFlow.Infrastructure --startup-project ERPFlow.Api
```

The migrations support both a new database and upgrade from earlier project phases, including ownership and password-hash backfill behavior.

## 9. Run The API

```powershell
dotnet run --project ERPFlow.Api --launch-profile https
```

The configured addresses are:

- HTTPS: `https://localhost:7165`
- HTTP: `http://localhost:5221` (redirects to HTTPS)

The remaining examples use:

```powershell
$baseUrl = "https://localhost:7165"
$demoPassword = "replace-with-a-development-only-password"
```

## 10. Use Swagger UI

Open `https://localhost:7165/swagger`. The Swagger UI and `/openapi/v1.json` are exposed only in Development.

1. Call `POST /api/auth/login` with a demo account.
2. Copy only the `accessToken` value.
3. Select **Authorize**.
4. Paste the raw token. Do not add the `Bearer ` prefix; the HTTP bearer scheme adds it.

Swagger persists authorization while the UI remains open.

## 11. Sign In As Sales

```powershell
$salesLogin = Invoke-RestMethod `
    -Method Post `
    -Uri "$baseUrl/api/auth/login" `
    -ContentType "application/json" `
    -Body (@{
        email = "sales@erpflow.local"
        password = $demoPassword
    } | ConvertTo-Json)

$salesHeaders = @{ Authorization = "Bearer $($salesLogin.accessToken)" }
$salesLogin.expiresAt
```

Invalid credentials return `401`. A blank or oversized credential request returns `400`.

## 12. Read Reference Data

```powershell
$customers = Invoke-RestMethod `
    -Method Get `
    -Uri "$baseUrl/api/reference/customers" `
    -Headers $salesHeaders

$products = Invoke-RestMethod `
    -Method Get `
    -Uri "$baseUrl/api/reference/products" `
    -Headers $salesHeaders

$customers | Format-Table
$products | Format-Table
```

These endpoints require any authenticated user. Copy an active customer ID and product ID from their responses.

## 13. Create A Draft

```powershell
$createBody = @{
    customerId = $customers[0].id
    items = @(
        @{
            productId = $products[0].id
            quantity = 2
        }
    )
} | ConvertTo-Json -Depth 4

$order = Invoke-RestMethod `
    -Method Post `
    -Uri "$baseUrl/api/sales-orders" `
    -Headers $salesHeaders `
    -ContentType "application/json" `
    -Body $createBody

$order | ConvertTo-Json -Depth 5
```

Creation returns `201 Created`. The response contains the generated order ID, creator ID, item price and total, timestamps, status, items, and reservations. Status enums use their default numeric JSON values: Draft `0`, PendingApproval `1`, Approved `2`, and Rejected `3`.

The request contains no user ID, role, unit price, or total. Those values are obtained from validated identity claims and persisted data.

## 14. Read And Submit The Order

Read the draft:

```powershell
$order = Invoke-RestMethod `
    -Method Get `
    -Uri "$baseUrl/api/sales-orders/$($order.id)" `
    -Headers $salesHeaders
```

Submit it:

```powershell
$order = Invoke-RestMethod `
    -Method Post `
    -Uri "$baseUrl/api/sales-orders/$($order.id)/submit" `
    -Headers $salesHeaders

$order | ConvertTo-Json -Depth 5
```

Submission verifies stock, creates reservations, changes the order to PendingApproval, and records a Submitted audit entry in one transaction. A Sales user cannot read or submit an order created by another Sales user; an Administrator is not restricted by Sales ownership.

## 15. Sign In As Approver

```powershell
$approverLogin = Invoke-RestMethod `
    -Method Post `
    -Uri "$baseUrl/api/auth/login" `
    -ContentType "application/json" `
    -Body (@{
        email = "approver@erpflow.local"
        password = $demoPassword
    } | ConvertTo-Json)

$approverHeaders = @{ Authorization = "Bearer $($approverLogin.accessToken)" }
```

## 16. List Pending Approvals

```powershell
$pending = Invoke-RestMethod `
    -Method Get `
    -Uri "$baseUrl/api/sales-orders/pending" `
    -Headers $approverHeaders

$pending | ConvertTo-Json -Depth 5
```

Only Approver and Administrator roles can use this queue.

## 17. Approve, Reject, And Read History

Approve the submitted order:

```powershell
$approved = Invoke-RestMethod `
    -Method Post `
    -Uri "$baseUrl/api/sales-orders/$($order.id)/approve" `
    -Headers $approverHeaders `
    -ContentType "application/json" `
    -Body (@{ comments = "Approved after review." } | ConvertTo-Json)
```

To test rejection, create and submit a second order, then call:

```powershell
$rejected = Invoke-RestMethod `
    -Method Post `
    -Uri "$baseUrl/api/sales-orders/$secondOrderId/reject" `
    -Headers $approverHeaders `
    -ContentType "application/json" `
    -Body (@{ reason = "Budget approval is required." } | ConvertTo-Json)
```

Approval retains active reservations. Rejection requires a non-empty reason and atomically releases active reservations back to product stock.

Any authenticated user may request an existing order's audit history, subject to the same visibility rule as `GET /api/sales-orders/{id}`. Sales users can read only their own orders; Approver and Administrator users can read any order. Fetch the approved order's history with:

```powershell
$history = Invoke-RestMethod `
    -Method Get `
    -Uri "$baseUrl/api/sales-orders/$($order.id)/history" `
    -Headers $approverHeaders

$history | ConvertTo-Json -Depth 3
```

`GET /api/sales-orders/{id}/history` returns entries chronologically, with a stable ID tie-breaker when timestamps match:

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

The response schema is an array of `{ action: number, actorId: UUID, timestampUtc: UTC date-time, remarks: string | null }`. Action values are Approved `0`, Rejected `1`, and Submitted `2`. Remarks reuse the persisted comments field, which contains the submission remark, approval comments, or rejection reason.

## 18. Authorization Matrix

| Operation | Sales | Approver | Administrator |
| --- | ---: | ---: | ---: |
| Read reference data | Yes | Yes | Yes |
| Create draft | Yes | No | Yes |
| Read order by ID | Own orders | Yes | Yes |
| Read order approval history | Own orders | Yes | Yes |
| Submit draft | Own orders | No | Yes |
| List pending orders | No | Yes | Yes |
| Approve or reject | No | Yes | Yes |

Missing or invalid tokens return `401`. A valid token with the wrong endpoint role returns `403`. Sales ownership is enforced in the Application layer and also returns `403`.

## 19. Error Responses And Troubleshooting

API failures use Problem Details with `status`, `title`, and a safe `detail`.

| Status | Typical cause |
| --- | --- |
| `400` | Invalid IDs, empty items, invalid quantities/reason, malformed JSON |
| `401` | Missing/invalid token or invalid login credentials |
| `403` | Wrong role or Sales ownership violation |
| `404` | Customer, product, user, or order does not exist |
| `409` | Inactive customer, invalid state transition, insufficient stock, concurrent update, duplicate reservation |
| `500` | Unexpected failure; implementation details are not returned |

Common startup issues:

- **JWT configuration is invalid:** set a signing key of at least 32 characters.
- **SQL connection failure:** override `ConnectionStrings:ERPFlowDb` and confirm the instance is running.
- **Certificate warning:** run `dotnet dev-certs https --trust` or use a trusted local certificate.
- **No demo records:** set `DemoUsers:Password` before starting in Development.
- **Unexpected `401`:** sign in again after token expiration; the default lifetime is 60 minutes.
- **Unexpected `409`:** fetch current state and stock rather than retrying a stale transition blindly.

## 20. Automated Verification

Run the same quality gates used for the completed MVP:

```powershell
dotnet build ERPFlow.slnx
dotnet test ERPFlow.slnx --no-restore
dotnet ef migrations has-pending-model-changes --project ERPFlow.Infrastructure --startup-project ERPFlow.Api
dotnet list ERPFlow.slnx package --vulnerable --include-transitive
```

Seed integration tests additionally check counts, all statuses, reservations, history actors/actions, repeated runs, record preservation, environment/secret gates, and seeded-user authentication/permissions. SQL integration tests create isolated databases, apply real migrations, and delete those databases afterward. Set `ERPFLOW_TEST_SQLSERVER` to override their SQL Server connection target; tests never target the user's `ERPFlowDb`.
