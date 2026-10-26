using ERPFlow.Application.Common;
using ERPFlow.Application.Equipment;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Application.Service;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class InventoryCutoverRuntimeTests
{
    private static SalesOrderWorkflow Sales(ERPFlowDbContext context, Guid actor) => new(
        new EfSalesOrderPersistence(context), new InventoryTestDatabase.TestActor(actor),
        new ProductAuthorityWarehouseSourcePolicy(context), InventoryCutoverTestSupport.Workflow(context, actor));
    private static ServiceWorkflow Service(ERPFlowDbContext context, Guid actor) => new(
        new EfServicePersistence(context), new InventoryTestDatabase.TestActor(actor),
        new ProductAuthorityWarehouseSourcePolicy(context), InventoryCutoverTestSupport.Workflow(context, actor));
    private static EquipmentWorkflow Equipment(ERPFlowDbContext context, Guid actor) => new(
        new EfEquipmentPersistence(context), new InventoryTestDatabase.TestActor(actor),
        new ProductAuthorityWarehouseSourcePolicy(context), InventoryCutoverTestSupport.Workflow(context, actor));

    [Fact]
    public async Task ConcurrentSalesServiceAndDispatchUseOneWarehouseAuthority()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        Guid jobcardId;
        await using (var setup = database.Context())
        {
            var order = await setup.SalesOrders.Include(x => x.Items).Include(x => x.Reservations).SingleAsync(x => x.Id == seed.SalesOrderId);
            setup.ApprovalHistories.Add(order.Approve(seed.ActorId));
            await setup.SaveChangesAsync();
            jobcardId = (await setup.JobcardItems.SingleAsync()).JobcardId;
        }
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var context = database.Context())
            await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        await Task.WhenAll(
            Retry(async context => { await Sales(context, seed.ActorId).SubmitAsync(seed.OtherOrderId); }),
            Retry(async context => { await Service(context, seed.ActorId).CompleteJobcardAsync(jobcardId, new("Complete", null)); }),
            Retry(async context => { await Equipment(context, seed.ActorId).DispatchOrderAsync(seed.SalesOrderId, new("Carrier", null, null)); }));
        await using var verification = database.Context();
        await new EfInventoryReconciliation(verification).ReconcileAsync();
        var stock = await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == LegacyDefaultWarehouse.Id);
        Assert.Equal(44, stock.StockOnHand);
        Assert.Equal(3, stock.ReservedQuantity);
        Assert.Equal(4, await verification.StockMovements.CountAsync());
        Assert.Single(await verification.DeliveryNotes.ToListAsync());
        Assert.Equal(3, await verification.MachineAssets.CountAsync());
        Assert.Equal(50, (await verification.Products.SingleAsync()).StockOnHand);
        Assert.Equal(6, (await verification.Products.SingleAsync()).ReservedQuantity);

        async Task Retry(Func<ERPFlowDbContext, Task> operation)
        {
            for (var attempt = 0; ; attempt++)
            {
                await using var context = database.Context();
                try { await operation(context); return; }
                catch (ConcurrencyConflictException) when (attempt < 3) { continue; }
            }
        }
    }

    [Fact]
    public async Task NonDefaultSalesDispatchAndServiceNeverUseDefaultStockAsFallback()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var context = database.Context())
            await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        await using (var setup = database.Context())
        {
            await using var transaction = await InventoryAuthoritySession.BeginAsync(setup);
            var stock = await setup.WarehouseStocks.SingleAsync(x => x.Id == seed.OtherStockId);
            await new EfInventoryPosting(setup, new InventoryCutoverTestSupport.WarehouseGate(setup), new InventoryTestDatabase.TestActor(seed.ActorId))
                .PostAsync(new("isolated-nondefault-opening", InventoryPostingKind.Opening, seed.OtherWarehouseId,
                    seed.ProductId, 2, stock.RowVersion));
            await transaction.CommitAsync();
        }
        await using (var context = database.Context())
        {
            var customer = await context.Customers.SingleAsync();
            var tooMuch = await Sales(context, seed.ActorId).CreateDraftAsync(new(customer.Id,
                [new(seed.ProductId, 3, seed.OtherWarehouseId)]));
            await Assert.ThrowsAsync<WorkflowConflictException>(() => Sales(context, seed.ActorId).SubmitAsync(tooMuch.Id));
        }
        Guid orderId;
        await using (var context = database.Context())
        {
            var customer = await context.Customers.SingleAsync();
            var sales = Sales(context, seed.ActorId);
            var order = await sales.CreateDraftAsync(new(customer.Id, [new(seed.ProductId, 1, seed.OtherWarehouseId)]));
            orderId = order.Id;
            await sales.SubmitAsync(orderId);
            await sales.ApproveAsync(orderId);
            await Equipment(context, seed.ActorId).DispatchOrderAsync(orderId, new("Carrier", null, null));
        }
        await using (var context = database.Context())
        {
            var customer = await context.Customers.SingleAsync();
            var service = Service(context, seed.ActorId);
            var jobcard = await service.CreateJobcardAsync(new(customer.Id, null, "Tech", "Other warehouse", ERPFlow.Domain.Enums.JobcardPriority.Medium));
            await service.RequestPartsAsync(jobcard.Id, new([new(seed.ProductId, 1, seed.OtherWarehouseId)]));
            await service.CompleteJobcardAsync(jobcard.Id, new("Complete", null));
        }
        await using var verification = database.Context();
        await new EfInventoryReconciliation(verification).ReconcileAsync();
        var source = await verification.WarehouseStocks.SingleAsync(x => x.Id == seed.OtherStockId);
        Assert.Equal(0, source.StockOnHand);
        Assert.Equal(0, source.ReservedQuantity);
        var untouched = await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == LegacyDefaultWarehouse.Id);
        Assert.Equal(50, untouched.StockOnHand);
        Assert.Equal(6, untouched.ReservedQuantity);
        Assert.Equal(seed.OtherWarehouseId, (await verification.StockReservations.SingleAsync(x => x.SalesOrderId == orderId)).WarehouseId);
    }

    [Fact]
    public async Task HistoricalUnknownClosuresStayClosedWithoutFabricatedConsumption()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        Guid historicalId;
        await using (var context = database.Context())
        {
            var product = await context.Products.SingleAsync();
            var order = await context.SalesOrders.Include(x => x.Items).Include(x => x.Reservations).SingleAsync(x => x.Id == seed.SalesOrderId);
            historicalId = order.Reservations[0].Id;
            context.ApprovalHistories.Add(order.ReturnForCorrection(seed.ActorId, [product], "Historical correction"));
            var jobcard = await context.Jobcards.Include(x => x.Items).SingleAsync();
            jobcard.Complete("Historical completion");
            product.ConsumeReservedQuantity(3);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE StockReservations SET ClosureKind=NULL WHERE Id={historicalId}");
        }
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using var verification = database.Context();
        await InventoryCutoverTestSupport.Cutover(verification, seed.ActorId).RehearseAsync(approval);
        Assert.Equal(InventoryReservationStatus.LegacyClosedUnknown, (await verification.InventoryReservations.SingleAsync(x => x.Id == historicalId)).Status);
        Assert.All(await verification.InventoryReservations.ToListAsync(), x => Assert.Equal(InventoryReservationStatus.LegacyClosedUnknown, x.Status));
        var movement = Assert.Single(await verification.StockMovements.ToListAsync());
        Assert.Equal(47, movement.PhysicalDelta);
        Assert.Equal(0, movement.ReservedDelta);
        Assert.Single(await verification.ApprovalHistories.ToListAsync());
        Assert.Equal(historicalId, (await verification.StockReservations.SingleAsync()).Id);
    }
}
