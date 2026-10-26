using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Procurement;
using ERPFlow.Infrastructure.Persistence;

namespace ERPFlow.Tests;

internal sealed record ProcurementSeed(InventorySeed Inventory, Guid Purchasing, Guid OtherPurchasing,
    Guid Approver, Guid Operator, Guid Sales, Guid Technician, Guid SupplierId)
{
    public CreatePurchaseOrderCommand Draft(int quantity = 2, decimal cost = 12.3456m) =>
        new(SupplierId, Inventory.WarehouseId, [new(Inventory.ProductId, quantity, cost)], "Commercial notes");
}

internal static class ProcurementTestSupport
{
    public static async Task<ProcurementSeed> SeedAsync(InventoryTestDatabase database)
    {
        var inventory = await database.SeedAsync();
        await using var context = database.Context();
        var purchasing = new AppUser("Purchasing", "purchasing@example.test", UserRole.Purchasing, "fixture-hash");
        var other = new AppUser("Other", "other-purchasing@example.test", UserRole.Purchasing, "fixture-hash");
        var approver = new AppUser("Approver", "po-approver@example.test", UserRole.Approver, "fixture-hash");
        var warehouse = new AppUser("Warehouse", "warehouse@example.test", UserRole.WarehouseOperator, "fixture-hash");
        var sales = new AppUser("Sales", "po-sales@example.test", UserRole.Sales, "fixture-hash");
        var tech = new AppUser("Technician", "po-tech@example.test", UserRole.Technician, "fixture-hash");
        var supplier = new Supplier("SUP-TEST", "Supplier");
        context.AddRange(purchasing, other, approver, warehouse, sales, tech, supplier);
        await context.SaveChangesAsync();
        return new(inventory, purchasing.Id, other.Id, approver.Id, warehouse.Id, sales.Id, tech.Id, supplier.Id);
    }
    public static ProcurementMasters Masters(ERPFlowDbContext context, Guid actor) =>
        new(new EfProcurementPersistence(context), new InventoryTestDatabase.TestActor(actor));
    public static PurchaseOrderWorkflow Orders(ERPFlowDbContext context, Guid actor) =>
        new(new EfProcurementPersistence(context), new InventoryTestDatabase.TestActor(actor));
}
