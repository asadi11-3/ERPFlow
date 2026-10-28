using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ERPFlow.Domain;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Procurement;
using ERPFlow.Application.Receiving;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed record ProfessionalDemoSeedOptions(string DatabaseName, string Password, bool DryRun);
public sealed record ProfessionalDemoSeedResult(bool Applied, string Summary);

public sealed partial class ProfessionalDemoSeeder(ERPFlowDbContext context)
{
    public const string ScenarioKey = "jordan-office-technology-2026";
    public const string Version = "6B.2";
    internal Func<string, string, Task>? InterruptAfterAsync { get; init; }

    internal static async Task RunAtAsync(DateTimeOffset utcNow, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (utcNow.Offset != TimeSpan.Zero || utcNow > TimeProvider.System.GetUtcNow().AddMinutes(5))
            throw new InvalidOperationException("Demo business time must be UTC and cannot be future-dated.");
        using var scope = BusinessClock.Use(new FixedTimeProvider(utcNow));
        await action();
    }

    public async Task<ProfessionalDemoSeedResult> SeedAsync(
        ProfessionalDemoSeedOptions options, CancellationToken cancellationToken = default)
    {
        ProfessionalDemoDatabaseGuard.ValidateDatabaseName(options.DatabaseName);
        if (!options.DryRun && string.IsNullOrWhiteSpace(options.Password))
            throw new InvalidOperationException("A demo password is required.");

        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The demo connection string is unavailable.");
        var expected = ProfessionalDemoDatabaseGuard.BuildLocalConnectionString(options.DatabaseName);
        if (!new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
            .ConnectionString.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The resolved database connection does not match the verified local demo target.");

        var exists = await ProfessionalDemoDatabaseGuard.ExistsAsync(connectionString, cancellationToken);
        if (options.DryRun)
            return new ProfessionalDemoSeedResult(false, exists ? "Existing target requires an owned manifest." : "Target is absent and eligible for isolated creation.");

        await using var lockConnection = new Microsoft.Data.SqlClient.SqlConnection(
            new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString);
        await lockConnection.OpenAsync(cancellationToken);
        await using var lockCommand = lockConnection.CreateCommand();
        lockCommand.CommandTimeout = 120;
        lockCommand.CommandText = "DECLARE @result int; EXEC @result=sp_getapplock @Resource=@resource, @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=120000; IF @result<0 THROW 51020, 'Professional demo command lock was not acquired.', 1;";
        lockCommand.Parameters.AddWithValue("@resource", $"ERPFlow.ProfessionalDemoSeed:{options.DatabaseName}");
        await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        try
        {
        exists = await ProfessionalDemoDatabaseGuard.ExistsAsync(connectionString, cancellationToken);
        var applied = !exists;
        if (!exists)
        {
            await context.Database.MigrateAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await context.Database.ExecuteSqlRawAsync("EXEC sp_getapplock @Resource = N'ERPFlow.ProfessionalDemoSeed.v1', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;", cancellationToken);
            await RunAtAsync(Utc(9, 1, 7), () => SeedMasterDataAsync(options.Password, cancellationToken));
            context.ProfessionalDemoSeedManifests.Add(new ProfessionalDemoSeedManifest(
                ScenarioKey, Version, options.DatabaseName, "{}", "{}"));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            var owners = await context.ProfessionalDemoSeedManifests.AsNoTracking()
                .Where(x => x.ScenarioKey == ScenarioKey && x.DatabaseName == options.DatabaseName)
                .OrderByDescending(x => x.CreatedAt).ToArrayAsync(cancellationToken);
            var owner = owners.FirstOrDefault()
                ?? throw new InvalidOperationException("The requested demo database is not owned by the expected professional demo scenario.");
            if (owner.Version is not ("6B.1" or Version))
                throw new InvalidOperationException($"Professional demo seed version '{owner.Version}' cannot advance to {Version}.");
            await context.Database.MigrateAsync(cancellationToken);
            if (owners.All(x => x.Version != Version))
            {
                context.ProfessionalDemoSeedManifests.Add(new ProfessionalDemoSeedManifest(
                    ScenarioKey, Version, options.DatabaseName, "{}", "{}"));
                await context.SaveChangesAsync(cancellationToken);
                applied = true;
            }
        }
        await SeedOperationsAsync(cancellationToken);
        var counts = await ReadCountsAsync(cancellationToken);
        return new ProfessionalDemoSeedResult(applied, JsonSerializer.Serialize(counts));
        }
        finally
        {
            lockCommand.CommandText = "EXEC sp_releaseapplock @Resource=@resource, @LockOwner=N'Session';";
            await lockCommand.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private async Task SeedOperationsAsync(CancellationToken ct)
    {
        var users = await context.AppUsers.AsNoTracking().ToDictionaryAsync(x => x.Email, x => x.Id, ct);
        var suppliers = await context.Suppliers.AsNoTracking().OrderBy(x => x.SupplierCode).Select(x => x.Id).ToArrayAsync(ct);
        var products = await context.Products.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Sku).Select(x => x.Id).ToArrayAsync(ct);
        var customers = await context.Customers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Email).Select(x => x.Id).ToArrayAsync(ct);
        var warehouses = await context.Warehouses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToDictionaryAsync(x => x.Code, x => x.Id, ct);
        var admin = users["admin@jot.example"];
        var buyer = users["purchasing@jot.example"];
        var sales = users["sales@jot.example"];
        var approver = users["approver@jot.example"];
        var warehouseOperator = users["warehouse@jot.example"];
        var technician = users["technician@jot.example"];
        var gate = new DemoWarehouseGate(context);

        await RunCutoverAsync(admin, ct);
        var events = new List<(DateTimeOffset At, string Key, Func<Task> Run)>();
        foreach (var seed in ProfessionalDemoSeedPlan.PurchaseOrders)
            events.Add((seed.CreatedAt, seed.Key, () => RunPurchaseOrderAsync(seed, suppliers[seed.SupplierIndex],
                products[seed.ProductIndex], warehouses[seed.WarehouseCode], buyer, approver, admin, ct)));
        foreach (var seed in ProfessionalDemoSeedPlan.GoodsReceipts)
            events.Add((seed.CreatedAt, seed.Key, () => RunGoodsReceiptAsync(seed, warehouseOperator, gate, ct)));
        foreach (var seed in ProfessionalDemoSeedPlan.Transfers)
            events.Add((seed.CreatedAt, seed.Key, () => RunTransferAsync(seed, products[seed.ProductIndex],
                warehouses[seed.SourceWarehouseCode], warehouses[seed.DestinationWarehouseCode], warehouseOperator, gate, ct)));
        foreach (var seed in ProfessionalDemoSeedPlan.SalesOrders)
        {
            events.Add((seed.CreatedAt, seed.Key, () => RunSalesOrderAsync(seed, customers[seed.CustomerIndex],
                customers[(seed.CustomerIndex + 1) % customers.Length], products[seed.ProductIndex],
                warehouses[seed.WarehouseCode], sales, approver, gate, ct)));
            if (seed.Dispatch)
                events.Add((seed.CreatedAt.AddHours(2), $"dispatch-{seed.Key}",
                    () => RunDispatchAsync(seed, sales, technician, gate, ct)));
        }
        foreach (var seed in ProfessionalDemoSeedPlan.ServiceJobcards)
        {
            events.Add((seed.CreatedAt, seed.Key, async () =>
            {
                MachineAsset? asset = null;
                if (seed.AssetIndex.HasValue)
                {
                    var assets = await context.MachineAssets.AsNoTracking()
                        .Where(x => x.SerialNumber.StartsWith("PD-6B2B-"))
                        .OrderBy(x => x.SerialNumber).ToArrayAsync(ct);
                    asset = assets[seed.AssetIndex.Value];
                }
                await RunServiceJobcardAsync(seed, asset?.CustomerId ?? customers[seed.CustomerIndex],
                    products[seed.ProductIndex], warehouses["AMM-SERVICE"], asset, technician, gate, ct);
            }));
        }
        foreach (var seedEvent in events.OrderBy(x => x.At).ThenBy(x => x.Key, StringComparer.Ordinal))
            await seedEvent.Run();
    }

    private async Task RunCutoverAsync(Guid admin, CancellationToken ct)
    {
        const string key = "cutover";
        var fingerprint = Fingerprint(key, LegacyDefaultWarehouse.Id.ToString());
        var entry = await BeginOperationAsync(key, "InventoryCutover", fingerprint, admin, ct);
        if (entry.CompletedAt is not null) return;
        await RunAtAsync(Utc(9, 1, 8), async () =>
        {
            var actor = new SeedActor(admin);
            if (!await context.InventoryAuthorities.AsNoTracking().AnyAsync(x => x.Mode == InventoryAuthorityMode.Warehouse, ct))
            {
                var preparation = await new EfLegacyInventoryImportPreparation(context, actor).PrepareAsync(Guid.NewGuid(), ct);
                await new EfInventoryCutover(context, actor, new DemoCutoverPermission(context)).RehearseAsync(
                    new(preparation.PreparationId, preparation.SourceFingerprint, LegacyDefaultWarehouse.Id,
                        "Professional demo isolated physical attribution approval", InventoryAuthority.SupportedWriterVersion), ct);
            }
        });
        entry.Complete(LegacyDefaultWarehouse.Id);
        await context.SaveChangesAsync(ct);
    }

    private async Task RunPurchaseOrderAsync(ProfessionalDemoPurchaseOrderSeed seed, Guid supplier, Guid product, Guid warehouse,
        Guid buyer, Guid approver, Guid admin, CancellationToken ct)
    {
        var fingerprint = Fingerprint(seed.Key, supplier.ToString(), product.ToString(), warehouse.ToString(),
            seed.Quantity.ToString(CultureInfo.InvariantCulture), seed.UnitCost.ToString(CultureInfo.InvariantCulture), seed.Status.ToString());
        var entry = await BeginOperationAsync(seed.Key, "PurchaseOrder", fingerprint, buyer, ct);
        if (entry.CompletedAt is not null) return;
        Guid documentId = Guid.Empty;
        await RunAtAsync(seed.CreatedAt, async () =>
        {
            var note = $"Professional demo 6B.2B {seed.Key}";
            var order = await context.PurchaseOrders.Include(x => x.Items).SingleOrDefaultAsync(x => x.Notes == note, ct);
            if (order is null)
            {
                var po = new PurchaseOrderWorkflow(new EfProcurementPersistence(context), new SeedActor(buyer));
                var created = await po.CreateAsync(new(supplier, warehouse, [new(product, seed.Quantity, seed.UnitCost)], note), ct);
                await ProbeAsync(seed.Key, "Created");
                order = await context.PurchaseOrders.Include(x => x.Items).SingleAsync(x => x.Id == created.Id, ct);
            }

            ValidatePurchaseOrder(seed, order, supplier, product, warehouse, buyer, note);
            var buyerWorkflow = new PurchaseOrderWorkflow(new EfProcurementPersistence(context), new SeedActor(buyer));
            if (seed.Status != PurchaseOrderStatus.Draft && order.Status == PurchaseOrderStatus.Draft)
            {
                var submitted = await buyerWorkflow.SubmitAsync(order.Id, new(order.RowVersion), ct);
                await ProbeAsync(seed.Key, "Submitted");
                order = await context.PurchaseOrders.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == submitted.Id, ct);
            }
            if (seed.Status == PurchaseOrderStatus.Rejected && order.Status == PurchaseOrderStatus.PendingApproval)
                await new PurchaseOrderWorkflow(new EfProcurementPersistence(context), new SeedActor(approver))
                    .RejectAsync(order.Id, new(order.RowVersion, "Professional demo rejection"), ct);
            else if (seed.Status is (PurchaseOrderStatus.Approved or PurchaseOrderStatus.Cancelled or
                     PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received) &&
                     order.Status == PurchaseOrderStatus.PendingApproval)
            {
                var approved = await new PurchaseOrderWorkflow(new EfProcurementPersistence(context), new SeedActor(approver))
                    .ApproveAsync(order.Id, new(order.RowVersion, "Professional demo approval"), ct);
                await ProbeAsync(seed.Key, "Approved");
                order = await context.PurchaseOrders.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == approved.Id, ct);
            }
            if (seed.Status == PurchaseOrderStatus.Cancelled && order.Status == PurchaseOrderStatus.Approved)
                await new PurchaseOrderWorkflow(new EfProcurementPersistence(context), new SeedActor(admin))
                    .CancelAsync(order.Id, new(order.RowVersion, "Professional demo cancellation"), ct);

            order = await context.PurchaseOrders.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == order.Id, ct);
            ValidatePurchaseOrder(seed, order, supplier, product, warehouse, buyer, note);
            var hasPostedReceipts = await context.GoodsReceipts.AsNoTracking()
                .AnyAsync(x => x.PurchaseOrderId == order.Id && x.Status == ERPFlow.Domain.Receiving.GoodsReceiptStatus.Posted, ct);
            var expectedStatus = seed.Status is PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received && !hasPostedReceipts
                ? PurchaseOrderStatus.Approved
                : seed.Status;
            if (order.Status != expectedStatus)
                throw new InvalidOperationException($"Professional demo purchase order '{seed.Key}' ended in {order.Status}, expected {expectedStatus}.");
            documentId = order.Id;
        });
        await CompleteOperationAsync(entry.Id, documentId, ct);
    }

    private async Task RunGoodsReceiptAsync(ProfessionalDemoReceiptSeed seed, Guid warehouseOperator, IInventoryWriteGate gate, CancellationToken ct)
    {
        var fingerprint = Fingerprint(seed.Key, seed.PurchaseOrderKey, seed.Quantity.ToString(CultureInfo.InvariantCulture), seed.Status.ToString());
        var entry = await BeginOperationAsync(seed.Key, "GoodsReceipt", fingerprint, warehouseOperator, ct);
        if (entry.CompletedAt is not null) return;
        Guid documentId = Guid.Empty;
        await RunAtAsync(seed.CreatedAt, async () =>
        {
            var note = $"Professional demo 6B.2B {seed.PurchaseOrderKey}";
            var reference = $"PD-6B2B-{seed.Key}".ToUpperInvariant();
            var order = await context.PurchaseOrders.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Notes == note, ct);
            var receipt = new GoodsReceiptWorkflow(new EfGoodsReceiptPersistence(context), new SeedActor(warehouseOperator), gate,
                new EfInventoryPosting(context, gate, new SeedActor(warehouseOperator)));
            var existingReceipt = await context.GoodsReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.SupplierDeliveryReference == reference, ct);
            if (existingReceipt is not null)
            {
                if (existingReceipt.Status == ERPFlow.Domain.Receiving.GoodsReceiptStatus.Draft &&
                    seed.Status == ERPFlow.Domain.Receiving.GoodsReceiptStatus.Posted)
                {
                    var current = await receipt.GetAsync(existingReceipt.Id, ct);
                    await receipt.PostAsync(current.Id, new($"professional-demo-{seed.Key}-post", current.RowVersion), ct);
                }
                else if (existingReceipt.Status == ERPFlow.Domain.Receiving.GoodsReceiptStatus.Draft &&
                    seed.Status == ERPFlow.Domain.Receiving.GoodsReceiptStatus.Cancelled)
                {
                    var current = await receipt.GetAsync(existingReceipt.Id, ct);
                    await receipt.CancelAsync(current.Id, new(current.RowVersion, "Professional demo cancellation"), ct);
                }
                else if (existingReceipt.Status != seed.Status)
                {
                    throw new InvalidOperationException($"Professional demo receipt '{seed.Key}' cannot reconcile {existingReceipt.Status} to {seed.Status}.");
                }
                documentId = existingReceipt.Id;
                return;
            }
            var draft = await receipt.CreateAsync(new($"professional-demo-{seed.Key}", order.Id, order.RowVersion,
                [new(order.Items.Single().Id, seed.Quantity)], reference), ct);
            if (seed.Status == ERPFlow.Domain.Receiving.GoodsReceiptStatus.Posted)
                documentId = (await receipt.PostAsync(draft.Id, new($"professional-demo-{seed.Key}-post", draft.RowVersion), ct)).Id;
            else if (seed.Status == ERPFlow.Domain.Receiving.GoodsReceiptStatus.Cancelled)
                documentId = (await receipt.CancelAsync(draft.Id, new(draft.RowVersion, "Professional demo cancellation"), ct)).Id;
            else documentId = draft.Id;
        });
        await CompleteOperationAsync(entry.Id, documentId, ct);
    }

    private async Task CompleteOperationAsync(Guid operationId, Guid documentId, CancellationToken ct)
    {
        var completedAt = BusinessClock.UtcNow;
        await context.ProfessionalDemoSeedOperations
            .Where(x => x.Id == operationId && x.CompletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ResultDocumentId, documentId)
                .SetProperty(x => x.CompletedAt, completedAt), ct);
    }

    private Task ProbeAsync(string key, string stage) => InterruptAfterAsync?.Invoke(key, stage) ?? Task.CompletedTask;

    private static void ValidatePurchaseOrder(ProfessionalDemoPurchaseOrderSeed seed, PurchaseOrder order,
        Guid supplier, Guid product, Guid warehouse, Guid buyer, string note)
    {
        var item = order.Items.SingleOrDefault();
        if (order.SupplierId != supplier || order.WarehouseId != warehouse || order.CreatedByUserId != buyer ||
            order.CreatedAt != seed.CreatedAt.UtcDateTime || order.Notes != note || item is null ||
            item.ProductId != product || item.Quantity != seed.Quantity || item.UnitCost != seed.UnitCost ||
            order.TotalAmount != seed.Quantity * seed.UnitCost)
            throw new InvalidOperationException($"Professional demo purchase order '{seed.Key}' does not match its immutable requested fields.");
    }

    private async Task<ProfessionalDemoSeedOperation> BeginOperationAsync(string key, string type, string fingerprint,
        Guid actorId, CancellationToken ct)
    {
        var entry = await context.ProfessionalDemoSeedOperations.SingleOrDefaultAsync(x => x.ScenarioKey == ScenarioKey && x.OperationKey == key, ct);
        if (entry is not null)
        {
            if (entry.OperationType != type || entry.InputFingerprint != fingerprint || entry.ActorId != actorId)
                throw new InvalidOperationException($"Professional demo operation '{key}' does not match its durable input fingerprint.");
            return entry;
        }
        entry = new ProfessionalDemoSeedOperation(ScenarioKey, key, type, fingerprint, actorId);
        context.ProfessionalDemoSeedOperations.Add(entry);
        await context.SaveChangesAsync(ct);
        return entry;
    }

    private static string Fingerprint(params string[] parts) => Convert.ToHexString(SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(string.Join('|', parts))));

    private async Task SeedMasterDataAsync(string password, CancellationToken ct)
    {
        var hasher = new PasswordHasher<object>();
        var users = new[]
        {
            ("Rami Al-Khatib", "admin@jot.example", UserRole.Administrator),
            ("Lina Haddad", "sales@jot.example", UserRole.Sales),
            ("Khaled Masri", "approver@jot.example", UserRole.Approver),
            ("Samer Nassar", "purchasing@jot.example", UserRole.Purchasing),
            ("Huda Saleh", "warehouse@jot.example", UserRole.WarehouseOperator),
            ("Yousef Tawalbeh", "technician@jot.example", UserRole.Technician)
        };
        context.AppUsers.AddRange(users.Select(x => new AppUser(x.Item1, x.Item2, x.Item3, hasher.HashPassword(new object(), password))));

        var customers = Enumerable.Range(1, 20).Select(i => new Customer(
            new[] { "Amman Medical Group", "Petra Logistics", "Cedar Schools", "Jordan Valley Foods", "Nabataean Hotels" }[(i - 1) % 5] + $" {i:D2}",
            $"customer{i:D2}@jot.example")).ToArray();
        customers[^1].Deactivate();
        context.Customers.AddRange(customers);

        var products = ProductDefinitions.Select(definition => new Product(definition.Name, definition.Sku, definition.Price, 0)).ToArray();
        foreach (var product in products.Where(x => x.Sku is "JOT-SPARE-014" or "JOT-CONS-019" or "JOT-LEGACY-035")) product.SetActive(false);
        context.Products.AddRange(products);

        context.Warehouses.AddRange(
            new Warehouse("AMM-CENTRAL", "Amman Central Distribution"),
            new Warehouse("AMM-SERVICE", "Amman Service Parts"));
        context.Suppliers.AddRange(Enumerable.Range(1, 10).Select(i => new Supplier(
            $"JOT-SUP-{i:D2}",
            new[] { "Levant Office Systems", "Gulf Imaging Supplies", "Arabian Barcode Technologies", "MENA Toner Trading" }[(i - 1) % 4],
            $"Supplier Contact {i:D2}", $"supplier{i:D2}@jot.example", $"+962 6 555 {i:D3}", "Amman, Jordan")));
        await context.SaveChangesAsync(ct);
    }

    private async Task<object> ReadCountsAsync(CancellationToken ct) => new
    {
        Users = await context.AppUsers.CountAsync(ct),
        Customers = await context.Customers.CountAsync(ct),
        Products = await context.Products.CountAsync(ct),
        ActiveProducts = await context.Products.CountAsync(x => x.IsActive, ct),
        Suppliers = await context.Suppliers.CountAsync(ct),
        Warehouses = await context.Warehouses.CountAsync(x => x.IsActive, ct)
    };

    private static readonly (string Name, string Sku, decimal Price)[] ProductDefinitions =
    [
        ("A4 Mono Laser Printer", "JOT-PRN-001", 185m), ("A4 Colour Laser Printer", "JOT-PRN-002", 325m),
        ("A3 Multifunction Copier", "JOT-PRN-003", 1290m), ("Desktop Document Scanner", "JOT-SCN-004", 245m),
        ("Production Document Scanner", "JOT-SCN-005", 875m), ("Direct Thermal Label Printer", "JOT-LBL-006", 210m),
        ("Industrial Barcode Scanner", "JOT-SCN-007", 155m), ("Wireless Barcode Scanner", "JOT-SCN-008", 89m),
        ("POS Receipt Printer", "JOT-PRN-009", 120m), ("Network Print Server", "JOT-NET-010", 145m),
        ("Black Toner Cartridge 78A", "JOT-TON-011", 42m), ("Colour Toner Set", "JOT-TON-012", 115m),
        ("A4 Copy Paper Carton", "JOT-PAP-013", 28.50m), ("Fuser Maintenance Kit", "JOT-SPARE-014", 165m),
        ("Printer Pickup Roller", "JOT-SPARE-015", 18m), ("Barcode Labels 100x150", "JOT-LBL-016", 14.75m),
        ("Thermal Ribbon Pack", "JOT-LBL-017", 22m), ("USB Printer Cable", "JOT-ACC-018", 4.50m),
        ("Ethernet Patch Cable Pack", "JOT-CONS-019", 8.25m), ("Printer Stand", "JOT-ACC-020", 95m),
        ("ADF Separation Pad", "JOT-SPARE-021", 12m), ("Scanner Cleaning Kit", "JOT-SPARE-022", 16.50m),
        ("External SSD 1TB", "JOT-ACC-023", 72m), ("UPS 1200VA", "JOT-UPS-024", 145m),
        ("UPS 2200VA", "JOT-UPS-025", 265m), ("Office Shredder", "JOT-OFF-026", 180m),
        ("Document Binding Machine", "JOT-OFF-027", 135m), ("Laminator A3", "JOT-OFF-028", 110m),
        ("Cash Drawer", "JOT-POS-029", 85m), ("POS Customer Display", "JOT-POS-030", 75m),
        ("Handheld Terminal", "JOT-POS-031", 240m), ("Maintenance Labour Pack", "JOT-SVC-032", 55m),
        ("Preventive Maintenance Kit", "JOT-SVC-033", 38m), ("Printer Network Card", "JOT-SPARE-034", 88m),
        ("Legacy Dot Matrix Ribbon", "JOT-LEGACY-035", 11m), ("Wide Format Printer", "JOT-PRN-036", 2150m),
        ("Wide Format Ink Set", "JOT-TON-037", 190m), ("Mobile Label Printer", "JOT-LBL-038", 295m)
    ];

    private static DateTimeOffset Utc(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SeedActor(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
    }

    private sealed class DemoWarehouseGate(ERPFlowDbContext context) : IInventoryWriteGate
    {
        public void EnsureWarehouseWritesAllowed()
        {
            if (!context.ProfessionalDemoSeedManifests.Any(x => x.ScenarioKey == ScenarioKey && x.Version == Version))
                throw new InvalidOperationException("Warehouse writes are available only to the manifest-owned professional demo command.");
            if (!context.InventoryAuthorities.AsNoTracking().Any(x => x.Mode == InventoryAuthorityMode.Warehouse))
                throw new InvalidOperationException("Professional demo warehouse writes require its completed isolated cutover.");
        }
    }

    private sealed class DemoCutoverPermission(ERPFlowDbContext context) : IInventoryCutoverPermission
    {
        public void EnsureOperatorExecutionAllowed()
        {
            if (!context.ProfessionalDemoSeedManifests.Any(x => x.ScenarioKey == ScenarioKey && x.Version == Version))
                throw new InvalidOperationException("Inventory cutover is available only to the manifest-owned professional demo command.");
        }
    }

    private sealed class DemoWarehousePolicy(ERPFlowDbContext context) : IWarehouseSourcePolicy
    {
        public async Task<Guid> ResolveAsync(Guid? requestedWarehouseId, Guid? existingWarehouseId = null, CancellationToken cancellationToken = default)
        {
            var selected = requestedWarehouseId ?? existingWarehouseId ?? throw new InvalidOperationException("Professional demo commands require an explicit warehouse.");
            if (!await context.Warehouses.AsNoTracking().AnyAsync(x => x.Id == selected && x.IsActive, cancellationToken))
                throw new InvalidOperationException("Professional demo command selected an unavailable warehouse.");
            return selected;
        }
    }

}
