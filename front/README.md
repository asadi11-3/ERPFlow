# ERPFlow web

Angular 22 client for the ERPFlow API. Setup, commands, layout, and conventions are in the root [README](../README.md#12-frontend).

```powershell
npm.cmd install
npm.cmd start   # http://localhost:4200, API expected at https://localhost:7165
```

## PWA

The production build is installable and caches the application shell, including lazy route code, local fonts, and icons. Records and all business actions require the API. No API responses, tokens, or offline commands are stored by the service worker. A failed save may already have reached the server: check the record after reconnecting before retrying.

Development `npm.cmd start` disables the service worker. To test the production worker locally:

```powershell
npm.cmd run build
npx.cmd wrangler dev --port 4300
```

Open `http://localhost:4300` online first. Wait for the service worker's initial static cache to finish downloading, then reload before testing offline launch. A first visit without connectivity cannot install the shell. API-backed browser testing normally uses `http://localhost:4200`, which the development API already allows; port 4300 needs its own allowed origin to make API calls.

Install through the browser's install menu or the **Install ERPFlow** button when the browser offers it. On iPhone/iPad, use Safari's Share menu, then **Add to Home Screen**. Installation does not extend the session: tokens remain in `sessionStorage`, and a new standalone session may require sign-in. There is no offline sign-in.

Updates appear as a notice. Finish or copy unsaved entries, choose **Review reload**, then confirm. Reload is disabled while offline or while an API write is in flight. Cancelling leaves the current app and form intact. Returning to the app rechecks connectivity, updates, and session expiry; reconnecting never replays a failed action or silently reloads a form.

### Production deployment

1. Set the single `API_BASE_URL` in `src/app/core/config.ts` to the real HTTPS API URL before building. It is deliberately still `https://localhost:7165` until that URL is supplied. `localhost` on a phone is the phone itself.
2. Have the API owner allow the deployed frontend origin, methods, and existing Authorization/Content-Type/Idempotency-Key headers. Verify trusted TLS from an actual device. The frontend Worker does not proxy `/api`.
3. Run the checks below, then `npm.cmd run deploy`. Deploy the complete build together, including `ngsw.json`, `ngsw-worker.js`, `safety-worker.js`, every hashed chunk, and icons. Do not replace only the manifest or delete assets out from under existing clients; retain old hashed assets where the host supports it.
4. Keep HTML, the manifest, and service-worker control files revalidating. `worker.ts` supplies `Cache-Control: no-cache` and prevents missing script/image files or `/api` from receiving the SPA HTML fallback. Keep `html_handling: "none"` so `/index.html` is served directly: a canonical redirect to `/` would break cached deep-link navigation. Do not override these rules with an edge "cache everything" policy. Keep the deployed app at the origin root, matching manifest/base/scope.
5. Test a real old-to-new deployment, install/relaunch on Android Chrome and iOS Safari, background/resume, expiry, account switching, and interrupted saves. Local browser automation does not replace device checks.

Rollback: publish a complete known-good build as a new deployment and let clients take the normal confirmed reload. For an emergency worker removal, serve Angular's generated `safety-worker.js` content at the existing `/ngsw-worker.js` URL with revalidation headers and keep it reachable while affected clients return. Removing registration code alone does not remove already installed workers. Do not clear unrelated origin storage or copy tokens to persistent storage.

### Verification

```powershell
npm.cmd run lint
npx.cmd tsc --noEmit -p tsconfig.app.json
npm.cmd test -- --watch=false
npm.cmd run test:hosting
npm.cmd run build
npx.cmd --yes madge --circular --extensions ts --ts-config tsconfig.app.json src/app
```

Browser checks: manifest and icon dimensions; offline login and deep links after shell installation; blocked offline writes; no `/api` entries in Cache Storage; lost-save feedback; update notice without automatic reload; cancel preserves entries; reload blocked during saves/offline; full-version confirmed reload; session expiry/resume; keyboard and 375px layout. The design decisions and broader release matrix are in [PWA_IMPLEMENTATION_PLAN.md](PWA_IMPLEMENTATION_PLAN.md).
