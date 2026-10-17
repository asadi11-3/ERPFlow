# ERPFlow Implementation Plan

## Objective

Deliver a compact, production-minded Sales Order and Approval MVP that demonstrates Clean Architecture, domain-driven workflow rules, transactional SQL Server persistence, secure API access, and meaningful automated tests.

## Completed Phases

| Phase | Outcome | Status |
| --- | --- | --- |
| 1. Domain foundation | Solution boundaries, entities, order states, item management, stock reservation/release, and domain tests | Complete |
| 2. SQL Server persistence | EF Core context, entity configurations, constraints, indexes, row-version concurrency, and initial migration | Complete |
| 3. Application workflow | Ports, transactional create/submit/approve/reject use cases, audit persistence, ownership checks, reference queries, and SQL integration tests | Complete |
| 4. Authenticated HTTP API | Minimal API endpoints, password hashing, JWT issuance/validation, role policies, Problem Details, OpenAPI/Swagger, development seeding, migration upgrade, and HTTP integration tests | Complete |

## MVP Completion Criteria

- A Sales user can create a draft for an active customer using persisted products and server-side prices.
- A Sales user can submit only an owned draft with sufficient stock.
- Submission atomically reserves stock, transitions the order, and records audit history.
- An Approver can list pending orders and approve or reject one.
- Approval retains stock reservations; rejection releases them exactly once and records its reason.
- An Administrator can perform Sales and Approver operations.
- Authentication, endpoint roles, application roles, and Sales ownership are enforced.
- Invalid input, missing resources, forbidden access, and conflicts have distinct HTTP outcomes.
- Schema creation and upgrade are represented by committed EF Core migrations.
- Domain behavior and real persistence/API paths are covered by automated tests.

All criteria are implemented.

## Verification Baseline

The completed MVP was verified with:

```powershell
dotnet build ERPFlow.slnx
dotnet test ERPFlow.slnx --no-restore
dotnet ef migrations has-pending-model-changes --project ERPFlow.Infrastructure --startup-project ERPFlow.Api
dotnet list ERPFlow.slnx package --vulnerable --include-transitive
```

Recorded result:

- Build: 0 warnings, 0 errors.
- Tests: 21 passed.
- EF Core: no pending model changes.
- Packages: no known vulnerable direct or transitive packages.
- Manual API checks: Swagger JWT attachment, role-based `403` responses, and the end-to-end approval flow behaved as designed.

## Design Decisions

- Business transitions remain in Domain entities rather than API handlers.
- Application services own orchestration, transactions, trusted actor lookup, and use-case authorization.
- Infrastructure implements explicit persistence ports rather than exposing EF Core throughout the solution.
- Product prices and actor IDs are server-controlled.
- Optimistic concurrency and relational constraints complement in-memory invariants.
- Problem Details is the common HTTP error format.
- Development startup migration and optional seeding optimize local evaluation without enabling production seeding.

## Deferred Work

The following items are intentionally outside this MVP rather than partially implemented:

- Refresh-token rotation, revocation, and account lifecycle management.
- Fine-grained permission-based authorization or SQL row-level security.
- Frontend and mobile clients.
- Pagination, filtering, sorting, and broader order-query use cases.
- Production observability, rate limiting, health checks, and audit export.
- CI/CD, containers, cloud infrastructure, and managed-secret integration.
- Purchasing, receiving, invoicing, accounting, and other ERP bounded contexts.

## Suggested Next Phases

| Phase | Candidate scope |
| --- | --- |
| 5 | Permission-based authorization and user/role administration |
| 6 | Refresh tokens, revocation, security events, and rate limiting |
| 7 | Query pagination/filtering plus operational health and telemetry |
| 8 | Containerized local environment and CI/CD verification |
| 9 | Frontend workflow and additional ERP bounded contexts |

Future phases should preserve the existing dependency direction and add tests at the lowest layer that can prove each requirement.
