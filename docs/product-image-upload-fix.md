# Product Image Upload Investigation and Fix

## Confirmed Cause and Limits

The expected empty-image read used exception control flow:
`EfProductImageWorkflow.GetAsync` threw `ProductImageException(NotFound)` with
"The product or its image was not found." for a real product without an image.
`ApiExceptionHandler` handled that exception and returned HTTP 404. A Visual Studio
break-on-thrown/user-unhandled setting can stop at that throw before the handler runs; the
exception does not, by itself, mean the API process has terminated. This investigation did not
attach to or inspect the user's Visual Studio exception settings.

The second confirmed defect was ambiguous absence handling: missing products and missing images
had the same error, and Angular treated every image GET 404 as normal image absence. A missing
product could therefore appear eligible for a first upload.

The original transport-level "Connection lost" event was **not reproduced** against the isolated
running API. Before the fix, the real Angular flow already returned GET 404, PUT 201, then GET 200;
the native input was empty after selection but the captured File remained valid. Consequently,
no unsupported claim is made that HTTP 404 was converted into status 0, that file clearing lost
the upload, or that the Demo Seeder caused this report. The first-create PUT contract was already
correct and has not been weakened.

## Actual Fix

- Normal missing-image reads return a nullable application result rather than throwing.
- The GET endpoint returns HTTP 404 ProblemDetails with `code: product_image_missing`.
- Missing products return HTTP 404 with `code: product_not_found` on GET, PUT and DELETE.
- Angular treats only the explicit missing-image code as a normal placeholder. Missing products
  and unrecognized 404s have distinct feedback and block mutation until state is resolved.
- Status 0 remains a transport error; 401/403, 412/428, validation failures and server failures
  retain distinct messages. Blob ProblemDetails parsing preserves status and the machine code.
- First uploads retain create-only `If-None-Match: *`, without `If-Match`; replacement and deletion
  retain the strong image ETag contract. Native File capture, Blob cleanup, cancellation/stale-read
  protection, Administrator-only controls and follow-up image refresh remain intact.

## Real Request Evidence

No seeder was used. A fresh owned SQL Express database `ERPFlow_ImageUploadQA_20261028` contained
two master products and password-backed Administrator/Sales users, with dedicated private image
storage and process-scoped API configuration. No existing database was reset.

Baseline product: `e3158136-19d2-4b75-a201-7116bff7a6c4`.
Post-fix lifecycle product: `51ff4d47-3f4c-4852-bdc4-2c9c9265f8fe`.
Image route: `https://localhost:7165/api/products/{productId}/image`.

| Real Angular/API scenario | Method | Result |
| --- | --- | --- |
| Existing product, no image | GET | 404, `product_image_missing`; normal placeholder, no alert |
| First PNG selected; native input files length is zero but selection retained | PUT | 201; no If-Match; If-None-Match `*`; multipart field `file` |
| Fetch newly uploaded image | GET | 200; strong ETag; decoded 64x48 image visible immediately |
| Replace with current strong ETag | PUT | 200; new ETag; decoded replacement is 96x72 |
| Stale ETag | PUT | 412; no overwrite |
| Missing If-Match on replacement | PUT | 428; no overwrite |
| Real concurrent replacement, followed by stale Angular save | PUT | 412; Angular reloads latest 20x12 image and shows concurrency feedback |
| Delete with latest strong ETag | DELETE | 204; placeholder immediately |
| Read after deletion | GET | 404, `product_image_missing` |
| Upload after deletion | PUT | 201; no If-Match; subsequent GET 200 and visible image |
| Wrong product ID | GET / PUT / DELETE | 404, `product_not_found` |
| Anonymous read | GET | 401 |
| Invalid PNG bytes | PUT | 400, validation ProblemDetails |
| Sales user | GET / PUT | 200 / 403; preview works, upload/delete controls absent |
| Controlled browser offline fault | GET | No HTTP response; `net::ERR_INTERNET_DISCONNECTED`, Angular status 0 and connection message |
| Reconnect and refresh | GET | 200; error cleared |
| Controlled missing owned blob | GET | 503, sanitized server ProblemDetails and service-unavailable feedback, not connection loss |
| Restore owned blob and refresh | GET | 200 with unchanged ETag; error cleared |

The same post-fix API process (PID 26776) remained responsive throughout, including after all
negative cases and the controlled storage fault. Its OpenAPI endpoint still returned 200.
The temporary blob rename was restored without changing SQL metadata.

For the first post-fix upload, the PUT metadata ETag and following GET ETag were both
`"AAAAAAAARlE="`; replacement returned `"AAAAAAAARlI="`. Delete used the reconciled validator
`"AAAAAAAARlM="`; re-upload created a new association with `"AAAAAAAARlQ="`.

Detailed request IDs, statuses, ProblemDetails/metadata payloads and selected-file/image state
are retained in `.playwright-mcp/image-upload-http-results.json`. Screenshots and network evidence
are `.playwright-mcp/image-upload-placeholder.png`, `image-upload-created.png`,
`image-upload-sales-readonly.png`, `image-upload-mobile-readonly.png`,
`image-upload-baseline-network.log`, and `image-upload-final-network.log`.

## Changed Files

Production:

- `ERPFlow.Application/ProductImages/ProductImageContracts.cs`
- `ERPFlow.Infrastructure/ProductImages/EfProductImageWorkflow.cs`
- `ERPFlow.Api/ProductImageEndpoints.cs`
- `ERPFlow.Api/ApiExceptionHandler.cs`
- `front/src/app/core/api/product-image-errors.ts`
- `front/src/app/core/api/product-image-state.ts`
- `front/src/app/pattern/product-image/product-image.ts`
- `front/src/app/pattern/product-image/product-image.html`

Regression coverage:

- `ERPFlow.Tests/ProductImageApiTests.cs`
- `ERPFlow.Tests/ApiExceptionHandlerTests.cs`
- `front/src/app/core/api/product-images.spec.ts`
- `front/src/app/core/api/product-image-state.spec.ts`
- `front/src/app/pattern/product-image/product-image.spec.ts`

The first-chance observer is test-only, scoped inside the request workflow with AsyncLocal, and
counts atomically. Temporarily restoring the old missing-image throw made its regression fail
with three observed exceptions; restoring the fix passed with zero. Event subscription and
scope state are always cleared. No production exception suppression or debugger-setting changes
were added.

## Verification Status

- Focused backend image/handler suite: 69 passed. The final request-scoped first-chance observer
  was additionally verified by the exact regression and all 65 ProductImage cases.
- Final Angular suite: 118 passed across 15 test files. All 46 focused image cases pass.
- Angular production build and lint pass; hosting-worker tests pass 2/2.
- Solution compilation passes without warnings or errors. Frontend language servers were
  unavailable (installation previously declined); Angular compilation and lint provide the
  checked type/template verification. Earlier main-session C# LSP requests timed out under load;
  the final retry returned no diagnostics for all six changed C# source/test files.
- The first full backend attempt used a temporary output folder: 520 passed, one OpenAPI test
  failed because its repository-relative snapshot path resolved outside the repository. No
  snapshot or test was changed to hide this harness issue. The complete rerun in the normal
  output layout passed **521/521**, with zero failures, errors, timeouts or skipped tests.
  Namespaced TRX counters and every recorded outcome were verified directly in
  `ERPFlow.Tests/TestResults/image-upload-full-final.trx`. The console reports 14 minutes 45
  seconds of test duration; the run finished at 00:25:37 +03:00 on 29 October 2026.
- The independent read-only review found no implementation or diagnostic-honesty blocker. Its
  sole verification blocker was the temporary-output failure above; the required normal-output
  full pass cleared that blocker. Final evidence review returned **APPROVE**, confidence **HIGH**,
  after independently checking all 521 outcomes and the report. No production code changed after
  manual QA. The implementation is ready for user review; no further changes are planned.

## Safety and Cleanup

The owned browser, API and Angular processes were stopped. The deliberately hidden storage blob
and browser offline mode were restored before teardown. The task-created database was removed
only after verifying its exact name, both original product IDs and the two fixture actors; no
pre-existing database was reset or deleted. Temporary fixture code, private storage and obsolete
isolated build output were removed. Sanitized HTTP evidence, screenshots, a valid sample PNG and
ignored regression logs remain available for review.

All eight protected configuration/seeder/inventory source SHA-256 hashes match the task baseline.
`appsettings.Development.json` remains
`FD0E460E8B05A8ADFC8ED87E32DE0D29110B095277C8BFA408B8043E18532FFD`.
No seeder or inventory-authority implementation was changed. No commit or push was performed by
the task.
