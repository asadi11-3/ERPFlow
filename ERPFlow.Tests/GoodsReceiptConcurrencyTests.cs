using System.Text.Json;
using ERPFlow.Application.Common;
using ERPFlow.Application.Receiving;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class GoodsReceiptConcurrencyTests
{
    [Fact]
    public async Task ConcurrentCreateWithTheSameKeyProducesOneDurableDraft()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var first = database.Context();
        await using var second = database.Context();
        var results = await Task.WhenAll(GoodsReceiptTestSupport.Workflow(first, seed.Procurement.Operator).CreateAsync(seed.Draft(4)),
            GoodsReceiptTestSupport.Workflow(second, seed.Procurement.Operator).CreateAsync(seed.Draft(4)));
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(results[1]));
        await using var verification = database.Context();
        Assert.Single(await verification.GoodsReceipts.ToListAsync());
        Assert.Single(await verification.GoodsReceiptCommands.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentPostWithTheSameKeyProducesOneMovementAndOneResponse()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        GoodsReceiptResult draft;
        await using (var setup = database.Context()) draft = await GoodsReceiptTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync(seed.Draft(4));
        await using var first = database.Context();
        await using var second = database.Context();
        var command = new PostGoodsReceiptCommand("same-post", draft.RowVersion);
        var results = await Task.WhenAll(GoodsReceiptTestSupport.Workflow(first, seed.Procurement.Operator).PostAsync(draft.Id, command),
            GoodsReceiptTestSupport.Workflow(second, seed.Procurement.Operator).PostAsync(draft.Id, command));
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(results[1]));
        await using var verification = database.Context();
        Assert.Single(await verification.StockMovements.Where(x => x.Kind == InventoryPostingKind.Receipt).ToListAsync());
        Assert.Equal(4, (await verification.PurchaseOrderItems.SingleAsync()).ReceivedQuantity);
        await new EfInventoryReconciliation(verification).ReconcileAsync();
    }

    [Theory]
    [InlineData(7, 7, 1, 7)]
    [InlineData(3, 4, 2, 7)]
    public async Task SamePoConcurrentPartialsAreSerializedAndCannotOverReceive(int firstQuantity, int secondQuantity, int successful, int received)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        GoodsReceiptResult one, two;
        await using (var setup = database.Context())
        {
            var workflow = GoodsReceiptTestSupport.Workflow(setup, seed.Procurement.Operator);
            one = await workflow.CreateAsync(seed.Draft(firstQuantity));
            two = await workflow.CreateAsync(seed.Draft(secondQuantity, "second-draft") with { PurchaseOrderRowVersion = one.PurchaseOrderRowVersion });
        }
        await using var first = database.Context();
        await using var second = database.Context();
        var outcomes = await Task.WhenAll(Post(first, one, "post-one"), Post(second, two, "post-two"));
        Assert.Equal(successful, outcomes.Count(x => x is null));
        Assert.All(outcomes.Where(x => x is not null), x => Assert.IsType<WorkflowConflictException>(x));
        await using var verification = database.Context();
        Assert.Equal(received, (await verification.PurchaseOrderItems.SingleAsync()).ReceivedQuantity);
        Assert.Equal(received, (await verification.WarehouseStocks.SingleAsync(x => x.Id == seed.Procurement.Inventory.StockId)).StockOnHand);
        Assert.Equal(successful, await verification.GoodsReceipts.CountAsync(x => x.Status == GoodsReceiptStatus.Posted));
        await new EfInventoryReconciliation(verification).ReconcileAsync();

        async Task<Exception?> Post(ERPFlowDbContext context, GoodsReceiptResult receipt, string key)
        {
            try { await GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator).PostAsync(receipt.Id, new(key, receipt.RowVersion)); return null; }
            catch (WorkflowConflictException exception) { return exception; }
        }
    }

    [Fact]
    public async Task DifferentPosReceivingIntoTheSameWarehouseProductShareOneStockLedger()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        GoodsReceiptResult one, two;
        await using (var setup = database.Context())
        {
            var buyer = ProcurementTestSupport.Orders(setup, seed.Procurement.Purchasing);
            var order = await buyer.CreateAsync(seed.Procurement.Draft(5));
            order = await buyer.SubmitAsync(order.Id, new(order.RowVersion));
            order = await ProcurementTestSupport.Orders(setup, seed.Procurement.Approver).ApproveAsync(order.Id, new(order.RowVersion));
            var workflow = GoodsReceiptTestSupport.Workflow(setup, seed.Procurement.Operator);
            one = await workflow.CreateAsync(seed.Draft(2));
            two = await workflow.CreateAsync(new("other-po-draft", order.Id, order.RowVersion, [new(order.Items.Single().Id, 3)]));
        }
        await using var first = database.Context();
        await using var second = database.Context();
        await Task.WhenAll(GoodsReceiptTestSupport.Workflow(first, seed.Procurement.Operator).PostAsync(one.Id, new("post-a", one.RowVersion)),
            GoodsReceiptTestSupport.Workflow(second, seed.Procurement.Operator).PostAsync(two.Id, new("post-b", two.RowVersion)));
        await using var verification = database.Context();
        var stock = await verification.WarehouseStocks.SingleAsync(x => x.Id == seed.Procurement.Inventory.StockId);
        Assert.Equal((5, 2L), (stock.StockOnHand, stock.Sequence));
        await new EfInventoryReconciliation(verification).ReconcileAsync();
    }
}
