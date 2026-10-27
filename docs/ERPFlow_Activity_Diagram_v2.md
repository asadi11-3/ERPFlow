# ERPFlow — implemented business activity diagram

Reviewed: 2026-10-24. Scope: current backend implementation, including sales, equipment delivery, and service operations. Three linked activity views keep decisions readable. These describe executable API workflows, not a proposed future process.

## 1. Sales order and approval

```mermaid
flowchart TD
    Start((Start)) --> Login[User: sign in with email and password]
    Login --> Credentials{Credentials valid?}
    Credentials -->|No| LoginError[Return authentication error]
    LoginError --> Login
    Credentials -->|Yes| Session[System: issue JWT with user role]
    Session --> Role{Role / intended activity?}
    Role -->|Sales or Administrator| Compose[Select customer, products, and quantities]
    Role -->|Approver or Administrator: review existing order| Queue
    Role -->|Technician or Administrator: service| ServiceLink([Continue in service activity])
    Compose --> Valid{Active customer, existing products,
positive quantities, no duplicate products?}
    Valid -->|No| Correct[Return validation / lookup / conflict error]
    Correct --> Compose
    Valid -->|Yes| Draft[System: save Draft
Snapshot server prices; calculate total
No stock reservation]
    Draft --> Submit{Sales owner or Administrator
chooses to submit?}
    Submit -->|Later| Saved([Draft remains saved])
    Submit -->|Yes| Stock{Draft with items and
enough available stock for every item?}
    Stock -->|No| Conflict[Return conflict; keep Draft
No partial stock reservation]
    Conflict --> Saved
    Stock -->|Yes| Reserve[System: reserve all item quantities
Set PendingApproval
Record Submitted history]
    Reserve --> Queue[Approver / Administrator:
open pending queue and review order + history]
    Queue --> Decision{Review decision?}
    Decision -->|Return for correction| ReturnReason{Nonblank return reason?}
    ReturnReason -->|No| Queue
    ReturnReason -->|Yes| Returned[System: release reservations
Set Draft; clear decision timestamp
Record Returned history]
    Returned --> Submit
    Decision -->|Reject| RejectReason{Nonblank rejection reason?}
    RejectReason -->|No| Queue
    RejectReason -->|Yes| Rejected[System: release reservations
Set Rejected; record decision + history]
    Rejected --> RejectedEnd((End))
    Decision -->|Approve| Limit{Total greater than $10,000?}
    Limit -->|No| Approved
    Limit -->|Yes| Admin{Reviewer is Administrator?}
    Admin -->|No| Escalate[Approval denied: Administrator required
Order stays PendingApproval]
    Escalate --> Queue
    Admin -->|Yes| Approved[System: set Approved
Record decision + history
Keep stock reserved; comments optional]
    Approved --> EquipmentLink([Continue in dispatch activity])
```

**Guards:** Submit requires Draft; approve, reject, and return require PendingApproval. Sales can access only their own orders. Administrator can create/submit and review orders, including their own; no separation-of-duties rule exists. The $10,000 threshold applies to approval, not rejection or return. Exactly $10,000 may be approved by an Approver.

**Returned orders:** The API returns them to Draft and allows resubmission, but exposes no item/customer edit endpoint. The loop above therefore means resubmission of the saved draft, not an implemented correction editor.

## 2. Dispatch and machine registration

```mermaid
flowchart TD
    Start((Start)) --> Dispatch[Any authenticated user:
request dispatch of an existing sales order
Carrier / tracking and serial list are optional]
    Dispatch --> Exists{Order exists?}
    Exists -->|No| Missing[Return not found]
    Missing --> Fail((End))
    Exists -->|Yes| Status{Order is Approved?}
    Status -->|No| Conflict[Return conflict; no dispatch saved]
    Conflict --> Fail
    Status -->|Yes| Delivery[System: create delivery note
Link customer and sales order
Record dispatch timestamp]
    Delivery --> Serials{Serial numbers supplied?}
    Serials -->|Yes| Supplied[Register one machine asset
per supplied serial entry]
    Serials -->|No| Generated[Generate serial and tag numbers
Register one asset per ordered unit]
    Supplied --> Asset[System: link assets to customer,
product, and sales order
Set Active; installation time = now
Warranty expiry = now + one year]
    Generated --> Asset
    Asset --> Commit[Commit delivery note and assets together
Sales order stays Approved
Stock quantities / reservations unchanged]
    Commit --> End((End))
    Commit -. Optional later service request .-> Service([Service activity: asset link optional])
```

**Implementation boundary:** Dispatch is authenticated-only, with no Sales/Administrator policy or ownership check. It does not mark an order Dispatched, consume stock, confirm delivery, or explicitly prevent repeat dispatch. Supplied serials are not checked against ordered product quantities. Persistence constraints may still reject conflicting identifiers.

## 3. Service jobcard and spare parts

```mermaid
flowchart TD
    Start((Start)) --> Request[Technician / Administrator:
create service request with customer,
reported fault, priority, technician name,
and optional machine asset]
    Request --> Valid{Customer exists, fault nonblank,
and supplied nonempty asset ID exists?}
    Valid -->|No| Fix[Return validation / lookup error]
    Fix --> Request
    Valid -->|Yes| Open[System: generate ticket number
Save jobcard as Open
Blank technician name becomes Unassigned]
    Open --> Parts{Spare parts needed?}
    Parts -->|Yes| Choose[Technician / Administrator:
request products and quantities]
    Choose --> Available{Nonempty request, existing products,
positive quantities, sufficient available stock,
and no duplicate jobcard products?}
    Available -->|No| PartsError[Reject request; roll back transaction
Jobcard and stock remain unchanged]
    PartsError --> Choose
    Available -->|Yes| Reserve[System: increase reserved stock
Add parts at server prices
Open becomes PartsRequested]
    Reserve --> More{More distinct parts needed?}
    More -->|Yes| Choose
    More -->|No| Work
    Parts -->|No| Work[Technician: perform service work
Business activity; no start-work API]
    Work --> Complete[Technician / Administrator:
complete jobcard with optional notes
and optional final meter reading]
    Complete --> Completed[System: set Completed + timestamp
Save notes and final reading
Part reservations remain unchanged]
    Completed --> Meter{Asset linked and
final meter reading supplied?}
    Meter -->|Yes| Update[Update asset meter only when
new reading is at least current reading]
    Meter -->|No| Commit
    Update --> Commit[Commit jobcard and asset changes]
    Commit --> End((End))
```

**Implementation boundary:** Service creation does not require an active customer, an asset, or a prior order/dispatch. Asset/customer ownership matching is not checked. `InProgress` and `Cancelled` exist in the domain enum but have no API transition. Completion has no status guard; repeated completion and requesting parts after completion are not explicitly blocked. Parts requests increment reserved stock, but the returned `StockReservation` object is not added to persistence; completion neither consumes nor releases that stock. Duplicate-part domain failures currently become generic 500 responses rather than mapped workflow conflicts.

## Shared rules and consistency

- `AvailableStock = StockOnHand - ReservedQuantity`. Sales submission reserves; rejection and return release; approval keeps reservations. Dispatch and service completion do not reduce StockOnHand.
- Each mutation shown is saved in a database transaction. Sales order/product row versions protect against concurrent updates; conflicts roll back rather than partially applying stock changes.
- JWT authentication protects all operations except login. Missing/invalid authentication returns 401; policy denial returns 403. Service writes require Technician or Administrator. Reference, equipment, and service reads require authentication.
- Missing resources return 404; mapped validation errors return 400; mapped workflow/concurrency conflicts return 409. Unmapped exceptions return generic 500. Error branches preserve prior persisted state.
- Sales history records Submitted, Approved, Rejected, and Returned. There is no global history-feed endpoint.

## Contract drift found

The current route file and OpenAPI contract contain **21 operations**, not the 11 stated in AGENTS.md / the original frontend handoff. Current code includes Technician, return-for-correction, equipment/delivery, service jobcards, and the Administrator approval threshold. These diagrams follow current routes and implementation.

## Source map

| Behavior | Authoritative source |
| --- | --- |
| Routes and endpoint policies | `ERPFlow.Api/ApiEndpoints.cs`, `ERPFlow.Api/Program.cs` |
| Sales roles, ownership, validation, threshold, transactions | `ERPFlow.Application/SalesOrders/SalesOrderWorkflow.cs` |
| Sales transitions, reservation release, price snapshot | `ERPFlow.Domain/Entities/SalesOrder.cs`, `SalesOrderItem.cs` |
| Available stock and reservation accounting | `ERPFlow.Domain/Entities/Product.cs`, `StockReservation.cs` |
| Dispatch and automatic asset registration | `ERPFlow.Application/Equipment/EquipmentWorkflow.cs`, `ERPFlow.Domain/Entities/MachineAsset.cs` |
| Service request, parts, completion, meter updates | `ERPFlow.Application/Service/ServiceWorkflow.cs`, `ERPFlow.Domain/Entities/Jobcard.cs` |
| Persistence and transactional boundaries | `ERPFlow.Infrastructure/Persistence/EfSalesOrderPersistence.cs`, `EfEquipmentPersistence.cs`, `EfServicePersistence.cs` |
| Error response mapping | `ERPFlow.Api/ApiExceptionHandler.cs` |
| Wire contract | `docs/openapi/erpflow-v1.json` |
