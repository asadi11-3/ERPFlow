# Phase 4 API Integration

## Prerequisites

- .NET 10 SDK
- SQL Server Express at `.\SQLEXPRESS`, or update `ConnectionStrings:ERPFlowDb`

## Local Secrets

The API fails closed when its signing key is absent or shorter than 32 characters. Set development values in the current PowerShell session; do not commit them:

```powershell
$env:Jwt__SigningKey = "<at-least-32-random-characters>"
$env:DemoUsers__Password = "<development-only-password>"
```

`DemoUsers__Password` is optional. When present in Development, startup applies migrations and idempotently creates reference data plus these accounts:

| Email | Role |
| --- | --- |
| `admin@erpflow.local` | Administrator |
| `sales@erpflow.local` | Sales |
| `approver@erpflow.local` | Approver |

All demo accounts use the configured development password. Passwords are stored through ASP.NET Core `PasswordHasher`; JWTs expire after 60 minutes by default.

## Run

```powershell
dotnet run --project ERPFlow.Api
```

Open `https://localhost:7165/scalar/v1` for the development-only Scalar API client. Authenticate with `POST /api/auth/login`, then enter the returned token under the Bearer scheme.

## Request Flow

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "sales@erpflow.local",
  "password": "<development-only-password>"
}
```

Use the returned `accessToken` on protected requests:

```http
GET /api/reference/customers
Authorization: Bearer <accessToken>
```

```http
POST /api/sales-orders
Authorization: Bearer <salesAccessToken>
Content-Type: application/json

{
  "customerId": "<customer-id>",
  "items": [
    { "productId": "<product-id>", "quantity": 2 }
  ]
}
```

Submit with the same Sales user, then approve with an Approver or Administrator:

```http
POST /api/sales-orders/<order-id>/submit
Authorization: Bearer <salesAccessToken>
```

```http
POST /api/sales-orders/<order-id>/approve
Authorization: Bearer <approverAccessToken>
Content-Type: application/json

{ "comments": "Approved." }
```

Sales users can create and submit orders and can only read or submit their own orders. Approvers can list pending orders and approve or reject them. Administrators satisfy both policies. Actor IDs, roles, and prices always come from authenticated claims or server-side persistence, never request input.

## Verification

```powershell
dotnet build ERPFlow.slnx
dotnet test ERPFlow.slnx --no-restore
$env:Jwt__SigningKey = "<at-least-32-random-characters>"
dotnet ef migrations has-pending-model-changes --project ERPFlow.Infrastructure --startup-project ERPFlow.Api
dotnet list ERPFlow.slnx package --vulnerable --include-transitive
```

API integration tests create isolated SQL Server databases, run real migrations, exercise authentication and authorization over HTTP, and delete the databases afterward. Override the test server with `ERPFLOW_TEST_SQLSERVER` when needed.
