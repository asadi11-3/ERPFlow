# ERPFlow — MonsterASP.NET Windows IIS deployment

## Demo release: existing Cloudflare UI + MonsterASP API (2026-10-29)

**Startup diagnostics replacement (use this instead of the earlier API ZIP when addressing HTTP 500.30):**
`C:/Users/User/AppData/Local/Temp/opencode/ERPFlow-MonsterASP-StartupDiagnostics-20261029/ERPFlow-Api-win-x86-startup-diagnostics.zip`.
The replacement contains 60 files; SHA-256 `3272efcb563b4ea2d18bb1014e26300e9040cff648ebbd4bf30676dc39b9097c`. It adds non-sensitive startup failure codes without changing migration, inventory admission or private-image requirements. Once extracted to `/wwwroot` and the pool restarts, inspect `FailureCode=...` or the parenthesized code in the 500.30 startup log. `IMAGE_ACL_DENIED` means the application-pool identity cannot apply the protected private-folder ACL; `IMAGE_STORAGE_IO_FAILED` means private-store I/O or a rejected reparse point; `IMAGE_STORE_IDENTITY_MISMATCH` requires reviewed SQL/file recovery. `SCHEMA_INCOMPATIBLE` and `DATABASE_ACCESS_DENIED` require schema-history or runtime SQL privilege review; `INVENTORY_AUTHORITY_REJECTED` requires operator/reconciliation review, not a runtime bypass. A Product-authority database alone does not fail startup, but reports readiness 503. The previous log's generic message cannot distinguish these causes. Never delete `.store-id`, relax ACLs, or rerun migrations to silence a code.

Use the new local release root:

`C:/Users/User/AppData/Local/Temp/opencode/ERPFlow-MonsterASP-Demo-20261029-r1`

- **MonsterASP upload:** `ERPFlow-Api-win-x86.zip` (API only, framework-dependent .NET 10 x86, IIS InProcess, workstation GC).
- **Frontend:** `frontend/browser/` and `ERPFlow-Frontend.zip`, built for `https://demperpsystetmkdt.runasp.net`. Keep the existing UI at `https://erpflow-web.mohammadabuaisheh40.workers.dev`.
- **PC-only administration:** `operations/` and `Prepare-db76540.ps1`; neither is in the API ZIP.
- **Artifact checks:** `artifact-verification.json` and per-ZIP SHA-256 manifests. The detailed verification table later in this document records the earlier remediation baseline, not this new artifact's hashes.

| New release check | Result |
|---|---|
| API ZIP | **60 files**, **37,227,794 bytes**, SHA-256 `0bd56290c8c8abd25fe5f8ba16eb7e94af6e40d3413bbc024d065194208704aa` |
| Frontend ZIP | **61 files**, **503,223 bytes**, SHA-256 `9b22e7786998066f82dc0a4b7926dfbd0439807c0dabf5a904ed17e64745e3ef` |
| Local x86 smoke | **74/74 passed** under a 256,000,000-byte process limit; peak private bytes **58,507,264**, including the missing-RootPath default, real API login, image/restart and workers.dev CORS. Evidence: `smoke-ca2d5972a2f24de1b0567e0d18afdb37/summary.json`. |
| Frontend | **118/118 tests**, lint and `production,monsterasp` build passed; Wrangler `--dry-run` passed. |
| Backend | Focused hosting/storage tests **22/22**; Release solution build **0 warnings / 0 errors**. Previous full remediation regression **555/555** predates this small default-path change. |

The supplied `http://demperpsystetmkdt.runasp.net/` hostname has **not** yielded a verified HTTPS response in this preparation. Confirm that HTTPS is active before updating the live Cloudflare Worker: an HTTPS page cannot use an HTTP API.

### Automatic private image path

When `ProductImages:RootPath` is absent in **Production**, startup now uses:

```csharp
Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "private", "ProductImages"))
```

MonsterASP's `/wwwroot` application root therefore uses the existing sibling `/private/ProductImages`. **Leave `ProductImages__RootPath` unset** for this layout. Remove any old relative `/private/ProductImages` value: explicit values take precedence and invalid values are still rejected. Directory privacy, ACL, reparse-point, identity/binding and restart checks remain active. Development and Staging do not receive this default. Provider reference: [private folder layout](https://forum.monsterasp.net/d/231-how-do-i-programmatically-access-private-folder-from-aspnet-core-app).

### Prepare db76540 from Windows — user-run only

In MonsterASP **Databases → db76540 → Users → Remote access**, enable remote access and copy the public SQL server hostname (normally `db76540.public.databaseasp.net`). Have .NET 10 installed on the PC. Keep the website offline during setup. The supplied script prompts privately for credentials, targets only `db76540`, checks each exit code, preserves non-secret preparation state for resuming, and clears credential environment variables in `finally`. It never saves passwords.

```powershell
$release = 'C:\Users\User\AppData\Local\Temp\opencode\ERPFlow-MonsterASP-Demo-20261029-r1'
$setup = Join-Path $release 'Prepare-db76540.ps1'

# Recommended: one SQL password prompt; runs all five steps in order.
& $setup -SqlServer 'db76540.public.databaseasp.net'

# Alternatively, run individual steps, stopping if a step fails (do not run both sequences):
# & $setup -Step migrate
# & $setup -Step first-admin
# & $setup -Step prepare-cutover
# & $setup -Step complete-cutover
# & $setup -Step verify
```

The five underlying commands are `dotnet ERPFlow.Operations.dll <step> --confirm-database db76540`; `complete-cutover` additionally receives the script-generated `db76540-setup/approved-cutover.json`. The script obtains the real first-admin ID and preparation fingerprint; it never fabricates them or enables the Development seeder. The empty-database workflow refuses nonzero product/reservation/service counts and asks you to review the approval before cutover. Keep the chosen Administrator email/password for the UI login. An empty database has no demo business records until you create them through the application.

The PC uses **Remote** SQL access with `Encrypt=True;TrustServerCertificate=True`, as required by [MonsterASP's SQL TLS documentation](https://help.monsterasp.net/databases/mssql/secure-connection). This encrypts traffic but trusts the provider certificate without CA validation. Use the **Local access** connection string from the panel for the hosted API. Remote access can be disabled after PC setup if no longer needed. No hosted SQL command has been executed during this preparation.

### File Manager installation

1. Enable HTTPS for `demperpsystetmkdt.runasp.net`; the HTTPS Cloudflare UI cannot use an HTTP API. Select .NET 10, x86 / 32-bit, InProcess.
2. Create `/wwwroot/app_offline.htm` for maintenance; keep `/private/ProductImages` intact.
3. Run the PC database setup above. In **Website → Scripting → Environment variables**, set the following values privately:

   | Variable | Value |
   |---|---|
   | `DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `AllowedHosts` | `demperpsystetmkdt.runasp.net` |
   | `ConnectionStrings__ERPFlowDb` | Local-access runtime connection for `db76540` from the panel |
   | `Jwt__Issuer` | `ERPFlow` |
   | `Jwt__Audience` | `ERPFlow.Web` |
   | `Jwt__SigningKey` | Private randomly generated key, at least 32 characters |
   | `Cors__ProductionOrigins__0` | `https://erpflow-web.mohammadabuaisheh40.workers.dev` |

   Leave `ProductImages__RootPath` unset; keep demo seeding disabled.
4. **Websites → site → Files → Upload file**: upload `ERPFlow-Api-win-x86.zip` outside `/wwwroot`, then **Unzip → Target `/wwwroot`**. Select **Overwrite existing files** and **Restart application pool before extracting**. The archive has no wrapping directory: `web.config` and `ERPFlow.Api.dll` land directly in `/wwwroot`. Wait for the ZIP job to finish; delete the uploaded archive afterwards. Preserve the maintenance file until configuration is ready.
5. Remove `app_offline.htm`, restart, and require HTTPS `/health/live` and `/health/ready` to return 200.
6. Update the existing Cloudflare Worker with the prepared frontend, from the repository's `front` directory, only when ready to deploy:

   ```powershell
   npx wrangler deploy --assets 'C:\Users\User\AppData\Local\Temp\opencode\ERPFlow-MonsterASP-Demo-20261029-r1\frontend\browser'
   ```

   For later builds use `npm run deploy:monsterasp`; plain `npm run deploy` uses the separate default build configuration. This preparation did not run either deploy command.
7. Open the existing workers.dev URL, reload/update its PWA, sign in with the newly provisioned Administrator and create the demo records. Network requests must target the HTTPS runasp.net API. The API root `/` has no UI and may return 404; use the Cloudflare URL for the application.

[File Manager ZIP instructions](https://help.monsterasp.net/websites/deploy/zip-file) · [Environment variables](https://help.monsterasp.net/websites/configuration/environment-variables) · [Remote SQL access](https://help.monsterasp.net/databases/mssql/remote-access)

## Decision and scope

**Local remediation: implemented, verified and independently approved for review. Public deployment: NO-GO until the hosting prerequisites below are confirmed and deployment is authorized.**

This document supersedes the code-blocker conclusions of `backend-production-readiness.md` for the current working tree. That earlier report remains the historical baseline. This phase changes application source and adds tests/operator tooling; it does not deploy anything.

Target: framework-dependent **.NET 10 / ASP.NET Core, Windows x86, IIS in-process, 256MB process memory, SQL Server**. No connection to MonsterASP.NET, any hosted database or any pre-existing operational database was made. All SQL execution used new GUID-named owned local test databases. Existing images and `appsettings.Development.json` were preserved. No commit or push was performed.

**Hosting capability is not assumed from the provider name.** A shared-hosting plan that cannot provide a persistent private directory outside the application content root, required Windows ACL rights, .NET 10 x86/ANCM support, external configuration or SQL permissions is **NO-GO for this implementation**. Confirm these capabilities with the hosting control panel/support before uploading. No hosted IIS result is claimed: runtime smoke testing used the published x86 Windows artifact on local Kestrel with HTTPS under an enforced 256,000,000-byte process limit.

## 1. Exact implementation changes

| Files | Change |
|---|---|
| `ERPFlow.Infrastructure/Deployment/DatabaseSchemaVerifier.cs` | Read-only exact migration-history check, expected relational columns/indexes/checks/foreign keys, enabled/trusted constraints, required SQL triggers and procedures. No migration or repair. |
| `ERPFlow.Infrastructure/Deployment/ProductionInventoryAdmission.cs` | Startup admission only after schema validation, exclusive authority fence, completed Warehouse authority/protocol/provenance checks and full existing reconciliation. Admission is bound to the database identity and immutable authority rowversion. |
| `ERPFlow.Infrastructure/Persistence/Inventory/ProductionWarehouseWriteGate.cs`, `DependencyInjection.cs` | Production gate requires that admission, rechecks durable authority and enabled SQL guards, and rejects an existing unfenced transaction. Normal HTTP composition keeps `DisabledInventoryCutoverPermission`. No Development/permissive gate is used. |
| `ERPFlow.Operations/*`, `ERPFlow.slnx` | Separate executable for explicit migration, verification, first-admin, cutover preparation and approved cutover completion. Not referenced by or included in the website publish. |
| `ERPFlow.Infrastructure/Deployment/FirstAdministratorProvisioner.cs` | First administrator only on a schema-compatible database with **zero users**; serializable transaction plus application lock; password hashing; no reset/upsert/overwrite. |
| `ERPFlow.Api/Program.cs` | Development retains automatic migration; other environments perform prerequisite validation only. Production schema/data is never migrated or seeded at ordinary startup. Adds secure hosting middleware and small public status-only health endpoints. |
| `ERPFlow.Api/ProductionHosting.cs` | Mandatory explicit host allowlist, exact HTTPS production origins, private image path (Production defaults to MonsterASP's private sibling), JWT/database settings; HTTPS redirect/HSTS; optional explicitly trusted proxy IPs; per-source login rate policy. |
| `ERPFlow.Api/LoginAttemptLimiter.cs`, `ApiEndpoints.cs` | Per-account and global bounded login attempts; generic 429 and Retry-After. No account/password/token values logged. Login OpenAPI now documents 429. |
| `ERPFlow.Api/BoundedRequestStream.cs`, `ProductImageRequestMiddleware.cs` | Shared byte-counting request stream; bounded login JSON, including chunked requests, while retaining image request limits. |
| `ERPFlow.Api/ProductionReadiness.cs` | Safe cached readiness: read-only schema/authority verification, native codec check, storage probe and existing store binding validation. No automatic SQL image binding or rebinding. |
| `ERPFlow.Infrastructure/ProductImages/LocalProductImageStorage.cs` | Small uniquely named read/write/durable-flush/delete-on-close permission probe. Existing identity, private ACLs, path checks, immutable object writes and leases retained. |
| `ERPFlow.Infrastructure/Persistence/DevelopmentDataSeeder.cs` | Removed compiled default password, optional-password fallback and Production override; explicit externally supplied Development password required. Demo services are registered only in Development. |
| `ERPFlow.Api/ERPFlow.Api.csproj`, `Properties/PublishProfiles/MonsterAsp.pubxml`, `.gitignore` | Explicit IIS in-process, framework-dependent win-x86 profile with workstation GC; no apphost, trimming or single-file packaging; exclude environment/local settings and development assets from publish. Only the credential-free profile is unignored. |
| `ERPFlow.Api/ConstrainedRequestAdmission.cs`, `SkiaProductImageNormalizer.cs` | On a 32-bit Production worker, admit at most four concurrent requests, one image operation and no new request above 192MiB private memory; retain liveness during overload. Limit decoded image input to 2,000,000 pixels before pixel-buffer allocation and serialize x86 decoding. |
| `ERPFlow.Tests/ProductionDeploymentTests.cs`, `ProductionHttpTests.cs` | Normal-composition SQL workflows, failed admission/DDL restrictions, first-admin safety, HTTPS/host/CORS/JWT/rate limits/health/upload and restart tests. |
| Existing API/seeder tests, `docs/openapi/erpflow-v1.json` | Adapt former unconditional-gate expectations to validated Production availability; retain fail-closed baseline tests; require proper isolated production configuration. OpenAPI adds only the login 429 response. |

No EF model or migration source was changed. Business authorization, transaction boundaries, SQL fences/triggers, stock reconciliation, immutable ownership, idempotency and image validators remain in force.

### Inventory admission semantics

- Startup on an already legitimate Warehouse-authority database verifies its completed preparation/fingerprint/counts, writer version, fixed default warehouse and current ledger before issuing process-local admission. Every application-pool restart/recycle repeats this verification.
- Product-authority databases may start for controlled legacy/read access, but warehouse writes remain refused and `/health/ready` is **503**. Runtime does not switch authority. Perform the separate operator procedure and then restart to obtain Warehouse admission.
- Missing/old/newer-incompatible schema, invalid authority, missing provenance, disabled guards or failed reconciliation fail closed. A startup failure does not run migrations to repair the condition.
- Existing request workflows still acquire their authority/stock locks and enforce exact source, rowversion, actor and document checks. The gate does not create a transaction or skip these checks.
- Readiness verifies the runtime/schema/storage conditions; it is **not a continuous full-ledger audit**. Full reconciliation is performed at admission and by the offline `verify` command. Production SQL grants must prevent arbitrary ledger/schema tampering; a DBA modifying data requires maintenance and re-verification.
- Runtime cutover permission remains disabled, including for an Administrator token. Only the separate operator executable has a narrowly scoped cutover permission requiring explicit target confirmation and maintenance acknowledgement. The existing cutover request validation/reconciliation/fingerprint/atomic replay safeguards still execute.

## 2. Required MonsterASP.NET capabilities

Obtain affirmative confirmation for the selected plan:

1. **.NET 10 ASP.NET Core x86 runtime**, ASP.NET Core Module V2, IIS in-process hosting, a 32-bit application pool (`Enable 32-Bit Applications = True`), and HTTPS certificate/binding. Framework-dependent publish does not bring the runtime with it.
2. Service-owned environment settings or a protected host configuration mechanism; the application pool can receive secrets without placing them in source, upload archives, generated profiles or public files.
3. A stable, persistent, private image directory **outside both the website/application content root and public wwwroot**, with sufficient quota, native filesystem locking, and the intended worker identity's read/write/create/delete/traverse permissions.
4. Permission to enforce protected owner ACLs on that private directory (the current storage adapter applies owner-only ACLs). Some shared hosts permit file writes but not ACL ownership changes; that is insufficient. Ask the provider to provision an appropriate private path and permissions. Do not weaken the adapter to use public App_Data/wwwroot or a writable release subdirectory.
5. SQL Server supports the existing `rowversion`, filtered indexes, `SESSION_CONTEXT`, application locks, triggers, JSON checks and procedures. Use a currently supported SQL Server version and supported compatibility level. Confirm application-lock use, metadata visibility and procedure execution.
6. Separate migration and runtime database users where the plan permits. Migration identity can create/alter application schema and objects. Runtime needs DML on application tables, required stored-procedure execution and metadata visibility (`VIEW DEFINITION` for verification), but **not** database/server administration or DDL. Verify the exact grants on a rehearsal database.
7. An approved migration/provisioning execution route: a private administrative workstation can connect with the operator credentials, or the provider supplies a controlled SQL execution mechanism. The application never migrates itself to compensate for missing host tooling.
8. Full SQL backups/recovery, filesystem backups including hidden identity marker, application stop/recycle controls, logs and operational monitoring. Confirm backup access is compatible with the private ACLs.
9. If scheduled cleanup is required to run while no traffic exists, confirm Always On/preload or an authorized external health scheduler. IIS idle shutdown/recycle pauses hosted background services.

If the provider supplies only a single SQL account, document that permission limitation explicitly. Do not claim least privilege was deployed simply because it was tested locally. Do not grant DDL to runtime as an automatic fallback. Resolve the risk before approval.

## 3. Environment variables and IIS transport

### Required names — no secret values

| Name | Purpose |
|---|---|
| `DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT` | Explicit and consistent Production. |
| `ConnectionStrings__ERPFlowDb` | Runtime database connection. Use encrypted transport, validated certificate and the runtime identity. |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey` | Agreed issuer/audience and externally managed high-entropy signing key. Key validation requires at least 32 characters; deploy a randomly generated secret, not a human password. |
| `AllowedHosts` | Semicolon-separated exact backend hostnames; no `*`, URL schemes, paths or ports. Startup refuses the checked-in wildcard default. |
| `ProductImages__RootPath` | Optional override: absolute persistent private path outside application content. In Production, omit it to use `../private/ProductImages` resolved from the content root. |

Optional / topology-dependent:

| Name | Purpose |
|---|---|
| `Jwt__ExpirationMinutes` | Access-token lifetime; default 60, accepted 1–1440. Choose an approved short lifetime and rotation policy. |
| `Cors__ProductionOrigins__0`, subsequent numeric indices | Exact Angular HTTPS origins when frontend is separate. No wildcards, credentials, path, query, fragment or trailing slash. Empty list grants no cross-origin access. |
| `ReverseProxy__KnownProxies__0`, subsequent numeric indices | Only when an additional trusted proxy requires explicit forwarded-header handling. Exact IPs, one forwarded hop. Leave unset for ordinary direct IIS in-process hosting. |
| `Logging__LogLevel__Default`, `Logging__LogLevel__Microsoft.AspNetCore` | Approved logging levels/retention; do not enable sensitive data or request-body logging. |

Do not supply `DemoUsers__Password`, `ERPFLOW_DEMO_PASSWORD`, demo switches, or `DemoUsers__EnableAutomaticSeed=true` in Production. Startup refuses enabled automatic demo seeding. Do not set `ASPNETCORE_FORWARDEDHEADERS_ENABLED` to broadly trust proxy headers.

Default ASP.NET Core environment configuration is used. No vault client is automatically registered; the hosting platform may inject vault-backed values. Do not put secrets into publish profiles, command-line arguments, checked-in JSON, `web.config` committed to Git, diagnostic screenshots or support tickets. Restrict host-panel access and recycle after configuration changes.

### IIS, HTTPS and CORS behavior

- The generated `web.config` uses `AspNetCoreModuleV2`, `processPath="dotnet"`, `arguments=".\ERPFlow.Api.dll"`, `hostingModel="InProcess"`, and `stdoutLogEnabled="false"`.
- IIS in-process supplies the request scheme through its native server integration. Ordinary IIS requires no manually trusted forwarded headers. Additional proxies must have an explicit verified trust configuration; do not guess addresses or clear trust restrictions globally.
- HTTP redirects to HTTPS with **308**, with port 443 configured, and HTTPS uses HSTS. An untrusted `X-Forwarded-Proto: https` must not bypass that redirect. The IIS certificate and HTTPS binding must exist; redirect middleware cannot create a certificate/binding.
- Public requests must match `AllowedHosts`. Health endpoints use the same HTTPS/host restrictions.
- Production CORS is distinct from Development origins. Approved Angular origins can use GET/POST/PUT/DELETE, Authorization, Content-Type, Idempotency-Key, If-Match and If-None-Match; ETag/Location are exposed. Credentials/cookies are not enabled. Bearer authentication and role checks still apply.
- Host request-filtering/proxy limits must permit the application's **6MiB request / 5MiB image-file** contract. Host-level rejection may have an IIS response rather than application ProblemDetails. Verify those limits on the host.
- The x86 worker additionally rejects images above **2,000,000 decoded pixels** with 413 before allocating the pixel buffer. Resize larger source images before upload. This lower bound is intentional for the 256MB plan; the file-size and edge limits remain defense-in-depth ceilings.
- Under x86 Production, the process accepts at most four simultaneous application requests and one image request. New work receives generic 503 with `Retry-After: 5` when those slots are occupied or private memory is at least 192MiB. `/health/live` remains available so the host can distinguish overload from a dead worker.

### Login abuse protection and its bounds

Production applies **20 requests/minute per source address**, **5 attempts/minute per normalized account**, and **100 attempts/minute per process**, with no waiting queue. Account keys are SHA-256 fingerprints in bounded memory (at most 2,048 retained account windows); raw identifiers/passwords/tokens are not logged. The login body is capped at **16KiB**, including chunked input. Rejection is generic **429** with `Retry-After: 60`.

These are in-process controls and reset on recycle. Multiple workers/instances require coordinated edge protection if aggregate limits matter. Keep a single worker for this local image-storage topology, verify client-address handling and NAT effects, and configure provider-level protection/monitoring rather than trusting caller-supplied source headers. Token revocation remains expiry/user deletion/key rotation; this change does not add a session management system.

## 4. SQL migrations, first administrator and controlled cutover

### Artifacts and safety rules

Audit/release root:

`C:/Users/User/AppData/Local/Temp/opencode/erpflow-monsterasp-x86-20261029-d27e`

- Website: `publish/`.
- Separate administrative executable: `operations/ERPFlow.Operations.dll`.
- Reviewed migration script: `migration-idempotent.sql`.
- Checksummed publish inventory: `publish-manifest.json`.

The generated SQL is identical to the previous reviewed 16-migration chain: **144,860 bytes**, SHA-256 **`7a3d217012a1225659a730b4df7a36d3c6e1a1763130fe413391a41e2504e3ac`**. It ends at `20261028154045_ProfessionalDemoSeedOperationsPhase6B2`; the EF model is unchanged. It includes trigger/procedure replacement, constraint/index changes and historical backfills, not merely additive table creation. The script was generated/reviewed, **not executed against an existing or hosted database**. The operator migration command was executed only on fresh owned smoke resources, including idempotent replay.

Before any authorized real execution, verify backups, schema/migration history, duplicates and inventory/source consistency on a restored clone. Preserve all triggers and constraints. Keep writers stopped during migration/cutover. History-driven idempotency does not repair manually drifted schema or partially created objects. Use a batch-aware SQL tool for `GO` separators.

### Operator configuration

The operator executable reads **environment variables only** for credentials and accepts an explicit non-system target database confirmation. It does not load application Development settings.

| Name | Operator purpose |
|---|---|
| `ERPFLOW_OPERATIONS_CONNECTION` | Dedicated migration/operator SQL connection; never the runtime connection by implicit fallback. |
| `ERPFLOW_OPERATIONS_APPROVAL` | Explicit maintenance acknowledgement for migration/preparation/completion; required literal is `BACKUP-VERIFIED-WRITERS-STOPPED`. This is an acknowledgement, not a secret or replacement for access control. |
| `ERPFLOW_INITIAL_ADMIN_NAME`, `ERPFLOW_INITIAL_ADMIN_EMAIL`, `ERPFLOW_INITIAL_ADMIN_PASSWORD` | First-admin inputs only; password 16–256 characters, independently generated. Remove from the administrative process environment after use. |
| `ERPFLOW_OPERATOR_USER_ID` | Existing persisted Administrator approving preparation/cutover. |

### Manual process — future authorization required

The commands below show syntax; `<exact-database-name>` must be replaced only after an independent target review. No command here was run against MonsterASP.NET.

1. Stop application writers/cleanup, take and verify the coordinated SQL/image recovery point, and provision dedicated deployment credentials through an approved secret mechanism.
2. Apply the reviewed SQL through an approved SQL client **or** execute the separate migration command from the private administrative host:

   ```powershell
   dotnet ERPFlow.Operations.dll migrate --confirm-database <exact-database-name>
   ```

   This is the only application-provided migration executable. Ordinary Production API startup never calls `MigrateAsync`. A database with incomplete/newer/invalid schema is refused instead of repaired.

3. For a **fresh, user-empty** database, inject the three initial-admin variables and run:

   ```powershell
   dotnet ERPFlow.Operations.dll first-admin --confirm-database <exact-database-name>
   ```

   It creates one hashed Administrator under an exclusive application lock and serializable transaction, emits only the new user ID, and refuses any existing user. It is not a password reset command. Do not use it on an existing user database, a demo database, or to replace users. Record the user ID and securely remove the transient password input. Arrange secure provisioning of additional roles separately; the first Administrator is not a demo account.

4. If the database already has legitimate Warehouse authority, do not cut over again. Run `verify` and restart the API; admission recognizes compatible existing authority:

   ```powershell
   dotnet ERPFlow.Operations.dll verify --confirm-database <exact-database-name>
   ```

5. If authority is still Product, perform a separately approved physical-attribution review with all writers stopped. Supply the operator Administrator ID and maintenance acknowledgement, then prepare:

   ```powershell
   dotnet ERPFlow.Operations.dll prepare-cutover --confirm-database <exact-database-name>
   ```

   Retain the output's `PreparationId` and `SourceFingerprint`. Preparation is a real immutable manifest write, not a dry run. Independent review must confirm physical stock attribution to the fixed legacy-default warehouse and the unchanged source.

6. Create a protected reviewed request JSON using the **exact** `InventoryCutoverRequest` fields: `PreparationId`, `ExpectedFingerprint`, `DefaultWarehouseId` (the fixed `LegacyDefaultWarehouse.Id` from this release), `PhysicalAttributionApproval` (nonblank, at most 200 characters), and `ApplicationWriterVersion` (this release supports 2). No secret values belong in this file. Do not fabricate a fingerprint or manually update authority rows.

   ```powershell
   dotnet ERPFlow.Operations.dll complete-cutover --confirm-database <exact-database-name> <approved-request.json>
   dotnet ERPFlow.Operations.dll verify --confirm-database <exact-database-name>
   ```

   Completion revalidates source fingerprint, manifest, Administrator, writer protocol and ledger in the existing atomic fenced cutover. Replays require the same approval/actor/request. Ordinary API configuration cannot grant this permission.

7. Remove operator credentials from the web/service environment, start one runtime instance using the runtime identity, and require readiness plus the smoke checklist before traffic admission.

Operator failures return a nonzero exit with a generic message, never the exception/connection/password. Validate the input and reproduce on a private isolated rehearsal before investigating. Do not repeatedly execute a failed migration blindly on live data. Schema verification checks migration history and required object/guard presence and trust, not cryptographic equivalence of every SQL object definition; only controlled DDL and the exact reviewed migration artifacts should be used.

## 5. External product-image storage and recovery

1. Use MonsterASP's persistent sibling `/private/ProductImages` with `ProductImages__RootPath` unset, or provide an explicit absolute private path override. Keep it stable across Web Deploy/SFTP uploads and app-pool recycling. No automatic file move occurs.
2. Run the worker as the intended stable identity. The adapter rejects public/content paths, filesystem/profile roots and symlinks/reparse points. On Windows it protects the directory ACL and grants only the current identity inheritable Full Control. Confirm parent traversal, file create/read/write/flush/lease/delete and ACL-change rights. Do not fall back to website files if initialization fails.
3. Production startup performs a unique delete-on-close permission probe, verifies any existing SQL store binding, and exercises Skia PNG encode/decode. A missing/broken native library or inaccessible/mismatched store refuses startup. Readiness rechecks these conditions on a 15-second cache.
4. Existing PUT/replace/delete/GET semantics, serializable metadata publication, strong rowversion ETags, per-product leases, durable Pending/DeletePending registry and five-minute cleanup worker are retained. Files are immutable keys; original upload filenames are not paths. Restart tests proved preserved bytes/ETag.

### Identity and restore limits

Store identity remains a hash of **machine name + canonical absolute directory + `.store-id`**. Preserve the marker. Copying files to a new machine or changing the path does not preserve identity. The application does **not** move files, overwrite/rebind `ProductImageStores.Identity`, or silently adopt mismatched storage.

The adapter supports same-host processes sharing the same path/identity and filesystem locks; it does not support active multi-host replicas. Shared hosting may move a site to another node or change worker identity/path: obtain the provider's guarantees and recovery procedure before approval. A host move requires an independently reviewed offline migration/reconciliation/rebind design; it is not implemented as an automatic recovery shortcut.

For backup: quiesce image mutations and cleanup, then take coordinated SQL and full-directory snapshots including `.store-id`. For restore: stop every instance; restore both to a compatible point on the original machine identity/path; restore private permissions; reconcile active SQL object keys and sizes, Pending/DeletePending work and files before startup. A database-only rollback can resurrect references to already cleaned-up files. Do not start the worker before recovery reconciliation.

### IIS recycle / idle behavior

Admission and storage/native verification rerun after every recycle. Image contents persist because the directory is external. The five-minute cleanup timer runs only while the process is alive, so idle shutdown delays physical deletion; durable queue state survives and later executions retry safely. Never depend on uninterrupted in-process background execution for a deadline. Monitor storage quota/backlog, arrange provider-supported warmup/health checks if needed, and retain one stable-host deployment. Do not widen ACLs or disable safe cleanup to work around an app-pool restriction.

## 6. Build, publish and manual upload

Build on the reviewed working tree with .NET 10 SDK and an EF tool aligned to the project's `10.0.13` packages. The local machine used SDK `10.0.401`; its EF CLI `10.0.11` emitted an older-tool warning but completed the checks. Align/pin versions in a repeatable release pipeline.

```powershell
dotnet restore ERPFlow.slnx
dotnet build ERPFlow.slnx --configuration Release --no-restore
dotnet publish ERPFlow.Api/ERPFlow.Api.csproj --configuration Release --runtime win-x86 --self-contained false -p:PublishProfile=MonsterAsp --output <empty-private-release-directory>
dotnet publish ERPFlow.Operations/ERPFlow.Operations.csproj --configuration Release --self-contained false -p:UseAppHost=false --output <separate-private-operator-directory>
```

The website artifact is framework-dependent win-x86, **60 files / 133,097,592 bytes** at the audited publish. `web.config` was generated correctly; both `libSkiaSharp.dll` and `Microsoft.Data.SqlClient.SNI.dll` have x86 PE machine type `0x014C`. The generated runtime configuration uses workstation GC. The output contains no Development JSON, local connection string, signing-key value, user-secret file, image/database, test assembly/result, operator executable or private certificate. Compiled DLL scanning confirms the removed legacy demo-password literal is absent. Only base `appsettings.json` remains, with an empty signing key and no connection/image override; external production settings are mandatory.

API DLL SHA-256: `1bb1c12db519b9589eb478faa39678c697d86667c2202e786597692fe6eec0a5`. Any subsequent source/publish change requires new verification/checksums.

### Visual Studio Web Deploy — after approval

1. Use a provider-issued publish profile obtained through the authenticated control panel. Keep it private/untracked; it may contain credentials. The checked-in `MonsterAsp.pubxml` is a credential-free folder profile, not host authorization.
2. Select Release, framework-dependent, win-x86, in-process and the intended site. Preview file changes. Confirm the application pool is 32-bit. Disable database automatic migration options and any publish step that seeds users or activates cutover.
3. Configure external environment settings and private image storage through the provider's supported secure mechanism. Do not copy Development JSON. Confirm whether Web Deploy would overwrite provider-managed `web.config` settings; preserve the approved configuration without embedding secrets into the source artifact.
4. Back up the prior website artifact and paired recovery point, stop traffic/application (or use the provider-supported `app_offline.htm` maintenance mechanism), upload only website publish contents, then restart/warm up after the separately approved SQL step.
5. Remove maintenance mode only after the smoke checklist passes. Do not enable stdout logs permanently; temporary diagnostics must be private, sanitized and removed after use.

### SFTP / provider-supported secure file transfer — after approval

Use SFTP only if the plan actually offers it; otherwise use the provider-supported secure transport, such as FTPS with certificate validation. Do not use plaintext FTP or disable host-key/certificate checks. Upload **only `publish/` contents**, including generated `web.config`, during maintenance; preserve the external image directory and provider-managed settings. Avoid a partially updated running application. Do not upload the repository, tests, operator output, SQL credentials/scripts, PFX, bin/obj trees, audit logs or the entire temporary release root.

## 7. Verification evidence

All artifacts below are under the local release root. Earlier test/driver failures are retained rather than relabeled as success.

| Check | Actual result |
|---|---|
| Release solution build | Passed, **0 warnings / 0 errors**, including the separate Operations project. Final build elapsed **7.74s**. |
| Restore | Passed; all six solution projects restored/up-to-date. |
| Full backend regression | **555/555 passed, 0 failed, 0 skipped**, exit **0**, wall time **00:15:39.0623649**; `full-release.trx`. Includes the Production SQL/HTTP cases, **8 constrained-hosting cases**, and the updated former gate/CORS/seeder tests. |
| Focused Production iteration | Initial **18/19** exposed a chunked-login size-limit gap, fixed with bounded stream. Next **28/29** exposed a JWT test fixture that accidentally retained the valid original audience; corrected to carry only identity claims. Final cases are included in the full regression. |
| OpenAPI update | Focused export **1/1 passed**, 25s; intended change is login 429 response. Final full regression passed the snapshot deep-equality and two image-contract checks with export disabled. |
| EF pending model | Passed, no pending changes; only older EF tooling warning. |
| Idempotent SQL | Generated successfully; unchanged hash/order, reviewed; no existing/hosted execution. |
| API and Operations publish | Both successful; website is framework-dependent win-x86 with x86 native Skia and SQL Client SNI. |
| Publish audit | All **11** content/native/configuration checks passed; `publish-manifest.json`. |
| Package advisories | All six solution projects reported no known vulnerable packages in the queried sources at audit time. |
| Real published x86 Windows HTTPS smoke | **73/73 passed**, **230,338ms**, including owned-resource cleanup. `smoke-6998c12adf3f4343b877554cd1f902e1/summary.json`. The API ran under 32-bit .NET inside a Windows Job Object capped at **256,000,000 bytes**; measured peak private memory was **58,445,824 bytes**. |
| Existing resources | Protected Development file hash unchanged; no existing database/image migration or host connection. |
| Production demo/Development activation CLI | Published binary rejected both commands before configuration/database access; expected Windows process exit `-532462766`. |
| Workspace checks | `git diff --check` passed; only normal Git LF/CRLF conversion notices. New profile is visible to Git and contains no credentials. LSP checks timed out; compiler and tests are the completed code-validation evidence. |
| Hosted IIS / MonsterASP.NET | **Not run.** Generated IIS config and Windows-native artifact inspected; actual TLS smoke was local Kestrel. |
| Independent final gate review | **APPROVE, high confidence**; no blocking, high or medium findings. One nonblocking test-coverage note: add simultaneous first-admin provisioning regression coverage in follow-up; current serializable/app-lock protection and sequential refusal were verified. |

Real smoke used the **separate published operator executable** for migration/replay, first-admin creation/repeat refusal, preparation/completion/replay and final reconciliation. It then launched the published Production API with 32-bit .NET over real loopback HTTPS using an ephemeral owned certificate and enforced the 256,000,000-byte process cap. It performed login/authenticated reads, 401/404 checks, exact CORS allow/deny, HSTS and spoofed-forwarded-proto redirect, image create/GET/conditional/replace/stale/delete, a full process stop/restart retaining bytes+ETag, the x86 2MP accept/reject boundary, bounded concurrent reads with safe 503 responses and readiness recovery, transfer post/idempotent replay and login 429. The exact new owned database, test certificate and image directory were removed and processes stopped.

An earlier smoke driver preflight failed because a newly created empty SQL database exposed a null recovery GUID. That failure occurred before application migration; the exact owned empty database was independently ownership/time/emptiness checked and removed, then the driver switched to explicit ownership-marker validation. The final run passed. This was an audit-tool issue, not a silent application fix.

Regression command (export and test-server override unset):

```powershell
dotnet test ERPFlow.Tests/ERPFlow.Tests.csproj --configuration Release --no-build --no-restore --logger "trx;LogFileName=full-release.trx" --results-directory "C:/Users/User/AppData/Local/Temp/opencode/erpflow-monsterasp-x86-20261029-d27e"
dotnet ef migrations has-pending-model-changes --project ERPFlow.Infrastructure/ERPFlow.Infrastructure.csproj --startup-project ERPFlow.Api/ERPFlow.Api.csproj --configuration Release --no-build
dotnet ef migrations script --idempotent --project ERPFlow.Infrastructure/ERPFlow.Infrastructure.csproj --startup-project ERPFlow.Api/ERPFlow.Api.csproj --configuration Release --no-build --output "C:/Users/User/AppData/Local/Temp/opencode/erpflow-monsterasp-x86-20261029-d27e/migration-idempotent.sql"
```

Full test local start/end: `2026-10-29T05:53:50.7492618+03:00` / `2026-10-29T06:09:29.8116267+03:00`. Final cleanup inspection found no owned smoke API/operator processes, image directories or private test certificates remaining. Release binaries and sanitized evidence remain outside the repository for review.

### QA matrix

| Scenario | Evidence | Expected |
|---|---|---|
| GRN/transfers/reservations/dispatch/service through Production registrations | `ProductionDeploymentTests` in full TRX | Reconciled warehouse effects, preserved legacy balances, idempotent replay; runtime cutover remains denied. |
| Missing schema/Product authority/wrong DB/unfenced/disabled guards/inconsistent ledger | `ProductionDeploymentTests` in full TRX | Fail closed without automatic migration. |
| Runtime without DDL | SQL `EXECUTE AS` test in full TRX | No ALTER permission; schema verification and GRN posting succeed with bounded runtime grants. |
| JWT, host, TLS, exact CORS, health, upload limits and restart | `ProductionHttpTests` in full TRX | Correct 401/403/308/429/413, status-only health, secure image persistence. |
| Actual published executable and operator | Final smoke summary above | 73/73 success on new owned local resources with cleanup and 58,445,824-byte peak private memory. |
| IIS artifact and no secrets/default credential | Publish audit | Framework-dependent win-x86, correct generated web.config/native PE types, no forbidden files/literals. |

## 8. Post-deployment smoke checklist — do not execute before authorization

1. Verify approved release hashes and actual worker environment is Production. Confirm SQL history was applied only by the deployment process, runtime SQL has the agreed grants, and no demo account/seed switch exists.
2. Visit the approved HTTPS hostname with full certificate validation; HTTP must redirect to HTTPS. Check HSTS, correct proxy behavior and denial of unexpected hosts/origins. Verify spoofed public forwarded headers cannot alter trust.
3. `/health/live` → 200 with status only. `/health/ready` → 200 only after schema/Warehouse admission/private-store/native checks; 503 is not an acceptable complete-system launch. No SQL names, paths, versions, exceptions or secret values are returned.
4. `/swagger/index.html` and `/openapi/v1.json` → 404; protected API without valid JWT → 401; wrong role → 403. Perform a controlled login through the deployed Angular origin and confirm CORS/ETag visibility.
5. Verify an authorized known image read and ETag. Use an explicitly owned validation product for upload/replacement/delete; never alter customer images as a health probe. Verify persistence after one controlled app-pool recycle and readiness recovery.
6. Using approved validation data, confirm procurement/GRN, transfer post, reservation/dispatch and service consumption, exact idempotent replay and reconciliation. Do not activate cutover merely to make a smoke check pass.
7. Check logs, disk quota, cleanup backlog, latency and SQL errors. Enable monitoring without secrets. Confirm backup/restore and host-move arrangements before traffic admission.

## 9. Rollback and remaining release gates

- Keep the previous compatible publish artifact and checksums plus coordinated SQL/log/image recovery points. Stop traffic/writes and cleanup before rollback.
- Code rollback requires compatible schema, writer version and inventory authority; do not deploy an older Product-only writer to a cut-over database. Never disable the SQL protocol triggers to force compatibility.
- Do not assume EF downgrade works: several existing inventory/procurement/receiving/transfer/image/catalog migration `Down` paths deliberately throw; other downs destroy history/data. Prefer a reviewed forward repair, or restore the paired database/log and complete image store with a compatible binary.
- Restore the same image machine identity/canonical root/marker and private permissions. A different host/path requires a separately reviewed migration/rebind procedure. Reconcile SQL/file manifest and inventory before restarting any cleanup process.
- Quantify post-backup data loss and external business effects before reopening. Repeat readiness, authenticated API and image checks after recovery.

Remaining public-deployment gates are host capability/permissions, real IIS certificate/proxy/native/ACL verification, target migration rehearsal and backups/recovery, secure external settings/roles, and explicit deployment approval. These cannot be truthfully marked complete without connecting to the host, which this task prohibits. The deliverable is a locally verified release for review; no public deployment has occurred.

### References

- [ASP.NET Core IIS in-process hosting](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/in-process-hosting?view=aspnetcore-10.0)
- [ASP.NET Core Module and web.config](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/aspnet-core-module?view=aspnetcore-10.0)
- [Proxy and load-balancer trust](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
- [Rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [EF migration deployment](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)

Protected `appsettings.Development.json` SHA-256: `fd0e460e8b05a8adfc8ed87e32de0d29110b095277c8bfa408b8043e18532ffd` (verified unchanged by the publish audit).
