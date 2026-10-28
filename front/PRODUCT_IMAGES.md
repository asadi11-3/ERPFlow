# Phase 6A product image frontend integration

## Entry point

Open **Reference Data → Products**. Every product has a 48px image button. Select it
to open the full preview; missing and unavailable images use the existing package glyph.
Administrators can choose a JPEG, PNG, or WebP, preview the selection, then explicitly
save it. Deletion has a separate confirmation step. Other authenticated roles have
preview and refresh controls only. Backend authorization remains authoritative.

## Implementation

- `core/api/product-images.ts`: HttpClient adapter for protected Blob reads, FormData
  PUT, and conditional DELETE. The browser supplies the multipart boundary. Existing
  auth and connectivity interceptors provide bearer tokens and offline behavior.
- `core/api/product-image-state.ts`: component-scoped lifecycle, ETag ownership,
  operation state, reconciliation, request cancellation, and object URL ownership.
- `core/api/product-image-errors.ts`: format/size validation and actionable HTTP errors.
- `pattern/product-image/`: reusable thumbnail and native-dialog preview/editor.
- `feature/reference/reference-view.ts`: integration into the existing product column.
- Product reference types accept the backend's additive `hasImage` and `imageVersion`.
  `Reference.updateProductImage()` updates those fields immediately without reloading
  unrelated customer, price, or inventory data.
- `DESIGN.md` records the existing tokens and component conventions used by this feature.

## Concurrency and cache behavior

Image GETs use `responseType: 'blob'`, observe the full response, and bypass the browser
cache. The client preserves the quoted ETag exactly. Creation sends `If-None-Match: *`;
replacement and deletion send `If-Match`. Writes are never automatically retried.

Successful replacement removes the old URL and fetches the normalized server image.
Successful deletion immediately removes the image and reference association. A conflict,
missing precondition, uncertain write outcome, or lost connection triggers an authenticated
read for reconciliation, retaining an explanatory message and requiring a new user action.
Invalid-file responses preserve the selection for correction. A hidden/missing ETag allows
preview but disables replacement/deletion until a usable validator is obtained.

Every created object URL is revoked on refresh, replacement, deletion, discarded selection,
component destruction/pagination, or session change. Cancelled reads and session/generation
checks prevent late results from restoring stale or signed-out images.

## Error behavior

| Status | UI behavior |
| --- | --- |
| 401 | Existing auth interceptor expires the session and returns to sign-in. |
| 403 | Explain insufficient permission and reconcile the current image. |
| 404 | Missing image becomes a placeholder; missing mutation target is explained. |
| 412 | Explain the conflict, load the latest image, and require review before retry. |
| 413 | Explain the 5 MiB limit; no automatic retry. |
| 428 | Explain the missing version and refresh it. |
| 400 / 415 | Show validation guidance or safe backend Problem Details. |
| Network / 5xx | Explain uncertainty and refresh state without retrying the mutation. |

## Verification

From `front/`:

```powershell
npm run build
npm test -- --watch=false
npm run lint
```

The Angular suite includes HTTP contract tests, component-scoped lifecycle/error/race tests,
URL revocation checks, real file-input rendering, Administrator/read-only role visibility,
native-dialog interaction, size boundaries, and existing application regressions.

Verification results: production build passed (371.91 kB initial bundle, within existing
budgets); all 88 Angular tests passed (49 existing plus 39 focused image tests); lint passed.
The two hosting tests also passed. Their pre-existing Node module-type warning remains;
the package/module configuration is unchanged.

Browser QA drives the real Angular app using **controlled HTTP contract fixtures**; it does
not claim an end-to-end run against a database-backed API. It covers actual file selection,
multipart/bearer/ETag requests, replacement, conflict reconciliation, deletion, validation,
read-only Sales access, Escape, and zero remaining object URLs after sign-out. Captures
cover listing, loaded/empty previews, and preview scroll-end at 390, 1440, and 1920 CSS-pixel
widths under both system color preferences. ERPFlow's existing warm-light theme is retained.
Local artifacts are under `.playwright-mcp/frontend-images-*` and
`.playwright-mcp/frontend-image-http-evidence.json` at the repository root.
Independent functional and visual reviews passed with no blocking findings. The visual
review covered all 32 captures, including read-only access, selection, validation,
conflict recovery, and deletion confirmation. Backend/handoff hash verification covered
259 protected files and matched the start-of-task digest exactly.

No backend, migration, image storage, Demo Seeder, or application database changes belong
to this integration. No dependencies were added. No commit or push is performed.
