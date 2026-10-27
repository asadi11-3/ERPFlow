using ERPFlow.Application.Inventory;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Application.Service;
using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Tests;

internal sealed record TransferSeed(ReceivingSeed Receiving, Guid? ReservedOrderId)
{
    public ProcurementSeed Procurement => Receiving.Procurement;
    public Guid Source => Procurement.Inventory.WarehouseId;
    public Guid Destination => Procurement.Inventory.OtherWarehouseId;
    public Guid Product => Procurement.Inventory.ProductId;
    public CreateStockTransferCommand Draft(int quantity = 5) => new(Source, Destination, [new(Product, quantity)]);
}

internal static class StockTransferTestSupport
{
    public static async Task<TransferSeed> SeedAsync(InventoryTestDatabase database, bool reserve = true, int source = 10, int destination = 2, bool cutover = true)
    {
        var seed = await GoodsReceiptTestSupport.SeedAsync(database, cutover: cutover);
        Guid? reservation = null;
        if (cutover)
        {
            await using var context = database.Context();
            var gate = new GoodsReceiptTestSupport.OwnedWarehouseGate(context);
            var poster = new EfInventoryPosting(context, gate, new InventoryTestDatabase.TestActor(seed.Procurement.Inventory.ActorId));
            foreach (var pair in new[] { (seed.Order.WarehouseId, source), (seed.Procurement.Inventory.OtherWarehouseId, destination) })
            {
                var row = await context.WarehouseStocks.SingleAsync(x => x.WarehouseId == pair.Item1 && x.ProductId == seed.Procurement.Inventory.ProductId);
                await poster.PostAsync(new($"transfer-fixture:{pair.Item1:N}", InventoryPostingKind.Opening, pair.Item1,
                    row.ProductId, pair.Item2, row.RowVersion));
            }
            if (reserve)
            {
                var customer = await context.Customers.SingleAsync();
                var sales = Sales(context, seed.Procurement.Inventory.ActorId);
                var draft = await sales.CreateDraftAsync(new(customer.Id, [new(seed.Procurement.Inventory.ProductId, 3, seed.Order.WarehouseId)]));
                await sales.SubmitAsync(draft.Id);
                reservation = draft.Id;
            }
        }
        return new(seed, reservation);
    }
    public static StockTransferWorkflow Workflow(ERPFlowDbContext context, Guid actor, IInventoryWriteGate? gate = null)
    {
        gate ??= new GoodsReceiptTestSupport.OwnedWarehouseGate(context);
        var current = new InventoryTestDatabase.TestActor(actor);
        return new(new EfStockTransferPersistence(context), current, gate, new EfInventoryPosting(context, gate, current));
    }
    public static SalesOrderWorkflow Sales(ERPFlowDbContext context, Guid actor) => new(new EfSalesOrderPersistence(context),
        new InventoryTestDatabase.TestActor(actor), new ProductAuthorityWarehouseSourcePolicy(context), InventoryCutoverTestSupport.Workflow(context, actor));
    public static ServiceWorkflow Service(ERPFlowDbContext context, Guid actor) => new(new EfServicePersistence(context),
        new InventoryTestDatabase.TestActor(actor), new ProductAuthorityWarehouseSourcePolicy(context), InventoryCutoverTestSupport.Workflow(context, actor));
}
