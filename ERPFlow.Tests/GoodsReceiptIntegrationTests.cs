using System.Text.Json;
using ERPFlow.Application.Common;
using ERPFlow.Application.Receiving;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class GoodsReceiptIntegrationTests
{
    [Fact]
    public async Task PartialThenFullReceivingIncreasesOnlyTheExactWarehouseAndReconciles()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var source = await context.Products.AsNoTracking().SingleAsync();
        var workflow = GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator);
        var first = await workflow.CreateAsync(seed.Draft(3, reference: "Delivery-1"));
        Assert.Equal(GoodsReceiptStatus.Draft, first.Status);
        Assert.Equal(0, (await context.PurchaseOrderItems.AsNoTracking().SingleAsync()).ReceivedQuantity);
        first = await workflow.PostAsync(first.Id, new("post-first", first.RowVersion));
        Assert.Equal(GoodsReceiptStatus.Posted, first.Status);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, first.PurchaseOrderStatus);
        var second = await workflow.CreateAsync(seed.Draft(7, "create-second", "Delivery-2") with { PurchaseOrderRowVersion = first.PurchaseOrderRowVersion });
        second = await workflow.PostAsync(second.Id, new("post-second", second.RowVersion));
        Assert.Equal(PurchaseOrderStatus.Received, second.PurchaseOrderStatus);
        var line = await context.PurchaseOrderItems.AsNoTracking().SingleAsync();
        Assert.Equal((10, 0), (line.ReceivedQuantity, line.RemainingQuantity));
        var destination = await context.WarehouseStocks.AsNoTracking().SingleAsync(x => x.Id == seed.Procurement.Inventory.StockId);
        Assert.Equal((10, 0, 2L), (destination.StockOnHand, destination.ReservedQuantity, destination.Sequence));
        var archived = await context.Products.AsNoTracking().SingleAsync();
        Assert.Equal(source.RowVersion, archived.RowVersion);
        Assert.Equal((source.StockOnHand, source.ReservedQuantity, source.UnitPrice), (archived.StockOnHand, archived.ReservedQuantity, archived.UnitPrice));
        Assert.Equal(2, await context.StockMovements.CountAsync(x => x.Kind == InventoryPostingKind.Receipt));
        Assert.All(await context.StockMovements.Where(x => x.Kind == InventoryPostingKind.Receipt).ToArrayAsync(), x => Assert.Equal(0, x.ReservedDelta));
        Assert.Single(await context.StockMovements.Where(x => x.Kind == InventoryPostingKind.Opening).ToListAsync());
        await new EfInventoryReconciliation(context).ReconcileAsync();
    }

    [Fact]
    public async Task CreateAndPostReplayReturnTheirDurableOriginalResponses()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        var command = seed.Draft(4);
        GoodsReceiptResult created;
        GoodsReceiptResult posted;
        await using (var context = database.Context())
        {
            var workflow = GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator);
            created = await workflow.CreateAsync(command);
            posted = await workflow.PostAsync(created.Id, new("post-key", created.RowVersion));
        }
        await using var replay = database.Context();
        var retry = GoodsReceiptTestSupport.Workflow(replay, seed.Procurement.Operator);
        Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(await retry.CreateAsync(command)));
        Assert.Equal(JsonSerializer.Serialize(posted), JsonSerializer.Serialize(await retry.PostAsync(created.Id, new("post-key", created.RowVersion))));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => retry.CreateAsync(command with { Items = [new(command.Items.Single().PurchaseOrderItemId, 5)] }));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => retry.PostAsync(created.Id, new("post-key", posted.RowVersion)));
        Assert.Single(await replay.GoodsReceipts.ToListAsync());
        Assert.Single(await replay.StockMovements.Where(x => x.Kind == InventoryPostingKind.Receipt).ToListAsync());
        Assert.Equal(2, await replay.GoodsReceiptCommands.CountAsync());
    }
}
