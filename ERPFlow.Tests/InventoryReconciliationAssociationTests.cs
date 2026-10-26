using ERPFlow.Application.Common;
using ERPFlow.Application.Equipment;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Application.Service;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class InventoryReconciliationAssociationTests
{
    [Theory]
    [InlineData(InventoryPostingKind.Reserve, 1)]
    [InlineData(InventoryPostingKind.Reserve, 3)]
    [InlineData(InventoryPostingKind.Release, 3)]
    [InlineData(InventoryPostingKind.Consume, 3)]
    public async Task BalancedPostingCannotBeReassignedToAnotherValidOwner(InventoryPostingKind kind, int quantity)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var context = database.Context())
            await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        Guid nativeOwner;
        Guid importedOwner;
        await using (var context = database.Context())
        {
            var sales = Sales(context, seed.ActorId);
            var customer = await context.Customers.SingleAsync();
            var order = await sales.CreateDraftAsync(new(customer.Id, [new(seed.ProductId, quantity, LegacyDefaultWarehouse.Id)]));
            await sales.SubmitAsync(order.Id);
            if (kind == InventoryPostingKind.Release) await sales.ReturnAsync(order.Id, "Correction");
            if (kind == InventoryPostingKind.Consume)
            {
                await sales.ApproveAsync(order.Id);
                await Equipment(context, seed.ActorId).DispatchOrderAsync(order.Id, new("Carrier", null, null));
            }
            nativeOwner = (await context.StockReservations.SingleAsync(x => x.SalesOrderId == order.Id)).Id;
            importedOwner = (await context.InventoryReservations.SingleAsync(x => x.SalesOrderId == seed.SalesOrderId)).Id;
            await new EfInventoryReconciliation(context).ReconcileAsync();
        }
        await using var tampered = database.Context();
        var before = await tampered.WarehouseStocks.SingleAsync(x => x.WarehouseId == LegacyDefaultWarehouse.Id);
        var totals = (before.StockOnHand, before.ReservedQuantity, before.Sequence);
        Assert.Equal(1, await tampered.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE InventoryPostings SET ReservationId={importedOwner} WHERE ReservationId={nativeOwner} AND Kind={(int)kind}"));
        var after = await tampered.WarehouseStocks.AsNoTracking().SingleAsync(x => x.Id == before.Id);
        Assert.Equal(totals, (after.StockOnHand, after.ReservedQuantity, after.Sequence));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => new EfInventoryReconciliation(tampered).ReconcileAsync());
    }

    [Fact]
    public async Task EqualQuantityPostingCannotClaimAnOwnerInAnotherWarehouse()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using var context = database.Context();
        await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        await using (var transaction = await InventoryAuthoritySession.BeginAsync(context))
        {
            var stock = await context.WarehouseStocks.SingleAsync(x => x.Id == seed.OtherStockId);
            await new EfInventoryPosting(context, new InventoryCutoverTestSupport.WarehouseGate(context),
                new InventoryTestDatabase.TestActor(seed.ActorId)).PostAsync(new("association-other-opening",
                    InventoryPostingKind.Opening, seed.OtherWarehouseId, seed.ProductId, 3, stock.RowVersion));
            await transaction.CommitAsync();
        }
        var customer = await context.Customers.SingleAsync();
        var sales = Sales(context, seed.ActorId);
        var order = await sales.CreateDraftAsync(new(customer.Id, [new(seed.ProductId, 3, seed.OtherWarehouseId)]));
        await sales.SubmitAsync(order.Id);
        await new EfInventoryReconciliation(context).ReconcileAsync();
        var native = await context.InventoryReservations.SingleAsync(x => x.SalesOrderId == order.Id);
        var imported = await context.InventoryReservations.SingleAsync(x => x.SalesOrderId == seed.SalesOrderId);
        Assert.Equal(native.Quantity, imported.Quantity);
        Assert.NotEqual(native.WarehouseStockId, imported.WarehouseStockId);
        Assert.Equal(1, await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE InventoryPostings SET ReservationId={imported.Id} WHERE ReservationId={native.Id}"));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => new EfInventoryReconciliation(context).ReconcileAsync());
    }

    [Fact]
    public async Task ClosedSalesMirrorCannotDisagreeWithItsInventoryOwner()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var context = database.Context())
        {
            await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
            await Sales(context, seed.ActorId).ReturnAsync(seed.SalesOrderId, "Correction");
            await new EfInventoryReconciliation(context).ReconcileAsync();
        }
        await using var tampered = database.Context();
        await using (var transaction = await InventoryAuthoritySession.BeginAsync(tampered))
        {
            Assert.Equal(1, await tampered.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE StockReservations SET ClosureKind=2 WHERE SalesOrderId={seed.SalesOrderId}"));
            await transaction.CommitAsync();
        }
        await Assert.ThrowsAsync<WorkflowConflictException>(() => new EfInventoryReconciliation(tampered).ReconcileAsync());
    }

    [Fact]
    public async Task ConsumedJobcardCannotChangeToCancelled()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        Guid jobcardId;
        await using (var context = database.Context())
        {
            await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
            jobcardId = (await context.JobcardItems.SingleAsync()).JobcardId;
            var workflow = new ServiceWorkflow(new EfServicePersistence(context), new InventoryTestDatabase.TestActor(seed.ActorId),
                new ProductAuthorityWarehouseSourcePolicy(context), InventoryCutoverTestSupport.Workflow(context, seed.ActorId));
            await workflow.CompleteJobcardAsync(jobcardId, new("Complete", null));
            await new EfInventoryReconciliation(context).ReconcileAsync();
        }
        await using var tampered = database.Context();
        await using (var transaction = await InventoryAuthoritySession.BeginAsync(tampered))
        {
            await tampered.Database.ExecuteSqlInterpolatedAsync($"UPDATE Jobcards SET Status=4 WHERE Id={jobcardId}");
            await transaction.CommitAsync();
        }
        await Assert.ThrowsAsync<WorkflowConflictException>(() => new EfInventoryReconciliation(tampered).ReconcileAsync());
    }

    [Fact]
    public async Task ReturnedEditedAndResubmittedSalesRetainsValidClosedHistory()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using var context = database.Context();
        await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        var sales = Sales(context, seed.ActorId);
        await sales.ReturnAsync(seed.SalesOrderId, "Correct quantity");
        var order = await sales.GetByIdAsync(seed.SalesOrderId);
        await sales.UpdateDraftAsync(order.Id, new(order.CustomerId,
            [new(seed.ProductId, 1, LegacyDefaultWarehouse.Id)], order.RowVersion));
        await sales.SubmitAsync(order.Id);
        await sales.ReturnAsync(order.Id, "Second correction");
        order = await sales.GetByIdAsync(order.Id);
        await sales.UpdateDraftAsync(order.Id, new(order.CustomerId,
            [new(seed.ProductId, 2, LegacyDefaultWarehouse.Id)], order.RowVersion));
        await sales.SubmitAsync(order.Id);
        await sales.ApproveAsync(order.Id);
        await Equipment(context, seed.ActorId).DispatchOrderAsync(order.Id, new("Carrier", null, null));
        await new EfInventoryReconciliation(context).ReconcileAsync();
        Assert.Equal(3, await context.InventoryReservations.CountAsync(x => x.SalesOrderId == order.Id));
        Assert.Equal(2, await context.InventoryReservations.CountAsync(x => x.SalesOrderId == order.Id && x.Status == InventoryReservationStatus.Released));
    }

    private static SalesOrderWorkflow Sales(ERPFlowDbContext context, Guid actor) => new(
        new EfSalesOrderPersistence(context), new InventoryTestDatabase.TestActor(actor),
        new ProductAuthorityWarehouseSourcePolicy(context), InventoryCutoverTestSupport.Workflow(context, actor));

    private static EquipmentWorkflow Equipment(ERPFlowDbContext context, Guid actor) => new(
        new EfEquipmentPersistence(context), new InventoryTestDatabase.TestActor(actor),
        new ProductAuthorityWarehouseSourcePolicy(context), InventoryCutoverTestSupport.Workflow(context, actor));
}
