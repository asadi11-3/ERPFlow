# Frontend plan after inventory/procurement merge

Baseline: 2026-10-27, merge `d8bdd39` on local `master`. No push performed.
Scope: plan the client against merged backend; preserve current visual design.

## 1. Findings and source of truth

- Existing landing, login, sales order list/create/detail/edit, approval queue/review, reference data, assets, delivery notes and service/jobcard screens already exist. Finish their gaps rather than rebuild them.
- Merged API exposes **57 operations**: 22 existing plus 35 warehouse, supplier, purchasing, receiving, transfer and inventory operations. Verify actual handlers in `ERPFlow.Api/*Endpoints.cs`; use `docs/openapi/erpflow-v1.json` for wire shapes.
- `AGENTS.md` is stale: 11 operations, four roles and GET/POST-only CORS no longer describe backend. Actual roles also include Purchasing and WarehouseOperator; PUT is allowed. Update repository guidance separately before implementation.
- Frontend role union recognizes new roles, but their home currently falls through to `/orders`, whose guard rejects them. New navigation and home destinations are required.
- New contracts use base64 JSON rowversions. Preserve returned strings exactly; never synthesize version tokens.
- New document numbers are server-generated. Display `poNumber`/`number` for purchasing, receipts and transfers; retain short GUID presentation for sales orders.

## 2. Deployment dependencies

1. **Transfer browser blocker:** create/post require `Idempotency-Key` header. Development CORS currently allows only Authorization and Content-Type. A separately authorized backend change must allow that header and verify its preflight. Do not work around this by changing transfer request bodies.
2. **Warehouse posting is intentionally closed in application DI.** `ProductAuthorityWriteGate` and `DisabledInventoryCutoverPermission` remain registered. Receipt creation/posting and transfer posting cannot be promised as working against the default host. Transfer draft creation/cancellation is supported before cutover.
3. Deployment/migrations, inventory authority cutover and gate activation are separate work. Do not add a frontend activation control or infer readiness from empty inventory results. There is no readiness endpoint.
4. Browser verification needs a host running the merged binary/schema and accounts for all six roles. The API process running during merge verification may still be the older binary.

References: `docs/procurement-phase3.md`, `docs/goods-receipts-phase4.md`, `docs/stock-transfers-phase5.md`, `docs/inventory-authority-cutover-phase2c.md`.

## 3. Required new surfaces

Routes below are proposals; APIs listed are existing contracts. Reuse create/edit components where practical. Master-data forms can be inline rather than separate routes.

| Surface / proposed route | Existing API | Access / behavior |
| --- | --- | --- |
| Warehouses `/warehouses` | GET/POST `/api/warehouses`; GET/PUT `/{id}`; POST `/{id}/activate`, `/deactivate` | All authenticated users can read. Administrator alone manages. Page/active filters; code immutable after creation. |
| Suppliers `/suppliers` | GET/POST `/api/suppliers`; GET/PUT `/{id}`; POST activation/deactivation | Administrator/Purchasing manage; Approver reads. Server paging, search and active filter. Contact fields are supported here. |
| Purchase orders `/purchase-orders` | GET `/api/purchase-orders?page&pageSize&status` | Administrator/Purchasing/Approver. Purchasing receives own records only. |
| PO create/edit `/purchase-orders/new`, `/:id/edit` | POST `/api/purchase-orders`; PUT `/{id}` | Administrator/Purchasing; edit Draft only. Supplier, warehouse, products, quantities, unit costs and optional notes. Costs are entered for purchasing, unlike read-only sales prices. |
| PO detail/review `/purchase-orders/:id` | GET `/{id}`, `/{id}/history`; POST submit/approve/reject/cancel | One detail page with allowed-action panel. Purchasing ownership and non-self approval enforced by server. No separate approval endpoint/feed is invented. |
| Warehouse receiving queue `/receiving` | GET `/api/purchase-orders/operational?page&pageSize&status`, GET `/{id}` | Administrator/WarehouseOperator operational view, with PO number and remaining quantities. Cost-free DTOs only for warehouse users. |
| Goods receipts `/goods-receipts`, `/:id`, `/new` | GET/POST `/api/goods-receipts`; GET `/{id}`; POST `/{id}/post`, `/cancel` | Administrator/WarehouseOperator. Start creation from operational PO detail; support partial quantities, delivery reference and notes. Gate dependency applies. |
| Inventory `/inventory` | GET `/api/inventory/stocks`, `/movements` | Administrator/Purchasing/Approver/WarehouseOperator. Stock and movement tabs; paged warehouse/product filters, movement date range. |
| Stock transfers `/stock-transfers`, `/new`, `/:id` | GET/POST `/api/stock-transfers`; GET `/{id}`; POST post/cancel | Administrator/WarehouseOperator only. Distinct warehouses, positive quantities and unique products. Draft lines immutable; corrections mean cancel and recreate. |

No dedicated supplier/warehouse detail page is necessary initially: list plus editable detail panel covers supported operations. A PO approvals tab can filter the existing PO list by PendingApproval; there is no new cross-domain approval feed.

## 4. Workflow rules to carry into UI

- PO: Draft -> PendingApproval -> Approved or Rejected. Draft/Pending can be cancelled by authorized creator or Administrator; only Administrator can cancel Approved before receiving has started. Do not offer PO resubmit, return, short-close or delete: no routes support those actions.
- PO status values: Draft 0, PendingApproval 1, Approved 2, Rejected 3, Cancelled 4, PartiallyReceived 5, Received 6, ShortClosed 7. Display all; only expose transitions actually supported by handlers.
- Purchasing ownership applies to PO list, detail, history, operational reads and mutations. Approver/Administrator cannot decide their own PO. Rejection/cancellation require reason.
- Warehouse operators must use operational PO DTOs. Do not fetch commercial records then hide their cost fields. Supplier endpoint is not available to WarehouseOperator; show supplier ID when no permitted name source exists.
- Receipts: Draft 0, Posted 1, Cancelled 2. Carry PO rowversion on create, receipt rowversion on post/cancel, and refresh receipt plus PO after posting. No receipt edit/reversal/delete route exists.
- Transfers: Draft 0, Posted 1, Cancelled 2. Posting moves physical available stock atomically and leaves reserved quantities intact. No reservation on draft creation; no edit/reversal route.
- Idempotency: generate one key per logical create/post operation and retain the same key plus unchanged payload when retrying an uncertain result. Receipt key is in the JSON body; transfer key is in the header. Different operations need distinct keys. Do not blindly retry mutations with new keys.
- All 409s retain useful input, explain conflict and refetch affected records. Do not automatically reapply stale edits, post again with a fresh token, or claim that reload proves an uncertain command failed.
- Inventory numbers come from inventory APIs. Reference product stock is not a warehouse-by-warehouse balance. Movement data is cost-free and includes source document references; link only to routes the role can access.
- All paged lists use supported server filters only. Do not implement misleading local search over a single loaded page.

## 5. Existing-flow adjustments

1. Add idle session expiry handling and guard resource reads during loading/error states. Preserve central 401/403/Problem Details behavior.
2. Add explicit Purchasing/WarehouseOperator home destinations and role-aware shell navigation; suggested homes `/purchase-orders` and `/receiving`. Keep existing four-role destinations.
3. Add nullable `warehouseId` to sales order line and service parts wire types. Preserve it when editing existing records; expose source selection in the existing line UI using active warehouse choices. Respect legacy default/source rules rather than inventing warehouse availability for Sales/Technician, which lack inventory-query access.
4. Display source warehouse on order items/reservations and jobcard parts, with ID fallback. Avoid filtering inactive historical sources out of read-only display.
5. Optional later polish: asset selector on general jobcard creation; supplied serial capture on dispatch. Empty dispatch serial array is already valid because backend generates serials.

## 6. Current-style implementation constraints

- Keep warm-paper OKLCH neutrals, teal accent, Geist Variable and Geist Mono numeric text from `src/styles.scss`.
- Reuse existing shell, page headings, flat bordered cards, toolbars, tables/stacked mobile records, tabs, timeline, alerts and empty/skeleton states.
- Reuse `ui/select`, pagination, pill, icon, row-menu and confirmation controls. Use existing split detail/action layout for purchasing, receiving and transfers.
- Keep 44px controls, visible keyboard focus, labels and inline errors; preserve reduced-motion behavior and brief existing CSS transitions. No new UI library or visual redesign.
- Continue lazy standalone Angular features, signal state/forms and existing layer boundaries. Read relevant architecture references before choosing new structural extractions. Add ESLint boundary declarations only if introducing a new layer, not for ordinary feature folders.
- Keep domain-specific HTTP/contracts with their corresponding existing core domain conventions; extract shared code only after concrete reuse appears. Do not expand the sales order view pattern into a universal document framework.

## 7. Delivery sequence and acceptance

### Phase A: readiness and existing-flow integration

Confirm merged schema/host; obtain backend CORS change approval; reconcile stale guidance. Add role homes/nav, warehouse-aware existing contracts and session/resource reliability fixes.

Acceptance: all roles land on accessible routes; sales edits retain warehouse sources; existing order/approval/service flows still work. New header preflight passes before transfer mutation integration.

### Phase B: masters and purchasing

Build warehouses and suppliers first, then PO list/form/detail/history/actions. Reuse PO detail for Approver review. Implement complete paginated lookup loading where needed rather than assuming first page contains all choices.

Acceptance: Purchasing creates/edits/submits own PO; another authorized actor decides it; ownership, self-decision, inactive source and stale rowversion errors are handled. Administrator master activation/deactivation conflicts display accurately.

### Phase C: inventory and receiving

Build stock/movement tabs and operational receiving queue, then receipt draft/detail/post/cancel. Validate successful warehouse writes only against an explicitly prepared isolated warehouse-authority environment; default gate rejection remains a real error state.

Acceptance: partial receipt updates remaining PO quantities and stock once; replay does not double-post; stale versions and over-receipt errors preserve context. Warehouse users never receive commercial costs through client requests.

### Phase D: transfers

Implement list/create/detail/post/cancel after header/CORS integration. Validate source/destination, quantities and immutable draft behavior.

Acceptance: create/post uncertain-response retries reuse their original key/payload; source/destination balances and movement references update exactly once; insufficient available stock and stale versions display cleanly.

### Phase E: regression and responsive verification

Run frontend typecheck/build, lint and targeted tests for permissions, versions/idempotency and changed workflows. Browser-check all six roles against merged API, keyboard access, narrow mobile records, reduced motion and loading/empty/403/404/409 states. Avoid snapshot-only tests mirroring markup.

## 8. Merge verification record

- All fetched remote branches are ancestors of local HEAD; working tree was clean after merge. Local master is 10 commits ahead of origin/master.
- `dotnet build ERPFlow.slnx -c Release`: passed, zero warnings/errors. Debug build was blocked by DLLs locked by an already-running API process.
- `dotnet test ERPFlow.slnx -c Release --no-build --no-restore`: timed out after 120 seconds; no test result established. Backend SQL suite remains unverified in this session.
- `npm.cmd run lint`, `npm.cmd run build`: passed.
- `npm.cmd test -- --watch=false`: passed, 4 files / 27 tests.
- No database migration, cutover or browser workflow verification performed during this merge. No frontend implementation beyond this plan.
