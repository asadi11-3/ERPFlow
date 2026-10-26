using ERPFlow.Application.Common;
using ERPFlow.Application.Receiving;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Receiving;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ERPFlow.Tests;

public sealed class GoodsReceiptAtomicityTests
{
    [Fact]
    public async Task SecondLineFailureRollsBackEverySkuIncludingNewStockProvisioning()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        GoodsReceiptResult draft;
        Guid productId;
        await using (var setup = database.Context())
        {
            var product = new Product("New received SKU", "GRN-ATOMIC", 7m, 0);
            productId = product.Id;
            setup.Products.Add(product);
            await setup.SaveChangesAsync();
            var buyer = ProcurementTestSupport.Orders(setup, seed.Procurement.Purchasing);
            var order = await buyer.CreateAsync(new(seed.Order.SupplierId, seed.Order.WarehouseId,
                [new(seed.Procurement.Inventory.ProductId, 3, 1m), new(productId, 4, 2m)]));
            order = await buyer.SubmitAsync(order.Id, new(order.RowVersion));
            order = await ProcurementTestSupport.Orders(setup, seed.Procurement.Approver).ApproveAsync(order.Id, new(order.RowVersion));
            draft = await GoodsReceiptTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync(new("multi-create", order.Id,
                order.RowVersion, order.Items.Select(x => new GoodsReceiptLineCommand(x.Id, x.Quantity)).ToArray()));
        }
        await using (var failing = database.Context(new FailOnSecondStockSave()))
            await Assert.ThrowsAsync<InjectedFailure>(() => GoodsReceiptTestSupport.Workflow(failing, seed.Procurement.Operator)
                .PostAsync(draft.Id, new("multi-post", draft.RowVersion)));
        await using var verification = database.Context();
        Assert.Empty(await verification.StockMovements.Where(x => x.Kind == InventoryPostingKind.Receipt).ToListAsync());
        Assert.False(await verification.WarehouseStocks.AnyAsync(x => x.ProductId == productId));
        Assert.Equal(0, (await verification.WarehouseStocks.SingleAsync(x => x.Id == seed.Procurement.Inventory.StockId)).StockOnHand);
        Assert.All(await verification.PurchaseOrderItems.Where(x => x.PurchaseOrderId == draft.PurchaseOrderId).ToListAsync(), x => Assert.Equal(0, x.ReceivedQuantity));
        Assert.Equal(GoodsReceiptStatus.Draft, (await verification.GoodsReceipts.SingleAsync()).Status);
        await GoodsReceiptTestSupport.Workflow(verification, seed.Procurement.Operator).PostAsync(draft.Id, new("multi-post", draft.RowVersion));
        await new EfInventoryReconciliation(verification).ReconcileAsync();
    }

    [Fact]
    public async Task PhysicalOverflowRejectsPostWithoutAnyReceiptCounterOrLedgerChange()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database, int.MaxValue);
        await using var context = database.Context();
        var workflow = GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator);
        var maximum = await workflow.CreateAsync(seed.Draft(int.MaxValue));
        await workflow.PostAsync(maximum.Id, new("max-stock-post", maximum.RowVersion));
        var buyer = ProcurementTestSupport.Orders(context, seed.Procurement.Purchasing);
        var order = await buyer.CreateAsync(seed.Procurement.Draft(1));
        order = await buyer.SubmitAsync(order.Id, new(order.RowVersion));
        order = await ProcurementTestSupport.Orders(context, seed.Procurement.Approver).ApproveAsync(order.Id, new(order.RowVersion));
        var overflow = await workflow.CreateAsync(new("overflow-create", order.Id, order.RowVersion, [new(order.Items.Single().Id, 1)]));
        await Assert.ThrowsAsync<WorkflowValidationException>(() => workflow.PostAsync(overflow.Id, new("overflow-post", overflow.RowVersion)));
        Assert.Equal(GoodsReceiptStatus.Draft, (await workflow.GetAsync(overflow.Id)).Status);
        Assert.Equal(0, (await context.PurchaseOrderItems.AsNoTracking().SingleAsync(x => x.PurchaseOrderId == order.Id)).ReceivedQuantity);
        Assert.Single(await context.StockMovements.Where(x => x.Kind == InventoryPostingKind.Receipt).ToListAsync());
        Assert.Equal(int.MaxValue, (await context.WarehouseStocks.SingleAsync(x => x.Id == seed.Procurement.Inventory.StockId)).StockOnHand);
        await new EfInventoryReconciliation(context).ReconcileAsync();
    }
    private sealed class InjectedFailure : Exception;
    private sealed class FailOnSecondStockSave : SaveChangesInterceptor
    {
        private int count;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (++count == 3) throw new InjectedFailure();
            return ValueTask.FromResult(result);
        }
    }
}
