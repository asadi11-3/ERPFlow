using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProcurementMigrationTests
{
    [Fact]
    public async Task UpgradeFromPhase2CPreservesInventoryAndAddsEmptyProcurementTables()
    {
        await using var database = await InventoryTestDatabase.CreateAsync("20261026140337_AtomicInventoryCutoverRehearsal");
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        var before = await context.Products.AsNoTracking().SingleAsync();
        var warehouses = await context.Warehouses.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.RowVersion);
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();
        var after = await context.Products.AsNoTracking().SingleAsync();
        Assert.Equal((50, 6), (after.StockOnHand, after.ReservedQuantity));
        Assert.Equal(before.RowVersion, after.RowVersion);
        Assert.All(await context.Warehouses.AsNoTracking().ToListAsync(), x => Assert.Equal(warehouses[x.Id], x.RowVersion));
        Assert.NotNull(await context.StockReservations.SingleOrDefaultAsync(x => x.SalesOrderId == seed.SalesOrderId));
        Assert.Empty(await context.Suppliers.ToListAsync());
        Assert.Empty(await context.PurchaseOrders.ToListAsync());
        Assert.Empty(await context.PurchaseOrderItems.ToListAsync());
        Assert.Empty(await context.PurchaseOrderHistories.ToListAsync());
        Assert.Equal(InventoryAuthorityMode.Product, (await context.InventoryAuthorities.SingleAsync()).Mode);
        Assert.Empty(await context.StockMovements.ToListAsync());
    }

    [Fact]
    public void MigrationHasOnlyNewTablesIndexesAndGuardsWithNoDestructiveDowngrade()
    {
        using var context = new ERPFlowDbContext(new DbContextOptionsBuilder<ERPFlowDbContext>().UseSqlServer(
            "Server=unreachable;Database=DesignOnly;Integrated Security=True").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var assembly = context.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Single(x => x.Value.Name == "ProcurementPhase3");
        var migration = assembly.CreateMigration(entry.Value, context.Database.ProviderName!);
        Assert.All(migration.UpOperations, x => Assert.True(x is CreateTableOperation or CreateIndexOperation or SqlOperation));
        Assert.Equal(new[] { "PurchaseOrderHistories", "PurchaseOrderItems", "PurchaseOrders", "Suppliers" },
            migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name).Order(StringComparer.Ordinal));
        var sql = migration.UpOperations.OfType<SqlOperation>().ToArray();
        Assert.Equal(5, sql.Count(x => x.Sql.TrimStart().StartsWith("CREATE TRIGGER", StringComparison.Ordinal)));
        Assert.Single(sql, x => x.Sql.TrimStart().StartsWith("CREATE PROCEDURE dbo.TransitionPurchaseOrder", StringComparison.Ordinal));
        Assert.Throws<NotSupportedException>(() => { _ = migration.DownOperations; });
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("quantity")]
    [InlineData("cost")]
    public async Task DatabaseRejectsInvalidCommercialLines(string invalid)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var order = await ProcurementTestSupport.Orders(context, seed.Purchasing).CreateAsync(seed.Draft());
        var exception = invalid switch
        {
            "duplicate" => await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO PurchaseOrderItems(Id,PurchaseOrderId,ProductId,Quantity,UnitCost)
                VALUES({Guid.NewGuid()},{order.Id},{seed.Inventory.ProductId},1,1)
                """)),
            "quantity" => await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE PurchaseOrderItems SET Quantity=0 WHERE PurchaseOrderId={order.Id}")),
            _ => await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE PurchaseOrderItems SET UnitCost=-1 WHERE PurchaseOrderId={order.Id}"))
        };
        Assert.Contains(exception.Number, new[] { 547, 2601, 2627 });
    }

    [Fact]
    public async Task SqlCannotSkipSubmissionOrMutatePendingStateWithoutAudit()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = ProcurementTestSupport.Orders(context, seed.Purchasing);
        var order = await workflow.CreateAsync(seed.Draft());
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PurchaseOrders SET Status=2 WHERE Id={order.Id}"));
        order = await workflow.SubmitAsync(order.Id, new(order.RowVersion));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PurchaseOrders SET Status=4 WHERE Id={order.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PurchaseOrders SET Revision=Revision+1 WHERE Id={order.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PurchaseOrders SET SubmittedAt=DATEADD(day,1,SubmittedAt) WHERE Id={order.Id}"));
    }

    [Fact]
    public async Task ForgedAuditCannotAuthorizeAStateChangeEvenWithAnEventMarker()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        Guid id;
        int revision;
        await using (var setup = database.Context())
        {
            var workflow = ProcurementTestSupport.Orders(setup, seed.Purchasing);
            var order = await workflow.CreateAsync(seed.Draft());
            await workflow.SubmitAsync(order.Id, new(order.RowVersion));
            var stored = await setup.PurchaseOrders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
            (id, revision) = (order.Id, stored.Revision);
        }
        await using (var forged = database.Context())
        {
            await using var transaction = await InventoryAuthoritySession.BeginAsync(forged);
            var eventId = Guid.NewGuid();
            var error = await Assert.ThrowsAsync<SqlException>(() => forged.Database.ExecuteSqlInterpolatedAsync($"""
                EXEC sys.sp_set_session_context @key=N'ERPFlow.POTransitionEvent', @value={eventId};
                INSERT INTO PurchaseOrderHistories(Id,PurchaseOrderId,ActorId,Action,FromStatus,ToStatus,Revision,CreatedAt)
                VALUES({eventId},{id},{seed.Sales},1,1,2,{revision + 1},SYSUTCDATETIME());
                UPDATE PurchaseOrders SET Status=2,Revision=Revision+1,DecidedAt=(SELECT CreatedAt FROM PurchaseOrderHistories WHERE Id={eventId}) WHERE Id={id};
                """));
            Assert.Equal(51012, error.Number);
        }
        await using var verification = database.Context();
        Assert.Equal(ERPFlow.Domain.Procurement.PurchaseOrderStatus.PendingApproval, (await verification.PurchaseOrders.SingleAsync(x => x.Id == id)).Status);
        Assert.Single(await verification.PurchaseOrderHistories.Where(x => x.PurchaseOrderId == id).ToListAsync());
    }

    [Fact]
    public async Task SupplierCodeIsImmutableInSqlWhileMetadataEditsRemainSupported()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Suppliers SET SupplierCode=N'RENAMED' WHERE Id={seed.SupplierId}"));
        Assert.Equal(51010, error.Number);
        var masters = ProcurementTestSupport.Masters(context, seed.Purchasing);
        var supplier = await masters.GetSupplierAsync(seed.SupplierId);
        var updated = await masters.UpdateSupplierAsync(supplier.Id, new("Renamed supplier", null, null, null, null, supplier.RowVersion));
        Assert.Equal("SUP-TEST", updated.SupplierCode);
        Assert.Equal("Renamed supplier", updated.Name);
    }

    [Fact]
    public async Task DatabaseProtectsPurchaseNumberStatusAndReferencedMasterHistory()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = ProcurementTestSupport.Orders(context, seed.Purchasing);
        var order = await workflow.CreateAsync(seed.Draft());
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PurchaseOrders SET Status=99 WHERE Id={order.Id}"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM Suppliers WHERE Id={seed.SupplierId}"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM Warehouses WHERE Id={order.WarehouseId}"))).Number);
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PurchaseOrders(Id,PONumber,SupplierId,WarehouseId,CreatedByUserId,Status,CreatedAt,TotalAmount,Revision)
            VALUES({Guid.NewGuid()},{order.PONumber},{order.SupplierId},{order.WarehouseId},{order.CreatedByUserId},0,SYSUTCDATETIME(),0,1)
            """));
        Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
    }
}
