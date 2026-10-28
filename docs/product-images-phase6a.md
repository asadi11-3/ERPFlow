# ERPFlow Phase 6A: product images

## Scope and contract status

Phase 6A adds one private image per product through an authenticated backend API. This document is the fixed handoff contract for backend implementation, operations, security review, and later Angular work. It doesn't add Angular code, change inventory authority, or authorize a production deployment.

The implementation includes private filesystem storage, SQL metadata and recovery tracking, protected HTTP endpoints, an additive migration, and isolated tests. Verification uses disposable databases and directories only; no configured application or production database is part of the rehearsal.

## HTTP contract

All routes require a JWT Bearer token. Reads are available to authenticated users. Writes require the Administrator role.

| Method and route | Request | Successful response |
| --- | --- | --- |
| `PUT /api/products/{id}/image` | `multipart/form-data` with a real file part named `file` | First image: `201 Created` with `Location` and a strong `ETag`. Replacement: `200 OK` with a new strong `ETag`. |
| `GET /api/products/{id}/image` | Optional `If-None-Match` | `200 OK` with binary image bytes and the current strong `ETag`, or `304 Not Modified` when the validator matches. |
| `DELETE /api/products/{id}/image` | Exact strong `If-Match` | `204 No Content`. |

Replacement and deletion use optimistic concurrency. A replacement requires an exact strong `If-Match` value from the current image response. Missing `If-Match` returns `428 Precondition Required`; a stale or nonmatching value returns `412 Precondition Failed`. Deletion follows the same exact `If-Match` rule. The initial upload doesn't require `If-Match` because no image exists.

Missing products or missing images return `404 Not Found`, as applicable. Invalid multipart input, a missing or empty file, malformed validators, or invalid image content return `400 Bad Request`. A file type outside JPEG, PNG, or WebP returns `415 Unsupported Media Type`. A file over the payload limit, or a request over the request limit, returns `413 Content Too Large`.

An explicit stale validator for an image that has been deleted returns `412`; a nonexistent product returns `404`. `If-None-Match: *` optionally makes PUT create-only. It cannot be combined with `If-Match`. Wildcard, weak, malformed, or multiple mutation validators are rejected. Transient storage/database failures return sanitized `503` responses. A lost commit acknowledgement may return `503` even though the change committed: GET the current state before retrying.

The accepted image payload is at most 5 MiB (5,242,880 bytes). The whole multipart request is capped at 6 MiB. Each decoded edge must be at most 8,192 pixels, and the total decoded pixel count must be at most 16,000,000. Two image decodes may run concurrently per process. These decode limits are checked before pixel allocation and are separate from compressed byte limits. Animated and truncated images are rejected. Normalized output must also fit within 5 MiB.

JPEG, PNG, and WebP input stays in the same image format after server processing. The returned bytes are normalized output, not a byte-for-byte copy of the uploaded compressed file. Metadata is stripped, including EXIF and other embedded metadata. Clients must not depend on original compression settings, byte length, metadata, or file hash.

Every `200` image response is private and must include `Cache-Control: private, no-cache` and `X-Content-Type-Options: nosniff`, along with the normalized image `Content-Type` and strong `ETag`. A conditional `304` carries no image body.

## Authentication, CSRF, and transport

The API uses JWT Bearer authentication only. It doesn't use authentication cookies for these routes. Never put access tokens in query strings, image URLs, logs, or persisted product records.

Only the multipart upload endpoint may use a narrow `DisableAntiforgery` exception. The exception is justified because authentication comes exclusively from the `Authorization` header, not an automatically attached cookie. Don't broaden it to other endpoints or treat it as a replacement for authorization. Administrator authorization and current persisted-role checks still apply to upload, replacement, and deletion.

Use HTTPS. Keep the image response private and prevent MIME sniffing. If Angular runs on another allowed origin, CORS must allow `Authorization`, `Content-Type`, `If-Match`, and `If-None-Match`, and expose `ETag` so browser code can read it. Don't enable credentialed CORS for this Bearer-only flow.

## Storage and consistency

`ProductImages:RootPath` points to a private external durable volume. It must not be under the web root or served as a public static-file directory. Grant the application only the filesystem permissions it needs. Stored objects use immutable random filenames rather than product IDs, SKUs, user filenames, or predictable paths. User filenames are not trusted storage paths.

Configure the root through deployment configuration or `ProductImages__RootPath`, without editing a developer's settings file. The default is `LocalApplicationData/ERPFlow/ProductImages`; production should explicitly select a durable backed-up volume. The local adapter enforces owner-only permissions and rejects application-content roots and symlink/reparse-point paths.

`ProductImages` has `Id`, unique `ProductId`, unique `StorageKey`, `ContentType`, `FileSize`, `Width`, `Height`, `CreatedAt`, `UpdatedAt`, and an independent SQL `RowVersion`. Restrictive foreign keys enforce one image per product and a registered object for every association. `ProductImageObjects` tracks `Pending`, `Active`, and `DeletePending` objects. `ProductImageStores` binds the database to the local host/root identity so an accidentally changed storage root fails closed rather than publishing inaccessible records.

SQL `ProductImage` metadata is the authoritative current-image record. A durable object registry tracks stored objects so replacement, deletion, and cleanup remain retry-safe across process restarts. File creation and SQL commit can't form one atomic transaction, so cleanup is deferred:

1. Commit a pending object reservation in SQL before creating the file.
2. Write the normalized immutable file and flush it to durable storage.
3. Atomically publish the new `ProductImage` metadata and retire any previous object in the same SQL transaction.
4. Retry physical cleanup idempotently. A missing retired object is already clean.

Never delete a current or candidate object when the SQL commit outcome is uncertain, or while the database is unavailable. Preserve it for later registry reconciliation. Storage leaks are safer than deleting an object that may be referenced by a committed row.

The hosted cleanup worker runs every five minutes in batches of up to 64 objects. Pending uploads expire after one hour. Expired publishers cannot publish; cleanup and all reads/mutations coordinate through an OS-visible exclusive per-product lease. Downloads are read into bounded memory before releasing that lease, so cleanup cannot invalidate an in-flight response. Cleanup rechecks SQL ownership/references, retries idempotently, and retains durable work after deletion or acknowledgement failures. `.store-id`, `.store-lock`, and product `.lease` files are coordination artifacts, not orphan images; do not manually remove them while the service is running.

Monitor logged write/cleanup failures, queued object count/age, and volume pressure. There is no new monitoring endpoint in this phase. Back up the durable image volume together with its paired database at a compatible recovery point. A database-only or volume-only restore can break references. Host/root migration requires a reviewed paired-store migration; do not reset the binding to hide missing files. Multiple cooperating processes on the same host/root share filesystem leases. Multi-host replicas, arbitrary shared/network filesystems, and cloud storage are not supported by this local adapter.

Image writes must not modify Product inventory fields or the Product inventory rowversion. Image concurrency belongs to the `ProductImage` record and its image `ETag`, not inventory concurrency.

## Migration and deployment

Apply `20261028094525_ProductImagesPhase6A` through the normal reviewed deployment process. It creates only the three image tables and their indexes, constraints, and relationships. Destructive downgrade is refused. Review the generated SQL, back up the paired database and image volume, and deploy matching application/native dependencies. No production migration or application database command was executed for this increment.

Phase 6A uses SkiaSharp `4.154.0`, licensed under MIT. Deploy matching Linux native assets for the target runtime and architecture. Retain the SkiaSharp and native dependency license notices in the deployment artifact and software-notice inventory. Confirm native library loading in the actual Linux image before release. Don't mix package and native asset versions.

The project references `SkiaSharp.NativeAssets.Linux.NoDependencies` `4.154.0` alongside SkiaSharp. Windows native execution is covered by the local tests. Linux native loading must still be smoke-tested in the intended deployment image. The installed EF CLI is `10.0.11`, while EF packages are `10.0.13`; the CLI reports this tooling warning but model verification succeeds. The design-time DbContext factory uses an unreachable design-only connection and never starts the application's migration/seeding path.

## Manual Swagger walkthrough

Use a Development host whose database and image root are explicitly approved for disposable manual testing. Don't start the application against a configured shared or production database just to perform this walkthrough.

1. Open Swagger and call `POST /api/auth/login` with an Administrator test account.
2. Copy the access token. Choose **Authorize**, paste the raw token as required by the current Swagger security dialog, and authorize.
3. Open `PUT /api/products/{id}/image`. Choose **Choose File** and select a real JPEG, PNG, or WebP file. Don't paste a filesystem path into a JSON body.
4. Execute the initial upload. Confirm `201 Created`, a `Location` header, and a quoted strong `ETag` without claiming the response bytes match the source file.
5. Call `GET /api/products/{id}/image`. Confirm `200`, the expected normalized image content type, binary bytes, and the current `ETag`. Download or inspect the response as an image. The bytes should decode to the uploaded visual content, but they are normalized and won't necessarily match the original compressed bytes.
6. Repeat GET with `If-None-Match` set to the returned `ETag`. Confirm `304` and no response body.
7. Choose a second real image in the same PUT operation and set `If-Match` to the exact current `ETag`. Confirm `200 OK` and a changed strong `ETag`.
8. Optionally demonstrate concurrency by retrying replacement with the old `ETag`; expect `412`. Omitting `If-Match` on replacement should return `428`.
9. Call DELETE with `If-Match` set to the exact latest `ETag`. Confirm `204`. A following GET should return `404`.

Record actual status codes, headers, and observations only when this walkthrough is performed. Don't infer success from the OpenAPI description or from a build.

## Angular handoff, guidance only

No Angular implementation is included in Phase 6A. A later client should use `HttpClient` with the Bearer interceptor and request the body as a blob while observing the full response, for example `responseType: 'blob'` with `observe: 'response'`. Read the `ETag` response header and retain it as the image concurrency token.

Create an object URL from the returned `Blob` and bind that local URL to the image element. Revoke the previous object URL before replacing it, and revoke the current URL in component destruction. Don't bind a plain protected API URL to `<img src>`, because the browser won't attach the Bearer header. Never work around that by adding a token to the query string.

Send `If-None-Match` only when retaining the corresponding cached Blob/object URL. A `304` has no new body: handle that status without replacing the cached image, including the client's non-2xx response path where applicable. Send the exact current `If-Match` for replacement and deletion, and handle `412` by fetching the latest state rather than silently overwriting it. Handle `428` as a missing concurrency token. Development CORS allows DELETE and conditional headers and exposes `ETag` and `Location`; production cross-origin policy remains a deployment concern.

`GET /api/reference/products` appends `hasImage: boolean` and `imageVersion: string | null` without changing existing fields. `imageVersion` contains the quoted strong image ETag and changes on every successful replacement, including identical pixel content. Preserve its quotes when sending `If-Match`; it is neither an access token nor a public URL. Existing inventory calculations are unchanged.

## Isolated test safety

Backend tests must use uniquely named disposable databases and temporary image roots owned by the fixture. Set test connection, JWT, and image-root values through the test process only. Don't fall back to `ConnectionStrings:ERPFlowDb`, `ProductImages:RootPath`, development secrets, or a developer's durable volume.

Tests should create their temporary database and directory only after all isolation guards pass, then remove both in fixture cleanup. A failed test may leave artifacts for diagnosis, but cleanup must never target a path or database the fixture didn't create. Don't start a configured Development host, run `database update`, or invoke production storage tools as part of isolated verification.

## Verification evidence

- Focused image suite: 62 passed, zero failed/skipped, including supported formats, limits, auth/roles, stale concurrent mutations, metadata normalization, rollback, ambiguous commit, database outage, expired-writer fencing, and cleanup retry.
- Restore/build succeed; EF reports no pending model changes; migration tests cover additive operations and preservation of existing Product balances/rowversion.
- OpenAPI is exported through the isolated existing API fixture and includes a required multipart `file` with `type: string`, `format: binary` and Bearer security.
- A disposable Kestrel host was exercised in a real browser: actual Choose File selection and JWT-authenticated Swagger upload returned `201`. HTTP download, `304`, replacement, `412`, `428`, `413`, `415`, `400`, deletion, subsequent `404`, and unchanged reference stock were observed.
- Local evidence: `.playwright-mcp/phase6a-final-swagger-upload-evidence.json`, `.playwright-mcp/phase6a-final-http-lifecycle-evidence.json`, and `.playwright-mcp/phase6a-final-file-picker.png`. These are ignored local QA artifacts, not application assets.
- Final full-backend suite: 477 passed, zero failed/skipped (415 existing tests plus 62 image tests). This includes inventory, authority, receiving, transfers, procurement, sales, service, and migration workflows. Infrastructure dependency auditing reports no known vulnerable packages from the configured NuGet sources.

## Changed files

21 files created:

```text
ERPFlow.Api/DesignTimeERPFlowDbContextFactory.cs
ERPFlow.Api/ProductImageCleanupWorker.cs
ERPFlow.Api/ProductImageEndpoints.cs
ERPFlow.Api/ProductImageRequestMiddleware.cs
ERPFlow.Application/ProductImages/ProductImageContracts.cs
ERPFlow.Domain/Entities/ProductImage.cs
ERPFlow.Infrastructure/Persistence/Configurations/ProductImageConfiguration.cs
ERPFlow.Infrastructure/Persistence/ERPFlowDbContext.ProductImages.cs
ERPFlow.Infrastructure/Persistence/Migrations/20261028094525_ProductImagesPhase6A.cs
ERPFlow.Infrastructure/Persistence/Migrations/20261028094525_ProductImagesPhase6A.Designer.cs
ERPFlow.Infrastructure/ProductImages/EfProductImageWorkflow.cs
ERPFlow.Infrastructure/ProductImages/LocalProductImageStorage.cs
ERPFlow.Infrastructure/ProductImages/SkiaProductImageNormalizer.cs
ERPFlow.Tests/ProductImageApiTests.cs
ERPFlow.Tests/ProductImageFailureTests.cs
ERPFlow.Tests/ProductImageMigrationTests.cs
ERPFlow.Tests/ProductImageNormalizationTests.cs
ERPFlow.Tests/ProductImageOpenApiTests.cs
ERPFlow.Tests/ProductImageStorageTests.cs
ERPFlow.Tests/ProductImageTestSupport.cs
docs/product-images-phase6a.md
```

9 existing files modified:

```text
ERPFlow.Api/ApiEndpoints.cs
ERPFlow.Api/ApiExceptionHandler.cs
ERPFlow.Api/Program.cs
ERPFlow.Application/Abstractions/IReferenceDataQueries.cs
ERPFlow.Infrastructure/DependencyInjection.cs
ERPFlow.Infrastructure/ERPFlow.Infrastructure.csproj
ERPFlow.Infrastructure/Persistence/EfReferenceDataQueries.cs
ERPFlow.Infrastructure/Persistence/Migrations/ERPFlowDbContextModelSnapshot.cs
docs/openapi/erpflow-v1.json
```

The pre-existing local `ERPFlow.Api/appsettings.Development.json` change is not part of this implementation and remains byte-for-byte unchanged. No Angular, demo-seeder, historical inventory migration, or inventory-authority gate file was modified. No commit or push was performed.
