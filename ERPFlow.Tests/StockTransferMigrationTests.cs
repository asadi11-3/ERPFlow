using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ERPFlow.Tests;

public sealed class StockTransferMigrationTests
{
    [Fact]
    public void TransferMigrationIsAdditiveAndRefusesDestructiveDowngrade()
    {
        using var context = new ERPFlowDbContext(new DbContextOptionsBuilder<ERPFlowDbContext>().UseSqlServer("Server=unreachable;Database=DesignOnly;Integrated Security=True").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var assembly = context.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Single(x => x.Value.Name == "StockTransfersPhase5");
        var migration = assembly.CreateMigration(entry.Value, context.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, x => x is DropTableOperation or DropColumnOperation or AlterColumnOperation or DeleteDataOperation or UpdateDataOperation);
        Assert.Equal(new[] { "StockTransferCommands", "StockTransferItems", "StockTransfers" }, migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.Contains(migration.UpOperations.OfType<AddColumnOperation>(), x => x.Table == "InventoryPostings" && x.Name == "StockTransferItemId" && x.IsNullable);
        Assert.Equal(5, migration.UpOperations.OfType<SqlOperation>().Count());
        Assert.All(migration.UpOperations.OfType<SqlOperation>(), x => Assert.StartsWith("CREATE TRIGGER", x.Sql.TrimStart()));
        Assert.Throws<NotSupportedException>(() => { _ = migration.DownOperations; });
    }
    [Fact]
    public async Task UpgradeFromPhase4PreservesLegacyBalancesVersionsAndOwnership()
    {
        await using var database = await InventoryTestDatabase.CreateAsync("20261026203843_GoodsReceiptPartialReceiving");
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        var product = await context.Products.AsNoTracking().SingleAsync();
        var reservation = await context.StockReservations.AsNoTracking().SingleAsync();
        var versions = await context.WarehouseStocks.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.RowVersion);
        await context.Database.MigrateAsync(); await context.Database.MigrateAsync();
        Assert.Equal(product.RowVersion, (await context.Products.AsNoTracking().SingleAsync()).RowVersion);
        Assert.Equal((50, 6), ((await context.Products.SingleAsync()).StockOnHand, (await context.Products.SingleAsync()).ReservedQuantity));
        Assert.All(await context.WarehouseStocks.AsNoTracking().ToListAsync(), x => Assert.Equal(versions[x.Id], x.RowVersion));
        Assert.NotNull(await context.StockReservations.FindAsync(reservation.Id));
        Assert.Empty(await context.StockTransfers.ToListAsync());
        Assert.Empty(await context.StockTransferCommands.ToListAsync());
        Assert.Empty(await context.StockMovements.ToListAsync());
        Assert.Equal(InventoryAuthorityMode.Product, (await context.InventoryAuthorities.SingleAsync()).Mode);
    }
}
