# Development warehouse inventory

The API enables warehouse posting only in the `Development` environment and only after the existing atomic cutover has completed. Production registrations remain disabled.

## Activate an existing development database

Stop the development API and other database writers, then run from the repository root:

```powershell
dotnet run --project ERPFlow.Api --launch-profile https -- --activate-warehouse-inventory
```

This uses the configured database, applies pending migrations, seeds legacy demo data if needed, prepares an import as a persisted Administrator, imports stock and reservations into the legacy default warehouse, reconciles them, and exits. A repeated activation reconciles the completed warehouse state without importing openings again. Inconsistent legacy reservations fail preflight rather than being silently repaired.

For disposable development data only, reset and initialize instead:

```powershell
dotnet run --project ERPFlow.Api --launch-profile https -- --activate-warehouse-inventory --reset-development-database
```

**The reset command deletes the entire configured database.** Both switches require the Development environment, and reset additionally requires the activation switch.

Start the API normally afterward:

```powershell
dotnet run --project ERPFlow.Api --launch-profile https
```

Startup skips the legacy seeder once warehouse authority is active. New stock must arrive through goods receipts; transfers move existing available stock between warehouses. Development CORS allows the `Idempotency-Key` header used by transfer creation/posting. No HTTP activation endpoint is exposed.

Development activation is not a production deployment mechanism. The production cutover prerequisites in `inventory-authority-cutover-phase2c.md` still apply.
