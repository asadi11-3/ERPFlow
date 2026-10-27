using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Procurement;
using ERPFlow.Application.Receiving;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Tests;

internal sealed record ReceivingSeed(ProcurementSeed Procurement, PurchaseOrderResult Order)
{
    public CreateGoodsReceiptCommand Draft(int quantity, string key = "create-grn", string? reference = null) =>
        new(key, Order.Id, Order.RowVersion, [new(Order.Items[0].Id, quantity)], reference);
}

internal static class GoodsReceiptTestSupport
{
    public static async Task<ReceivingSeed> SeedAsync(InventoryTestDatabase database, int quantity = 10, bool cutover = true)
    {
        var seed = await ProcurementTestSupport.SeedAsync(database);
        PurchaseOrderResult order;
        await using (var setup = database.Context())
        {
            var buyer = ProcurementTestSupport.Orders(setup, seed.Purchasing);
            order = await buyer.CreateAsync(seed.Draft(quantity));
            order = await buyer.SubmitAsync(order.Id, new(order.RowVersion));
            order = await ProcurementTestSupport.Orders(setup, seed.Approver).ApproveAsync(order.Id, new(order.RowVersion));
        }
        if (cutover)
        {
            var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.Inventory.ActorId);
            await using var setup = database.Context();
            await InventoryCutoverTestSupport.Cutover(setup, seed.Inventory.ActorId).RehearseAsync(approval);
        }
        return new(seed, order);
    }
    public static GoodsReceiptWorkflow Workflow(ERPFlowDbContext context, Guid actor, IInventoryWriteGate? gate = null)
    {
        gate ??= new OwnedWarehouseGate(context);
        var current = new InventoryTestDatabase.TestActor(actor);
        return new(new EfGoodsReceiptPersistence(context), current, gate, new EfInventoryPosting(context, gate, current));
    }
    internal sealed class OwnedWarehouseGate(ERPFlowDbContext context) : IInventoryWriteGate
    {
        public void EnsureWarehouseWritesAllowed()
        {
            if (!InventoryTestDatabase.Owns(context)) throw new InvalidOperationException("GRN rehearsal permission requires exact fixture ownership.");
            if (!context.InventoryAuthorities.AsNoTracking().Any(x => x.Mode == InventoryAuthorityMode.Warehouse))
                throw new WorkflowConflictException("GRN rehearsal requires a completed isolated cutover.");
        }
    }
}
