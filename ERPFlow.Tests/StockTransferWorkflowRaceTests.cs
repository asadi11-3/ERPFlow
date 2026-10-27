using ERPFlow.Application.Common;
using ERPFlow.Application.Receiving;
using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class StockTransferWorkflowRaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransferRacingSalesOrServiceReservationCannotUseReservedStock(bool service)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        StockTransferResult transfer;
        Guid document;
        await using (var setup = database.Context())
        {
            transfer = await StockTransferTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync("race-transfer", seed.Draft());
            var customer = await setup.Customers.SingleAsync();
            document = service
                ? (await StockTransferTestSupport.Service(setup, seed.Procurement.Technician).CreateJobcardAsync(new(customer.Id, null, "Tech", "Transfer race", JobcardPriority.Medium))).Id
                : (await StockTransferTestSupport.Sales(setup, seed.Procurement.Inventory.ActorId).CreateDraftAsync(new(customer.Id, [new(seed.Product, 5, seed.Source)]))).Id;
        }
        var barrier = new InventoryTransactionBarrier();
        await using var mover = database.Context(barrier); await using var reserver = database.Context(barrier);
        var outcomes = await Task.WhenAll(Capture(async () => { await StockTransferTestSupport.Workflow(mover, seed.Procurement.Operator)
                .PostAsync(transfer.Id, "race-post", new(transfer.RowVersion)); }),
            Capture(async () =>
            {
                if (service) await StockTransferTestSupport.Service(reserver, seed.Procurement.Technician).RequestPartsAsync(document, new([new(seed.Product, 5, seed.Source)]));
                else await StockTransferTestSupport.Sales(reserver, seed.Procurement.Inventory.ActorId).SubmitAsync(document);
            })).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Single(outcomes, x => x is null);
        Assert.Single(outcomes, x => x is WorkflowConflictException or ConcurrencyConflictException);
        await using var verification = database.Context();
        var stock = await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Source);
        Assert.True((stock.StockOnHand == 5 && stock.ReservedQuantity == 3) || (stock.StockOnHand == 10 && stock.ReservedQuantity == 8));
        Assert.Equal(2, stock.AvailableStock);
        await new EfInventoryReconciliation(verification).ReconcileAsync();
    }
    [Fact]
    public async Task TransferRacingGoodsReceiptPreservesBothEffects()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        StockTransferResult transfer; GoodsReceiptResult receipt;
        await using (var setup = database.Context())
        {
            transfer = await StockTransferTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync("grn-transfer", seed.Draft());
            receipt = await GoodsReceiptTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync(seed.Receiving.Draft(6));
        }
        var barrier = new InventoryTransactionBarrier();
        await using var mover = database.Context(barrier); await using var receiver = database.Context(barrier);
        await Task.WhenAll(StockTransferTestSupport.Workflow(mover, seed.Procurement.Operator).PostAsync(transfer.Id, "grn-transfer-post", new(transfer.RowVersion)),
            GoodsReceiptTestSupport.Workflow(receiver, seed.Procurement.Operator).PostAsync(receipt.Id, new("grn-receive-post", receipt.RowVersion))).WaitAsync(TimeSpan.FromSeconds(60));
        await using var verification = database.Context();
        var stock = await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Source);
        var destination = await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Destination);
        Assert.Equal((11, 3, 7), (stock.StockOnHand, stock.ReservedQuantity, destination.StockOnHand));
        await new EfInventoryReconciliation(verification).ReconcileAsync();
    }
    [Fact]
    public async Task TransferAndGoodsReceiptSafelyShareMissingDestinationProvisioning()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        StockTransferResult transfer; GoodsReceiptResult receipt; Guid warehouseId;
        await using (var setup = database.Context())
        {
            warehouseId = (await ProcurementTestSupport.Masters(setup, seed.Procurement.Inventory.ActorId).CreateWarehouseAsync(new("RACE-DEST", "Concurrent destination"))).Id;
            var buyer = ProcurementTestSupport.Orders(setup, seed.Procurement.Purchasing);
            var po = await buyer.CreateAsync(seed.Procurement.Draft(4) with { WarehouseId = warehouseId });
            po = await buyer.SubmitAsync(po.Id, new(po.RowVersion));
            po = await ProcurementTestSupport.Orders(setup, seed.Procurement.Approver).ApproveAsync(po.Id, new(po.RowVersion));
            receipt = await GoodsReceiptTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync(new("race-new-grn", po.Id, po.RowVersion, [new(po.Items.Single().Id, 4)]));
            transfer = await StockTransferTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync("race-new-transfer", seed.Draft(3) with { DestinationWarehouseId = warehouseId });
        }
        var barrier = new InventoryTransactionBarrier();
        await using var mover = database.Context(barrier); await using var receiver = database.Context(barrier);
        await Task.WhenAll(StockTransferTestSupport.Workflow(mover, seed.Procurement.Operator).PostAsync(transfer.Id, "new-transfer-post", new(transfer.RowVersion)),
            GoodsReceiptTestSupport.Workflow(receiver, seed.Procurement.Operator).PostAsync(receipt.Id, new("new-receive-post", receipt.RowVersion))).WaitAsync(TimeSpan.FromSeconds(60));
        await using var verification = database.Context();
        var row = Assert.Single(await verification.WarehouseStocks.Where(x => x.WarehouseId == warehouseId && x.ProductId == seed.Product).ToListAsync());
        Assert.Equal((7, 0, 2L), (row.StockOnHand, row.ReservedQuantity, row.Sequence));
        Assert.DoesNotContain(await verification.StockMovements.Where(x => x.WarehouseStockId == row.Id).ToListAsync(), x => x.Kind == InventoryPostingKind.Opening);
        await new EfInventoryReconciliation(verification).ReconcileAsync();
    }
    private static async Task<Exception?> Capture(Func<Task> operation)
    {
        try { await operation(); return null; }
        catch (WorkflowConflictException ex) { return ex; }
        catch (ConcurrencyConflictException ex) { return ex; }
    }
}
