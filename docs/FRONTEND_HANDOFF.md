# ERPFlow Frontend Integration Handoff

## Local Environment

The API targets .NET 10 and SQL Server. From the repository root:

```powershell
dotnet restore ERPFlow.slnx
dotnet dev-certs https --trust
dotnet run --project ERPFlow.Api --launch-profile https
```

Before starting, configure `ConnectionStrings:ERPFlowDb`, `Jwt:SigningKey`, and optionally `DemoUsers:Password` through local User Secrets or environment variables. Obtain secret values locally; never put them in frontend configuration, source control, or this document. See [local setup](phase4-integration.md) for configuration details.

Use `https://localhost:7165` as the API base URL (append `/api/...` routes below). Swagger is at `/swagger`; live OpenAPI is at `/openapi/v1.json`, both Development-only. HTTP port 5221 redirects to HTTPS; point the browser directly at HTTPS so preflight is not redirected. Trust the certificate before debugging CORS errors.

The named `DevelopmentFrontend` CORS policy is active only in Development. Its origins come from `Cors:AllowedOrigins` in `ERPFlow.Api/appsettings.Development.json`, initially:

- `http://localhost:4200` (Angular)
- `http://localhost:5173` (Vite)

Origins must match scheme, hostname, and port exactly, without a trailing slash. For another local origin, update the array or override entries with `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, etc., then restart. GET and POST plus Authorization and Content-Type are allowed. Browser OPTIONS preflights are handled before JWT authorization. No wildcard origins or credentialed CORS are enabled. Do not use `withCredentials: true` or `credentials: 'include'`. Production has no development CORS policy, even if the setting is populated; a production cross-origin deployment requires an explicit reviewed policy.

## OpenAPI Source

[Generated OpenAPI v1 JSON](openapi/erpflow-v1.json) is the actual response exported from the application running in the isolated integration-test host, with the local HTTPS base address. It is OpenAPI **3.1.1**, not a handwritten schema. Set a generated client's base URL explicitly for each environment.

The integration test compares the saved JSON to the live generated document, checks Bearer security on all protected operations, and checks that test secrets are absent. Regenerate after intentional API contract changes:

```powershell
$env:ERPFLOW_EXPORT_OPENAPI = '1'
dotnet test ERPFlow.slnx --filter 'FullyQualifiedName~DevelopmentOpenApiAndSwaggerUiEndpointsAreAvailable'
Remove-Item Env:ERPFLOW_EXPORT_OPENAPI
```

This command uses an isolated test database, not `ERPFlowDb`. Do not manually edit the exported JSON. The generator does not encode every application validation rule or error status; the runtime rules below remain authoritative. Numeric enums appear as integer schemas, and numeric schemas can include numeric strings accepted by JSON binding. Responses emit numeric values.

## Authentication

`POST /api/auth/login` accepts JSON `{ "email": string, "password": string }` and returns `{ "accessToken": string, "expiresAt": UTC date-time }`.

For every protected request send `Authorization: Bearer <accessToken>`. Send `Content-Type: application/json` on JSON POST bodies. In Swagger's Authorize dialog paste only the raw token; Swagger supplies the prefix.

Tokens contain `sub` (user UUID), `email`, `name`, `role` (string: Sales, Approver, Administrator), and `jti`. UI role checks are presentation only; the backend validates JWTs and consults persisted actors for workflow authorization. The default lifetime is 60 minutes. There is no refresh, logout/revocation, registration, or current-user endpoint. On expiration clear the client session and return to login; never repeatedly retry a failed request with the same token. Prefer an in-memory token; do not place tokens in URLs or logs. No authentication cookies are used.

## Endpoint Table

All paths are relative to the base URL. All except login require JWT.

| Method | Path | Body/query | Successful response |
| --- | --- | --- | --- |
| POST | `/api/auth/login` | LoginRequest | 200 AccessTokenResult |
| GET | `/api/reference/customers` | None | 200 CustomerReference[] (active only) |
| GET | `/api/reference/products` | None | 200 ProductReference[] |
| POST | `/api/sales-orders` | CreateSalesOrderCommand | 201 SalesOrderResult; Location header |
| GET | `/api/sales-orders` | page, pageSize, optional numeric status | 200 SalesOrderPage |
| GET | `/api/sales-orders/{id}` | UUID path | 200 SalesOrderResult |
| POST | `/api/sales-orders/{id}/submit` | No body | 200 SalesOrderResult |
| GET | `/api/sales-orders/pending` | None | 200 SalesOrderResult[] |
| POST | `/api/sales-orders/{id}/approve` | ApproveOrderRequest | 200 SalesOrderResult |
| POST | `/api/sales-orders/{id}/reject` | RejectOrderRequest | 200 SalesOrderResult |
| GET | `/api/sales-orders/{id}/history` | UUID path | 200 ApprovalHistoryResult[] |

The Location header is not CORS-exposed; use the create response's `id` for browser navigation.

## Models

These are JSON shapes, not extra API resources. UUIDs and date-times are strings; monetary and quantity fields are numbers. Arrays are present, possibly empty. Fields explicitly marked nullable may be null.

| Model | Exact fields |
| --- | --- |
| LoginRequest | `email: string`, `password: string` |
| AccessTokenResult | `accessToken: string`, `expiresAt: date-time` |
| CustomerReference | `id: UUID`, `name: string`, `email: string` |
| ProductReference | `id: UUID`, `name: string`, `sku: string`, `unitPrice: number`, `availableStock: integer` |
| CreateSalesOrderCommand | `customerId: UUID`, `items: CreateSalesOrderItem[]` |
| CreateSalesOrderItem | `productId: UUID`, `quantity: integer` |
| ApproveOrderRequest | `comments: string or null` (send `{ "comments": null }` if none) |
| RejectOrderRequest | `reason: string` (nonblank) |
| SalesOrderResult | `id: UUID`, `customerId: UUID`, `createdByUserId: UUID`, `status: OrderStatus`, `createdAt: date-time`, `submittedAt: date-time or null`, `decidedAt: date-time or null`, `totalAmount: number`, `items: SalesOrderItemResult[]`, `reservations: StockReservationResult[]` |
| SalesOrderItemResult | `id: UUID`, `productId: UUID`, `quantity: integer`, `unitPrice: number`, `lineTotal: number` |
| StockReservationResult | `id: UUID`, `productId: UUID`, `quantity: integer`, `isReleased: boolean`, `createdAt: date-time`, `releasedAt: date-time or null` |
| ApprovalHistoryResult | `action: ApprovalAction`, `actorId: UUID`, `timestampUtc: date-time`, `remarks: string or null` |
| SalesOrderPage | `items: SalesOrderResult[]`, `page: integer`, `pageSize: integer`, `totalCount: integer`, `totalPages: integer` |

Create requires an active customer, at least one line, nonempty product IDs, positive quantities, and no duplicate products. Prices, totals, creator IDs, roles, and status are server-controlled. Join customer/product reference data locally for names; order DTOs carry IDs, not display names. Product responses expose available stock, not stock-on-hand or reserved totals.

Business timestamps originate in UTC. History explicitly returns `timestampUtc` with a UTC kind/`Z` suffix. Existing order/reservation timestamps are stored as SQL `datetime2`; their JSON can omit an offset after database reads. Interpret these existing fields as UTC rather than browser-local time. Do not rename `createdAt` to `createdAtUtc` in requests or generated contracts.

History is oldest-first (`CreatedAt`, then ID) and includes persisted submission remarks, approval comments, or rejection reasons. It does not include actor names; no user-directory endpoint exists.

## Numeric Enums

| OrderStatus | Value | ApprovalAction | Value |
| --- | ---: | --- | ---: |
| Draft | 0 | Approved | 0 |
| PendingApproval | 1 | Rejected | 1 |
| Approved | 2 | Submitted | 2 |
| Rejected | 3 | | |

Do not confuse the two numeric mappings. JWT `role` is a string, not these enum values.

## Role And Ownership Matrix

| Operation | Sales | Approver | Administrator |
| --- | --- | --- | --- |
| Reference data | Yes | Yes | Yes |
| List orders/counts | Own only | All | All |
| Read detail/history | Own only | All | All |
| Create draft | Yes | No | Yes |
| Submit draft | Own only | No | Any |
| Pending queue | No | Yes | Yes |
| Approve/reject pending | No | Yes | Yes |

All actions remain subject to state, input, stock, and concurrency checks. The pending endpoint returns an unpaginated array; the general list endpoint can provide a paginated pending view with `status=1`.

## Pagination

Use `GET /api/sales-orders?page=1&pageSize=10&status=1`. Page defaults to 1 and must be positive. PageSize defaults to 10 and must be 1-100. Omit status for all states; otherwise use a defined numeric OrderStatus, not its name.

Ownership and status filters precede SQL counting and paging. Sort is UTC CreatedAt descending, then ID descending. The response contains items, echoed page/pageSize, filtered totalCount, and totalPages. No matches means `items: []`, totalCount 0, totalPages 0. A page beyond the last returns an empty array but retains totals. There is no sort or owner query parameter. Reset page to 1 when changing filters. Separate count/page reads and offset pagination are not a transactional snapshot; concurrent inserts can shift later pages.

## Errors

Handle `application/problem+json` using optional `type`, `title`, `status`, `detail`, and `instance`. Validation responses can also contain an `errors` dictionary. Do not assume every failure has a body (for example unmatched routes or proxy errors). Avoid matching exact error text to determine behavior.

| HTTP status | Meaning | Frontend behavior |
| --- | --- | --- |
| 400 | Invalid body/query, malformed JSON, missing rejection reason | Show validation; fix inputs before retry |
| 401 | Missing/invalid/expired token or invalid login | Login error or clear session and request login |
| 403 | Wrong role or Sales ownership violation | Show access denied; do not retry with the same user |
| 404 | Entity not found | Show unavailable resource and return to list |
| 409 | Invalid transition, insufficient stock, inactive customer, duplicate reservation, concurrent update | Refresh detail/stock, explain conflict, require explicit user action |

Unexpected failures return 500 with generic details. CORS does not grant authorization: an allowed-origin request can still receive 401/403. Disallowed preflight returns no Allow-Origin header, which browsers treat as blocked; it is not necessarily an HTTP 403. CORS is not an access-control boundary for nonbrowser clients.

## Development Demo Data

Enable only through the local `DemoUsers:Password` secret and Development environment. All newly seeded accounts use that configured secret; existing hashes are preserved.

| Email | Role |
| --- | --- |
| `sales@erpflow.local` | Sales |
| `sales2@erpflow.local` | Sales |
| `sales3@erpflow.local` | Sales |
| `approver@erpflow.local` | Approver |
| `admin@erpflow.local` | Administrator |

A fresh database contains 10 customers (8 active, 2 inactive), 20 products, and 15 orders: 4 Draft, 5 PendingApproval, 3 Approved, 3 Rejected. Each Sales user owns five orders. There are 30 line items, 23 reservations (17 active, 6 released), and 17 history entries (11 Submitted, 3 Approved, 3 Rejected). `BATT-100` has zero stock; `DISP-002` has stock 2 for insufficient-stock scenarios.

Seeding is transactional and idempotent. Stable order IDs and reference keys avoid duplicates. Existing records are not reset; conflicting missing scenarios are skipped rather than overwriting user changes. Counts on an existing development database may differ. No production seeding is enabled.

## Suggested Pages And API Mappings

| Frontend page | API mapping |
| --- | --- |
| Login | POST login; retain accessToken and expiry |
| Orders table | GET list with page/pageSize/status; resolve names from references |
| New order | GET customer/product references; POST create |
| Order detail | GET detail; POST submit for allowed Draft; refresh response |
| Approval inbox | GET pending or paginated list with status=1 |
| Decision panel | POST approve or reject; refresh detail and inbox |
| Audit timeline | GET history; map numeric actions and UTC timestamps |

These are recommendations for the next frontend project, not implemented pages. Hide unavailable actions by role/state, while still handling server denials.

## Complete Sales-To-Approver Workflow

1. Login as Sales; retain the returned accessToken and expiresAt.
2. Fetch customer/product reference arrays using that token. Choose an active customer and a stocked product.
3. POST `/api/sales-orders` with `{ "customerId": "<customer UUID>", "items": [{ "productId": "<product UUID>", "quantity": 2 }] }`. Read the 201 body's id and server prices.
4. GET `/api/sales-orders/{id}` or use the returned draft. Do not send price, actor, or status fields.
5. POST `/api/sales-orders/{id}/submit` with the Sales token and no body. The returned state is PendingApproval; stock is reserved.
6. Login as Approver and replace the request token. GET `/api/sales-orders/pending` or `/api/sales-orders?status=1`.
7. POST `/api/sales-orders/{id}/approve` with `{ "comments": "Stock checked; approved for dispatch." }`. Approval retains reservations.
8. GET detail/history to show Approved and the Submitted/Approved timeline.
9. To demonstrate rejection, create and submit another order, then POST its `/reject` route with `{ "reason": "Customer requested a revised delivery date." }`. Reservations become released and the reason appears in history.
10. Login as another Sales user and confirm the original order detail/history returns 403 and is absent from that user's list/counts.

## Known Limitations

- No edit, delete, cancel, resubmit, shipment, invoicing, or stock-adjustment endpoint. Draft creation/submission and pending decisions are the supported lifecycle.
- No user/customer/product administration, user lookup, current-user profile, refresh token, logout/revocation, or inactive-customer listing endpoint.
- No free-text search, custom sort, owner filter, date filter, or paging on reference/history/pending arrays.
- No cookie authentication or credentialed CORS. CORS is disabled outside Development until explicitly designed for deployment.
- OpenAPI describes generated wire schemas, not every runtime business rule, role, status enum member, or error response. Use this handoff and handle Problem Details consistently.
- UTC offsets are explicit for history but can be absent on existing SQL-backed order/reservation fields; normalize those known UTC fields in the client.
- No new frontend code or production deployment configuration is included in this handoff.
