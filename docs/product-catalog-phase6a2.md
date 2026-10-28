# Phase 6A.2 — Product catalog management

## HTTP contract

All endpoints use the existing JWT authentication and RFC 7807 error conventions.
Mutations require both the Administrator endpoint policy and a current persisted
Administrator role. Authenticated users may read details and catalog history.

| Endpoint | Request | Success |
| --- | --- | --- |
| `POST /api/products` | `{ name, sku, unitPrice }` | `201`, Location, ProductDetails |
| `GET /api/products/{id}` | — | `200`, ProductDetails |
| `PUT /api/products/{id}` | `{ name, sku, unitPrice, rowVersion }` | `200`, ProductDetails |
| `POST /api/products/{id}/activate` | `{ rowVersion }` | `200`, ProductDetails |
| `POST /api/products/{id}/deactivate` | `{ rowVersion }` | `200`, ProductDetails |

ProductDetails contains identity, name, SKU, price, active status, creation time,
base64 SQL rowVersion, image presence/version, and the latest 100 catalog changes.
No inventory quantities are accepted in these requests. Unknown JSON properties,
including StockOnHand, ReservedQuantity, and IsActive on create/update, are rejected.
Missing products return 404, invalid requests 400, forbidden writes 403, duplicate
SKUs and stale changes 409. No DELETE product endpoint exists.

## Catalog rules

- A new product is active and starts with **zero physical stock and zero reserved quantity**.
  Creation generates no warehouse balance, opening movement, inventory posting, or cutover.
- Name is trimmed, required, and limited to 200 characters. SKU is trimmed, required,
  unique under the existing SQL collation, limited to 64 characters, and disallows
  control characters. UnitPrice is stored as decimal(18,2), with no silent rounding.
  Catalog input is consistently limited to **0–9,999,999,999,999.99** on both backend
  and browser, keeping cent values within the browser's safe numeric range. The limit
  does not rewrite older stored prices or transaction snapshots.
- **SKU is immutable after creation**, even before its first transaction. A different
  identifier requires a new product. This deliberately avoids identifier changes racing
  with document creation or changing how historical documents identify products.
- Catalog updates retain ProductId and change only Name/UnitPrice. Activation changes
  only IsActive. StockOnHand/ReservedQuantity are never changed by the catalog workflow.
- Update and availability commands require the exact eight-byte Product rowVersion.
  Stale requests return 409; meaningful changes advance it. No-op requests create no
  redundant audit entries. Product and image versions are independent.
- Catalog changes do not update SalesOrderItem, PurchaseOrderItem, JobcardItem, totals,
  reservations, costs, or committed transactions. Future sales/parts lines snapshot the
  current selling price; purchase costs continue to come from the PO's supplied terms.
  Explicitly editing a Sales draft retains that workflow's existing repricing behavior.
- Every create, meaningful edit, activation, and deactivation records ProductId, actor,
  UTC timestamp, action, SKU, previous/new name, previous/new price, and previous/new
  active state in ProductCatalogChanges. Audit creation is atomic with the catalog change.
  There are no audit mutation or product hard-delete API operations.

## Deactivation and existing commitments

Deactivation is availability control, not cancellation and not inventory adjustment.

| Area | Blocked after deactivation | Still valid |
| --- | --- | --- |
| Sales | New drafts, draft edits containing inactive products, draft submission | Submitted orders, decisions, release/return/reject, approved dispatch, historical reads |
| Procurement | New PO drafts, draft edits containing inactive products, draft submission | Submitted PO approval/rejection/cancellation and existing stored costs |
| Receiving | No standalone new inactive-product selection | GRN creation/posting against legitimate approved/receiving POs, including inactive products |
| Service | New parts requests using an inactive product | Existing part reservations, completion/consumption, asset service and history |
| Inventory transfers | New transfer drafts selecting inactive products | Existing transfer posting/cancellation and historical balances/movements |

An uncommitted draft containing inactive products must remove those lines or wait for
reactivation before it can be edited/submitted. The catalog/reference list retains inactive
products for history and filtering; new-selection controls disable or exclude them.
Inventory filters continue to include inactive products so existing stock remains visible.

New-selection product reads hold transaction locks until the business operation completes;
availability changes take an update lock. This prevents a deactivation from slipping between
selection validation and a new commitment. Completion-only persistence reads retain their
existing optimistic-concurrency behavior. The existing shared inventory-authority fence is
used by catalog writes; authority metadata, cutover permissions, write gates, triggers,
and inventory reconciliation rules are unchanged.

## Migration

`20261028114503_ProductCatalogPhase6A2` adds:

- `Products.IsActive`, non-null, default true (existing products stay available).
- `ProductCatalogChanges` with restrictive product/actor foreign keys, decimal precision,
  action/price constraints, and a product/time index.

No existing migrations or inventory guards are edited. Destructive downgrade is refused.
The design-time factory performs model checks without starting the configured application.
Deployment must apply the additive migration before the matching application. Older
application writers do not understand deactivation, so retire them before enabling catalog
administration. No application/production database migration was performed in this task.

The pre-existing uncommitted development-settings change was preserved, not reverted.
Its start-of-task and final SHA-256 are both
`FD0E460E8B05A8ADFC8ED87E32DE0D29110B095277C8BFA408B8043E18532FFD`.

Historical migration tests now seed the historical Product column set and project only
columns present at that point in the migration chain. Their original preservation and
concurrency assertions remain in place; production compatibility logic is not weakened.

## Angular routes and behavior

- `/reference?tab=products`: existing product list, thumbnails, Active/Inactive status,
  availability filter, Administrator Add Product/Edit Product, and read-only detail links.
- `/reference/products/new`: Administrator-only creation form.
- `/reference/products/:id/edit`: Administrator-only editor with immutable SKU,
  validated name/price, versioned save, confirmed activation/deactivation, and audit history.
- `/reference/products/:id`: authenticated read-only catalog details.

Forms preserve entries after a 409, stop further stale submissions, and offer an explicit
reload. Duplicate SKU on creation remains correctable without a reload dead end.
Successful writes update the current detail from the server response and reload references.
New/edit routes are role-guarded and mutation methods also check the current role.

The existing ProductImage component is reused on details/forms and in the listing.
Its multipart upload, bearer requests, Blob/object URL cleanup, independent ETags,
replacement, deletion, and error handling are unchanged. A catalog edit does not change
the image version or stored bytes.

## Verification and isolation

Backend tests use uniquely generated, fixture-owned SQL Server databases. Cleanup of
only those newly created test databases was explicitly allowed by the user. Configured
application databases, Demo Seeder, appsettings.Development.json, and inventory authority
gates are not modified. Existing Product Image work is preserved. No commit or push.

Commands:

```powershell
dotnet restore ERPFlow.slnx
dotnet build ERPFlow.slnx --no-restore -m:1
dotnet test ERPFlow.Tests/ERPFlow.Tests.csproj --no-build
dotnet ef migrations has-pending-model-changes --project ERPFlow.Infrastructure --startup-project ERPFlow.Api --context ERPFlowDbContext --no-build
# from front/
npm run build
npm test -- --watch=false
npm run lint
```

Live browser QA uses the real Angular application and a disposable Kestrel API with an
isolated SQL database. A browser request forwarder maps the configured frontend API origin
to that loopback test API; catalog responses are not fixtures. The image fixture is uploaded
using a real authenticated multipart API request, then rendered through the existing Blob
preview. Browser evidence covers create/edit, activation/deactivation, real stale-write 409,
preserved draft/reload, image compatibility, and Sales read-only access/route denial.
Local evidence: `.playwright-mcp/catalog-live-evidence.json`, `catalog-sales-evidence.json`,
`catalog-capture-manifest.json`, and `catalog-*.png`. Captures cover new/edit/detail/list
at 390/1440/1920 CSS-pixel widths under both system preferences; the existing warm-light
theme is retained. Final test counts and any residual issues are recorded in the delivery report.

### Final verification results

| Check | Result |
| --- | --- |
| Backend restore/build | Passed; final build has 0 warnings and 0 errors |
| Backend full release regression | 504 passed, 0 failed/skipped; includes all 477 pre-catalog tests |
| Final focused catalog closeout | 28 passed, 0 failed/skipped; includes the additional price-boundary case added after the full release run |
| Angular suite | 111 passed (88 existing plus 23 catalog tests) |
| Angular production build | Passed; 371.94 kB initial, about 100.71 kB estimated transfer, existing budgets satisfied |
| Angular lint | Passed |
| Hosting tests | 2 passed; pre-existing Node module-type warning remains |
| EF model | No pending changes |
| OpenAPI snapshot | Exported and verified through the isolated API fixture |
| Live UI/API/SQL rehearsal | Passed catalog create/edit/status, 409/reload, read-only access, and image compatibility |
| Independent review | Final gate APPROVE; visual review PASS |

The existing EF CLI 10.0.11 reports its version warning against EF runtime/packages 10.0.13;
model verification still succeeds. The additive migration has not been applied to the
configured application database. All rehearsal resources were stopped and the owned
database/storage were removed using the explicitly approved isolated cleanup path.

Local test artifacts:
`C:/Users/User/AppData/Local/Temp/opencode/ERPFlow-catalog-results/catalog-backend-release.trx`
and `catalog-focused-closeout.trx` in the same directory. The full run and focused closeout
overlap; their counts must not be added together as distinct tests.

## Files in this increment

New backend files:
`ERPFlow.Api/ProductCatalogEndpoints.cs`,
`ERPFlow.Application/Products/ProductCatalogContracts.cs`,
`ERPFlow.Domain/Entities/ProductCatalogChange.cs`,
`ERPFlow.Infrastructure/Persistence/EfProductCatalog.cs`,
`ERPFlow.Infrastructure/Persistence/ERPFlowDbContext.Products.cs`,
`ERPFlow.Infrastructure/Persistence/Configurations/ProductCatalogChangeConfiguration.cs`,
and the new migration/designer.

Modified backend files: Product and ProductConfiguration; ApiEndpoints and infrastructure
DI; reference DTO/projection; Sales, Service, Procurement, and Transfer workflow/persistence
contracts and implementations; EF model snapshot; OpenAPI snapshot.

New tests: ProductCatalogApiTests, ProductCatalogIntegrityTests, and LegacyProductFixture.
Historical test adaptations: InventoryTestDatabase, InventorySchemaIntegrationTests,
EquipmentWorkflowIntegrationTests, ProcurementMigrationTests, GoodsReceiptMigrationTests,
StockTransferMigrationTests, InventoryCutoverMigrationTests, LegacyOriginMigrationIntegrationTests.

New Angular files: core/api/product-catalog.ts and its spec;
feature/reference/product-form.ts, product-form.html, and product-form.spec.ts.
Modified Angular files: Product model, Reference, reference routes/listing, Sales product
choices, purchase/transfer choices, Service parts choices, and DESIGN.md.

### Exact file manifest

```text
NEW
ERPFlow.Api/ProductCatalogEndpoints.cs
ERPFlow.Application/Products/ProductCatalogContracts.cs
ERPFlow.Domain/Entities/ProductCatalogChange.cs
ERPFlow.Infrastructure/Persistence/Configurations/ProductCatalogChangeConfiguration.cs
ERPFlow.Infrastructure/Persistence/ERPFlowDbContext.Products.cs
ERPFlow.Infrastructure/Persistence/EfProductCatalog.cs
ERPFlow.Infrastructure/Persistence/Migrations/20261028114503_ProductCatalogPhase6A2.cs
ERPFlow.Infrastructure/Persistence/Migrations/20261028114503_ProductCatalogPhase6A2.Designer.cs
ERPFlow.Tests/LegacyProductFixture.cs
ERPFlow.Tests/ProductCatalogApiTests.cs
ERPFlow.Tests/ProductCatalogIntegrityTests.cs
front/src/app/core/api/product-catalog.ts
front/src/app/core/api/product-catalog.spec.ts
front/src/app/feature/reference/product-form.ts
front/src/app/feature/reference/product-form.html
front/src/app/feature/reference/product-form.spec.ts
docs/product-catalog-phase6a2.md

MODIFIED IN THIS INCREMENT
ERPFlow.Api/ApiEndpoints.cs
ERPFlow.Application/Abstractions/IProcurementPersistence.cs
ERPFlow.Application/Abstractions/IReferenceDataQueries.cs
ERPFlow.Application/Abstractions/ISalesOrderPersistence.cs
ERPFlow.Application/Abstractions/IServicePersistence.cs
ERPFlow.Application/Abstractions/IStockTransferPersistence.cs
ERPFlow.Application/Procurement/PurchaseOrderWorkflow.cs
ERPFlow.Application/SalesOrders/SalesOrderWorkflow.cs
ERPFlow.Application/Service/ServiceWorkflow.cs
ERPFlow.Application/Transfers/StockTransferWorkflow.cs
ERPFlow.Domain/Entities/Product.cs
ERPFlow.Infrastructure/DependencyInjection.cs
ERPFlow.Infrastructure/Persistence/Configurations/ProductConfiguration.cs
ERPFlow.Infrastructure/Persistence/EfProcurementPersistence.cs
ERPFlow.Infrastructure/Persistence/EfReferenceDataQueries.cs
ERPFlow.Infrastructure/Persistence/EfSalesOrderPersistence.cs
ERPFlow.Infrastructure/Persistence/EfServicePersistence.cs
ERPFlow.Infrastructure/Persistence/EfStockTransferPersistence.cs
ERPFlow.Infrastructure/Persistence/Migrations/ERPFlowDbContextModelSnapshot.cs
ERPFlow.Tests/EquipmentWorkflowIntegrationTests.cs
ERPFlow.Tests/GoodsReceiptMigrationTests.cs
ERPFlow.Tests/InventoryCutoverMigrationTests.cs
ERPFlow.Tests/InventorySchemaIntegrationTests.cs
ERPFlow.Tests/InventoryTestDatabase.cs
ERPFlow.Tests/LegacyOriginMigrationIntegrationTests.cs
ERPFlow.Tests/ProcurementMigrationTests.cs
ERPFlow.Tests/StockTransferMigrationTests.cs
docs/openapi/erpflow-v1.json
front/DESIGN.md
front/src/app/core/api/models.ts
front/src/app/core/api/reference.ts
front/src/app/feature/orders/order-new.ts
front/src/app/feature/procurement/purchase-form.ts
front/src/app/feature/reference/reference-view.ts
front/src/app/feature/reference/reference.routes.ts
front/src/app/feature/service/jobcard-detail.ts
```

Other dirty files in the working tree belong to the already implemented Product Image
increments or pre-existing local work; they were neither discarded nor replaced.
