using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ERPFlow.Tests;

public sealed class InventorySchemaIntegrationTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(5, -1)]
    [InlineData(5, 6)]
    public async Task SqlRejectsInvalidWarehouseStockBalances(int onHand, int reserved)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE WarehouseStocks SET StockOnHand={onHand}, ReservedQuantity={reserved} WHERE Id={seed.StockId}"));
        Assert.Equal(547, exception.Number);
        var product = await context.Products.SingleAsync();
        Assert.Equal(50, product.StockOnHand);
        Assert.Equal(6, product.ReservedQuantity);
    }

    [Fact]
    public async Task SqlRejectsDuplicateStockKeyAndInvalidReservationOwnership()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO WarehouseStocks(Id,WarehouseId,ProductId,StockOnHand,ReservedQuantity,Sequence) VALUES({Guid.NewGuid()},{seed.WarehouseId},{seed.ProductId},0,0,0)"));
        Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
        var owner = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO InventoryReservations(Id,WarehouseStockId,ProductId,Quantity,SalesOrderId,JobcardItemId,Status,CreatedAt) VALUES({Guid.NewGuid()},{seed.StockId},{seed.ProductId},3,{seed.SalesOrderId},{seed.JobcardItemId},0,SYSUTCDATETIME())"));
        Assert.Equal(547, owner.Number);
    }

    [Fact]
    public async Task AdditiveMigrationPreservesExistingWorkflowRecordsAndBalances()
    {
        await using var database = await InventoryTestDatabase.CreateAsync(InventoryTestDatabase.LegacyMigration);
        Guid productId;
        Guid orderId;
        Guid reservationId;
        Guid jobcardId;
        Guid deliveryId;
        Guid assetId;
        Guid historyId;
        byte[] version;
        await using (var context = database.Context())
        {
            var actor = new AppUser("Admin", "legacy@example.test", UserRole.Administrator, "unchanged-hash");
            var customer = new Customer("Legacy", "legacy@example.test");
            var product = new Product("Legacy product", "LEGACY", 15m, 20);
            var pending = new SalesOrder(customer.Id, actor.Id);
            pending.AddItem(product, 4);
            pending.Submit([product]);
            var shipped = new SalesOrder(customer.Id, actor.Id);
            shipped.AddItem(product, 2);
            shipped.Submit([product]);
            var history = shipped.Approve(actor.Id, "Keep this history");
            shipped.Dispatch([product]);
            var delivery = new DeliveryNote(shipped.Id, customer.Id, "LEGACY-DN", "Carrier");
            var asset = new MachineAsset("LEGACY-TAG", "LEGACY-SERIAL", product.Id, customer.Id, shipped.Id, DateTime.UtcNow.AddYears(1));
            var jobcard = new Jobcard("LEGACY-JC", customer.Id, asset.Id, "Tech", "Keep fault");
            product.ReserveQuantity(3);
            jobcard.AddPart(product, 3);
            context.AddRange(actor, customer, product, pending, shipped, history, delivery, asset, jobcard);
            await context.SaveChangesAsync();
            (productId, orderId, reservationId, jobcardId, deliveryId, assetId, historyId) =
                (product.Id, pending.Id, pending.Reservations[0].Id, jobcard.Id, delivery.Id, asset.Id, history.Id);
            version = product.RowVersion.ToArray();
        }
        await using (var context = database.Context())
        {
            await context.Database.MigrateAsync();
            await context.Database.MigrateAsync();
            var product = await context.Products.SingleAsync(x => x.Id == productId);
            Assert.Equal(18, product.StockOnHand);
            Assert.Equal(7, product.ReservedQuantity);
            Assert.Equal(version, product.RowVersion);
            Assert.Equal("unchanged-hash", (await context.AppUsers.SingleAsync()).PasswordHash);
            Assert.Equal(OrderStatus.PendingApproval, (await context.SalesOrders.SingleAsync(x => x.Id == orderId)).Status);
            Assert.False((await context.StockReservations.SingleAsync(x => x.Id == reservationId)).IsReleased);
            Assert.Equal(1, await context.JobcardItems.CountAsync(x => x.JobcardId == jobcardId));
            Assert.NotNull(await context.DeliveryNotes.FindAsync(deliveryId));
            Assert.NotNull(await context.MachineAssets.FindAsync(assetId));
            Assert.Equal("Keep this history", (await context.ApprovalHistories.FindAsync(historyId))!.Comments);
            Assert.Empty(await context.Warehouses.ToListAsync());
            Assert.Empty(await context.WarehouseStocks.ToListAsync());
            Assert.Empty(await context.InventoryReservations.ToListAsync());
            Assert.Empty(await context.InventoryPostings.ToListAsync());
            Assert.Empty(await context.StockMovements.ToListAsync());
        }
    }

    [Fact]
    public async Task FoundationMigrationContainsNoLegacyTableOrDataChanges()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        await using var context = database.Context();
        var migrations = context.GetService<IMigrationsAssembly>().Migrations;
        var entry = migrations.Single(x => x.Value.Name == "InventoryFoundation");
        var migration = context.GetService<IMigrationsAssembly>().CreateMigration(entry.Value, context.Database.ProviderName!);
        Assert.All(migration.UpOperations, operation => Assert.True(operation is
            Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation or
            Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation));
        var tables = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>().Select(x => x.Name);
        Assert.Equal(new[] { "InventoryPostings", "InventoryReservations", "StockMovements", "WarehouseStocks", "Warehouses" }
            .Order(StringComparer.Ordinal), tables.Order(StringComparer.Ordinal));
    }
}
