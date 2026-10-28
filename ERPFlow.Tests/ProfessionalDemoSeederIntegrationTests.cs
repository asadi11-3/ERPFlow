using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Procurement;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProfessionalDemoSeederIntegrationTests : IAsyncLifetime
{
    private readonly string databaseName = $"ERPFlow_Demo_Test_{Guid.NewGuid():N}";
    private string connectionString = null!;

    public Task InitializeAsync()
    {
        connectionString = ProfessionalDemoDatabaseGuard.BuildLocalConnectionString(databaseName);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(@name) IS NOT NULL ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; IF DB_ID(@name) IS NOT NULL DROP DATABASE [{databaseName}];";
        command.Parameters.AddWithValue("@name", databaseName);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task DryRunDoesNotCreateDatabase()
    {
        await using var context = CreateContext();
        var result = await new ProfessionalDemoSeeder(context).SeedAsync(
            new ProfessionalDemoSeedOptions(databaseName, "test-password", DryRun: true));

        Assert.False(result.Applied);
        Assert.False(await ProfessionalDemoDatabaseGuard.ExistsAsync(connectionString, default));
    }

    [Fact]
    public async Task SeedsStageAThroughDWorkflowHistoryAndRerunDoesNotDuplicate()
    {
        await using var context = CreateContext();
        var seeder = new ProfessionalDemoSeeder(context);
        var first = await seeder.SeedAsync(new ProfessionalDemoSeedOptions(databaseName, "test-password", false));

        Assert.True(first.Applied);
        await new EfInventoryReconciliation(context).ReconcileAsync();
        Assert.Equal(6, await context.AppUsers.CountAsync());
        Assert.Equal(20, await context.Customers.CountAsync());
        Assert.Equal(38, await context.Products.CountAsync());
        Assert.Equal(35, await context.Products.CountAsync(x => x.IsActive));
        Assert.Equal(10, await context.Suppliers.CountAsync());
        Assert.Equal(3, await context.Warehouses.CountAsync(x => x.IsActive));
        Assert.Equal(22, await context.PurchaseOrders.CountAsync());
        Assert.Equal(24, await context.GoodsReceipts.CountAsync());
        Assert.Equal(122, await context.ProfessionalDemoSeedOperations.CountAsync());
        Assert.All(await context.ProfessionalDemoSeedOperations.AsNoTracking().ToArrayAsync(), x => Assert.NotNull(x.CompletedAt));
        Assert.Equal(2, await context.PurchaseOrders.CountAsync(x => x.Status == ERPFlow.Domain.Procurement.PurchaseOrderStatus.Draft));
        Assert.Equal(2, await context.PurchaseOrders.CountAsync(x => x.Status == ERPFlow.Domain.Procurement.PurchaseOrderStatus.PendingApproval));
        Assert.Equal(3, await context.PurchaseOrders.CountAsync(x => x.Status == ERPFlow.Domain.Procurement.PurchaseOrderStatus.Approved));
        Assert.Equal(3, await context.PurchaseOrders.CountAsync(x => x.Status == ERPFlow.Domain.Procurement.PurchaseOrderStatus.Rejected));
        Assert.Equal(3, await context.PurchaseOrders.CountAsync(x => x.Status == ERPFlow.Domain.Procurement.PurchaseOrderStatus.Cancelled));
        Assert.Equal(4, await context.PurchaseOrders.CountAsync(x => x.Status == ERPFlow.Domain.Procurement.PurchaseOrderStatus.PartiallyReceived));
        Assert.Equal(5, await context.PurchaseOrders.CountAsync(x => x.Status == ERPFlow.Domain.Procurement.PurchaseOrderStatus.Received));
        Assert.Equal(1, await context.GoodsReceipts.CountAsync(x => x.Status == ERPFlow.Domain.Receiving.GoodsReceiptStatus.Draft));
        Assert.Equal(22, await context.GoodsReceipts.CountAsync(x => x.Status == ERPFlow.Domain.Receiving.GoodsReceiptStatus.Posted));
        Assert.Equal(1, await context.GoodsReceipts.CountAsync(x => x.Status == ERPFlow.Domain.Receiving.GoodsReceiptStatus.Cancelled));
        Assert.Equal(22, await context.InventoryPostings.CountAsync(x => x.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.Receipt));
        Assert.Equal(12, await context.StockTransfers.CountAsync());
        Assert.Equal(3, await context.StockTransfers.CountAsync(x => x.Status == ERPFlow.Domain.Transfers.StockTransferStatus.Draft));
        Assert.Equal(6, await context.StockTransfers.CountAsync(x => x.Status == ERPFlow.Domain.Transfers.StockTransferStatus.Posted));
        Assert.Equal(3, await context.StockTransfers.CountAsync(x => x.Status == ERPFlow.Domain.Transfers.StockTransferStatus.Cancelled));
        Assert.Equal(18, await context.StockTransferCommands.CountAsync());
        Assert.Equal(6, await context.InventoryPostings.CountAsync(x => x.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.TransferOut));
        Assert.Equal(6, await context.InventoryPostings.CountAsync(x => x.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.TransferIn));
        Assert.Equal(6, await SqlCountAsync("""
            SELECT COUNT(*) FROM StockTransferItems item
            JOIN StockTransfers transfer ON transfer.Id = item.StockTransferId
            JOIN InventoryPostings outgoing ON outgoing.StockTransferItemId = item.Id AND outgoing.Kind = 5
            JOIN StockMovements outgoingMovement ON outgoingMovement.PostingId = outgoing.Id
            JOIN WarehouseStocks sourceStock ON sourceStock.Id = outgoingMovement.WarehouseStockId AND sourceStock.WarehouseId = transfer.SourceWarehouseId
            JOIN InventoryPostings incoming ON incoming.StockTransferItemId = item.Id AND incoming.Kind = 6
            JOIN StockMovements incomingMovement ON incomingMovement.PostingId = incoming.Id
            JOIN WarehouseStocks destinationStock ON destinationStock.Id = incomingMovement.WarehouseStockId AND destinationStock.WarehouseId = transfer.DestinationWarehouseId
            WHERE transfer.Status = 1 AND outgoingMovement.PhysicalDelta = -item.Quantity AND incomingMovement.PhysicalDelta = item.Quantity;
            """));
        Assert.Equal(0, await SqlCountAsync("SELECT COALESCE(SUM(PhysicalDelta), 0) FROM StockMovements WHERE Kind IN (5, 6);"));
        var receiptMovements = await context.StockMovements.AsNoTracking()
            .Where(x => x.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.Receipt).ToArrayAsync();
        Assert.Equal(22, receiptMovements.Length);
        Assert.Equal(22, receiptMovements.Select(x => x.PostingId).Distinct().Count());
        Assert.Equal(49, await context.WarehouseStocks.SumAsync(x => x.StockOnHand));
        Assert.Equal(24, await context.WarehouseStocks.SumAsync(x => x.ReservedQuantity));
        Assert.All(await context.WarehouseStocks.AsNoTracking().ToArrayAsync(), stock =>
            Assert.True(stock.StockOnHand >= stock.ReservedQuantity));
        Assert.Equal(0, await context.Products.SumAsync(x => x.StockOnHand));
        Assert.Equal(0, await context.Products.SumAsync(x => x.ReservedQuantity));
        var warehouseTotals = await context.WarehouseStocks.Join(context.Warehouses, stock => stock.WarehouseId, warehouse => warehouse.Id,
            (stock, warehouse) => new { warehouse.Code, stock.StockOnHand }).GroupBy(x => x.Code)
            .Select(x => new { x.Key, Total = x.Sum(y => y.StockOnHand) }).ToDictionaryAsync(x => x.Key, x => x.Total);
        Assert.Equal(16, warehouseTotals["AMM-CENTRAL"]);
        Assert.Equal(16, warehouseTotals["AMM-SERVICE"]);
        Assert.Equal(17, warehouseTotals["LEGACY-DEFAULT"]);
        var movementCounts = await context.StockMovements.Join(context.WarehouseStocks, movement => movement.WarehouseStockId, stock => stock.Id,
            (movement, stock) => new { movement, stock.WarehouseId }).Join(context.Warehouses, x => x.WarehouseId, warehouse => warehouse.Id,
            (x, warehouse) => new { warehouse.Code, x.movement }).Where(x => x.movement.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.Receipt)
            .GroupBy(x => x.Code).Select(x => new { x.Key, Count = x.Count(), Quantity = x.Sum(y => y.movement.PhysicalDelta) })
            .ToDictionaryAsync(x => x.Key, x => (x.Count, x.Quantity));
        Assert.Equal((6, 20), movementCounts["AMM-CENTRAL"]);
        Assert.Equal((7, 22), movementCounts["AMM-SERVICE"]);
        Assert.Equal((9, 15), movementCounts["LEGACY-DEFAULT"]);
        var receipts = await context.GoodsReceipts.AsNoTracking().ToArrayAsync();
        var orders = await context.PurchaseOrders.AsNoTracking().ToDictionaryAsync(x => x.Id);
        Assert.All(receipts, receipt => Assert.True(receipt.CreatedAt >= orders[receipt.PurchaseOrderId].CreatedAt));
        Assert.All(receipts.Where(x => x.PostedAt.HasValue), receipt => Assert.True(receipt.PostedAt >= receipt.CreatedAt));
        await AssertChronologySqlAsync();
        await AssertStageCSqlAsync();
        await AssertStageDSqlAsync();

        context.ChangeTracker.Clear();
        var second = await seeder.SeedAsync(new ProfessionalDemoSeedOptions(databaseName, "different-password", false));

        Assert.False(second.Applied);
        await new EfInventoryReconciliation(context).ReconcileAsync();
        Assert.Equal(1, await context.ProfessionalDemoSeedManifests.CountAsync());
        Assert.Equal(38, await context.Products.CountAsync());
        Assert.Equal(24, await context.GoodsReceipts.CountAsync());
        Assert.Equal(22, await context.StockMovements.CountAsync(x => x.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.Receipt));
        Assert.Equal(12, await context.StockTransfers.CountAsync());
        Assert.Equal(12, await context.StockMovements.CountAsync(x => x.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.TransferOut || x.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.TransferIn));
        await AssertChronologySqlAsync();
        await AssertStageCSqlAsync();
        await AssertStageDSqlAsync();
    }

    [Fact]
    public async Task ExistingUnownedDatabaseIsRejected()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProfessionalDemoSeeder(context).SeedAsync(
                new ProfessionalDemoSeedOptions(databaseName, "test-password", false)));

        Assert.Contains("not owned by the expected professional demo scenario", exception.Message);
    }

    [Fact]
    public async Task InterruptedPurchaseOrderCreateSubmitApproveAndCancelResumeThroughWorkflow()
    {
        await using var context = CreateContext();
        var options = new ProfessionalDemoSeedOptions(databaseName, "test-password", false);

        await Assert.ThrowsAsync<SimulatedInterruption>(() =>
            InterruptingSeeder(context, "po-20", "Created").SeedAsync(options));
        context.ChangeTracker.Clear();
        var order = await context.PurchaseOrders.AsNoTracking().Include(x => x.Items)
            .SingleAsync(x => x.Notes == "Professional demo 6B.2B po-20");
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);

        await Assert.ThrowsAsync<SimulatedInterruption>(() =>
            InterruptingSeeder(context, "po-20", "Submitted").SeedAsync(options));
        context.ChangeTracker.Clear();
        order = await context.PurchaseOrders.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == order.Id);
        Assert.Equal(PurchaseOrderStatus.PendingApproval, order.Status);

        await Assert.ThrowsAsync<SimulatedInterruption>(() =>
            InterruptingSeeder(context, "po-20", "Approved").SeedAsync(options));
        context.ChangeTracker.Clear();
        order = await context.PurchaseOrders.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == order.Id);
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);

        await new ProfessionalDemoSeeder(context).SeedAsync(options);
        context.ChangeTracker.Clear();
        order = await context.PurchaseOrders.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == order.Id);
        Assert.Equal(PurchaseOrderStatus.Cancelled, order.Status);
        Assert.Equal(order.Id, await context.ProfessionalDemoSeedOperations.Where(x => x.OperationKey == "po-20")
            .Select(x => x.ResultDocumentId).SingleAsync());

        await context.Database.ExecuteSqlRawAsync("UPDATE ProfessionalDemoSeedOperations SET CompletedAt = NULL, ResultDocumentId = NULL WHERE OperationKey IN (N'po-01', N'po-06')");
        context.ChangeTracker.Clear();
        await new ProfessionalDemoSeeder(context).SeedAsync(options);

        Assert.Equal(PurchaseOrderStatus.Received, await context.PurchaseOrders.Where(x => x.Notes == "Professional demo 6B.2B po-01").Select(x => x.Status).SingleAsync());
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, await context.PurchaseOrders.Where(x => x.Notes == "Professional demo 6B.2B po-06").Select(x => x.Status).SingleAsync());
        Assert.Equal(22, await context.PurchaseOrders.CountAsync());
        Assert.Equal(24, await context.GoodsReceipts.CountAsync());
    }

    [Fact]
    public async Task InterruptedSalesCorrectionEditsAlternateCustomerBeforeResubmit()
    {
        await using var context = CreateContext();
        var options = new ProfessionalDemoSeedOptions(databaseName, "test-password", false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InterruptingSeeder(context, "so-18", "Returned").SeedAsync(options));
        Assert.IsType<SimulatedInterruption>(exception.InnerException);
        context.ChangeTracker.Clear();

        var customers = await context.Customers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Email).Select(x => x.Id).ToArrayAsync();
        var interrupted = await context.SalesOrders.AsNoTracking().Include(x => x.Items)
            .SingleAsync(x => x.CreatedAt == new DateTime(2026, 10, 5, 13, 0, 0, DateTimeKind.Utc));
        Assert.Equal(OrderStatus.Draft, interrupted.Status);
        Assert.Equal(customers[18], interrupted.CustomerId);
        Assert.Contains(ApprovalAction.Returned, await context.ApprovalHistories.Where(x => x.SalesOrderId == interrupted.Id)
            .Select(x => x.Action).ToArrayAsync());

        await new ProfessionalDemoSeeder(context).SeedAsync(options);
        context.ChangeTracker.Clear();
        var resumed = await context.SalesOrders.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == interrupted.Id);
        Assert.Equal(OrderStatus.PendingApproval, resumed.Status);
        Assert.Equal(customers[17], resumed.CustomerId);
        var item = Assert.Single(resumed.Items);
        var expectedProduct = await context.Products.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Sku)
            .Select(x => x.Id).ElementAtAsync(2);
        Assert.Equal(expectedProduct, item.ProductId);
        Assert.Equal(1, item.Quantity);
        Assert.Equal("LEGACY-DEFAULT", await context.Warehouses.Where(x => x.Id == item.WarehouseId).Select(x => x.Code).SingleAsync());
        Assert.Equal(2, await context.ApprovalHistories.CountAsync(x => x.SalesOrderId == resumed.Id && x.Action == ApprovalAction.Submitted));
        Assert.Equal(resumed.Id, await context.ProfessionalDemoSeedOperations.Where(x => x.OperationKey == "so-18")
            .Select(x => x.ResultDocumentId).SingleAsync());
    }

    [Fact]
    public async Task ReplayReconcilesPostedTransferWhenCheckpointWasNotCompleted()
    {
        await using var context = CreateContext();
        var seeder = new ProfessionalDemoSeeder(context);
        await seeder.SeedAsync(new ProfessionalDemoSeedOptions(databaseName, "test-password", false));

        await context.Database.ExecuteSqlRawAsync("UPDATE ProfessionalDemoSeedOperations SET CompletedAt = NULL, ResultDocumentId = NULL WHERE OperationKey = N'trf-01'");
        context.ChangeTracker.Clear();

        await seeder.SeedAsync(new ProfessionalDemoSeedOptions(databaseName, "test-password", false));

        Assert.Equal(24, await context.GoodsReceipts.CountAsync());
        Assert.Equal(12, await context.StockTransfers.CountAsync());
        Assert.Equal(12, await context.StockMovements.CountAsync(x => x.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.TransferOut || x.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.TransferIn));
        Assert.NotNull(await context.ProfessionalDemoSeedOperations
            .Where(x => x.OperationKey == "trf-01").Select(x => x.CompletedAt).SingleAsync());
    }

    [Fact]
    public async Task ReplayReconcilesDispatchedSalesOrderWhenCheckpointWasNotCompleted()
    {
        await using var context = CreateContext();
        var seeder = new ProfessionalDemoSeeder(context);
        await seeder.SeedAsync(new ProfessionalDemoSeedOptions(databaseName, "test-password", false));

        await context.Database.ExecuteSqlRawAsync("UPDATE ProfessionalDemoSeedOperations SET CompletedAt = NULL, ResultDocumentId = NULL WHERE OperationKey = N'dispatch-so-30'");
        context.ChangeTracker.Clear();
        await seeder.SeedAsync(new ProfessionalDemoSeedOptions(databaseName, "test-password", false));

        Assert.Equal(45, await context.SalesOrders.CountAsync());
        Assert.Equal(3, await context.DeliveryNotes.CountAsync());
        Assert.Equal(3, await context.MachineAssets.CountAsync());
        var order = await context.SalesOrders.AsNoTracking().SingleAsync(x => x.CreatedAt == new DateTime(2026, 10, 11, 13, 0, 0, DateTimeKind.Utc));
        var delivery = await context.DeliveryNotes.AsNoTracking().SingleAsync(x => x.SalesOrderId == order.Id);
        Assert.Equal(order.CustomerId, delivery.CustomerId);
        Assert.Equal("JOT Logistics", delivery.Carrier);
        Assert.Equal("PD-6B2B-so-30", delivery.TrackingNumber);
        var asset = await context.MachineAssets.AsNoTracking().SingleAsync(x => x.SalesOrderId == order.Id);
        Assert.Equal("PD-6B2B-SO-30-01", asset.SerialNumber);
        Assert.Equal(order.CustomerId, asset.CustomerId);
        Assert.NotNull(await context.ProfessionalDemoSeedOperations.Where(x => x.OperationKey == "dispatch-so-30")
            .Select(x => x.CompletedAt).SingleAsync());
    }

    [Fact]
    public async Task ReplayReconcilesCompletedServiceCaseWhenCheckpointWasNotCompleted()
    {
        await using var context = CreateContext();
        var seeder = new ProfessionalDemoSeeder(context);
        await seeder.SeedAsync(new ProfessionalDemoSeedOptions(databaseName, "test-password", false));

        await context.Database.ExecuteSqlRawAsync("UPDATE ProfessionalDemoSeedOperations SET CompletedAt = NULL, ResultDocumentId = NULL WHERE OperationKey = N'srv-11'");
        context.ChangeTracker.Clear();
        await seeder.SeedAsync(new ProfessionalDemoSeedOptions(databaseName, "test-password", false));

        Assert.Equal(15, await context.Jobcards.CountAsync());
        Assert.Equal(5, await SqlCountAsync("SELECT COUNT(*) FROM InventoryPostings posting JOIN InventoryReservations reservation ON reservation.Id = posting.ReservationId WHERE posting.Kind = 3 AND reservation.JobcardItemId IS NOT NULL;"));
        var completed = await context.Jobcards.AsNoTracking().SingleAsync(x => x.ReportedFault == "Professional demo service case srv-11.");
        Assert.Equal(JobcardStatus.Completed, completed.Status);
        Assert.Equal("Professional demo repair completed and verified.", completed.TechnicianNotes);
        Assert.Equal(1000, completed.FinalMeterReading);
        Assert.Equal(new DateTime(2026, 10, 25, 11, 0, 0, DateTimeKind.Utc), completed.CompletedAt);
        Assert.NotNull(await context.ProfessionalDemoSeedOperations.Where(x => x.OperationKey == "srv-11")
            .Select(x => x.CompletedAt).SingleAsync());
    }

    [Fact]
    public async Task ReplayRejectsChangedOperationFingerprint()
    {
        await using var context = CreateContext();
        var seeder = new ProfessionalDemoSeeder(context);
        await seeder.SeedAsync(new ProfessionalDemoSeedOptions(databaseName, "test-password", false));

        await context.Database.ExecuteSqlRawAsync("UPDATE ProfessionalDemoSeedOperations SET InputFingerprint = REPLICATE('0', 64), CompletedAt = NULL WHERE OperationKey = N'trf-01'");
        context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            seeder.SeedAsync(new ProfessionalDemoSeedOptions(databaseName, "test-password", false)));
        Assert.Contains("does not match its durable input fingerprint", exception.Message);
        Assert.Equal(24, await context.GoodsReceipts.CountAsync());
        Assert.Equal(12, await context.StockTransfers.CountAsync());
    }

    [Fact]
    public async Task ConcurrentCommandsSerializeWithoutDuplicateEffects()
    {
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();

        var results = await Task.WhenAll(
            new ProfessionalDemoSeeder(firstContext).SeedAsync(new(databaseName, "test-password", false)),
            new ProfessionalDemoSeeder(secondContext).SeedAsync(new(databaseName, "test-password", false)));

        Assert.Single(results, x => x.Applied);
        await using var verification = CreateContext();
        Assert.Equal(22, await verification.PurchaseOrders.CountAsync());
        Assert.Equal(24, await verification.GoodsReceipts.CountAsync());
        Assert.Equal(22, await verification.StockMovements.CountAsync(x => x.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.Receipt));
        Assert.Equal(12, await verification.StockTransfers.CountAsync());
        Assert.Equal(12, await verification.StockMovements.CountAsync(x => x.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.TransferOut || x.Kind == ERPFlow.Domain.Inventory.InventoryPostingKind.TransferIn));
    }

    private async Task<int> SqlCountAsync(string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task AssertStageCSqlAsync()
    {
        Assert.Equal(10, await SqlCountAsync("SELECT COUNT(*) FROM SalesOrders WHERE Status = 0;"));
        Assert.Equal(12, await SqlCountAsync("SELECT COUNT(*) FROM SalesOrders WHERE Status = 1;"));
        Assert.Equal(10, await SqlCountAsync("SELECT COUNT(*) FROM SalesOrders WHERE Status = 2;"));
        Assert.Equal(13, await SqlCountAsync("SELECT COUNT(*) FROM SalesOrders WHERE Status = 3;"));
        Assert.Equal(19, await SqlCountAsync("SELECT COUNT(*) FROM StockReservations WHERE IsReleased = 0;"));
        Assert.Equal(8, await SqlCountAsync("SELECT COUNT(*) FROM ApprovalHistories WHERE Action = 3;"));
        Assert.Equal(3, await SqlCountAsync("SELECT COUNT(*) FROM DeliveryNotes;"));
        Assert.Equal(3, await SqlCountAsync("SELECT COUNT(*) FROM MachineAssets;"));
        Assert.Equal(3, await SqlCountAsync("SELECT COUNT(*) FROM InventoryPostings posting JOIN InventoryReservations reservation ON reservation.Id = posting.ReservationId WHERE posting.Kind = 3 AND reservation.SalesOrderId IS NOT NULL;"));
        Assert.Equal(0, await SqlCountAsync("SELECT COUNT(*) FROM WarehouseStocks WHERE StockOnHand < 0 OR ReservedQuantity < 0 OR ReservedQuantity > StockOnHand;"));
        Assert.Equal(0, await SqlCountAsync("SELECT COUNT(*) FROM DeliveryNotes GROUP BY SalesOrderId HAVING COUNT(*) > 1;"));
        Assert.Equal(0, await SqlCountAsync("SELECT COUNT(*) FROM MachineAssets GROUP BY SerialNumber HAVING COUNT(*) > 1;"));
        Assert.Equal(0, await SqlCountAsync("SELECT COUNT(*) FROM SalesOrders orderHeader JOIN DeliveryNotes note ON note.SalesOrderId = orderHeader.Id WHERE note.DispatchedAt < orderHeader.CreatedAt;"));
    }

    private async Task AssertChronologySqlAsync()
    {
        Assert.Equal(0, await SqlCountAsync("""
            WITH ordered AS
            (
                SELECT movement.WarehouseStockId, movement.Sequence, posting.CreatedAt,
                    LAG(posting.CreatedAt) OVER
                        (PARTITION BY movement.WarehouseStockId ORDER BY movement.Sequence) AS PreviousCreatedAt
                FROM StockMovements movement
                JOIN InventoryPostings posting ON posting.Id = movement.PostingId
            )
            SELECT COUNT(*) FROM ordered WHERE PreviousCreatedAt > CreatedAt;
            """));
        Assert.Equal(0, await SqlCountAsync("""
            DECLARE @firstDocument datetime2 =
            (
                SELECT MIN(CreatedAt) FROM
                (
                    SELECT CreatedAt FROM PurchaseOrders
                    UNION ALL SELECT CreatedAt FROM GoodsReceipts
                    UNION ALL SELECT CreatedAt FROM StockTransfers
                    UNION ALL SELECT CreatedAt FROM SalesOrders
                    UNION ALL SELECT CreatedAt FROM Jobcards
                ) documents
            );
            SELECT COUNT(*) FROM
            (
                SELECT CreatedAt FROM AppUsers
                UNION ALL SELECT CreatedAt FROM Customers
                UNION ALL SELECT CreatedAt FROM Products
                UNION ALL SELECT CreatedAt FROM Suppliers
                UNION ALL SELECT CreatedAt FROM Warehouses WHERE Code <> N'LEGACY-DEFAULT'
            ) masters
            WHERE CreatedAt >= @firstDocument;
            """));
    }

    private async Task AssertStageDSqlAsync()
    {
        Assert.Equal(5, await SqlCountAsync("SELECT COUNT(*) FROM Jobcards WHERE Status = 0;"));
        Assert.Equal(5, await SqlCountAsync("SELECT COUNT(*) FROM Jobcards WHERE Status = 1;"));
        Assert.Equal(5, await SqlCountAsync("SELECT COUNT(*) FROM Jobcards WHERE Status = 3;"));
        Assert.Equal(10, await SqlCountAsync("SELECT COUNT(*) FROM JobcardItems;"));
        Assert.Equal(5, await SqlCountAsync("SELECT COUNT(*) FROM InventoryReservations WHERE JobcardItemId IS NOT NULL AND Status = 0;"));
        Assert.Equal(5, await SqlCountAsync("SELECT COUNT(*) FROM InventoryReservations WHERE JobcardItemId IS NOT NULL AND Status = 2;"));
        Assert.Equal(5, await SqlCountAsync("SELECT COUNT(*) FROM InventoryPostings posting JOIN InventoryReservations reservation ON reservation.Id = posting.ReservationId WHERE posting.Kind = 3 AND reservation.JobcardItemId IS NOT NULL;"));
        Assert.Equal(0, await SqlCountAsync("SELECT COUNT(*) FROM Jobcards WHERE Status IN (2, 4);"));
        Assert.Equal(0, await SqlCountAsync("SELECT COUNT(*) FROM Jobcards WHERE CompletedAt < CreatedAt;"));
        Assert.Equal(3, await SqlCountAsync("SELECT COUNT(*) FROM MachineAssets WHERE SerialNumber LIKE 'PD-6B2B-%' AND MeterReading >= 1000;"));
        Assert.Equal(0, await SqlCountAsync("SELECT COUNT(*) FROM InventoryReservations WHERE JobcardItemId IS NOT NULL GROUP BY JobcardItemId HAVING COUNT(*) > 1;"));
    }

    private ERPFlowDbContext CreateContext() => new(new DbContextOptionsBuilder<ERPFlowDbContext>()
        .UseSqlServer(connectionString).Options);

    private static ProfessionalDemoSeeder InterruptingSeeder(ERPFlowDbContext context, string key, string stage) =>
        new(context)
        {
            InterruptAfterAsync = (observedKey, observedStage) => observedKey == key && observedStage == stage
                ? Task.FromException(new SimulatedInterruption())
                : Task.CompletedTask
        };

    private sealed class SimulatedInterruption : Exception;
}
