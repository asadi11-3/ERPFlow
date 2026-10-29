# Backend production readiness audit

**Verdict: NO-GO for deployment of the complete ERPFlow backend.**

Audit date: **2026-10-29** (audit host UTC+03:00). Source: local working tree at commit `c2f9fa341e01bbb04c3352d9ac1e166757b695a4`. The initial tracked/untracked Git status was clean; the audit used actual local files, not an earlier report or remote branch. This report is the only intended repository change.

Release compilation, all **521 backend regression tests**, the OpenAPI snapshot checks, the EF model check, publishing, and **55 isolated published-Production smoke assertions** passed. These results do **not** make the complete production workflow deployable: the normal inventory composition deliberately prohibits warehouse writes. Startup migration, transport configuration, credential provisioning, and image-store recovery also require resolution before deployment.

No deployment, commit, push, existing-database migration/reset, existing-image move, or application/configuration source change was performed. `appsettings.Development.json` was preserved. Database writes were confined to newly owned test catalogs. The generated migration SQL was **reviewed but not executed**.

## 1. Exact build, test, and verification results

### Environment and evidence locations

- Audit OS: Windows `10.0.26300`, `win-x64`.
- .NET SDK: `10.0.401`; MSBuild: `18.9.11`.
- Installed host and ASP.NET Core runtime used: `10.0.12`.
- Backend target: `net10.0`; EF Core, JWT bearer, and ASP.NET OpenAPI package versions: `10.0.13`.
- EF CLI: `10.0.11`. Its older-tools warning appeared on both EF commands; neither command failed. Align/pin tooling before a repeatable deployment pipeline.
- Isolated SQL test server: local SQL Express with Windows integrated authentication. No repository application connection was used for verification.
- Temporary artifact root, called **`AUDIT_ROOT`** below:
  `C:/Users/User/AppData/Local/Temp/opencode/erpflow-backend-audit-20261029-41b8c7`.

| Check | Exact result |
|---|---|
| `dotnet restore ERPFlow.slnx --verbosity minimal` | Exit **0**; all projects up to date for restore. |
| `dotnet build ERPFlow.slnx --configuration Release --no-restore --verbosity minimal` | Exit **0**; all five projects built; **0 warnings, 0 errors**; elapsed **00:00:40.29**. |
| Full backend Release tests | Exit **0**; **521 total, 521 passed, 0 failed, 0 skipped**. Runner wall time **00:15:30.7814522**; test console duration **15m25s**. |
| EF pending-model check | Exit **0**; **“No changes have been made to the model since the last migration.”** Older EF tool warning only. |
| OpenAPI contract verification | **3/3 relevant tests passed within the full suite**: full generated-document/snapshot deep equality, image operation security/statuses, and required single binary multipart file schema. Export was disabled; snapshot was not regenerated to make the test pass. |
| Idempotent migration script generation | Exit **0**, offline; **16 migrations**, **144,860 bytes**. Reviewed, not executed. |
| Release API publish | Exit **0**; **88 files**, **576,578,117 bytes**; external temporary exclusion profile used. See section 7 for content limitations. |
| Published Production HTTP smoke | Exit **0**; **55/55 assertions**, including cleanup; startup **5,247ms**, total **17,829ms**. Normal Production DI and guards retained. |
| NuGet advisory query | All five solution projects reported **no vulnerable packages** in the configured sources at audit time. This is a point-in-time feed result, not proof of absence of all vulnerabilities. |

Full regression command, executed with `ERPFLOW_EXPORT_OPENAPI` and `ERPFLOW_TEST_SQLSERVER` unset in the test runner:

```powershell
dotnet test ERPFlow.Tests/ERPFlow.Tests.csproj --configuration Release --no-build --no-restore --results-directory "C:/Users/User/AppData/Local/Temp/opencode/erpflow-backend-audit-20261029-41b8c7/tests" --logger "trx;LogFileName=full-release.trx"
```

Evidence: `AUDIT_ROOT/tests/full-release.trx`, `full-release.metadata.txt`, and `full-release.console.log`. Runner UTC start/end: `2026-10-28T21:47:52.9418559Z` / `2026-10-28T22:03:23.7456095Z` (local audit date October 29). TRX counters confirm zero failed, errored, timed-out, aborted, or unexecuted cases.

Offline EF commands:

```powershell
dotnet ef migrations has-pending-model-changes --project ERPFlow.Infrastructure/ERPFlow.Infrastructure.csproj --startup-project ERPFlow.Api/ERPFlow.Api.csproj --configuration Release --no-build
dotnet ef migrations script --idempotent --project ERPFlow.Infrastructure/ERPFlow.Infrastructure.csproj --startup-project ERPFlow.Api/ERPFlow.Api.csproj --configuration Release --no-build --output "C:/Users/User/AppData/Local/Temp/opencode/erpflow-backend-audit-20261029-41b8c7/migration-idempotent.sql"
dotnet list ERPFlow.slnx package --vulnerable --include-transitive --no-restore
```

`ERPFlow.Api/DesignTimeERPFlowDbContextFactory.cs:7-10` constructs an unreachable, design-only SQL context. These two EF operations use the model/migration assembly and do not run the normal application startup migration. This safety conclusion is specific to the inspected factory and commands.

OpenAPI snapshot: `docs/openapi/erpflow-v1.json`, OpenAPI **3.1.1**, **59 paths / 73 operations**, SHA-256 `997b5556d5d750d54db183197c81e14d65405877b4133964732cb6a14228b8b9`.

Additional verification notes:

- Production invocations of `--seed-professional-demo --dry-run`, `--activate-warehouse-inventory`, and `--reset-development-database` each failed intentionally **before database access**, with the appropriate environment restriction and Windows process exit `-532462766`. They were not treated as failed regression tests.
- LSP initially timed out; a later `Program.cs` result contained three unnecessary-using diagnostics (`CS8019`) and no errors. The compiler result above is authoritative; a complete clean LSP audit is not claimed.
- The temporary smoke-driver project built with zero errors but four warnings (two advisories repeated at restore/build) for its own transitive `System.Security.Cryptography.Pkcs 9.0.11`: GHSA-2fmv-rv2c-59x7 and GHSA-846f-f764-76mx. That package is **absent from the published API dependency manifest**. The driver is disposable audit tooling, outside the release directory; do not distribute it as application content.

### Isolation and real HTTP verification

The regression harnesses use per-fixture GUID-suffixed databases and clean up their owned catalogs. `InventoryTestDatabase.cs:29-49,89-96` and the other SQL fixtures were inspected before execution. Professional-demo regression fixtures use separately named GUID demo-test databases, not the application's configured demo or operational database.

The temporary smoke driver was reviewed and executed against the published API, not an in-memory web host. It explicitly created a previously absent GUID database, recorded database ID/create time/database GUID and an ownership marker, supplied configuration only to its child process, and used fresh in-memory credentials. The API bound an OS-assigned loopback port. Image files were synthetic and stored in a new owned external temporary directory. No cutover, permissive write gate, Development composition, or demo seeder was used.

Evidence: `AUDIT_ROOT/smoke-results-6f206b8f15af4484861a54af02bb29d1/summary.json` and sanitized log files. The exact owned database was `ERPFlow_AuditHttp_20261029_6f206b8f15af4484861a54af02bb29d1`; process stop, ownership-checked database deletion, and owned-image-directory deletion all passed. The migration SQL artifact was not used by this smoke run; normal application startup migrated only this newly created disposable database.

| API area | Published Production observation | Regression coverage |
|---|---|---|
| Login/JWT | Valid administrator and low-privilege login **200**; wrong password **401**; absent/invalid JWT **401**. | JWT validation, malformed input, ownership and role checks in `ApiIntegrationTests`. |
| Products/catalog | Product reference and product detail **200**. | **21** catalog API + **7** integrity cases cover create/edit/activation, concurrency and catalog invariants. |
| Product images | Create **201**, GET **200** with matching strong ETag and decodable PNG, conditional GET **304**, replacement **200** with changed ETag, stale replacement **412**, DELETE **204**, subsequent GET **404**; non-admin mutation **403**. | **63** image API/failure/migration/normalization/OpenAPI/storage cases. |
| Procurement | Warehouses, suppliers, purchase orders and operational PO reads **200**; low-privilege supplier read **403**. | Procurement API/workflow/draft/concurrency/migration/domain cases, including analytics tests in the partial API class. |
| Goods receipts | Authorized document list **200**. | **15** API cases plus workflow/atomicity/concurrency/rollback/safety coverage; normal Production posting rejection is explicitly tested. |
| Inventory | Stocks and movements **409**, the expected Product-authority restriction. | Inventory posting, reconciliation, authority/cutover, SQL guards and race tests. |
| Stock transfers | List **200**, real HTTP draft creation **201**, posting **409** with the exact default-gate reason; no stock movement/posting or Product balance change. | **12** API cases plus domain/atomicity/concurrency/rollback/safety/race tests. |
| Sales | List **200**. | Sales draft/edit/submit/approval/ownership and workflow cases. |
| Dispatch | Delivery notes and machine assets **200**. | Equipment/service API and dispatch consistency/integration cases. |
| Service | Jobcard list **200**. | Parts/reservation/completion/rollback and service API cases. |
| Analytics | Sales, approvals, procurement, receiving, transfers, dispatch and service **200**; inventory **409** under Product authority. | Role, window, aggregation and SQL execution checks. |
| Development-only exposure | `/openapi/v1.json` and `/swagger/index.html` **404**; no CORS response/preflight grants in Production. | Development OpenAPI contract and Production CORS isolation checks also passed. |

Successful warehouse-lifecycle tests frequently use **fixture-only permissions/cutover composition**. Their success proves those workflows under the fixture conditions, not availability in normal Production. The smoke test verified the actual Production restrictions rather than bypassing them.

## 2. Deployment configuration requirements

### Configuration and secrets

`Program.cs` uses `WebApplication.CreateBuilder`. Environment variables override JSON; nested names use `__`. Production may receive secrets through the service manager/orchestrator's protected environment injection. User Secrets are Development-only. No managed secret-store provider is registered in this application: using a vault requires a reviewed provider integration or an external mechanism that injects secrets securely. Do not put secrets in command-line arguments, tracked JSON, logs, the report, or the publish folder.

Base `appsettings.json` has an **empty signing key**, no connection string, and `AllowedHosts=*`. This is not a complete Production configuration. There is no production environment file containing deployment settings. Never copy the Development file into Production.

| Environment variable / setting | Deployment requirement |
|---|---|
| `DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT` | Explicit, consistent **Production** selection. Confirm the actual service process environment. |
| `ConnectionStrings__ERPFlowDb` | Required external SQL Server configuration. Use the intended least-privilege runtime identity, encrypted SQL transport and a validated server certificate; do not reuse test trust settings. No credential value is recorded here. |
| `Jwt__Issuer`, `Jwt__Audience` | Required agreed identifiers matching token issuance and validation. Defaults exist but must be deliberately accepted or overridden. |
| `Jwt__SigningKey` | Required high-entropy externally managed secret; code checks at least 32 characters, which is a length check rather than an entropy guarantee. Plan rotation and emergency invalidation. |
| `Jwt__ExpirationMinutes` | Optional override; default **60**, accepted **1–1440**. Choose an explicitly approved lifetime. |
| `ProductImages__RootPath` | Operationally required explicit, persistent, private absolute directory outside application/web content, release folders and ephemeral/container layers. See section 5. |
| `AllowedHosts` | Restrict to intended hostnames; the checked-in wildcard is not a production host allowlist. |
| `ASPNETCORE_URLS` or `Kestrel__Endpoints__...__Url` | Define intended bindings. An HTTP upstream must be inaccessible to untrusted clients and protected by the chosen TLS edge. |
| `ASPNETCORE_HTTPS_PORT` / HTTPS endpoint configuration | Set consistently if HTTPS redirection is used. Redirection alone does not create a TLS listener. |
| `Kestrel__Certificates__Default__Path`, `Kestrel__Certificates__Default__Password` | Conditional for certificate-file-based Kestrel TLS; certificate and secret remain external. An approved OS certificate store/hosting integration is another option. |
| `Logging__LogLevel__Default`, `Logging__LogLevel__Microsoft.AspNetCore` | Set approved production verbosity, retention and restricted sink access. Do not enable sensitive-data logging. |
| `DemoUsers__EnableAutomaticSeed` | Keep false/unset; Production startup also checks the environment. |
| `DemoUsers__Password`, `ERPFLOW_DEMO_PASSWORD` | **Do not supply in Production.** They are demo-only inputs, not production bootstrap credentials. |

`Cors__AllowedOrigins__0` and later indices are consumed **only in Development**. Setting them in Production does not enable cross-origin access. A separate-origin frontend is therefore blocked at browser integration unless a reviewed explicit Production CORS policy or an approved same-origin proxy arrangement is supplied. CORS absence is not an authentication vulnerability. Do not use wildcard credentialed origins or switch the application to Development to solve integration.

### Authentication, authorization and bootstrap

- `Program.cs:65-103` validates issuer, audience, signature, lifetime and live user existence; 30-second clock skew; explicit `sub`, `name`, and `role` claim handling.
- `EfUserDirectory.cs` uses ASP.NET Identity password hashing and returns failed verification for invalid hash encoding. The conditional legacy ownership identity in the authentication migration is not a usable demo login.
- Endpoint policies and application-layer persisted-user/ownership checks protect sensitive writes. Product/image mutations require a current Administrator. Procurement and inventory workflows also recheck roles; stale token role claims are not sufficient to retain these write permissions after demotion.
- The API has login but no reviewed Production first-administrator provisioning or account-management procedure. A fresh Production database does not receive working demo accounts. Before deployment, define a controlled, auditable first-user provisioning process using correctly hashed unique credentials; do not enable demo seeding or copy a demo database.

Effective role summary:

| Surface | Authorization |
|---|---|
| Login | Anonymous; credentials required. |
| Reference/product/image reads, warehouse reads | Authenticated; relevant workflows verify the persisted actor. |
| Product catalog/image and warehouse mutations | Administrator. |
| Sales create/edit/submit | Sales or Administrator, with ownership rules. |
| Sales approvals | Approver or Administrator. Sales reads have role/ownership-specific visibility. |
| Dispatch/service mutations | Technician or Administrator. |
| Equipment/service reads | Any authenticated role; organization-wide operational visibility is intentional in current tests and requires business sign-off. |
| Supplier/PO commercial reads | Administrator, Purchasing, Approver. |
| Supplier/PO writes | Administrator, Purchasing; approval decisions use Approver/Administrator. |
| PO operational reads | Administrator, Purchasing, Approver, WarehouseOperator. |
| Goods receipts/transfers | Administrator, WarehouseOperator. |
| Operational inventory | Administrator, WarehouseOperator, Purchasing, Approver; also requires Warehouse authority. |
| Analytics | Domain-specific persisted-role checks; sales/purchasing ownership restrictions where implemented. |

### HTTPS, reverse proxy, exceptions and logging

`Program.cs:186` enables HTTPS redirection, but the application contains no explicit trusted forwarded-header setup or HSTS middleware. The published Production API accepted login over owned loopback **HTTP** when no HTTPS port was configured; a sanitized warning recorded the missing HTTPS port. Thus Production selection plus `UseHttpsRedirection` does not fail closed against plaintext authentication.

Choose and test the actual topology: direct Kestrel TLS with external certificate provisioning, or an HTTPS reverse proxy with a protected upstream and appropriately trusted scheme/client-address forwarding. Configure HSTS at the appropriate edge/application layer. Do not broadly trust arbitrary forwarded headers. IIS hosting integration can supply hosting-specific behavior; it was not tested here. Verify there are no redirect loops and that HTTP is rejected or redirected before credential submission.

`ApiExceptionHandler.cs:25-80` returns generic 5xx details and logs server exceptions; image IO/database failures become **503**. Expected 4xx details use the exception message. No explicit sensitive-data EF logging or credential/request-body logging was found in the reviewed application paths. Exception logs may nevertheless include SQL, paths and operational details: restrict access, configure retention/redaction, and verify the real production sink. Startup failures occur before the HTTP exception handler can serve a response and require service-level monitoring.

There is **no dedicated liveness/readiness endpoint**. A running process or a 401 response alone does not validate database access, image storage or inventory capability. Section 8 specifies interim authenticated checks and the missing health-contract requirement.

## 3. Database readiness and deployment safety

### Migration inventory and compatibility

The source and generated script contain these migrations in order:

| # | Migration |
|---|---|
| 1 | `20261017090727_InitialCreate` |
| 2 | `20261017092052_Phase3ApplicationWorkflow` |
| 3 | `20261017112122_Phase4AuthenticationApi` |
| 4 | `20261024083830_Phase5EnterpriseShowcase` |
| 5 | `20261024110336_CriticalWorkflowConsistency` |
| 6 | `20261025184342_InventoryFoundation` |
| 7 | `20261025193004_WarehouseSourcePreparation` |
| 8 | `20261026140337_AtomicInventoryCutoverRehearsal` |
| 9 | `20261026154834_ProcurementPhase3` |
| 10 | `20261026203843_GoodsReceiptPartialReceiving` |
| 11 | `20261027133632_StockTransfersPhase5` |
| 12 | `20261028094525_ProductImagesPhase6A` |
| 13 | `20261028114503_ProductCatalogPhase6A2` |
| 14 | `20261028131436_ProfessionalDemoSeederPhase6B` |
| 15 | `20261028143605_HistoricalBusinessClockPhase6B1` |
| 16 | `20261028154045_ProfessionalDemoSeedOperationsPhase6B2` |

The recent missing-image/product-not-found fix changes application behavior and error contracts; it adds no migration. Phase 6A creates image associations, durable object registry and store binding. Phase 6A.2 adds active-state/catalog audit history. Phase 6B adds demo manifest/checkpoint tables; 6B.1 updates receiving procedures for the business clock. These schema changes do not automatically seed professional demo data or activate warehouse authority.

Reviewed SQL: `AUDIT_ROOT/migration-idempotent.sql`; SHA-256 **`7a3d217012a1225659a730b4df7a36d3c6e1a1763130fe413391a41e2504e3ac`**; 3,128 lines, 16 migration transactions. Static review found no `DROP TABLE` or `DROP COLUMN` in the forward script. It does drop/recreate indexes, checks and generated defaults, performs data backfills, and installs/replaces triggers and procedures. It is not risk-free or a target-schema repair tool.

Important preconditions and hazards:

- Authentication backfills missing ownership and can create a synthetic legacy owner (`migration-idempotent.sql:275-333`).
- Critical consistency replaces the delivery-note index with a unique index (`:572-595`); conflicting existing records can stop migration.
- Warehouse-source preparation introduces `LEGACY-DEFAULT`, source relationships, filtered uniqueness and constraints (`:842-1115`). Inspect existing reservations/source associations and code conflicts before applying.
- Inventory authority triggers fence compatible writers, freeze legacy Product balances after cutover and protect authority metadata (`InventorySqlGuards.cs`). Do not disable triggers, alter session-context checks, manually flip authority, or run an older incompatible writer.
- Receiving constraints/triggers/procedures (`ReceivingSqlGuards*`, including `AdmitGoodsReceipt` and `PostGoodsReceipt`) and transfer guards enforce document ownership, quantities, posting state and balance conservation. Apply the full ordered migration chain; do not cherry-pick Phase 6 scripts without prerequisites.
- Historical migrations call shared SQL-generation helpers. Preserve the exact audited source/script and checksum; regenerating from a later source tree may produce different historical SQL.
- `__EFMigrationsHistory` controls idempotent skipping. Missing history with pre-existing objects can fail; existing history with manual schema drift can silently skip needed repairs. `GO` requires a batch-aware SQL deployment tool.
- SQL Server features include filtered indexes, `rowversion`, `SESSION_CONTEXT`, application locks, JSON checks and `CREATE OR ALTER PROCEDURE` (SQL Server 2016 SP1-era features or newer). Use a currently supported SQL Server version and validate compatibility level, collation, permissions and execution plan on the actual target/restore clone. The target was not inspected.

### Startup is a deployment blocker

`Program.cs:207-220` resolves a database context and **unconditionally calls `Database.MigrateAsync()` before serving requests**, including Production. There is no configuration switch to disable this behavior. Migration can occur before hosted startup validation of all options completes; an invalid JWT configuration is not a safe guard against database changes.

Recommended remediation is a separately reviewed change that moves schema application to an explicit deployment step with a dedicated migration identity and leaves runtime startup with a read-only schema compatibility check. Use a DML/procedure-scoped runtime identity. Do not assume pre-applying the script alone removes startup migration execution, and do not solve this by permanently granting the runtime principal broad DDL access. Test startup under the real least-privilege grants after the change.

### Proposed SQL deployment plan — not executed

1. Resolve the inventory/runtime architecture blockers before scheduling rollout. Confirm deployment OS, supported SQL Server and service identities.
2. Obtain a verified full database backup plus required differential/log chain, and a coordinated full image-store backup. Record recovery point/RPO/RTO and backup encryption/access controls.
3. On an authorized restored clone, inspect migration history, schema drift, duplicate delivery notes/SKUs/source keys, reservation consistency, receipt/transfer state, inventory authority and reconciliation output. Do not use the live database as a rehearsal.
4. Rehearse the exact checksummed idempotent script with a batch-aware tool and dedicated migration credentials. Validate triggers, procedures, constraints and final history; execute representative workflows with the intended runtime permissions. Re-run the script on the clone to verify expected no-op behavior.
5. Rehearse paired SQL/image restore and compatible application rollback. Inventory cutover is a separate approved operational change, not an automatic part of schema deployment.
6. After explicit deployment authorization: quiesce all writers and image cleanup, take the final coordinated recovery point, apply the reviewed script once through the controlled deployment job, verify schema/history, then start one compatible instance and run the health procedure. Only then admit traffic.

### Seeder safety

Normal Production startup does not automatically seed demo users. The professional-demo CLI rejects non-Development environments before opening a database (`Program.cs:33-42`); its target must match the dedicated local demo naming policy and exact local SQL Express connection (`ProfessionalDemoDatabaseGuard.cs`). Existing targets additionally require the professional-demo ownership manifest (`ProfessionalDemoSeeder.cs:41-95`). Production activation/reset CLI attempts were also verified rejected.

There are still defense-in-depth limitations: both seeders are registered/compiled into the application; `DevelopmentDataSeeder.SeedAsync` has an internal-call `allowProduction` parameter, although the normal Production host does not invoke it. `ProfessionalDemoSeeder` relies on the host for the environment check. Preserve the normal host guards; do not add an HTTP seeding route or call these services from production initialization. Deployment controls must enforce Production, omit all demo switches/passwords and prohibit a Development-configured service against operational data. A separate follow-up should remove development credential defaults from the release path.

## 4. Inventory gate status — architectural NO-GO

`ERPFlow.Infrastructure/DependencyInjection.cs:38-43` always registers:

- `IInventoryWriteGate` → **`ProductAuthorityWriteGate`**, whose sole operation always throws a workflow conflict (`ProductAuthorityWriteGate.cs:6-10`).
- `IInventoryCutoverPermission` → **`DisabledInventoryCutoverPermission`**, whose operator permission always throws (`DisabledInventoryCutoverPermission.cs:6-10`).

Only Development overrides the runtime write gate (`Program.cs:171-175`). Neither a Production environment variable, approval payload, existing Warehouse authority row nor successful test fixture cutover supplies a production-ready permission path.

| Operation in normal Production DI | Database authority: Product | Database authority: Warehouse |
|---|---|---|
| Login, catalog/image operations, procurement documents and authorized ordinary reads | Available subject to their own roles/validation. | Same. |
| Product catalog create/update/activate | Available; new product starts with zero stock and does not create opening stock or a warehouse balance row. | Available; no stock is provisioned. |
| Sales draft create/edit | Available with legacy-default source; non-default source rejected. | Available for valid active persisted sources. |
| Sales submission/reservation; reject/return when releasing reservations | Legacy Product-stock path can operate with valid balances/state. | Warehouse reserve/release path hits the throwing gate and is blocked. Approval-only operations still follow their document rules. |
| Sales dispatch | Legacy Product reservation consumption can operate. | Warehouse consumption hits the gate and is blocked. |
| Service jobcard creation | Available. | Available. |
| Service parts request/completion | Product-backed reserve/consume can operate with valid stock and origins. | Warehouse reservation/consumption blocked. Completion calls the warehouse consumption gate even for an empty parts collection. |
| GRN creation and posting | **Blocked** by the write gate. | **Still blocked**, despite correct database authority. |
| GRN cancellation | Cannot complete: posting-scope persistence requires Warehouse authority. | Document-only cancellation can operate with valid actor/state/version; no write-gate call. |
| Transfer draft creation/cancellation | Available, document-only. | Available, document-only. |
| Transfer posting / direct warehouse postings | **Blocked**. | **Still blocked**. |
| Operational inventory stocks/movements and inventory analytics | **409** until Warehouse authority. | Reads available to authorized roles. |
| Cutover operator | **Blocked** by disabled permission. | No permissive normal-Production path; existing Warehouse state does not unlock writes. |

Source paths: `GoodsReceiptWorkflow.cs:16-98`; `EfGoodsReceiptPersistence.cs:74-78`; `StockTransferWorkflow.cs:15-110`; `SalesOrderWorkflow.cs:132-166` and release callers; `EquipmentWorkflow.cs:64-69`; `ServiceWorkflow.cs:112-135,186-203`; `EfWorkflowInventory.cs:11-77`; `ProductAuthorityWarehouseSourcePolicy.cs:11-20`; `InventoryQueryWorkflow.cs:20-55`; `EfAnalyticsQueries.Operations.cs:43-49`.

**Exact cause:** schema and business workflows support warehouse authority, but the normal Production composition intentionally has no approved warehouse-write or cutover authorization mechanism. Remaining on Product authority preserves legacy sales/service capabilities but leaves receiving, transfer posting and operational inventory unavailable. Switching the database to Warehouse would also block the sales/service stock paths under the unchanged runtime composition.

**Safe remediation:** implement and review a dedicated controlled cutover/operator host plus a production runtime gate that validates the durable authority/protocol/reconciliation prerequisites. Rehearse on restored isolated data; retain fencing, SQL triggers, rowversion, immutable ownership, idempotency and fail-closed behavior. Provide regression and normal-Production integration evidence for both states. **Do not enable cutover, return permissively from these guards, manually update authority metadata or deploy Development composition as a workaround.** No such changes were made by this audit.

## 5. Product image storage readiness

### Actual implementation and deployment requirements

`LocalProductImageStorage.cs:21-34` uses configured `ProductImages:RootPath`, otherwise the service account's local-application-data directory under `ERPFlow/ProductImages`. It rejects filesystem root, the user-profile root, application base/current directory, content/web roots and descendants. It does not itself guarantee that an otherwise valid location is durable, externally backed up, or outside all temporary filesystems. Supply an explicit dedicated persistent production path.

Initialization is **lazy**. Startup alone does not validate storage. `StoreIdentity`/a real image operation must exercise it. On Windows, initialization applies a protected owner ACL granting the current identity inheritable Full Control. On Unix, it requires an owner-only directory (0700) and creates files owner read/write (0600). Symlinks and reparse points are rejected on the path/ancestors. Ensure the service identity can create/open/read/write/flush/lock/delete files and traverse the parent path; plan backup access without weakening the application ACL. Verify free space, quotas, disk-full behavior, mount durability and alerting on the actual host.

Only newly owned temporary storage was exercised here. **Production filesystem permissions, mount persistence, real-image availability and backup restoration remain unverified.** Existing image files and configured RootPath were not changed.

### Lifecycle, validation and concurrency

- Authenticated reads; Bearer-only current-Administrator mutations; no cookie authentication dependency. Disabling antiforgery is scoped to that bearer upload design.
- Exactly one multipart `file`; maximum **5MiB** file / **6MiB** request; maximum edge **8192**, maximum **16,000,000 pixels**. JPEG/PNG/WebP declarations/signatures/decoding are validated and normalized/re-encoded (`SkiaProductImageNormalizer.cs`, `ProductImageRequestMiddleware.cs`). Reverse-proxy limits must permit the application's intended request size while still bounding upload traffic.
- Original filenames never determine storage paths. Keys are lowercase 32-character hexadecimal identifiers. Objects use `CreateNew`, durable flush and no overwrite (`LocalProductImageStorage.cs:55-92`).
- PUT records a durable one-hour Pending object, writes bytes, and publishes metadata in a serializable transaction. Replacement atomically retires the previous object. Failed publication leaves recoverable registry work rather than exposing incomplete metadata (`EfProductImageWorkflow.cs:27-79`).
- First PUT can create without a precondition; `If-None-Match: *` is supported for explicit create-only behavior. Replacement/deletion require the exact strong rowversion ETag (`If-Match`); omission **428**, stale **412**, invalid/conflicting validators **400**. GET supports matching weak comparison/`*` for **304**. ETags are not authentication credentials.
- GET distinguishes **`product_image_missing`** from **`product_not_found`**. Existing-image retrieval verifies store identity, active registry and file size. IO/storage failure is not disguised as missing-image 404. Image responses use private revalidation caching and `nosniff`.
- Per-product exclusive filesystem leases coordinate mutation/GET/cleanup across processes using the same store. SQL rowversion and transactions provide additional concurrency control.

### Cleanup, backups and restore constraints

The worker runs every **five minutes**, selecting at most **64** DeletePending or expired Pending objects per run. It reacquires a product lease, verifies store identity and rechecks state/references transactionally before deleting. Active/referenced files are protected; failures retain durable work for retry. It processes registry entries rather than recursively deleting arbitrary unknown files (`ProductImageCleanupWorker.cs:7-18`; `EfProductImageWorkflow.cs:125-173`). Monitor backlog and repeated warnings/errors.

The store binding is **SHA-256 of machine name + canonical absolute root + `.store-id`**. SQL `ProductImageStores` must exactly match it. This is a deliberate fail-closed identity, not a portable storage identifier:

- Multiple processes on the same host/root/marker can coordinate.
- A different host name or root changes identity, even if files and `.store-id` are copied.
- Active multi-host replicas against one database are not supported by this adapter.
- There is **no supported automatic rebind/migration path**. Do not manually replace the database identity or change RootPath as a deployment shortcut.

Required backup/restore procedure: stop all writers and cleanup; take coordinated SQL and full-directory backups including `.store-id`; restore both to the same recovery point, machine identity and canonical path; verify owner/ACL, absence of links, active SQL key/file-size manifest and pending/deletion states **before starting any instance**; then validate authenticated reads. After five minutes a restarted worker can process restored expired candidates, so starting before reconciliation is unsafe. New-host/path recovery requires a separately reviewed offline identity migration/reconciliation procedure. Recovery portability and a successful paired restore drill are release gates, not established by the local smoke test.

### SkiaSharp / target OS

The project references `SkiaSharp 4.154.0` and `SkiaSharp.NativeAssets.Linux.NoDependencies 4.154.0`; native assets are present in the published output. The actual published Windows API successfully normalized and served PNG data. The regression suite additionally exercised JPEG/PNG/WebP and malformed/oversized inputs.

For Windows, match service-process architecture to the native Win32 asset and verify native loading under the actual service account. For Linux, select the correct architecture and glibc/musl RID and supported distribution; check `libSkiaSharp.so` dependencies on the target. “NoDependencies” removes the fontconfig dependency, **not** the OS loader/libc/libm/libpthread/libdl requirements. A portable managed DLL alone does not validate native portability. No Linux/native-container execution or production ACL check was performed.

## 6. Security findings

### Critical

**No confirmed Critical vulnerability** was established by the reviewed source, regression suite or isolated HTTP checks. This is not a penetration-test certification. Team-mode security tooling was unavailable; bounded source reviews and safe local verification were used.

### High / high-impact conditional findings

| ID | Finding and concrete precondition | Impact / evidence | Required remediation |
|---|---|---|---|
| H-01 | Plaintext authentication if a deployment exposes the API's HTTP listener without enforced TLS at the edge. | Owned Production HTTP login returned **200**. An on-path attacker on an exposed plaintext connection could obtain passwords/bearer tokens. Actual target exposure was not tested. `Program.cs:186` alone is insufficient. | Enforce/test TLS and trusted proxy handling; restrict the upstream; establish HSTS and certificate rotation. |
| H-02 | Known demo-default credential exposure if the deployment is mislabeled Development and automatic seeding is enabled, or an internal caller uses the seeder's Production override. | Compiled development seeder/default credential remains in the assembly. Role-bearing demo accounts would permit privileged access if seeded with that known default. Normal Production paths were verified guarded. | Remove release-path demo credential defaults in a follow-up; enforce Production and no seed flags; inspect authorized target account inventory before launch. Never use the demo seeder for real bootstrap. |
| H-03 | Excessive database privilege/startup mutation if the runtime identity is granted DDL rights for unconditional startup migration. | Startup can alter schema before serving traffic; compromise of that identity has broader database impact. This is a high operational/privilege-design risk, not a demonstrated remote exploit. `Program.cs:207-220`. | Separate migration and runtime identities and execution; fail closed on schema mismatch without applying migrations at service startup. |

The inventory gate is a **separate confirmed functional NO-GO**, not an authorization bypass. Missing target recovery/storage/TLS evidence also blocks approval even though local compilation succeeds.

### Other findings and positive controls

- **Medium:** anonymous login has no application rate limiter, lockout/backoff or bounded abuse policy. Reachable attackers can attempt password guessing or password-hash CPU exhaustion. Add/verify per-account and per-source controls and alerting at an appropriate layer; no brute-force load attack was run.
- **Medium / policy decision:** equipment/service reads expose broad operational data to any authenticated role. Existing tests explicitly preserve this behavior. Obtain business authorization for single-organization visibility or introduce scoped access in a separate change.
- **Medium:** no security stamp/session revocation mechanism; user existence is checked, but a stolen token normally lives until expiry/user deletion/key rotation. Persisted-role rechecks mitigate sensitive mutation demotion; short approved token lifetimes and a rotation/revocation plan are still needed.
- **Low / operational:** 4xx exception text can reveal workflow/state details; 5xx client responses are generic. Production logging/redaction/sink access was not verified on a target host.
- No usable Production JWT key or credential-bearing connection string was found in the inspected base configuration. Development/test credential literals exist and must not be treated as production secrets. Their values were not copied into this report.
- Production Swagger/OpenAPI are not mapped: real HTTP **404** checks passed. Production did not grant Development CORS origins. All mapped API families were reviewed for authentication/policies; login is intentionally anonymous.
- Upload limits, decode/format validation, immutable private keys, path checks, concurrency and current-role checks are substantive existing controls. No traversal or anonymous-upload bypass was established.
- No production secret-store, SQL grant, firewall, certificate, target log sink, key rotation, load, penetration or disaster-recovery drill was verified. These limits are explicit; source inspection cannot substitute for them.

## 7. Release publish result and artifact contents

Successful command:

```powershell
dotnet publish ERPFlow.Api/ERPFlow.Api.csproj --configuration Release --no-build --no-restore --output "C:/Users/User/AppData/Local/Temp/opencode/erpflow-backend-audit-20261029-41b8c7/publish" /p:PublishProfileFullPath="C:/Users/User/AppData/Local/Temp/opencode/erpflow-backend-audit-20261029-41b8c7/SecretFreeRelease.pubxml"
```

- Artifact: **`AUDIT_ROOT/publish`**; inventory: **`AUDIT_ROOT/publish-manifest.json`**.
- Exit **0**; **88 files**, **576,578,117 bytes** (approximately 549.87MiB); **34 packages**, **31 runtime-asset files**.
- `ERPFlow.Api.dll` SHA-256: **`93f316787c29c0debb37f9fec977142ebfff00a8b414feab41ff6e4832350d76`**.
- Framework-dependent `net10.0`, no specific RID in dependency target. Use `dotnet ERPFlow.Api.dll` with installed ASP.NET Core/.NET 10 runtime. A platform-specific apphost/native asset must not be assumed portable merely because the managed target is portable.
- Only safe base `appsettings.json` is included: blank signing key, no connection strings or RootPath override. External profile excluded all suffixed appsettings/local environment files and launch/test/upload content. No post-publish deletions were necessary.
- No test assemblies/databases, actual uploaded images, Development JSON, user-secret files, private certificate/key files or local configuration overrides were present in the inventoried output.
- **Release-content limitation:** compiled Development/professional seeder code and the hardcoded demo credential default remain part of the current application assembly. Excluding JSON does not remove compiled literals. Therefore this is an **audit publish artifact, not an approved secret-free production deliverable**. Removing that default/release path requires a separately reviewed source change; the audit did not alter business logic or binary-patch assemblies to hide the finding.

Do not deploy the temporary profile, driver, test logs, test credentials/resources or all of `AUDIT_ROOT`. The publish folder is retained for review only while the verdict is NO-GO.

## 8. Required fixes, prerequisites and operational procedures

### Required before deployment approval

| Priority | Required outcome |
|---|---|
| Blocker | Approved Production inventory authority/write composition and controlled cutover procedure, with normal-composition tests proving receiving, transfer posting, sales dispatch/reservations and service consumption. Preserve all current safety guards. |
| Blocker | Explicit migration deployment lifecycle; remove/replace unconditional Production startup migration, test least-privilege startup, and rehearse the exact SQL on an authorized restore clone. |
| Blocker | Agreed target OS/topology, external secret provisioning, usable Production accounts, SQL transport/grants, TLS and restricted host/upstream configuration. Resolve cross-origin frontend topology if applicable. |
| Blocker | Explicit durable image path/service identity, proven ACL/native loading, paired backup/restore, and a reviewed same-host or cross-host recovery strategy. Local-store active multi-host deployment is not supported. |
| Release content | Remove the compiled demo default credential from the production release path; preserve explicit Development-only tooling safeguards. |
| Operations/security | Define health/readiness behavior, login abuse protection, logging/monitoring, key/certificate rotation, and sign off broad read visibility/revocation limitations. |
| Reproducibility | Pin compatible SDK/EF tooling and record target RID/package/artifact hashes; rerun the relevant verification after authorized remediation. |

### Server prerequisites

1. Supported .NET 10 ASP.NET Core runtime, matching architecture and current servicing level; IIS Hosting Bundle if IIS is chosen. SDK is needed on build/migration tooling hosts, not inherently on the runtime host.
2. Supported SQL Server with required features and validated compatibility/collation; encrypted connectivity; separate migration/runtime identities; required DML and stored-procedure execution/application-lock permissions proven on a clone. Runtime must not be a server administrator.
3. Explicit service identity, working/content directory, controlled environment injection and protected certificate/secret sources.
4. Durable image volume with correct ownership, private permissions, locking semantics and tested native Skia runtime assets. Disk/capacity and cleanup-backlog monitoring.
5. Trusted TLS edge/direct TLS, restricted backend network exposure, approved hostnames/origins and request size/time limits compatible with 6MiB uploads.
6. Coordinated SQL/image backup tooling, verified restoration, protected logs, process restart policy and alerting. Target-specific checks remain outstanding.

### Startup and health verification procedure — for a future authorized rollout

1. Verify the approved artifact checksum, Production environment and absence of demo switches/configuration. Check the schema history through the controlled deployment process before application startup. **Current source still migrates on startup; resolve that blocker first.**
2. Verify external database/JWT/image configuration without logging values; verify secret access, certificate chain, service identity and storage permissions. Confirm supported runtime/native libraries.
3. Start one instance with traffic withheld. Observe startup failure/timeout and bounded resource use. Verify no automatic seeding or unexpected schema/data mutation.
4. Check TLS/redirect/HSTS/proxy behavior from the intended client path. Confirm `/openapi/v1.json` and `/swagger/index.html` remain 404; missing token returns 401 and disallowed role 403. A 401 is only a routing/auth reachability check.
5. Using a controlled validation identity, verify login, an authenticated database-backed read, required business-role queries and inventory authority/reconciliation status. Do not treat expected 409 gate responses as a successful full-feature launch.
6. Verify authenticated GET of a known backed-up image, storage binding and ETag. Perform write/replace/delete checks only with an explicitly owned validation product/resource and authorization; never overwrite a customer's image as a health probe. Verify cleanup safety/backlog monitoring.
7. Validate frontend integration, approved workload smoke cases and logs. Admit traffic gradually only after readiness criteria pass. Add a properly scoped health endpoint/contract in follow-up work; never use the mutating image or seeding paths as recurring health checks.

### Rollback plan

- Stop admission of writes and stop all application/cleanup processes before rollback. Capture incident diagnostics without secrets.
- Application-only rollback is permitted only when the prior binary is demonstrably compatible with the current schema, writer protocol and authority state. SQL triggers intentionally reject incompatible writers after cutover; do not remove them to make old binaries work.
- Do **not** assume `dotnet ef database update <older>` is a viable rollback. `WarehouseSourcePreparation`, `AtomicInventoryCutoverRehearsal`, Procurement, Goods Receipt, Stock Transfer, Product Images and Product Catalog migrations deliberately throw from `Down`. Earlier downs and demo-table downs can destroy data; 6B.1 changes procedures.
- Prefer a verified forward repair when appropriate, or restore the approved SQL/log recovery point **together with the full image store** and a compatible binary. Quantify lost post-backup transactions and reconcile external effects before reopening.
- Preserve image machine identity, canonical root and `.store-id`; a new host/path cannot be recovered by merely copying files or changing RootPath. Follow the separately approved rebind/migration procedure if the original identity cannot be restored.
- Validate migration history, SQL constraints/procedures, inventory reconciliation, user access and image SQL/file consistency offline, then run single-instance authenticated readiness checks before resuming traffic. No rollback or backup restore was executed against an existing database during this audit.

## 9. Conclusion

**NO-GO.** Current source builds, passes its regressions, publishes and runs under isolated Production configuration. The audit also reproduced its intentional warehouse restrictions. A green test suite that includes permissive test fixtures must not be interpreted as an enabled Production inventory system.

The release remains blocked by the inventory composition, unconditional startup migration and unresolved deployment/recovery requirements. The published artifact and unexecuted SQL script are retained for review. Remediation and deployment require a separate authorized phase; this audit did not enable cutover, weaken guards, change business logic/configuration, move images or alter any existing database.

### Reference documentation

- [ASP.NET Core 10 configuration and provider precedence](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0)
- [ASP.NET Core proxy/load-balancer configuration](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
- [ASP.NET Core HTTPS enforcement](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0)
- [EF CLI and tool updates](https://learn.microsoft.com/en-us/ef/core/cli/dotnet)
- [Applying EF migrations and idempotent scripts](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
- [.NET deployment/native dependencies](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
- [SkiaSharp Linux native package 4.154.0](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies/4.154.0)

Protected Development configuration SHA-256: `fd0e460e8b05a8adfc8ed87e32de0d29110b095277c8bfa408b8043e18532ffd`. Base configuration SHA-256: `b2f7bc4a6145160a5af0b8ffc4b24d34bd5b4c485976892b226c1455bc43c337`. Final comparison confirmed both unchanged from audit start, along with the OpenAPI snapshot and published API DLL. These are integrity fingerprints, not configuration/credential contents. Final Git status contained only this new report; `git diff --check` passed and the report had no trailing whitespace. Markdown LSP was unavailable; no server/configuration was installed or changed for this audit.
