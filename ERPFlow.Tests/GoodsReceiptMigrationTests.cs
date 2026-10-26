using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ERPFlow.Tests;

public sealed class GoodsReceiptMigrationTests
{
    [Fact]
    public void MigrationAddsReceivingWithoutDroppingLegacyTablesOrColumns()
    {
        using var context = new ERPFlowDbContext(new DbContextOptionsBuilder<ERPFlowDbContext>().UseSqlServer(
            "Server=unreachable;Database=DesignOnly;Integrated Security=True").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var assembly = context.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Single(x => x.Value.Name == "GoodsReceiptPartialReceiving");
        var migration = assembly.CreateMigration(entry.Value, context.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, x => x is DropTableOperation or DropColumnOperation or AlterColumnOperation or UpdateDataOperation or DeleteDataOperation);
        Assert.Equal(new[] { "GoodsReceiptCommands", "GoodsReceiptItems", "GoodsReceipts" }, migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.Contains(migration.UpOperations.OfType<AddColumnOperation>(), x => x.Table == "PurchaseOrderItems" && x.Name == "ReceivedQuantity" && Equals(x.DefaultValue, 0));
        Assert.Contains(migration.UpOperations.OfType<SqlOperation>(), x => x.Sql.StartsWith("CREATE PROCEDURE dbo.PostGoodsReceipt", StringComparison.Ordinal));
        Assert.Throws<NotSupportedException>(() => { _ = migration.DownOperations; });
    }
    [Fact]
    public async Task UpgradeFromPhase3PreservesApprovedTermsHistoryAndProductAuthority()
    {
        await using var database = await InventoryTestDatabase.CreateAsync("20261026154834_ProcurementPhase3");
        var inventory = await database.SeedAsync();
        var supplier = Guid.NewGuid(); var po = Guid.NewGuid(); var line = Guid.NewGuid(); var approver = Guid.NewGuid();
        await using var context = database.Context();
        var before = (await context.Products.AsNoTracking().SingleAsync()).RowVersion;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Suppliers(Id,SupplierCode,Name,IsActive,CreatedAt) VALUES({supplier},N'OLD-GRN',N'Preserved supplier',1,SYSUTCDATETIME());
            INSERT INTO AppUsers(Id,Name,Email,Role,PasswordHash,CreatedAt) VALUES({approver},N'Approver',N'grn-upgrade@example.test',2,N'fixture',SYSUTCDATETIME());
            INSERT INTO PurchaseOrders(Id,PONumber,SupplierId,WarehouseId,CreatedByUserId,Status,CreatedAt,TotalAmount,Revision)
            VALUES({po},N'PO-OLD-GRN',{supplier},{inventory.WarehouseId},{inventory.ActorId},0,SYSUTCDATETIME(),12.5,1);
            INSERT INTO PurchaseOrderItems(Id,PurchaseOrderId,ProductId,Quantity,UnitCost) VALUES({line},{po},{inventory.ProductId},5,2.5);
            """);
        await using (var transaction = await InventoryAuthoritySession.BeginAsync(context))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @version binary(8), @at datetime2(7)=SYSUTCDATETIME();
                SELECT @version=RowVersion FROM PurchaseOrders WHERE Id={po};
                EXEC dbo.TransitionPurchaseOrder @Id={po},@ExpectedVersion=@version,@ActorId={inventory.ActorId},@Action=0,@Comments=NULL,@HistoryId={Guid.NewGuid()},@At=@at;
                SELECT @version=RowVersion FROM PurchaseOrders WHERE Id={po};
                EXEC dbo.TransitionPurchaseOrder @Id={po},@ExpectedVersion=@version,@ActorId={approver},@Action=1,@Comments=N'Preserved approval',@HistoryId={Guid.NewGuid()},@At=@at;
                """);
            await transaction.CommitAsync();
        }
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();
        var order = await context.PurchaseOrders.AsNoTracking().Include(x => x.Items).SingleAsync();
        Assert.Equal("PO-OLD-GRN", order.PONumber);
        Assert.Equal(12.5m, order.TotalAmount);
        Assert.Equal(0, Assert.Single(order.Items).ReceivedQuantity);
        Assert.Equal(2, await context.PurchaseOrderHistories.CountAsync());
        Assert.Equal(before, (await context.Products.AsNoTracking().SingleAsync()).RowVersion);
        Assert.Equal(InventoryAuthorityMode.Product, (await context.InventoryAuthorities.SingleAsync()).Mode);
        Assert.Empty(await context.GoodsReceipts.ToListAsync());
        Assert.Empty(await context.GoodsReceiptCommands.ToListAsync());
        Assert.Empty(await context.StockMovements.ToListAsync());
    }
}
