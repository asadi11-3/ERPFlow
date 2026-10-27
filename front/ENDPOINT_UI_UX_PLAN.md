# Endpoint-by-endpoint UI/UX plan

Analyzed 2026-10-27 against merged baseline `d8bdd39`. Covers all **57 HTTP operations** in the current OpenAPI document, checked against endpoint handlers, application workflows and relevant domain rules. This is a design/implementation plan, not shipped UI.

## 1. Evidence, skill guidance and design direction

### Sources and precedence

1. `ERPFlow.Api/ApiEndpoints.cs`, `ProcurementEndpoints.cs`, `GoodsReceiptEndpoints.cs`, `StockTransferEndpoints.cs`, `InventoryQueryEndpoints.cs`: actual methods, paths, request binding and endpoint policies.
2. Corresponding `ERPFlow.Application/*/*Workflow.cs`, `ProcurementMasters.cs`, contracts and domain entities: effective authorization, ownership, transitions, validation and stock effects. Endpoint authentication alone does not establish workflow access.
3. `docs/openapi/erpflow-v1.json`: request/response field inventory. Integer enums and application rules need source inspection because the document does not fully describe their allowed values/conditions.
4. Current `front/src/styles.scss`, `ui/`, shell and feature routes: visual and interaction baseline.

`AGENTS.md` still describes 11 operations and GET/POST-only CORS. Actual backend has 57 operations, six roles and GET/POST/PUT CORS. The real handlers and workflow policies govern this plan.

### UI/UX skill applied

Used `ui-ux-pro-max` design-system search for ERP/procurement/warehouse productivity and targeted UX searches for forms, recovery, keyboard navigation, labels and focus. Applied high-priority guidance: explicit labels, visible focus, logical keyboard order, actionable error recovery and accessible controls. Generic generated font/color/framework recommendations do not override the requested existing Angular design.

Also applied the already-loaded design-taste, Impeccable product-register and Emil design-engineering guidance: familiar task layouts, complete states, restrained decoration, accessible reduced motion and brief purposeful feedback.

**Physical scene:** office purchasing and sales staff scan records on desktop, while warehouse/technical staff use the same light interface on smaller touch devices in bright working spaces. Preserve the current light warm-paper surfaces, teal action color and high-legibility data hierarchy.

### Visual contract for every screen

- Use existing OKLCH `--bg`, `--surface`, `--tint`, `--ink`, `--muted`, `--line`, primary and semantic status tokens. No new palette, fonts or UI dependency.
- Geist Variable for text; Geist Mono/tabular numerals for quantities, prices, document identifiers and versions where troubleshooting needs them. Do not expose raw rowversions as ordinary UI content.
- Page heading plus short task description; one primary action per task area. Use `.page`, `.page-head`, `.toolbar`, `.card`, `.table-wrap`, `.table`, `.split`, `.tabs`, `.timeline`, `.alert`, `.empty` and existing skeleton classes.
- Existing 12px card/10px control radii, flat borders, 20px desktop/16px mobile card padding, minimum 44px interactive targets. Reuse select, pagination, pill, icon, row menu and confirmation controls.
- Desktop lists are tables. At current container breakpoints, switch to labeled record rows; preserve identifier/status/main action, move secondary metadata underneath. Forms and detail/action splits become one column. Never hide required decision context on mobile.
- Keep existing short transition language, limit new state transitions to roughly 150–200ms, animate transform/opacity only, and respect reduced motion. Do not introduce decorative or repeated page-load choreography.

## 2. Shared interaction and contract rules

### Access vocabulary

`A` Administrator, `S` Sales, `P` Approver, `T` Technician, `B` Purchasing, `W` WarehouseOperator. “All” means authenticated users unless explicitly anonymous. Server roles/ownership remain authoritative.

- Home: S/A `/orders`, P `/approvals`, T `/service`; proposed B `/purchase-orders`, W `/receiving`. New roles currently fall through to a guarded `/orders`, so routing must be corrected.
- Service nav is T/A only. Equipment read pages may be shown to all authenticated roles. Sales order readers are S/P/T/A, with S restricted to own records; B/W do not gain sales access merely because a GET endpoint says authenticated.
- Procurement review stays inside PO detail/list. Sales approval and PO approval are separate queues; no unified approval feed endpoint exists.

### States and recovery inherited by every endpoint below

- **Read:** layout-matched skeleton on first load; preserve known rows during filter refresh with a modest busy indicator. Distinguish true empty data, no filter matches, permission failure and dependency failure. Pagination/filter changes return to page 1 and update URL query state where useful.
- **Write:** field-level validation after interaction/submission, error summary for failed submission, focus first invalid field. Keep entered data. Disable only the pending operation and competing mutations; expose busy semantics. A successful server response, not the click, establishes success.
- **400:** show safe Problem Details/field errors next to the relevant control, with a correction path. **401:** clear session and route to login with safe internal return URL; login's own invalid-credentials response stays on its form. **403:** inline access message, no endless redirect. **404:** inline missing-record state and back-to-list link. **409:** explain the domain/version conflict and refresh affected server state without discarding unsaved input. **5xx/network:** generic recovery message; no internals.
- Existing transient GET retries can remain bounded. Do not silently replay arbitrary writes. For non-idempotent writes after a lost response, reconcile observable state before offering a deliberate retry; if state is insufficient to establish success, say the result is uncertain.
- Keep exact base64 `rowVersion` strings from responses; versioned mutations use their latest loaded token. Do not auto-resubmit stale edits after a 409. Idempotent uncertain-result retries keep their original payload and key, including original versions.
- UTC timestamps, including those missing `Z`, display in local time with explicit date/time formatting. No live countdown unless the task needs one. History actor IDs show “You” for own `sub`, otherwise ID; no fabricated employee names.
- Join customer/product/warehouse IDs only from permitted loaded sources, otherwise show ID. A failed name lookup must not be presented as “record missing.” Historical inactive records remain readable.
- Paginated APIs use server filtering only where supported. Client search is permitted for fully loaded unpaged reference/equipment/queue arrays. Never search a single page as though it were the whole dataset. Paginated form lookups must load all needed pages or retain loaded known selections; no invented search endpoint.
- Persistent success confirmation should be inline near the task or represented by the new record/status. Use polite live regions; do not depend on fleeting toast text. Native buttons/links, associated labels, focus rings, logical tab order and explicit status text are mandatory.

### Document and field conventions

- Sales: short uppercase first eight GUID characters, full ID in route. Purchasing/receipt/transfer/jobcard/delivery/asset: use actual server PO number, number, ticket number, delivery number or tag/serial.
- Sales prices and totals are read-only server values. A new sales draft may show a labeled estimate. PO unit cost is an editable purchasing input; server returns authoritative totals. Do not assume all money-like fields establish a currency contract.
- Quantity: positive Int32 whole numbers for lines. Validate duplicates and required IDs. No fractional stock, negative quantities or invented discount/tax/date/shipping fields.
- PO statuses: Draft 0, PendingApproval 1, Approved 2, Rejected 3, Cancelled 4, PartiallyReceived 5, Received 6, ShortClosed 7. Display all, but there is no short-close action endpoint.
- Receipt and transfer statuses: Draft 0, Posted 1, Cancelled 2. Posted/cancelled are terminal. Sales return-for-correction restores Draft; it is not a new order status.

## 3. Existing sales, equipment and service operations

### E01 — POST /api/auth/login

- **Contract/access:** anonymous `{email,password}` -> `{accessToken,expiresAt}`. Email required/max 320, password required/max 256. Identity comes from JWT; token in sessionStorage only.
- **UX:** existing `/login`, labeled email/password, password visibility toggle, Enter-to-submit, single “Sign in” action. Preserve email after invalid credentials; never retain password in URLs, plan artifacts or demo tiles. Route to allowed return URL or role home on success.
- **States/check:** field errors versus general invalid-credentials message; pending label “Signing in…”. No forgot-password/register/remember-me controls. Verify six role destinations and idle expiry as well as guard-time expiry.

### E02 — GET /api/reference/customers

- **Contract/access:** all; unpaged active customers `{id,name,email}` only. No customer detail or mutation endpoint.
- **UX:** `/reference` Customers tab and shared customer selectors in sales/service forms. Search over the complete returned array. Show name/email; ID available for disambiguation. No phone/address/last-order/inactive-customer controls.
- **States/check:** “No active customers available” blocks dependent creation with a retry path; a failed reference fetch is not an empty customer list. Older order customer IDs may have no active-name match and must retain ID fallback.

### E03 — GET /api/reference/products

- **Contract/access:** all; unpaged `{id,name,sku,unitPrice,availableStock}`. Not warehouse-level on-hand/reserved data.
- **UX:** Products reference tab; searchable product selectors; show name/SKU and read-only sales unit price. Label available stock according to this reference source, never as selected-warehouse stock. PO entry uses independent unit-cost input.
- **States/check:** loading/empty/retry states shared with selectors. Product choice does not guarantee stock at submit/post time. No product create/edit, thumbnails or inventory-adjust buttons.

### E04 — GET /api/sales-orders

- **Contract/access:** effective S/P/T/A; S own only. `page=1`, `pageSize=10` (1–100), optional integer `status`; paged sales DTOs.
- **UX:** existing `/orders`: identifier, customer, created time, status, total and accessible open-row link. Server status filter and pagination; “Create order” S/A only. Keep list state when returning from detail.
- **States/check:** filtered empty has “Clear status”; true empty offers create only for S/A. No server search, creator/date filter or whole-dataset KPI inferred from current page. B/W get accessible destinations rather than this page as home.

### E05 — POST /api/sales-orders

- **Contract/access:** S/A; `{customerId,items:[{productId,quantity,warehouseId?}]}` -> created Draft. Active customer; at least one distinct product, positive quantities. Server chooses price, totals and creator.
- **UX:** existing `/orders/new`: customer selector, editable line list, nullable warehouse source, read-only unit price, labeled estimated total. Primary “Save draft”; “Save and submit” is two explicit requests, this endpoint then E09.
- **States/check:** if creation succeeds but submit fails, navigate to or retain the saved Draft and explain failed submit. Do not rerun creation. Preserve per-line source on all form edits; no stock reservation claim until submission succeeds.

### E06 — GET /api/sales-orders/{id}

- **Contract/access:** effective S/P/T/A; S own only. Order includes items, reservations, server total/status/timestamps and rowVersion.
- **UX:** `/orders/:id`: existing detail composition, line table, reservation section, workflow/history context and role/status actions. Show source warehouse per line and reservation, with fallback ID. Pending P/A can enter approval review.
- **States/check:** inline 403/404; no edit for non-Draft or unauthorized actor. Guard computed/resource reads while loading/error. Refresh detail after any transition; do not assume current total or stock state from list data alone.

### E07 — GET /api/sales-orders/{id}/history

- **Contract/access:** same effective order-read scope; unpaged action/actor/timestamp/remarks entries. Includes submit, approve, reject and return events.
- **UX:** timeline within order detail/review, chronological presentation with action text, local timestamp and actor ID/“You”. Returned reasons remain visible when the order is Draft again.
- **States/check:** “No workflow events yet” for a new Draft. A history error gets its own retry without replacing loaded order content. No global approval-history page: this is per order only.

### E08 — PUT /api/sales-orders/{id}

- **Contract/access:** S owner/A; Draft only. `{customerId,items:[{productId,quantity,warehouseId?}],rowVersion}` replaces editable draft data. Valid version required.
- **UX:** existing `/orders/:id/edit` reuses creation form, “Save changes” action, clear cancel/back path. Preserve warehouse IDs returned on original lines, even before selector enhancement ships.
- **States/check:** on stale version or state change retain the user's input and offer reload/review. Never overwrite newer Draft automatically. Validate duplicates/quantities/active source without optimistic success.

### E09 — POST /api/sales-orders/{id}/submit

- **Contract/access:** S owner/A; Draft -> PendingApproval; no request body. Reserves stock on submission and validates active customer, source and availability.
- **UX:** Draft detail “Submit for approval” with compact explanation that quantities become reserved. For save-and-submit, report saved Draft and submission as separate stages.
- **States/check:** 409 insufficient stock/invalid source preserves Draft and refreshes detail/reference context. Success updates status, reservations and history. No second stock reservation during client refresh.

### E10 — GET /api/sales-orders/pending

- **Contract/access:** P/A; complete unpaged pending queue.
- **UX:** existing `/approvals`, customer/identifier/total/submitted time and “Review” link; local search over complete queue is valid. Shell pending-sales badge uses returned queue length only.
- **States/check:** distinguish “No pending sales orders” from search no-match and fetch failure. Above-10,000 orders remain visible to P with “Administrator approval required”; do not hide them as though already resolved.

### E11 — POST /api/sales-orders/{id}/approve

- **Contract/access:** P/A, PendingApproval only; `{comments:null|string}`, max 1000 chars. Orders with total **greater than 10,000** require A. Approval retains reservations; does not dispatch stock.
- **UX:** existing approval review, optional comments and primary “Approve”. For P on higher totals replace approve affordance with a clear Administrator-required explanation; reject/return remain governed independently.
- **States/check:** send the required JSON body even when comments empty. Success refreshes detail/history and queue. Repeated/stale decision gets real 409/403 feedback. Do not apply PO self-decision restrictions to sales without a sales rule requiring them.

### E12 — POST /api/sales-orders/{id}/reject

- **Contract/access:** P/A, PendingApproval only; `{reason}` nonblank/max 1000. Rejected terminal state releases reservations.
- **UX:** same decision area, “Reject” secondary destructive action. Show required reason inline when selected; describe terminal rejection and stock release before confirmation. Preserve reason on failure.
- **States/check:** whitespace-only reason fails locally; success updates status/history/reservations/queue. Rejected sales orders have no resubmit/edit action.

### E13 — POST /api/sales-orders/{id}/return

- **Contract/access:** P/A, PendingApproval only; `{reason}` nonblank/max 1000. Returns to Draft for correction and releases reservations.
- **UX:** “Return for correction”, visually distinct from Reject. Inline required reason and explanation that Sales can edit and resubmit. History retains correction context.
- **States/check:** success refreshes all order panels and queue. Returned Draft exposes edit/submit only to owner S/A, not to reviewer solely because reviewer performed return.

### E14 — POST /api/equipment/sales-orders/{id}/dispatch

- **Contract/access:** T/A; Approved, not already dispatched. `{carrier,trackingNumber?,serialNumbers?:[{productId,serialNumber}]}` -> delivery note; consumes reserved stock and creates assets. Max 1000 units/serials; carrier/tracking max 100 chars. Supplied serials must be unique case-insensitively, max 100 chars, and exactly cover product quantities. Empty/null serial list generates serials server-side.
- **UX:** approved order detail dispatch panel: optional carrier/tracking and explicit “Generate serials” default versus “Enter serials” progressive input. Display affected quantities and final “Dispatch order” confirmation. Do not require carrier merely because its label exists; server permits blank.
- **States/check:** no partial dispatch UI. Refresh delivery notes/assets/order after success. No automatic retry after lost response; inspect delivery-note list for this order and reconcile. A conflict must not create a second set of assets in the client.

### E15 — GET /api/equipment/delivery-notes

- **Contract/access:** all; unpaged delivery number, salesOrderId/customerId, carrier/tracking, dispatchedAt/deliveredAt. No individual delivery detail/update/mark-delivered endpoint.
- **UX:** existing equipment area Delivery notes tab; client search across complete result, local date formatting and order links only for roles allowed to read sales. Expand row for secondary metadata if needed.
- **States/check:** null deliveredAt means no recorded delivery time, not permission to mark delivered. Empty state links eligible T/A to approved-order workflow, not a nonexistent delivery-create screen.

### E16 — GET /api/equipment/machine-assets

- **Contract/access:** all; complete asset list with tag, serial, product/customer/order IDs, installed/warranty times, string status and meter reading.
- **UX:** existing `/assets` list: tag/serial, joined product/customer, status, warranty date; client search valid. Use as customer-filtered selector for service creation where T/A is permitted.
- **States/check:** zero assets explains that dispatch creates them. No independent asset creation, warranty edit or invented equipment filters backed by nonexistent server queries.

### E17 — GET /api/equipment/machine-assets/{id}

- **Contract/access:** all; one asset DTO, no write endpoint.
- **UX:** existing `/assets/:id`: serial/tag identity, customer/product, installed/warranty dates and meter reading. T/A “Create jobcard” preselects this asset/customer. Show order link only for authorized sales readers.
- **States/check:** inline missing/access state. Expired warranty may be derived from actual timestamp with explicit label, not an invented maintenance schedule. No asset jobcard-history endpoint; do not imply one exists.

### E18 — GET /api/service/jobcards

- **Contract/access:** effective T/A even though endpoint group merely requires authentication. Optional defined `status`; returns unpaged jobcards.
- **UX:** existing `/service`: ticket, customer, technicianName, priority, status, created time. Server status filter; local search only over currently returned complete status set, labeled accordingly. “New jobcard” T/A.
- **States/check:** hide service nav for other roles and still handle direct-link 403. No arbitrary user reassignment, schedule filter or paginated server search. No “Start work” action because there is no endpoint for it.

### E19 — GET /api/service/jobcards/{id}

- **Contract/access:** T/A; ticket/customer/optional asset, technicianName, fault, priority/status, times, notes, final meter and part lines with warehouse source.
- **UX:** existing `/service/:id`, main fault/details and parts section plus action panel. Link permitted asset detail; display part source, reserved quantity/prices from returned DTO, and completion metadata.
- **States/check:** completed is read-only. Do not add edit/delete/cancel/history controls absent from API. Refresh after parts request or completion; failed parts subresource names should fall back to IDs rather than hide the jobcard.

### E20 — POST /api/service/jobcards

- **Contract/access:** T/A; `{customerId,machineAssetId?,technicianName,reportedFault,priority}`. Fault required/max 1000, name max 150, defined priority. Optional asset must belong to selected customer.
- **UX:** existing `/service/new`: customer first, optional compatible asset selector, technician display-name text field, priority and labeled fault textarea. Asset-origin creation preselects and explains relationship; changing customer clears incompatible asset selection with visible feedback.
- **States/check:** no fabricated user-directory dropdown. Save produces actual ticket and navigates to detail. Client validation mirrors real required fields, not invented assignment rules; customer/asset mismatch preserves form for correction.

### E21 — POST /api/service/jobcards/{id}/request-parts

- **Contract/access:** T/A, active jobcard; `{parts:[{productId,quantity,warehouseId?}]}`. Positive quantities, unique products, cannot re-add an existing jobcard product. Reserves stock, does not complete/consume it.
- **UX:** inline “Request parts” on detail, product/source/quantity rows. Exclude or clearly disable products already requested; show “Already requested” rather than silently merging quantities. Warehouse source is per part.
- **States/check:** stock preview is advisory and source-sensitive; T lacks inventory-query access, so do not fetch warehouse stock using elevated assumptions. 409 preserves request rows, refreshes jobcard and explains availability. No part edit/remove endpoint.

### E22 — POST /api/service/jobcards/{id}/complete

- **Contract/access:** T/A, active jobcard; `{technicianNotes?,finalMeterReading?}`. Notes max 2000; meter nonnegative and, when asset-linked, not below asset's current reading. Consumes reserved parts.
- **UX:** completion panel with optional work notes and meter reading, existing reading shown when available, “Complete jobcard” final confirmation describing consumed parts/read-only result.
- **States/check:** completed metadata appears in place of controls. Refetch asset/jobcard and relevant stock views after success. No reopen endpoint or optimistic completion before server response.

## 4. Warehouse and supplier master operations

Master screens use list plus inline create/edit detail panel, not six separate pages per resource. Active/inactive text accompanies semantic pills. Activation controls are explicit actions, not optimistic switches that obscure version failures.

### E23 — GET /api/warehouses

- **Contract/access:** all; `page=1,pageSize=20,active?`; paged `{id,code,name,isActive,createdAt,rowVersion}`. Page size 1–100.
- **UX:** proposed `/warehouses`: code/name/status table, active/all/inactive filter and pagination. A sees “New warehouse”; others read. Shared source selectors request active warehouses and correctly traverse pages.
- **States/check:** no invented server text search. Do not assume the first 20 entries are all choices. Preserve known inactive warehouse label when displaying historical records.

### E24 — POST /api/warehouses

- **Contract/access:** A; `{code,name}` -> 201 warehouse. Code required/max 32, name required/max 200. Creates master data, not stock.
- **UX:** “New warehouse” inline form on warehouse page, two labeled fields and “Create warehouse”. Help text: “Creates a location record. Stock is recorded through inventory workflows.”
- **States/check:** duplicate code/invalid data stays inline with retry after correction. Success inserts/refetches row and focuses created record; do not show opening-stock inputs.

### E25 — GET /api/warehouses/{id}

- **Contract/access:** all; single warehouse with current version.
- **UX:** inline detail panel or lookup hydration; show code, name, active state and created date. A gets edit/state actions; code stays immutable.
- **States/check:** load latest detail before editing. Missing warehouse must not clear a historical document's warehouse ID; show ID plus lookup issue.

### E26 — PUT /api/warehouses/{id}

- **Contract/access:** A; `{name,rowVersion}`. Code cannot change; name required/max 200.
- **UX:** edit name in detail panel; read-only code and clear “Save changes”/“Cancel”. Use latest loaded version.
- **States/check:** 409 retains proposed name and offers reload/review; no automatic overwrite. Success updates all visible labels for this ID.

### E27 — POST /api/warehouses/{id}/activate

- **Contract/access:** A; `{rowVersion}` -> active master.
- **UX:** inactive warehouse detail “Activate warehouse” action, short confirmation only if needed by existing pattern. Success updates status and active-source lookup caches.
- **States/check:** version conflict refreshes record; never flip status locally before success. Activation does not initialize stock balances.

### E28 — POST /api/warehouses/{id}/deactivate

- **Contract/access:** A; `{rowVersion}`. Legacy default cannot deactivate; stock, reservations and open business commitments can block deactivation.
- **UX:** explicit “Deactivate warehouse” confirmation explaining removal from future source selection while history remains. For known legacy default show protected-state explanation instead of a live destructive action.
- **States/check:** 409 displays backend commitment explanation with refresh path. Do not invent an “all blocking commitments” endpoint or claim an empty current table proves eligibility. Keep warehouse active until confirmed response.

### E29 — GET /api/suppliers

- **Contract/access:** A/B/P; `page=1,pageSize=20,search?,active?`. Search max 200; paged supplier DTOs.
- **UX:** proposed `/suppliers`, name/code/contact/email/status, debounced server search plus explicit clear, active filter and pagination. A/B create/manage; P read-only. Keep phone/address in detail to avoid wide-table clutter.
- **States/check:** W cannot call this for receiving labels. Distinct no-results state offers clear filters, not creation to unauthorized P. Ignore stale search responses so old results cannot replace a newer query.

### E30 — POST /api/suppliers

- **Contract/access:** A/B; `{supplierCode,name,contactName?,email?,phone?,address?}` -> 201 supplier. Code 1–32 ASCII letters/digits/hyphen/underscore; name required/max 200; contact 150, email 320 with valid address, phone 50, address 1000; no control chars.
- **UX:** inline create panel: code/name first, optional contact fields in secondary grouping. Native email input, visible labels and optional markers. Address textarea is supported for suppliers, not invented for customer/sales forms.
- **States/check:** duplicate/validation error stays at fields; success returns created supplier and makes it available to PO selector. Do not create a PO automatically unless user explicitly chose that next action.

### E31 — GET /api/suppliers/{id}

- **Contract/access:** A/B/P; one complete supplier DTO plus version.
- **UX:** supplier detail panel with code, name, contact, email, phone, address, state/date. A/B edit; P inspect only. Link from commercial PO where permitted.
- **States/check:** inline 403/404 within panel. Inactive historical supplier remains identified; activation eligibility is not inferred from visibility.

### E32 — PUT /api/suppliers/{id}

- **Contract/access:** A/B; `{name,contactName,email,phone,address,rowVersion}`, same field bounds. Supplier code immutable.
- **UX:** edit detail form, read-only code, save/cancel. Optional blanks normalize consistently to nullable API values.
- **States/check:** preserve input through field/version failure. Refresh commercial selector labels after success; no supplier delete control.

### E33 — POST /api/suppliers/{id}/activate

- **Contract/access:** A/B; `{rowVersion}`.
- **UX:** inactive supplier “Activate supplier” action, in-place success and active-selection cache refresh.
- **States/check:** do not optimistically toggle. A concurrent update prompts reload; show newly returned active state rather than assuming the requested transition succeeded.

### E34 — POST /api/suppliers/{id}/deactivate

- **Contract/access:** A/B; `{rowVersion}`. Changes eligibility for new purchasing source admission; history remains.
- **UX:** explicit confirmation: supplier no longer available for new purchasing selection. Avoid implying existing approved documents or history are deleted.
- **States/check:** use real backend response for any conflict, not the warehouse's unrelated stock-blocking rules. Success refreshes list/detail and active PO selectors.

## 5. Purchase-order operations

Commercial users see costs; W uses operational DTOs only. PO list/detail/form and review share a coherent feature, not a second global dashboard. Purchasing ownership is enforced on reads/history as well as writes.

### E35 — GET /api/purchase-orders

- **Contract/access:** A/B/P; B own only. `page=1,pageSize=20,status?`; commercial paged DTOs.
- **UX:** proposed `/purchase-orders`, PO number, supplier, warehouse, status, date and total. Server status filter; PendingApproval view supports P/A review. A/B “New purchase order”.
- **States/check:** no invented PO search/supplier/date filters. Page counts come from response. B empty state says “Your purchase orders” rather than implying organization-wide visibility.

### E36 — POST /api/purchase-orders

- **Contract/access:** A/B; `{supplierId,warehouseId,items:[{productId,quantity,unitCost}],notes?}` -> 201 Draft. Active supplier/warehouse, 1–100 unique products, positive quantities, bounded nonnegative cost with at most four decimals; notes max 1000.
- **UX:** proposed `/purchase-orders/new`: supplier and destination warehouse first, line editor with quantity and cost, estimated line/overall totals, optional notes. Show editable “Unit cost”, distinct from sales price. Primary “Save draft”; save-and-submit is sequential E36 then E40.
- **States/check:** retain form on inactive source/validation conflict; no receiving until Approved. If draft saves but submit fails, keep created PO, never duplicate it. Supply exact four-decimal values without silent rounding beyond contract precision.

### E37 — GET /api/purchase-orders/{id}

- **Contract/access:** A/B/P; B own only. Returns number, supplier/warehouse/creator, status, timestamps, notes, totals, items including received/remaining quantities, rowVersion.
- **UX:** `/purchase-orders/:id`: commercial line table and metadata, compact action rail, history section. Received/remaining quantities matter once receiving starts. Show “You”/ID for creator, not inferred name.
- **States/check:** decide actions depend on status, role and non-self condition. Warehouse users navigate to operational detail instead of fetching this DTO and hiding costs. A failed history request must not remove loaded PO detail.

### E38 — PUT /api/purchase-orders/{id}

- **Contract/access:** A/B owner; Draft only. Editable create fields plus `{rowVersion}`; complete line replacement, same bounds.
- **UX:** `/:id/edit` reuse draft form; PO number read-only, preserve existing line/source data. “Save changes” followed by optional separate submit.
- **States/check:** stale version preserves local changes and requires review; if now submitted, display updated state and prevent edit. No editing submitted/approved/receiving lines.

### E39 — GET /api/purchase-orders/{id}/history

- **Contract/access:** A/B/P, B own only; actor, action, previous/next state, comments, revision and created time.
- **UX:** timeline within commercial PO detail: transition labels, timestamp, actor/“You” and reason/comments. Revision is secondary audit context, not main visual hierarchy.
- **States/check:** separate empty/error history state and retry. W never calls this endpoint; operational PO has no commercial decision-history panel.

### E40 — POST /api/purchase-orders/{id}/submit

- **Contract/access:** A/B owner; `{rowVersion}`, Draft -> PendingApproval; active supplier/warehouse and valid lines.
- **UX:** draft summary “Submit purchase order” with totals and destination visible. Explain that commercial draft editing ends on submit; do not claim PO submission reserves sales stock.
- **States/check:** 409 stale/inactive source retains document and points to reload or Draft edit where still allowed. Success updates status/history/list. No resubmit after Rejected.

### E41 — POST /api/purchase-orders/{id}/approve

- **Contract/access:** A/P; `{rowVersion,comments?}`, pending only; actor cannot be PO creator, including A. Comments max 1000. No sales-specific 10,000 threshold is defined here.
- **UX:** PO detail review mode: supplier/destination, costs and quantities first, optional comments then primary “Approve purchase order”. Own PO shows “Another authorized user must decide this purchase order.”
- **States/check:** server state/version/role conflicts stay inline. Success makes PO eligible for receiving but does not add stock. Refresh history and pending-filter list; never imply approval itself received goods.

### E42 — POST /api/purchase-orders/{id}/reject

- **Contract/access:** A/P non-creator; `{rowVersion,reason}` required/nonblank/max 1000, pending only -> terminal Rejected.
- **UX:** inline reason, destructive “Reject purchase order”; explain a corrected purchase needs a new PO because there is no return/resubmit route.
- **States/check:** same self-decision check as approve; retain reason on 400/409. Success replaces decision controls with status/history; no misleading “Edit rejected PO”.

### E43 — POST /api/purchase-orders/{id}/cancel

- **Contract/access:** A/B owner for Draft/Pending; Approved only A and before receiving starts. `{rowVersion,reason}` nonblank/max 1000. Receiving-state/terminal orders cannot cancel.
- **UX:** contextual “Cancel purchase order”, required reason and explicit terminal outcome confirmation. Do not show to P or offer B cancellation of Approved.
- **States/check:** a receipt draft can establish receiving admission even if later cancelled, so quantity remaining alone cannot prove cancel eligibility. Backend conflict explains unavailable cancellation; no stock-reversal implication.

### E44 — GET /api/purchase-orders/operational

- **Contract/access:** A/B/P/W; B own only. `page,pageSize,status?`. W sees only approved/receiving-state POs; results omit costs/totals/notes/approval comments/contact information.
- **UX:** proposed `/receiving`: PO number, warehouse, supplier ID, status and quantity context; eligible-status filter and pagination. A/W “Receive goods” opens selected PO; B/P can inspect operational quantities if linked from their permitted screens but cannot receive.
- **States/check:** W has no supplier-name API, so use ID fallback. No hidden commercial fetch. “No receivable purchase orders” differs from inactive inventory authority error encountered by later receiving mutations.

### E45 — GET /api/purchase-orders/operational/{id}

- **Contract/access:** A/B/P/W with ownership/status restrictions above; number, supplier/warehouse, status/date, rowVersion and original PO line IDs with ordered/received/remaining quantities.
- **UX:** receiving PO detail, possibly `/receiving/:id`: quantity-only table and A/W “Create goods receipt”. Receipt form derives line selection from original `purchaseOrderItemId`, never product ID alone.
- **States/check:** actual receipt admission is Approved/PartiallyReceived; Received has no positive remaining quantities. Do not expose a live receive action for exhausted/invalid status even if operational record is readable. Reload version before starting receipt.

## 6. Goods-receipt operations

Application currently rejects receipt create/post without completed warehouse authority and the allowed write gate. Do not label that conflict “No receipts” or expose a frontend “Enable inventory” switch. Read-only and cancellation behavior remain determined by the actual endpoint response.

### E46 — GET /api/goods-receipts

- **Contract/access:** A/W; `page=1,pageSize=20,purchaseOrderId?,status?`; paged receipts with number/PO/supplier/warehouse/status, actors/times, notes, versions and original-line references.
- **UX:** `/goods-receipts`, number, PO, warehouse, status, created/posted time; server PO/status filters. PO context can preset its ID. Creation entry says “Choose purchase order” and leads through receiving queue.
- **States/check:** no invented text/date/supplier filters. An empty readable list does not establish posting readiness. No cost columns or supplier commercial contact lookup for W.

### E47 — POST /api/goods-receipts

- **Contract/access:** A/W; `{idempotencyKey,purchaseOrderId,purchaseOrderRowVersion,items:[{purchaseOrderItemId,quantity}],supplierDeliveryReference?,notes?}`. 1–100 distinct original lines, positive quantities, no over-receipt; Approved/PartiallyReceived PO. Body key max 128, no whitespace/control characters.
- **UX:** `/goods-receipts/new` from operational PO: locked PO/destination, per-line ordered/received/remaining and “Receive now” quantities; permit selected partial lines. Optional supplier delivery reference/notes. “Save receipt draft” explicitly does not post stock.
- **States/check:** refresh PO when remaining/version conflict occurs, preserve proposed quantities for review. Store logical create key and exact payload through uncertain retry; do not regenerate key per click. Show readiness conflict honestly; never claim default host can receive successfully.

### E48 — GET /api/goods-receipts/{id}

- **Contract/access:** A/W; receipt DTO includes current PO status/version, receipt version, lines with posting/movement IDs and post/cancel metadata.
- **UX:** `/goods-receipts/:id`: receipt/PO identity, destination, quantity table, reference/notes, status and explicit post/cancel panel for Draft. Posted detail links to permitted filtered movement view; immutable IDs can be secondary audit metadata.
- **States/check:** no receipt edit, reversal or PDF/print/export action invented. Cancellation reason and posting actor/time remain visible; versions held internally for actions.

### E49 — POST /api/goods-receipts/{id}/post

- **Contract/access:** A/W; `{idempotencyKey,rowVersion}`, Draft only. Posts stock once and updates PO received/remaining quantities/status atomically. Requires enabled warehouse authority/write permission.
- **UX:** receipt detail “Post receipt” final confirmation showing destination and quantities, text “Adds received quantities to warehouse stock.” Separate from saving draft.
- **States/check:** retain original key/payload for uncertain result replay. On confirmed success refresh receipt, operational/commercial PO where permitted, stock and movements. Concurrent quantity/version conflict needs review, not silent quantity reduction or fresh-key replay.

### E50 — POST /api/goods-receipts/{id}/cancel

- **Contract/access:** A/W; `{rowVersion,reason}` nonblank; Draft only. No posted-stock reversal.
- **UX:** “Cancel draft receipt”, required reason and clear “No stock was posted by this draft” context. Keep distinct from PO cancellation.
- **States/check:** stale/posted conflict refreshes detail; never offer cancellation on Posted as a reversal. Success shows cancelled metadata and clears draft actions; does not imply PO cancellation becomes available again.

## 7. Stock-transfer operations

Create/post use the **Idempotency-Key header**, unlike receipt body keys. Current dev CORS omits that header, so cross-origin create/post must wait for a separately scoped backend CORS fix. Draft creation/cancellation does not move or reserve stock; posting remains blocked by default production write gate.

### E51 — GET /api/stock-transfers

- **Contract/access:** A/W; `page=1,pageSize=20,warehouseId?,productId?,status?`; warehouse filter matches source or destination.
- **UX:** `/stock-transfers`: number, source -> destination, status, created/posted time; three supported filters and pagination. “New transfer” A/W. Label warehouse filter “Either warehouse” to avoid source-only misunderstanding.
- **States/check:** no unauthorized B/P transfer links even though they can read inventory. Empty state can invite draft creation once CORS integration is ready; no invented edit action.

### E52 — POST /api/stock-transfers

- **Contract/access:** A/W; header `Idempotency-Key`, body `{sourceWarehouseId,destinationWarehouseId,items:[{productId,quantity}]}` -> 201 Draft. Distinct nonempty warehouses, 1–100 unique products, positive whole quantities. Draft lines immutable.
- **UX:** `/stock-transfers/new`: source and destination first, then products/quantities; source stock preview when authorized inventory API is available. Explain stock is checked/moved on post, not reserved by draft. “Create draft transfer”.
- **States/check:** inability to read inventory before cutover must not be misreported as zero stock or block paperwork solely on a fabricated zero. Stable key/exact payload on uncertain create replay. Corrections after save use cancel and new transfer, not a nonexistent PUT.

### E53 — GET /api/stock-transfers/{id}

- **Contract/access:** A/W; number, both warehouses, immutable lines, status, version, creator/post/cancel metadata and paired movement/posting IDs.
- **UX:** `/stock-transfers/:id`: source/destination visual pair, quantity table, Draft post/cancel panel; Posted shows paired source/destination movement context. No financial cost/valuation panel.
- **States/check:** read-only Posted/Cancelled states, inline 403/404/retry. Never infer posting success solely from unchanged total stock across warehouses; use transfer status and actual server response.

### E54 — POST /api/stock-transfers/{id}/post

- **Contract/access:** A/W; header `Idempotency-Key`, body `{rowVersion}`, Draft only. Atomic physical stock debit/credit; cannot consume reserved stock; requires warehouse authority/write permission.
- **UX:** “Post transfer” final confirmation with product quantities and “From”/“To” warehouses; explain available stock check and terminal posted result. Pending state blocks competing cancel.
- **States/check:** same logical key/payload for lost-response replay. On success refresh transfer, both warehouse stock views and movements. Insufficient source stock, gate and version conflicts remain distinct; never post optimistically or send a new key automatically.

### E55 — POST /api/stock-transfers/{id}/cancel

- **Contract/access:** A/W; `{rowVersion,reason}` nonblank, Draft only; no idempotency header contract for cancellation. No stock movement/reversal.
- **UX:** “Cancel draft transfer” with reason and terminal outcome confirmation; explain corrected lines require a new transfer. Posted transfer has no cancel/reverse CTA.
- **States/check:** retain reason on failure, refresh state on conflict, show returned cancellation metadata on success. Do not add an edit-draft shortcut that sends unsupported PUT.

## 8. Inventory read operations

Both require A/B/P/W and **active warehouse authority**. Default Product-authority host returns a conflict: show “Warehouse inventory is not available in this environment” with safe backend explanation/retry, not empty/zero balances. No client activation/configuration switch exists.

### E56 — GET /api/inventory/stocks

- **Contract/access:** A/B/P/W; `page=1,pageSize=20,warehouseId?,productId?`; rows include warehouse code, product SKU/name, on-hand, reserved, available, sequence and version. Page size 1–100.
- **UX:** `/inventory` Stock tab: warehouse, product/SKU, on-hand, reserved, available in right-aligned tabular numbers. Filters use real warehouse/product IDs. Row “View movements” carries those two supported filters into Movement tab.
- **States/check:** distinguish unavailable authority, fetch failure, filtered empty and true no stock rows. Display returned available value rather than recomputing authoritative balances. No adjustment, stock-taking, reorder, cost, transfer CTA for unauthorized B/P or whole-warehouse total inferred from page.

### E57 — GET /api/inventory/movements

- **Contract/access:** A/B/P/W; `page,pageSize,warehouseId?,productId?,from?,to?`; `from` must be earlier than `to`. Date filters are DateTimeOffset. Rows include kind, physical/reserved deltas, before/after balances, sequence, actor/time and source IDs/number.
- **UX:** `/inventory` Movements tab: timestamp, warehouse/product, kind, signed physical/reserved delta and source reference. Expand row for before/after/sequence/actor details to keep main table usable. Labeled local date/time range converts to offset-bearing request values; document end-time semantics from implementation before claiming inclusive dates.
- **States/check:** reject invalid range inline and preserve filters. No unsupported source-document server filter: a receipt/transfer link can filter by warehouse/product and highlight matching movement IDs on the loaded page, but cannot promise all document movements on one page. Link source document only when destination route exists and role permits it. No ledger edits/deletes.

## 9. Screen composition and priority

### Information architecture

| Area | Pages / shared panels | Main job |
| --- | --- | --- |
| Sales | Existing orders list, new/edit, detail, approvals list/review | Draft -> submit/reserve -> decision -> dispatch |
| Equipment | Existing assets list/detail and delivery-note tab | Read dispatched equipment and delivery metadata |
| Service | Existing jobcard list/new/detail | Record fault -> reserve parts -> complete/consume |
| Masters | New warehouse/supplier list plus inline form/detail | Maintain valid sources for business documents |
| Purchasing | New PO list, new/edit, detail/review/history | Draft purchasing terms -> authorized decision |
| Receiving | New operational PO queue/detail, receipt list/new/detail | Receive remaining quantities without exposing costs |
| Inventory | New stock/movement tabs | Inspect source-specific balances and provenance |
| Transfers | New list/new/detail | Draft -> atomic inter-warehouse post |

Do not create one screen per endpoint. Read, history and transition operations compose a single task-focused detail surface. Existing landing has no endpoint and remains outside this 57-operation inventory.

### Delivery order

1. **Contract/readiness:** resolve stale guidance; verify merged schema/host; add six-role routing/navigation; separately authorize/fix CORS header. Preserve default inventory gate and obtain an explicitly prepared isolated host for positive posting verification.
2. **Existing-flow correctness:** service access/nav, high-value sales approval explanation, warehouse field preservation, resource/session reliability, status-dependent actions. This protects implemented flows before adding new ones.
3. **Master data and purchasing:** E23–E43. Master selectors precede PO form/review; ownership/self-decision/version behavior gets focused tests.
4. **Operational reads and receiving:** E44–E50 plus E56–E57. Build honest unavailable states before positive-posting integration. Verify quantity updates and replay in prepared environment.
5. **Transfers:** E51–E55 after CORS; verify paired stock movements and uncertain-response key retention.
6. **Polish and regression:** role-by-role browser checks, keyboard/focus, mobile records, reduced motion, field bounds, inactive historical lookups and safe errors. No frontend implementation is included in this document.

## 10. Verification matrix for implementation

| Scenario | Observable acceptance criterion |
| --- | --- |
| Six-role login/navigation | Each role lands on a route it can use; hidden nav and direct-link server errors agree. |
| Sales higher-total approval | P sees Administrator-required message above 10,000; A can decide; ordinary P decisions remain available within policy. |
| Warehouse source preservation | Editing a sourced sales line never drops warehouseId; new service part request includes chosen nullable source. |
| Sales save then submit conflict | Exactly one Draft remains; UI identifies saved Draft and failed submission. |
| Procurement ownership/self-decision | B cannot inspect another creator's PO; A/P cannot decide their own PO; failure preserves review context. |
| Supplier/warehouse inactive transition | Version/status updates follow server response, historical documents keep identification, rejected deactivation stays active. |
| Rowversion race | Losing edit/decision shows conflict, retains useful local input and does not overwrite or blindly repeat. |
| Operational confidentiality | W network requests never fetch commercial PO detail/history or suppliers; no unit cost/totals/comments leak through view models. |
| Product-authority environment | Inventory read and GRN/transfer-post gate conflicts appear as unavailable operations, not fake zero balances or successful posting. |
| Partial receiving | Original PO-line IDs used; remaining quantity/version refresh correctly; no over-receipt and no double posting. |
| Transfer replay | Same logical create/post retries use original header key and exact payload; one terminal posting and paired movements. |
| Uncertain non-idempotent result | Client reconciles observable state and avoids automatic duplicate create/dispatch/transition. |
| Accessibility/responsive | 375px and desktop layouts work; labels/keyboard/focus/live feedback remain usable; reduced motion removes unnecessary movement. |
| Build gates | Angular typecheck/build, lint and meaningful workflow/permission/idempotency tests pass; changed flows verified in browser against appropriate host. |

Endpoint coverage is exact by method/path, not merely by page count. See E01–E57 above. No register/profile/admin-user CRUD, aggregate approval feed, inventory adjustment/reversal, stock-taking, supplier return, short-close or readiness endpoint is invented.
