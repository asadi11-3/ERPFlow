using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Receiving;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class GoodsReceiptSafetyTests
{
    [Fact]
    public async Task BeforeCutoverEvenAPermissiveGateCannotCreateOrPostAReceipt()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database, cutover: false);
        await using var context = database.Context();
        var before = (await context.Products.AsNoTracking().SingleAsync()).RowVersion;
        var workflow = GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator, new PermissiveGate());
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.CreateAsync(seed.Draft(1)));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.PostAsync(Guid.NewGuid(), new("blocked-post", new byte[8])));
        Assert.Empty(await context.GoodsReceipts.ToListAsync());
        Assert.Empty(await context.GoodsReceiptCommands.ToListAsync());
        Assert.Empty(await context.InventoryPostings.ToListAsync());
        Assert.All(await context.WarehouseStocks.ToListAsync(), x => Assert.Equal((0, 0), (x.StockOnHand, x.ReservedQuantity)));
        Assert.Equal(before, (await context.Products.AsNoTracking().SingleAsync()).RowVersion);
    }

    [Fact]
    public async Task DefaultProductionGateStillRejectsACompletedCutoverDatabase()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        await Assert.ThrowsAsync<WorkflowConflictException>(() => GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator,
            new ProductAuthorityWriteGate()).CreateAsync(seed.Draft(1)));
        Assert.Empty(await context.GoodsReceipts.ToListAsync());
    }

    [Fact]
    public async Task DraftCancellationHasNoInventoryEffectsAndDoesNotEraseReceivingAdmission()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator);
        var draft = await workflow.CreateAsync(seed.Draft(2));
        var cancelled = await workflow.CancelAsync(draft.Id, new(draft.RowVersion, "Wrong delivery"));
        Assert.Equal(GoodsReceiptStatus.Cancelled, cancelled.Status);
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.PostAsync(draft.Id, new("post-cancelled", cancelled.RowVersion)));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => ProcurementTestSupport.Orders(context, seed.Procurement.Inventory.ActorId)
            .CancelAsync(seed.Order.Id, new(cancelled.PurchaseOrderRowVersion, "Cannot erase admission")));
        Assert.Empty(await context.StockMovements.Where(x => x.Kind == InventoryPostingKind.Receipt).ToListAsync());
        Assert.Equal(0, (await context.PurchaseOrderItems.AsNoTracking().SingleAsync()).ReceivedQuantity);
    }

    [Fact]
    public async Task ANewDestinationStockRowStartsWithAReceiptAndNoInventedOpening()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var warehouse = await ProcurementTestSupport.Masters(context, seed.Procurement.Inventory.ActorId).CreateWarehouseAsync(new("NEW-GRN", "Receipt-only stock"));
        var buyer = ProcurementTestSupport.Orders(context, seed.Procurement.Purchasing);
        var order = await buyer.CreateAsync(seed.Procurement.Draft(4) with { WarehouseId = warehouse.Id });
        order = await buyer.SubmitAsync(order.Id, new(order.RowVersion));
        order = await ProcurementTestSupport.Orders(context, seed.Procurement.Approver).ApproveAsync(order.Id, new(order.RowVersion));
        var workflow = GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator);
        var receipt = await workflow.CreateAsync(new("new-stock", order.Id, order.RowVersion, [new(order.Items.Single().Id, 4)]));
        await workflow.PostAsync(receipt.Id, new("post-new-stock", receipt.RowVersion));
        var stock = await context.WarehouseStocks.AsNoTracking().SingleAsync(x => x.WarehouseId == warehouse.Id);
        Assert.Equal((4, 0, 1L), (stock.StockOnHand, stock.ReservedQuantity, stock.Sequence));
        Assert.Equal(InventoryPostingKind.Receipt, (await context.StockMovements.SingleAsync(x => x.WarehouseStockId == stock.Id)).Kind);
        await new EfInventoryReconciliation(context).ReconcileAsync();
    }

    [Fact]
    public async Task InactiveSupplierRejectsPostWithoutChangingReceiptOrStock()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator);
        var receipt = await workflow.CreateAsync(seed.Draft(2));
        var masters = ProcurementTestSupport.Masters(context, seed.Procurement.Purchasing);
        var supplier = await masters.GetSupplierAsync(seed.Procurement.SupplierId);
        await masters.SetSupplierActiveAsync(supplier.Id, new(supplier.RowVersion), false);
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.PostAsync(receipt.Id, new("inactive-post", receipt.RowVersion)));
        Assert.Equal(GoodsReceiptStatus.Draft, (await workflow.GetAsync(receipt.Id)).Status);
        Assert.Empty(await context.StockMovements.Where(x => x.Kind == InventoryPostingKind.Receipt).ToListAsync());
        Assert.Single(await context.GoodsReceiptCommands.ToListAsync());
    }

    [Fact]
    public async Task PostedReceiptMovementAndDocumentAreImmutableInSql()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator);
        var receipt = await workflow.CreateAsync(seed.Draft(2));
        receipt = await workflow.PostAsync(receipt.Id, new("immutable-post", receipt.RowVersion));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.CancelAsync(receipt.Id, new(receipt.RowVersion, "No reversal")));
        Assert.Equal(51020, (await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE StockMovements SET ResultRowVersion=0x0000000000000000 WHERE Id={receipt.Items.Single().StockMovementId}"))).Number);
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM GoodsReceipts WHERE Id={receipt.Id}"));
        await new EfInventoryReconciliation(context).ReconcileAsync();
    }

    [Fact]
    public async Task DuplicateSupplierReferenceCannotCreateAnotherReceipt()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator);
        var first = await workflow.CreateAsync(seed.Draft(1, reference: "delivery-123"));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.CreateAsync(seed.Draft(1, "another-create", "DELIVERY-123") with { PurchaseOrderRowVersion = first.PurchaseOrderRowVersion }));
        Assert.Single(await context.GoodsReceipts.ToListAsync());
        Assert.Single(await context.GoodsReceiptCommands.ToListAsync());
    }

    [Fact]
    public async Task CancellingAnUnpostedDraftAllowsCorrectedSupplierPaperwork()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator);
        var first = await workflow.CreateAsync(seed.Draft(2, reference: "Delivery-Correct"));
        await workflow.CancelAsync(first.Id, new(first.RowVersion, "Correct quantity before posting"));
        var corrected = await workflow.CreateAsync(seed.Draft(3, "corrected-command", "Delivery-Correct") with { PurchaseOrderRowVersion = first.PurchaseOrderRowVersion });
        Assert.Equal(GoodsReceiptStatus.Draft, corrected.Status);
        Assert.NotEqual(first.Id, corrected.Id);
        Assert.Empty(await context.StockMovements.Where(x => x.Kind == InventoryPostingKind.Receipt).ToListAsync());
    }

    [Fact]
    public async Task PersistedRoleRevocationPreventsPostingAndReplay()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = GoodsReceiptTestSupport.Workflow(context, seed.Procurement.Operator);
        var command = seed.Draft(1);
        var receipt = await workflow.CreateAsync(command);
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE AppUsers SET Role=1 WHERE Id={seed.Procurement.Operator}");
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() => workflow.PostAsync(receipt.Id, new("revoked-post", receipt.RowVersion)));
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() => workflow.CreateAsync(command));
        Assert.Empty(await context.StockMovements.Where(x => x.Kind == InventoryPostingKind.Receipt).ToListAsync());
    }
    private sealed class PermissiveGate : IInventoryWriteGate { public void EnsureWarehouseWritesAllowed() { } }
}
