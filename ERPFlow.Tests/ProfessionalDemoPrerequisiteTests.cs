using ERPFlow.Application.Procurement;
using ERPFlow.Application.Receiving;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using ERPFlow.Domain.Transfers;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProfessionalDemoPrerequisiteTests
{
    [Fact]
    public async Task HistoricalScopeRejectsFutureBusinessTime()
    {
        var invoked = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProfessionalDemoSeeder.RunAtAsync(
            TimeProvider.System.GetUtcNow().AddDays(1), () =>
            {
                invoked = true;
                return Task.CompletedTask;
            }));

        Assert.False(invoked);
    }

    [Fact]
    public async Task HistoricalScopeControlsBusinessDatesAndRestoresSystemUtc()
    {
        var before = DateTime.UtcNow;
        Product? historical = null;
        PurchaseOrder? order = null;
        GoodsReceipt? receipt = null;
        StockTransfer? transfer = null;
        Jobcard? jobcard = null;
        InventoryPosting? posting = null;

        await ProfessionalDemoSeeder.RunAtAsync(new DateTimeOffset(2026, 9, 8, 9, 30, 0, TimeSpan.Zero), () =>
        {
            historical = new Product("Historical product", "HIST-001", 10m, 0);
            order = new PurchaseOrder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                [(historical.Id, 1, 7m)]);
            receipt = new GoodsReceipt(order.Id, order.SupplierId, order.WarehouseId, order.CreatedByUserId,
                [(Guid.NewGuid(), historical.Id, 1)]);
            transfer = new StockTransfer(Guid.NewGuid(), Guid.NewGuid(), order.CreatedByUserId,
                [(historical.Id, 1)]);
            jobcard = new Jobcard("HIST-SRV", Guid.NewGuid(), null, "Technician", "Historical fault");
            posting = new InventoryPosting(order.CreatedByUserId, "historical-posting", new byte[32],
                InventoryPostingKind.Receipt, null, Guid.NewGuid());
            return Task.CompletedTask;
        });
        var ordinary = new Product("Current product", "CURRENT-001", 10m, 0);

        Assert.Equal(new DateTime(2026, 9, 8, 9, 30, 0, DateTimeKind.Utc), historical!.CreatedAt);
        Assert.StartsWith("PO-20260908-", order!.PONumber);
        Assert.StartsWith("GRN-20260908-", receipt!.Number);
        Assert.StartsWith("TRF-20260908-", transfer!.Number);
        Assert.Equal(historical.CreatedAt, order.CreatedAt);
        Assert.Equal(historical.CreatedAt, receipt.CreatedAt);
        Assert.Equal(historical.CreatedAt, transfer.CreatedAt);
        Assert.Equal(historical.CreatedAt, jobcard!.CreatedAt);
        Assert.Equal(historical.CreatedAt, posting!.CreatedAt);
        Assert.InRange(ordinary.CreatedAt, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task ZeroStockReceiptsProvisionTwoExactWarehousesThroughTheLedger()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        Seed seed = null!;
        await ProfessionalDemoSeeder.RunAtAsync(Utc(2026, 9, 1, 8), async () => seed = await SeedMastersAsync(database));

        await ProfessionalDemoSeeder.RunAtAsync(Utc(2026, 9, 2, 8), async () =>
        {
            var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.Administrator);
            await using var context = database.Context();
            await InventoryCutoverTestSupport.Cutover(context, seed.Administrator).RehearseAsync(approval);
        });

        await ReceiveAsync(database, seed, seed.CentralWarehouse, 7, Utc(2026, 9, 8, 9), "central");
        await ReceiveAsync(database, seed, seed.ServiceWarehouse, 4, Utc(2026, 9, 10, 11), "service");

        await using var verification = database.Context();
        var stocks = await verification.WarehouseStocks.AsNoTracking()
            .Where(x => x.ProductId == seed.Product).OrderBy(x => x.WarehouseId).ToArrayAsync();
        var receipts = await verification.GoodsReceipts.Include(x => x.Items).OrderBy(x => x.CreatedAt).ToArrayAsync();
        var movements = await verification.StockMovements.AsNoTracking()
            .Where(x => x.Kind == InventoryPostingKind.Receipt).OrderBy(x => x.Sequence).ToArrayAsync();
        var product = await verification.Products.AsNoTracking().SingleAsync(x => x.Id == seed.Product);

        Assert.Equal(3, stocks.Length);
        Assert.All(stocks, stock => Assert.True(stock.StockOnHand >= 0 && stock.ReservedQuantity >= 0));
        Assert.Equal(7, stocks.Single(x => x.WarehouseId == seed.CentralWarehouse).StockOnHand);
        Assert.Equal(4, stocks.Single(x => x.WarehouseId == seed.ServiceWarehouse).StockOnHand);
        Assert.Equal(0, stocks.Sum(x => x.ReservedQuantity));
        Assert.Equal(2, receipts.Length);
        Assert.Equal(2, movements.Length);
        Assert.Equal(2, movements.Select(x => x.PostingId).Distinct().Count());
        Assert.All(movements, movement => Assert.True(movement.BeforeOnHand == 0 && movement.PhysicalDelta > 0));
        Assert.Equal(0, product.StockOnHand);
        Assert.Equal(0, product.ReservedQuantity);
        var postedItems = await verification.InventoryPostings.Where(x => x.Kind == InventoryPostingKind.Receipt)
            .Select(x => x.GoodsReceiptItemId!.Value).ToArrayAsync();
        Assert.Equal(receipts.SelectMany(x => x.Items).Select(x => x.Id).Order(), postedItems.Order());
    }

    private static async Task<Seed> SeedMastersAsync(InventoryTestDatabase database)
    {
        await using var context = database.Context();
        var administrator = new AppUser("Administrator", "demo-admin@example.test", UserRole.Administrator, "hash");
        var purchasing = new AppUser("Purchasing", "demo-purchasing@example.test", UserRole.Purchasing, "hash");
        var approver = new AppUser("Approver", "demo-approver@example.test", UserRole.Approver, "hash");
        var warehouse = new AppUser("Warehouse", "demo-warehouse@example.test", UserRole.WarehouseOperator, "hash");
        var product = new Product("Zero stock printer", "ZERO-PRN-001", 120m, 0);
        var supplier = new Supplier("DEMO-SUP", "Demo Supplier");
        var central = new Warehouse("DEMO-CENTRAL", "Demo Central");
        var service = new Warehouse("DEMO-SERVICE", "Demo Service");
        context.AddRange(administrator, purchasing, approver, warehouse, product, supplier, central, service);
        await context.SaveChangesAsync();
        return new(administrator.Id, purchasing.Id, approver.Id, warehouse.Id, product.Id, supplier.Id, central.Id, service.Id);
    }

    private static async Task ReceiveAsync(InventoryTestDatabase database, Seed seed, Guid warehouseId,
        int quantity, DateTimeOffset at, string key)
    {
        await ProfessionalDemoSeeder.RunAtAsync(at, async () =>
        {
            PurchaseOrderResult order;
            await using (var context = database.Context())
            {
                var buyer = ProcurementTestSupport.Orders(context, seed.Purchasing);
                order = await buyer.CreateAsync(new(seed.Supplier, warehouseId, [new(seed.Product, quantity, 80m)]));
                order = await buyer.SubmitAsync(order.Id, new(order.RowVersion));
                order = await ProcurementTestSupport.Orders(context, seed.Approver).ApproveAsync(order.Id, new(order.RowVersion));
            }
            await using var receiving = database.Context();
            var workflow = GoodsReceiptTestSupport.Workflow(receiving, seed.WarehouseOperator);
            var receipt = await workflow.CreateAsync(new($"create-{key}", order.Id, order.RowVersion,
                [new(order.Items.Single().Id, quantity)], $"SUP-{key.ToUpperInvariant()}"));
            Assert.StartsWith($"PO-{at:yyyyMMdd}-", order.PONumber);
            Assert.StartsWith($"GRN-{at:yyyyMMdd}-", receipt.Number);
            receipt = await workflow.PostAsync(receipt.Id, new($"post-{key}", receipt.RowVersion));
            Assert.Equal(quantity, receipt.Items.Single().Quantity);
            Assert.Equal(PurchaseOrderStatus.Received, receipt.PurchaseOrderStatus);
        });
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private sealed record Seed(Guid Administrator, Guid Purchasing, Guid Approver, Guid WarehouseOperator,
        Guid Product, Guid Supplier, Guid CentralWarehouse, Guid ServiceWarehouse);
}
