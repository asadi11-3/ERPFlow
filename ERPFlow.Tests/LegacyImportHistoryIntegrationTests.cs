using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed partial class LegacyImportPreparationIntegrationTests
{
    [Fact]
    public async Task ClosedRemovedProductReservationsArePreservedAndNotReplayed()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        Guid closedId;
        Guid historyId;
        var preparationId = Guid.NewGuid();
        await using (var context = database.Context())
        {
            var order = (await context.SalesOrders.Include(x => x.Items).Include(x => x.Reservations)
                .SingleAsync(x => x.Id == seed.SalesOrderId));
            var product = await context.Products.SingleAsync();
            closedId = order.Reservations[0].Id;
            var history = order.ReturnForCorrection(seed.ActorId, [product], "Change product");
            historyId = history.Id;
            context.ApprovalHistories.Add(history);
            var replacement = new Product("Replacement", "PREP-REPLACEMENT", 2m, 10);
            context.Products.Add(replacement);
            order.EditDraft(order.CustomerId, [(replacement, 2)]);
            order.Submit([replacement]);
            await context.SaveChangesAsync();
            Assert.Equal(closedId, (await context.StockReservations.SingleAsync(x => x.Id == closedId)).Id);
            var service = Service(context, seed.ActorId);
            Assert.True((await service.PreflightAsync()).CanPrepare);
            await service.PrepareAsync(preparationId);
        }
        await using var verification = database.Context();
        var closed = await verification.LegacyInventoryReservationImports.SingleAsync(x => x.BatchId == preparationId && x.SourceId == closedId);
        Assert.Equal(closedId, closed.TargetReservationId);
        Assert.Equal(seed.ProductId, closed.ProductId);
        Assert.True(closed.IsHistorical);
        Assert.Equal(LegacyReservationDisposition.Released, closed.Disposition);
        Assert.NotNull(await verification.ApprovalHistories.FindAsync(historyId));
        Assert.Equal(2, await verification.StockReservations.CountAsync());
        Assert.Empty(await verification.InventoryReservations.ToListAsync());
        Assert.Empty(await verification.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task AmbiguousTerminalJobcardIsStagedAsUnknownWithoutNewStockEffects()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE Jobcards SET Status=4 WHERE Id=(SELECT JobcardId FROM JobcardItems WHERE Id={seed.JobcardItemId})");
        var service = Service(context, seed.ActorId);
        Assert.Contains((await service.PreflightAsync()).Issues, x => x.Code == "ReservationTotalMismatch");
        (await context.Products.SingleAsync()).ReleaseReservedQuantity(3);
        await context.SaveChangesAsync();
        var id = Guid.NewGuid();
        await service.PrepareAsync(id);
        var part = await context.LegacyInventoryReservationImports.SingleAsync(x => x.BatchId == id && x.SourceKind == LegacyReservationSource.JobcardItem);
        Assert.Equal(LegacyReservationDisposition.LegacyClosedUnknown, part.Disposition);
        Assert.Null(part.SourceCreatedAt);
        Assert.Equal(3, (await context.LegacyInventoryOpenings.SingleAsync()).ReservedQuantity);
        Assert.Empty(await context.InventoryPostings.ToListAsync());
    }

    [Fact]
    public async Task ExplicitNonDefaultLegacyOriginBlocksPreparationRatherThanRelocatingStock()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        var line = await context.SalesOrderItems.FirstAsync(x => x.SalesOrderId == seed.SalesOrderId);
        context.Entry(line).Property(x => x.WarehouseId).CurrentValue = seed.OtherWarehouseId;
        await context.SaveChangesAsync();
        Assert.Contains((await Service(context, seed.ActorId).PreflightAsync()).Issues, x => x.Code == "UnsupportedWarehouseOrigin");
        await Assert.ThrowsAsync<WorkflowConflictException>(() => Service(context, seed.ActorId).PrepareAsync(Guid.NewGuid()));
        Assert.Empty(await context.LegacyInventoryImportBatches.ToListAsync());
    }

    [Fact]
    public async Task ExistingWarehousePostingActivityBlocksLegacyPreparation()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        await InventoryTestDatabase.Poster(context, seed.ActorId).PostAsync(seed.Opening());
        Assert.Contains((await Service(context, seed.ActorId).PreflightAsync()).Issues, x => x.Code == "WarehouseActivityExists");
        await Assert.ThrowsAsync<WorkflowConflictException>(() => Service(context, seed.ActorId).PrepareAsync(Guid.NewGuid()));
        Assert.Empty(await context.LegacyInventoryImportBatches.ToListAsync());
    }

    [Fact]
    public async Task PreparedSnapshotsCannotBeChangedThroughOrdinaryEfSaves()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        var result = await Service(context, seed.ActorId).PrepareAsync(Guid.NewGuid());
        var opening = await context.LegacyInventoryOpenings.SingleAsync(x => x.BatchId == result.PreparationId);
        context.Entry(opening).Property(x => x.StockOnHand).CurrentValue = 999;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        Assert.Equal(50, (await context.LegacyInventoryOpenings.SingleAsync()).StockOnHand);
    }
}
