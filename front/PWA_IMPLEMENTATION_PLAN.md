# ERPFlow PWA implementation plan

Date: 2026-10-28

Status: local implementation delivered; production API configuration, deployed update rehearsal, and physical-device release checks remain open.

### Implementation checkpoint (2026-10-28)

- Added Angular 22.2.3 service-worker registration, a static-only prefetch configuration, an install manifest, and branded PNG/SVG icons. API requests bypass browser HTTP caching; the worker has no business-data cache or command queue.
- Added connectivity/install/update notices, explicit reload confirmation, and in-flight-write reload protection. Session expiry now runs on a timer and is rechecked on resume.
- Added a small Cloudflare Worker to keep missing assets and `/api` out of the SPA fallback and revalidate service-worker control files.
- Local checks passed: lint, application typecheck, production build, 49 unit tests, 2 hosting tests, and no circular dependencies reported by madge. Browser checks exercised a real service-worker A-to-B manifest update, retained form input on cancellation, offline reload blocking, offline shell/login, empty API caches, and a 375px layout. The update rehearsal used an in-memory test host, not a deployed release. Cloudflare preview also verified offline deep-link code loading for orders, inventory, purchase orders, and stock transfers plus expiry with a test-only JWT/clock; this does not verify backend authorization or offline data availability.
- A browser flow against the running API verified Sales sign-in, orders, create-order form loading, and token retention when going offline. No business document was created by these checks.
- Before release: supply the production API URL (user chose to configure it later), validate its CORS/TLS from the deployed origin, test authenticated production lazy routes and account switching, rehearse deployed rollback/update, and verify Android/iOS installation and resume on hardware. See `README.md` for setup and recovery steps. Headless Chrome's private context cannot verify actual installation.

## 1. Recommendation

Make the existing Angular application installable as a Progressive Web App. Keep one application, one route tree, and the existing API. The first release provides installation, a cached application shell, safe updates, and clear connectivity feedback. Business data and commands remain online-only.

An installed PWA is not automatically an offline ERP. Sales, approvals, receiving, service, and stock transfers depend on current server state, authorization, stock, and concurrency checks. Offline mutation replay needs a separate backend-supported design.

### First-release behavior

| Capability | Planned behavior |
| --- | --- |
| Install | Android/desktop installation where supported; iOS Add to Home Screen guidance |
| Launch | Standalone window using the existing landing/login and role-aware navigation |
| Offline launch | Previously installed application assets can load after a successful online cache installation |
| Offline business screens | Explain that fresh data requires a connection; do not present cached API responses as current |
| Business writes | Online only; no queue, background sync, or automatic replay |
| Authentication | Existing JWT and `sessionStorage` policy; a cold standalone launch can require sign-in |
| Updates | Notify when a complete new version is ready; reload only when the user can safely do so |
| Mobile | Validate and refine existing responsive screens rather than create a second frontend |

Push notifications, native wrappers, offline drafts, persistent business-data storage, and new API operations are outside this release.

## 2. Project understanding and evidence

### Graph findings

Reviewed `graphify-out/graph.json` and `graphify-out/GRAPH_REPORT.md`, including graph queries for architecture and the authentication/API path. The graph contains 2,787 nodes, 10,489 edges, and 106 communities. Its report is dated 2026-10-27; `built_at_commit` matches inspected HEAD `d8bdd39`.

Relevant communities connect Angular bootstrap/build configuration, routing and guards, shared forms and errors, procurement screens, API contracts, persistence, and inventory workflows. High-connectivity backend nodes include `ERPFlowDbContext`, `WorkflowConflictException`, `SalesOrder`, and `Product`: the PWA must preserve server-owned transactional decisions.

Useful graph source anchors:

| Node | Graph source location | PWA relevance |
| --- | --- | --- |
| `provideCore()` | `front/src/app/core/core.ts:L10` | Existing global-provider composition point |
| `authInterceptor()` | `front/src/app/core/auth/interceptor.ts:L8` | Bearer header and expired-session handling |
| `Session` | `front/src/app/core/auth/session.ts:L67` | Token storage, identity, expiry, role home |
| `Api` / `retryReads()` | `front/src/app/core/api/api.ts:L78` / `L65` | Read retries and command boundary |
| `ProcurementApi` | `front/src/app/core/procurement/procurement-api.ts:L11` | GET/POST/PUT and command idempotency support |
| `SalesOrderWorkflow` | `ERPFlow.Application/SalesOrders/SalesOrderWorkflow.cs:L9` | Server-owned order lifecycle |
| `GoodsReceiptWorkflow` | `ERPFlow.Application/Receiving/GoodsReceiptWorkflow.cs:L13` | Transactional receiving |
| `StockTransferWorkflow` | `ERPFlow.Application/Transfers/StockTransferWorkflow.cs:L12` | Transactional inventory movement |

These are snapshot locations, not guaranteed current line numbers. The graph marks the `provideCore()` → `authInterceptor()` collection relationship as inferred, not a direct verified call. Two self-loops and ambiguous diagram relationships were present; they are not used as correctness evidence. Template coverage is incomplete, and newer analytics work is not represented fully. Current source was checked before choosing this plan; matching HEAD alone does not prove graph freshness with uncommitted changes.

### Current application

- Angular 22, standalone, zoneless, strict TypeScript, signals, SCSS; features load lazily through routes.
- Existing layers: headless `core`, application `layout`, generic `ui`, reusable business `pattern`, lazy `feature`.
- Existing functionality now extends beyond orders/approvals/reference data: assets and dispatch, service jobcards, warehouses, suppliers, purchasing, receiving, goods receipts, stock transfers, inventory, and analytics.
- `Session` recognizes Sales, Approver, Administrator, Technician, Purchasing, and WarehouseOperator. Existing guards and role home destinations remain authoritative.
- `provideCore()` supplies fetch-based HTTP and the auth interceptor. Keep `app.config.ts` as composition only.
- Token storage is `sessionStorage`; no refresh flow is available. Session validity checks are currently call-driven, without a dedicated resume/expiry lifecycle.
- API read retries exist; writes are not generally retried. Some procurement commands support an `Idempotency-Key`, but this is not a universal offline-replay contract.
- `front/angular.json` uses `@angular/build:application`; no service-worker configuration exists. `@angular/service-worker` is not currently a dependency.
- Assets come from `front/public/`; local fonts already avoid an external font dependency. There is no manifest or PWA icon set yet.
- `front/wrangler.jsonc` deploys `dist/erpflow-web/browser` as Cloudflare Worker static assets with SPA navigation fallback.
- `front/src/app/core/config.ts` currently targets `https://localhost:7165`. A phone would resolve that address to itself, so this cannot serve as the installed production API address.

### Contract drift to account for

Older `AGENTS.md`/handoff descriptions refer to 11 API operations, three roles, and GET/POST only. Current endpoint modules and frontend contain the expanded domains above. `ERPFlow.Api/Program.cs` currently permits GET/POST/PUT and `Authorization`, `Content-Type`, and `Idempotency-Key` headers.

For implementation, inspect actual endpoint mappings, including modules composed by `ApiEndpoints.cs`, and their current DTOs. Do not generate PWA behavior from the older endpoint inventory. This plan requires no new business endpoints. Any deployment/CORS change is a separately scoped backend or hosting task.

## 3. Cache, connectivity, and session contract

### Cache only application assets

Use Angular's maintained service worker rather than a custom worker or a second caching library.

- Prefetch the shell HTML, built JavaScript (including lazy chunks), CSS, local fonts, and required app icons. This permits already-installed routes to render their code offline without claiming their data is available.
- Keep asset patterns limited to application build files. Review generated output and font/image paths before finalizing patterns.
- Use no API `dataGroups` in the first release. Do not cache authenticated JSON, analytics responses, tokens, or command bodies in Cache Storage or IndexedDB.
- Preserve Angular's default navigation exclusions. Explicitly exclude `/api` and `/api/**` if same-origin API routing is introduced; never rewrite API calls to the app shell.
- Check browser/CDN HTTP caching independently of service-worker rules. If authenticated responses lack appropriate private/no-store policy, record and resolve that with the backend/hosting owner before production release.
- Do not persist form contents or credentials as part of shell caching. Existing in-memory data may remain visible during a transient disconnect, but must not imply freshness or enable offline stock decisions.

### Connectivity is advisory

`navigator.onLine` is a useful hint, not proof the API is reachable. Show an accessible connection message and handle actual request failures, including API outages while the browser reports online.

- Suppress business submissions when definitely offline; retain existing in-memory input where safe.
- Route network failures through existing error handling. A network/status-0 failure must not become a fake 401 or erase the session.
- Retry reads using existing behavior; avoid adding a second retry layer that multiplies attempts.
- Never automatically retry writes on reconnect. A timeout or dropped response can follow a successful server commit. Explain the uncertain result and refetch the affected record/list before offering a retry; preserve existing idempotency-key semantics where supported.
- Reconnect may offer an explicit read refresh. It must not replay a form or silently replace dirty input.

### Session lifecycle

- Keep the bearer token only in `sessionStorage`, including standalone mode. Do not move it to localStorage to make installation feel persistent.
- Recheck expiry when the installed app resumes or becomes visible, and handle expiry while open. Derive validity from current time rather than assume a JWT-based `computed()` reruns as time passes.
- Preserve existing 401 → clear session → login with return URL, 403 handling, and role guards. Do not add `/me`, refresh, or logout calls without backend support.
- Offline login is unavailable. An offline app may show a connection-required state before it can authenticate or fetch data.
- On logout/account switch, clear or invalidate user-specific in-memory state; verify the next user cannot see the previous user's records. Public shell assets may stay cached.

## 4. Ordered implementation phases

### Phase 0 — Confirm deployment and baseline

1. Identify the production web origin, reachable HTTPS API origin, and supported device/browser list.
2. Replace the development-only API address through the existing single configuration point and the smallest suitable production build configuration. Keep a documented local-development value; do not introduce a runtime configuration service unless deployment actually needs it.
3. Verify production CORS for the exact web origin and existing methods/headers. Verify API TLS on a real phone without certificate bypasses.
4. Record baseline build size and current lint/typecheck/build/test results so pre-existing failures are distinguishable.

**Exit:** the ordinary deployed web application works on target devices against the real API. Installation work cannot compensate for an unreachable API.

### Phase 1 — Add installability and static shell caching

1. Use the Angular PWA schematic matching the installed Angular 22 toolchain, initially with `npx.cmd ng add @angular/pwa@22 --dry-run`. Review its proposed changes before applying; align service-worker dependency versions with the installed Angular packages.
2. Review generated `angular.json`, manifest, icons, index metadata, and `ngsw-config.json`. Register `provideServiceWorker` through `provideCore()`; keep app configuration minimal. Enable it for production builds, normally disabled in development, with `registerWhenStable:30000` registration.
3. Set a stable manifest identity, root scope/start URL for the existing root deployment, `standalone` display, product name/short name, and theme/background colors from the current design. Use real ERPFlow icons, including 192/512 sizes, a correctly padded maskable asset, and an Apple touch icon.
4. Apply the asset-only cache contract above; include lazy code and locally served fonts. Do not accept a schematic-generated cache rule without checking its actual matched files.
5. Confirm the production build emits `ngsw.json`, `ngsw-worker.js`, the manifest, and icons into the directory Wrangler serves.

**Exit:** install succeeds on a supported browser; after the worker controls the app and caching completes, offline reload renders the shell. First-ever offline access is not promised.

### Phase 2 — Add safe updates, connectivity, and resume handling

1. Add small signal-driven PWA lifecycle state under `core/pwa/` only for the lifecycle logic actually shared by the application. Keep visual feedback in the existing application layout/composition, including the login experience where appropriate.
2. Observe `SwUpdate` only when enabled. On `VERSION_READY`, offer a user-controlled reload. Avoid `activateUpdate()` without a full reload because old code and new lazy chunks can become incompatible.
3. Defer disruptive updates while a form is dirty or a request is in flight. Reuse existing form/navigation protection where available; confirm before discarding unsaved work.
4. Handle update download failures without breaking the current usable version. For an unrecoverable version, explain that reload is needed and protect unsaved input where possible.
5. Add connectivity feedback and explicit retry/refresh actions consistent with existing error components. Recheck session expiry on resume and while open.
6. Keep browser listeners/subscriptions properly scoped and cleaned up. Use signals and Angular lifecycle interop; do not add custom event buses or state subjects.

**Exit:** updates do not silently reload a working form, reconnect never resubmits a command, and resumed expired sessions return to login correctly.

### Phase 3 — Validate installed mobile workflows and installation guidance

1. Verify existing navigation drawer, keyboard/focus handling, scroll containers, mobile cards/tables, form validation, charts, and dialogs in standalone mode.
2. Check small screens, safe-area insets, virtual keyboard overlap, touch targets, orientation changes, text zoom, contrast, and reduced motion. Make targeted fixes only where observed.
3. Offer installation only when supported and applicable. If using `beforeinstallprompt`, consume the actual browser event and hide the action after installation; do not show a dead button on unsupported browsers.
4. Provide concise iOS Share → Add to Home Screen guidance where needed. Installation UI is enhancement, not a prerequisite for normal web use.
5. Exercise representative role workflows: sales order creation/submission, approval, technician jobcard work, purchasing, warehouse receiving/transfer, and administrator navigation. Preserve server validation and concurrency handling.

**Exit:** target devices complete authorized online workflows and communicate offline limitations without losing work.

### Phase 4 — Production rollout and operational verification

1. Deploy a production build to an HTTPS staging origin using the same Wrangler asset configuration as production. Use production builds for service-worker tests; ordinary development serving is insufficient.
2. Verify correct MIME types and actual file contents for worker/manifest/assets. Missing worker/build files must not return `index.html` with a successful status through SPA fallback.
3. Verify deep links both through Cloudflare fallback and under the controlling service worker. Avoid long-lived CDN caching of `ngsw.json`, worker scripts, or HTML that prevents update discovery; retain hashed assets through the deployment transition where the hosting system permits it.
4. Deploy version A, install it, then deploy version B with a visible change. Confirm version detection, complete download, deferred prompt, safe reload, and successful lazy navigation afterward.
5. Document deployment/update recovery in `front/README.md`. For withdrawal of PWA support, use Angular's documented deactivation/safety-worker process at the existing worker URL/scope; simply removing registration code leaves installed workers behind. Test the chosen procedure on staging.

**Exit:** installation, API access, version upgrades, and recovery have been demonstrated on real target devices, not only an audit score.

## 5. Expected change surface

Paths are implementation targets, not files created by this planning task.

| Path | Intended change |
| --- | --- |
| `front/package.json`, `front/package-lock.json` | Angular-compatible service-worker dependency and schematic changes |
| `front/angular.json` | Production service-worker generation; production API configuration if build replacement is chosen |
| `front/ngsw-config.json` | Explicit asset groups, navigation rules, no API data cache |
| `front/public/manifest.webmanifest`, `front/public/icons/` | Install metadata and branded icons |
| `front/src/index.html` | Manifest/theme/Apple icon metadata |
| `front/src/app/core/core.ts` | Service-worker provider composition |
| `front/src/app/core/config.ts` and minimal production counterpart if needed | Reachable environment-specific API URL |
| `front/src/app/core/pwa/` | Small lifecycle/connectivity state, if shared behavior needs it |
| `front/src/app/core/auth/session.ts` | Expiry/resume behavior and session-state invalidation |
| Existing layout/app composition and affected forms | Feedback, safe reload integration, observed mobile fixes |
| `front/wrangler.jsonc` / hosting header configuration, only if verification requires changes | Correct static delivery and update caching |
| `front/README.md` | Local PWA verification, deployment, installation, recovery |

Keep current dependency boundaries. Do not create a new architecture layer, cross-import feature implementations, add a native framework, or replace existing forms/state. Before implementation, consult the required Trajan/Shvets sections for any structural choice and load the four required UI skills before visual work. Any new `core/pwa` logic stays headless; generic `ui` remains independent of `core`.

## 6. Verification and acceptance checklist

Run from `front/` using Windows command shims:

```powershell
npx.cmd tsc --noEmit -p tsconfig.app.json
npm.cmd run lint
npm.cmd run build
npm.cmd test -- --watch=false
```

Use the repository's current test/build configuration if its scripts change. Inspect generated `dist/erpflow-web/browser/ngsw.json` for the expected static assets and absence of API cache groups. Run `madge` with the project's existing setup when imports/structure change to check circular dependencies.

Add focused Vitest checks for meaningful new lifecycle behavior: update-ready state, disabled-service-worker behavior, expiry on resume, and no command replay after reconnect. Browser/real-device checks are required for actual installation and worker behavior; jsdom alone cannot verify them.

| Scenario | Pass condition |
| --- | --- |
| Android Chrome and desktop Chromium | Correct manifest/icons, install, standalone launch, route navigation |
| iOS Safari/Home Screen | Manual installation guidance, correct icon, usable standalone layout and session behavior |
| Browser without installation support | Existing web app remains fully usable; no misleading install action |
| First visit offline | Honest browser/connection failure; no claim that an uncached app can load |
| Installed shell offline | Cached app renders after completed installation; protected data requires connection |
| Offline login or expired JWT | Connection/sign-in requirement is explicit; no offline authentication bypass |
| API unreachable while browser is online | Existing network error handling works; no false session expiry |
| Disconnect before a command | No offline submission and no later automatic replay |
| Disconnect after server commit | Uncertain result explained; read reconciliation before any retry |
| Update while editing/submitting | No silent reload, discarded input, or duplicate command |
| Version A → B, including unopened lazy routes | Safe reload loads a coherent version and all authorized routes work |
| Expiry while suspended | Resume invalidates expired session and preserves safe return navigation |
| Logout/account switch | No prior user's data or credentials remain exposed to the next user |
| 401/403/409 | Existing auth, permissions, conflict/refetch behavior remains correct |
| Cache inspection | No API JSON, bearer tokens, or mutation payloads in service-worker storage |
| Direct deep link, then refresh | Correct route online; shell fallback works offline after cache installation |
| Missing static asset / failed update | No HTML masquerading as JS/manifest; current version remains usable where recoverable |
| PWA withdrawal | Tested worker deactivation restores normal web loading |

Use test accounts and non-production records for workflow verification. Validate role-specific access against the running API. A successful installation or Lighthouse check alone is not release acceptance.

## 7. Optional later offline scope

Only plan this after observing a concrete disconnected workflow and obtaining approval for its data-retention policy.

1. **Read-only offline data:** explicitly select allowed fields/screens; define per-user storage, retention, stale timestamps, expiry/logout cleanup, and device exposure. Do not cache every authenticated GET by URL.
2. **Local drafts:** agree on persistence, sensitive fields, schema migration, user isolation, recovery, and discard rules. Revalidate reference data/prices/stock online before submission.
3. **Offline commands:** require a backend-reviewed per-command contract for idempotency, authorization/expiry, concurrency, ordering, reconciliation, and uncertain results. Current partial idempotency support is insufficient for a universal queue.
4. **Push:** first needs a real notification requirement and backend subscription/delivery support. The current app cannot gain a notification feed from a service worker alone.

The first release can ship without these additions. Its release boundary is: installable application, reliable online workflows, cached static shell, safe updates, and honest offline states.

## References

- Project graph: `graphify-out/graph.json`, `graphify-out/GRAPH_REPORT.md`.
- Current application/API sources listed above; endpoint mappings take precedence over older handoff inventory.
- [Angular service-worker setup](https://angular.dev/ecosystem/service-workers/getting-started)
- [Angular service-worker configuration](https://angular.dev/ecosystem/service-workers/config)
- [Angular service-worker communication and updates](https://angular.dev/ecosystem/service-workers/communications)
- [Angular service-worker deployment and recovery](https://angular.dev/ecosystem/service-workers/devops)
