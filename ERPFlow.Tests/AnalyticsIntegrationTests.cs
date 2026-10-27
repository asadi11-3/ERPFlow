using ERPFlow.Application.Analytics;
using ERPFlow.Infrastructure.Persistence;
using Xunit;

namespace ERPFlow.Tests;

public sealed class AnalyticsIntegrationTests
{
    [Fact]
    public async Task PostedOperationsReconcileWithoutCountingTransferAsNetStockGrowth()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        var actor = seed.Procurement.Inventory.ActorId;
        await using (var context = database.Context())
        {
            var receipts = GoodsReceiptTestSupport.Workflow(context, actor);
            var receipt = await receipts.CreateAsync(seed.Receiving.Draft(4));
            await receipts.PostAsync(receipt.Id, new("analytics-post-receipt", receipt.RowVersion));
            var transfers = StockTransferTestSupport.Workflow(context, actor);
            var transfer = await transfers.CreateAsync("analytics-create-transfer", seed.Draft(2));
            await transfers.PostAsync(transfer.Id, "analytics-post-transfer", new(transfer.RowVersion));
            await transfers.CreateAsync("analytics-draft-transfer", seed.Draft(1));
        }
        await using var read = database.Context();
        var analytics = new EfAnalyticsQueries(read, new InventoryTestDatabase.TestActor(actor));
        var query = new AnalyticsQuery();
        var receiving = await analytics.ReceivingAsync(query);
        Assert.Equal(1, receiving.Posted.Sum(point => point.Count));
        Assert.Equal(4, receiving.Posted.Sum(point => point.Quantity));
        var transfersResult = await analytics.TransfersAsync(query);
        Assert.Equal(1, transfersResult.Posted.Sum(point => point.Count));
        Assert.Equal(2, transfersResult.Posted.Sum(point => point.Quantity));
        var inventory = await analytics.InventoryAsync(query);
        Assert.Equal(16, inventory.ByWarehouse.Where(row => row.WarehouseId == seed.Source || row.WarehouseId == seed.Destination).Sum(row => row.StockOnHand));
        Assert.All(inventory.ByWarehouse, row => Assert.Equal(row.StockOnHand - row.ReservedQuantity, row.AvailableQuantity));
        Assert.Contains(inventory.MovementsByKind, row => row.Label == "TransferOut" && row.Count == 1);
        Assert.Contains(inventory.MovementsByKind, row => row.Label == "TransferIn" && row.Count == 1);
        var procurement = await analytics.ProcurementAsync(query);
        Assert.Equal(1, procurement.OrderCount);
        Assert.Equal(10, procurement.OrderedQuantity);
        Assert.Equal(4, procurement.ReceivedQuantity);
        Assert.Equal(procurement.CommittedOrderValue, Assert.Single(procurement.TopSuppliers).Amount);
        var approvals = await analytics.ApprovalsAsync(query);
        Assert.Equal(1, approvals.PurchaseActivity.Sum(point => point.Submitted));
        Assert.Equal(1, approvals.PurchaseActivity.Sum(point => point.Approved));
    }

    [Fact]
    public void CalendarBucketsHandlePartialMonthsAndDateLimits()
    {
        var window = AnalyticsWindow.Create(new(new(2026, 1, 31), new(2026, 3, 2), "month"), DateTime.UtcNow);
        Assert.Equal(new[] { new DateOnly(2026, 1, 1), new(2026, 2, 1), new(2026, 3, 1) }, window.Buckets());
        var earliest = AnalyticsWindow.Create(new(To: new(1, 1, 2)), DateTime.UtcNow);
        Assert.Equal(DateOnly.MinValue, earliest.From);
        Assert.Single(earliest.Buckets());
        Assert.Single(new AnalyticsWindow(new(9999, 12, 30), DateOnly.MaxValue, "day").Buckets());
    }
}
