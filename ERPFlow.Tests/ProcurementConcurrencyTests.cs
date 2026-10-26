using ERPFlow.Application.Common;
using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Procurement;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProcurementConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentSubmissionsOrDecisionsCommitOneEvent(bool decide)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        PurchaseOrderResult order;
        await using (var setup = database.Context())
        {
            var workflow = ProcurementTestSupport.Orders(setup, seed.Purchasing);
            order = await workflow.CreateAsync(seed.Draft());
            if (decide) order = await workflow.SubmitAsync(order.Id, new(order.RowVersion));
        }
        await using var first = database.Context();
        await using var second = database.Context();
        var results = await Task.WhenAll(Attempt(first, false), Attempt(second, true));
        Assert.Single(results, x => x is null);
        Assert.Single(results, x => x is ConcurrencyConflictException or WorkflowConflictException);
        await using var verification = database.Context();
        Assert.Equal(decide ? 2 : 1, await verification.PurchaseOrderHistories.CountAsync(x => x.PurchaseOrderId == order.Id));
        Assert.Empty(await verification.StockMovements.ToListAsync());

        async Task<Exception?> Attempt(ERPFlowDbContext context, bool reject)
        {
            try
            {
                var workflow = ProcurementTestSupport.Orders(context, decide ? seed.Approver : seed.Purchasing);
                if (!decide) await workflow.SubmitAsync(order.Id, new(order.RowVersion));
                else if (reject) await workflow.RejectAsync(order.Id, new(order.RowVersion, "Not approved"));
                else await workflow.ApproveAsync(order.Id, new(order.RowVersion));
                return null;
            }
            catch (ConcurrencyConflictException exception) { return exception; }
            catch (WorkflowConflictException exception) { return exception; }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentMasterUpdatesCannotLoseAnEdit(bool warehouse)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var first = database.Context();
        await using var second = database.Context();
        var actor = warehouse ? seed.Inventory.ActorId : seed.Purchasing;
        var id = warehouse ? seed.Inventory.WarehouseId : seed.SupplierId;
        var masters = ProcurementTestSupport.Masters(first, actor);
        var token = warehouse ? (await masters.GetWarehouseAsync(id)).RowVersion : (await masters.GetSupplierAsync(id)).RowVersion;
        var outcomes = await Task.WhenAll(Edit(first, "First"), Edit(second, "Second"));
        Assert.Single(outcomes, x => x is null);
        Assert.Single(outcomes, x => x is ConcurrencyConflictException);

        async Task<Exception?> Edit(ERPFlowDbContext context, string name)
        {
            try
            {
                var workflow = ProcurementTestSupport.Masters(context, actor);
                if (warehouse) await workflow.UpdateWarehouseAsync(id, new(name, token));
                else await workflow.UpdateSupplierAsync(id, new(name, null, null, null, null, token));
                return null;
            }
            catch (ConcurrencyConflictException exception) { return exception; }
        }
    }

    [Fact]
    public async Task DraftEditRacingSubmissionCannotChangeSubmittedCommercialLines()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        PurchaseOrderResult order;
        await using (var setup = database.Context()) order = await ProcurementTestSupport.Orders(setup, seed.Purchasing).CreateAsync(seed.Draft());
        await using var first = database.Context();
        await using var second = database.Context();
        var outcomes = await Task.WhenAll(Attempt(first, true), Attempt(second, false));
        Assert.Single(outcomes, x => x is null);
        Assert.Single(outcomes, x => x is ConcurrencyConflictException);
        await using var verification = database.Context();
        var stored = await verification.PurchaseOrders.Include(x => x.Items).SingleAsync(x => x.Id == order.Id);
        Assert.Equal(2, stored.Revision);
        Assert.Equal(stored.Status == PurchaseOrderStatus.Draft ? 3 : 2, Assert.Single(stored.Items).Quantity);

        async Task<Exception?> Attempt(ERPFlowDbContext context, bool edit)
        {
            try
            {
                var workflow = ProcurementTestSupport.Orders(context, seed.Purchasing);
                if (edit) await workflow.UpdateAsync(order.Id, new(order.SupplierId, order.WarehouseId,
                    [new(seed.Inventory.ProductId, 3, 1m)], null, order.RowVersion));
                else await workflow.SubmitAsync(order.Id, new(order.RowVersion));
                return null;
            }
            catch (ConcurrencyConflictException exception) { return exception; }
        }
    }

    [Fact]
    public async Task InFlightPurchaseAdmissionBlocksDeactivationUntilItsCommitmentIsVisible()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        var pause = new PauseBeforePurchaseSave();
        await using var purchaser = database.Context(pause);
        await using var admin = database.Context();
        var masters = ProcurementTestSupport.Masters(admin, seed.Inventory.ActorId);
        var warehouse = await masters.GetWarehouseAsync(seed.Inventory.WarehouseId);
        var creation = ProcurementTestSupport.Orders(purchaser, seed.Purchasing).CreateAsync(seed.Draft());
        await pause.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Task<WarehouseResult> deactivation;
        try
        {
            deactivation = masters.SetWarehouseActiveAsync(warehouse.Id, new(warehouse.RowVersion), false);
            Assert.False(deactivation.IsCompleted);
        }
        finally { pause.Resume.TrySetResult(); }
        await creation;
        await Assert.ThrowsAsync<WorkflowConflictException>(() => deactivation);
        Assert.True((await masters.GetWarehouseAsync(warehouse.Id)).IsActive);
    }

    [Fact]
    public async Task SupplierDeactivationWaitsForAdmittedPurchaseAndThenBlocksNewOrders()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        var pause = new PauseBeforePurchaseSave();
        await using var purchaser = database.Context(pause);
        await using var editor = database.Context();
        var masters = ProcurementTestSupport.Masters(editor, seed.Purchasing);
        var supplier = await masters.GetSupplierAsync(seed.SupplierId);
        var creation = ProcurementTestSupport.Orders(purchaser, seed.Purchasing).CreateAsync(seed.Draft());
        await pause.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Task<SupplierResult> deactivation;
        try
        {
            deactivation = masters.SetSupplierActiveAsync(supplier.Id, new(supplier.RowVersion), false);
            Assert.False(deactivation.IsCompleted);
        }
        finally { pause.Resume.TrySetResult(); }
        await creation;
        Assert.False((await deactivation).IsActive);
        await Assert.ThrowsAsync<WorkflowConflictException>(() => ProcurementTestSupport.Orders(editor, seed.Purchasing).CreateAsync(seed.Draft()));
    }

    [Fact]
    public async Task ApprovedCancellationRejectsReceivingAdmissionAndKeepsApprovalHistory()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var buyer = ProcurementTestSupport.Orders(context, seed.Purchasing);
        var order = await buyer.CreateAsync(seed.Draft());
        order = await buyer.SubmitAsync(order.Id, new(order.RowVersion));
        order = await ProcurementTestSupport.Orders(context, seed.Approver).ApproveAsync(order.Id, new(order.RowVersion));
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE PurchaseOrders SET ReceivingStartedAt=SYSUTCDATETIME() WHERE Id={order.Id}");
        var admin = ProcurementTestSupport.Orders(context, seed.Inventory.ActorId);
        order = await admin.GetAsync(order.Id);
        await Assert.ThrowsAsync<WorkflowConflictException>(() => admin.CancelAsync(order.Id, new(order.RowVersion, "Too late")));
        Assert.Equal(2, (await admin.HistoryAsync(order.Id)).Count);
    }

    private sealed class PauseBeforePurchaseSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<PurchaseOrder>().Any(x => x.State == EntityState.Added))
            {
                Ready.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
}
