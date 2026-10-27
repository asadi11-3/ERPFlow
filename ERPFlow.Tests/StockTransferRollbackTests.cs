using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Transfers;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ERPFlow.Tests;

public sealed class StockTransferRollbackTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task FailureAfterEverySaveStageRollsBackBalancesPairsStatusAndReplay(int stage)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        StockTransferResult draft;
        await using (var setup = database.Context()) draft = await StockTransferTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync("failure-create", seed.Draft());
        await using (var failing = database.Context(new FailAfterSave(stage)))
            await Assert.ThrowsAsync<InjectedFailure>(() => StockTransferTestSupport.Workflow(failing, seed.Procurement.Operator).PostAsync(draft.Id, "retry-post", new(draft.RowVersion)));
        await using var verification = database.Context();
        var source = await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Source);
        var destination = await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Destination);
        Assert.Equal((10, 3, 2, 0), (source.StockOnHand, source.ReservedQuantity, destination.StockOnHand, destination.ReservedQuantity));
        var stored = await verification.StockTransfers.AsNoTracking().SingleAsync();
        Assert.Equal(StockTransferStatus.Draft, stored.Status);
        Assert.Equal(draft.RowVersion, stored.RowVersion);
        Assert.Empty(await verification.InventoryPostings.Where(x => x.StockTransferItemId != null).ToListAsync());
        Assert.Single(await verification.StockTransferCommands.ToListAsync());
        await StockTransferTestSupport.Workflow(verification, seed.Procurement.Operator).PostAsync(draft.Id, "retry-post", new(draft.RowVersion));
        await new EfInventoryReconciliation(verification).ReconcileAsync();
    }
    [Theory]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("outgoing")]
    [InlineData("incoming")]
    public async Task SqlFailureBetweenIndividualTransferLegsCannotLeavePartialEffects(string point)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        StockTransferResult draft;
        await using (var setup = database.Context())
        {
            var warehouse = await ProcurementTestSupport.Masters(setup, seed.Procurement.Inventory.ActorId).CreateWarehouseAsync(new("FAILURE-DEST", "Unprovisioned destination"));
            draft = await StockTransferTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync("sql-create", seed.Draft() with { DestinationWarehouseId = warehouse.Id });
            var sql = point switch
            {
                "source" => $"CREATE TRIGGER TR_Transfer_TestFailure ON WarehouseStocks AFTER UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE WarehouseId='{seed.Source:D}') THROW 51105,'Injected source failure.',1; END;",
                "destination" => $"CREATE TRIGGER TR_Transfer_TestFailure ON WarehouseStocks AFTER INSERT,UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE WarehouseId='{warehouse.Id:D}') THROW 51105,'Injected destination failure.',1; END;",
                "outgoing" => "CREATE TRIGGER TR_Transfer_TestFailure ON StockMovements AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Kind=5) THROW 51105,'Injected outgoing failure.',1; END;",
                _ => "CREATE TRIGGER TR_Transfer_TestFailure ON StockMovements AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Kind=6) THROW 51105,'Injected incoming failure.',1; END;"
            };
            await setup.Database.ExecuteSqlRawAsync(sql);
        }
        await using (var failing = database.Context())
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => StockTransferTestSupport.Workflow(failing, seed.Procurement.Operator)
                .PostAsync(draft.Id, "sql-retry", new(draft.RowVersion)));
            Assert.Equal(51105, Assert.IsType<SqlException>(exception.InnerException).Number);
        }
        await using var verification = database.Context();
        Assert.False(await verification.WarehouseStocks.AnyAsync(x => x.WarehouseId == draft.DestinationWarehouseId));
        Assert.Equal((10, 3), ((await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Source)).StockOnHand,
            (await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Source)).ReservedQuantity));
        Assert.Empty(await verification.InventoryPostings.Where(x => x.StockTransferItemId != null).ToListAsync());
        Assert.Equal(StockTransferStatus.Draft, (await verification.StockTransfers.SingleAsync()).Status);
        Assert.Single(await verification.StockTransferCommands.ToListAsync());
    }
    [Fact]
    public async Task FailedCreationLeavesNoDocumentOrSuccessfulKeyClaim()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        await using (var failing = database.Context(new FailAfterSave(2)))
            await Assert.ThrowsAsync<InjectedFailure>(() => StockTransferTestSupport.Workflow(failing, seed.Procurement.Operator).CreateAsync("failed-create", seed.Draft()));
        await using var verification = database.Context();
        Assert.Empty(await verification.StockTransfers.ToListAsync());
        Assert.Empty(await verification.StockTransferCommands.ToListAsync());
        await StockTransferTestSupport.Workflow(verification, seed.Procurement.Operator).CreateAsync("failed-create", seed.Draft());
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
