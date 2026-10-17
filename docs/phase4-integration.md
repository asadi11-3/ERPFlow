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

Development seeding creates missing records without duplicating existing email addresses or reference sets.

| Email | Role |
| --- | --- |
| `sales@erpflow.local` | Sales |
| `approver@erpflow.local` | Approver |
| `admin@erpflow.local` | Administrator |

All three users receive the configured demo password. The reference data is:

| Type | Name | Additional data |
| --- | --- | --- |
| Customer | Contoso Retail | `orders@contoso.local` |
| Product | Warehouse Scanner | SKU `SCAN-100`, price 349.00, stock 25 |
| Product | Shipping Label Roll | SKU `LABEL-500`, price 19.95, stock 200 |

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

## 17. Approve Or Reject

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

## 18. Authorization Matrix

| Operation | Sales | Approver | Administrator |
| --- | ---: | ---: | ---: |
| Read reference data | Yes | Yes | Yes |
| Create draft | Yes | No | Yes |
| Read order by ID | Own orders | Yes | Yes |
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

The verified baseline is 0 build warnings/errors, 21 passing tests, no pending EF model changes, and no known vulnerable packages. SQL integration tests create isolated databases, apply real migrations, and delete those databases afterward. Set `ERPFLOW_TEST_SQLSERVER` to override their SQL Server connection target.
