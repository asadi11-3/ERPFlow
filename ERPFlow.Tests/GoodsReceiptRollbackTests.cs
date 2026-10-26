using ERPFlow.Application.Receiving;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ERPFlow.Tests;

public sealed class GoodsReceiptRollbackTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task FaultAfterStockMovementOrFinalCommandSaveRollsBackTheEntirePost(int stage)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        GoodsReceiptResult draft;
        await using (var setup = database.Context()) draft = await GoodsReceiptTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync(seed.Draft(3));
        var command = new PostGoodsReceiptCommand("retry-post", draft.RowVersion);
        await using (var failing = database.Context(new FailAfterSave(stage)))
            await Assert.ThrowsAsync<InjectedFailure>(() => GoodsReceiptTestSupport.Workflow(failing, seed.Procurement.Operator).PostAsync(draft.Id, command));
        await using var verification = database.Context();
        var stored = await verification.GoodsReceipts.AsNoTracking().SingleAsync();
        Assert.Equal(GoodsReceiptStatus.Draft, stored.Status);
        Assert.Equal(draft.RowVersion, stored.RowVersion);
        Assert.Equal(PurchaseOrderStatus.Approved, (await verification.PurchaseOrders.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0, (await verification.PurchaseOrderItems.AsNoTracking().SingleAsync()).ReceivedQuantity);
        var stock = await verification.WarehouseStocks.AsNoTracking().SingleAsync(x => x.Id == seed.Procurement.Inventory.StockId);
        Assert.Equal((0, 0L), (stock.StockOnHand, stock.Sequence));
        Assert.Empty(await verification.StockMovements.Where(x => x.Kind == InventoryPostingKind.Receipt).ToListAsync());
        Assert.Single(await verification.GoodsReceiptCommands.ToListAsync());
        await GoodsReceiptTestSupport.Workflow(verification, seed.Procurement.Operator).PostAsync(draft.Id, command);
        Assert.Single(await verification.StockMovements.Where(x => x.Kind == InventoryPostingKind.Receipt).ToListAsync());
    }

    [Fact]
    public async Task FailedCreateRollsBackTheReceiptAdmissionAndItsIdempotencyClaim()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using (var failing = database.Context(new FailAfterSave(2)))
            await Assert.ThrowsAsync<InjectedFailure>(() => GoodsReceiptTestSupport.Workflow(failing, seed.Procurement.Operator).CreateAsync(seed.Draft(1)));
        await using var verification = database.Context();
        Assert.Empty(await verification.GoodsReceipts.ToListAsync());
        Assert.Empty(await verification.GoodsReceiptCommands.ToListAsync());
        Assert.Null((await verification.PurchaseOrders.AsNoTracking().SingleAsync()).ReceivingStartedAt);
        Assert.Equal(2, await verification.PurchaseOrderHistories.CountAsync());
        await GoodsReceiptTestSupport.Workflow(verification, seed.Procurement.Operator).CreateAsync(seed.Draft(1));
    }

    [Fact]
    public async Task SqlFailureAfterReceiptAndCountersUpdateRollsBackInventoryAndAllStates()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        GoodsReceiptResult draft;
        await using (var setup = database.Context())
        {
            draft = await GoodsReceiptTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync(seed.Draft(3));
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER TR_GoodsReceipt_TestFault ON PurchaseOrders AFTER UPDATE AS
                BEGIN
                    IF EXISTS(SELECT 1 FROM inserted WHERE Status IN (5,6)) THROW 51104, 'Injected receipt finalization failure.', 1;
                END;
                """);
        }
        await using (var failing = database.Context())
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => GoodsReceiptTestSupport.Workflow(failing, seed.Procurement.Operator).PostAsync(draft.Id, new("sql-fault", draft.RowVersion)));
            Assert.Equal(51104, error.Number);
        }
        await using var verification = database.Context();
        Assert.Equal(GoodsReceiptStatus.Draft, (await verification.GoodsReceipts.SingleAsync()).Status);
        Assert.Equal(0, (await verification.PurchaseOrderItems.SingleAsync()).ReceivedQuantity);
        Assert.Equal(0, (await verification.WarehouseStocks.SingleAsync(x => x.Id == seed.Procurement.Inventory.StockId)).StockOnHand);
        Assert.Empty(await verification.StockMovements.Where(x => x.Kind == InventoryPostingKind.Receipt).ToListAsync());
        Assert.Equal(3, await verification.PurchaseOrderHistories.CountAsync());
    }
    private sealed class InjectedFailure : Exception;
    private sealed class FailAfterSave(int stage) : SaveChangesInterceptor
    {
        private int count;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (++count == stage) throw new InjectedFailure();
            return ValueTask.FromResult(result);
        }
    }
}
