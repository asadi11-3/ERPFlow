using System.Text.Json;
using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Transfers;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class StockTransferIntegrationTests
{
    [Fact]
    public async Task TransferDebitsOnlyAvailableStockAndPostsAConservedPair()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var before = await context.Products.AsNoTracking().SingleAsync();
        var workflow = StockTransferTestSupport.Workflow(context, seed.Procurement.Operator);
        var draft = await workflow.CreateAsync("create-transfer", seed.Draft());
        Assert.Equal(StockTransferStatus.Draft, draft.Status);
        Assert.Equal(10, (await context.WarehouseStocks.AsNoTracking().SingleAsync(x => x.WarehouseId == seed.Source)).StockOnHand);
        var posted = await workflow.PostAsync(draft.Id, "post-transfer", new(draft.RowVersion));
        Assert.Equal(StockTransferStatus.Posted, posted.Status);
        var source = await context.WarehouseStocks.AsNoTracking().SingleAsync(x => x.WarehouseId == seed.Source);
        var destination = await context.WarehouseStocks.AsNoTracking().SingleAsync(x => x.WarehouseId == seed.Destination);
        Assert.Equal((5, 3, 2), (source.StockOnHand, source.ReservedQuantity, source.AvailableStock));
        Assert.Equal((7, 0, 7), (destination.StockOnHand, destination.ReservedQuantity, destination.AvailableStock));
        var line = Assert.Single(posted.Items);
        var movements = await context.StockMovements.Where(x => x.Id == line.SourceMovementId || x.Id == line.DestinationMovementId).ToArrayAsync();
        Assert.Equal(2, movements.Length);
        Assert.Equal(0, movements.Sum(x => (long)x.PhysicalDelta));
        Assert.All(movements, x => Assert.Equal(0, x.ReservedDelta));
        Assert.Contains(movements, x => x.Kind == InventoryPostingKind.TransferOut && x.PhysicalDelta == -5);
        Assert.Contains(movements, x => x.Kind == InventoryPostingKind.TransferIn && x.PhysicalDelta == 5);
        var archived = await context.Products.AsNoTracking().SingleAsync();
        Assert.Equal(before.RowVersion, archived.RowVersion);
        Assert.Equal((before.StockOnHand, before.ReservedQuantity, before.UnitPrice), (archived.StockOnHand, archived.ReservedQuantity, archived.UnitPrice));
        await new EfInventoryReconciliation(context).ReconcileAsync();
    }

    [Fact]
    public async Task CreationAndPostReplayAreDurableAndDoNotRepeatThePhysicalEffects()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        StockTransferResult draft, posted;
        await using (var context = database.Context())
        {
            var workflow = StockTransferTestSupport.Workflow(context, seed.Procurement.Operator);
            draft = await workflow.CreateAsync("create-key", seed.Draft());
            posted = await workflow.PostAsync(draft.Id, "post-key", new(draft.RowVersion));
        }
        await using var retry = database.Context();
        var replay = StockTransferTestSupport.Workflow(retry, seed.Procurement.Operator);
        Assert.Equal(JsonSerializer.Serialize(draft), JsonSerializer.Serialize(await replay.CreateAsync("create-key", seed.Draft())));
        Assert.Equal(JsonSerializer.Serialize(posted), JsonSerializer.Serialize(await replay.PostAsync(draft.Id, "post-key", new(draft.RowVersion))));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => replay.CreateAsync("create-key", seed.Draft(6)));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => replay.PostAsync(draft.Id, "different-post", new(posted.RowVersion)));
        Assert.Equal(2, await retry.InventoryPostings.CountAsync(x => x.StockTransferItemId != null));
        Assert.Equal(2, await retry.StockTransferCommands.CountAsync());
    }
    [Fact]
    public async Task MissingDestinationIsProvisionedWithTransferInNotOpening()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var warehouse = await ProcurementTestSupport.Masters(context, seed.Procurement.Inventory.ActorId).CreateWarehouseAsync(new("TRANSFER-NEW", "New destination"));
        var workflow = StockTransferTestSupport.Workflow(context, seed.Procurement.Operator);
        var draft = await workflow.CreateAsync("new-dest", seed.Draft(2) with { DestinationWarehouseId = warehouse.Id });
        await workflow.PostAsync(draft.Id, "post-new-dest", new(draft.RowVersion));
        var row = await context.WarehouseStocks.SingleAsync(x => x.WarehouseId == warehouse.Id && x.ProductId == seed.Product);
        Assert.Equal((2, 0, 1L), (row.StockOnHand, row.ReservedQuantity, row.Sequence));
        Assert.Equal(InventoryPostingKind.TransferIn, (await context.StockMovements.SingleAsync(x => x.WarehouseStockId == row.Id)).Kind);
        await new EfInventoryReconciliation(context).ReconcileAsync();
    }
}
