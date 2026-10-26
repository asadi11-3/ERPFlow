using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Procurement;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class GoodsReceiptCompatibilityTests
{
    [Fact]
    public async Task ReceivingSchemaDoesNotFreezeDraftPoSupplierOrWarehouseEdits()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var masters = ProcurementTestSupport.Masters(context, seed.Inventory.ActorId);
        var supplier = await masters.CreateSupplierAsync(new("REVISED-SOURCE", "Revised supplier"));
        var warehouse = await masters.CreateWarehouseAsync(new("REVISED-WH", "Revised destination"));
        var buyer = ProcurementTestSupport.Orders(context, seed.Purchasing);
        var draft = await buyer.CreateAsync(seed.Draft());
        var revised = await buyer.UpdateAsync(draft.Id, new(supplier.Id, warehouse.Id, [new(seed.Inventory.ProductId, 4, 2m)], "Revised source", draft.RowVersion));
        Assert.Equal((supplier.Id, warehouse.Id, 8m), (revised.SupplierId, revised.WarehouseId, revised.TotalAmount));
        Assert.Equal(draft.PONumber, revised.PONumber);
        Assert.Empty(await context.GoodsReceipts.ToListAsync());
    }

    [Fact]
    public async Task ReceiptSourceMustMatchItsPoEvenWhenIndividualForeignKeysAreValid()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        await using var transaction = await InventoryAuthoritySession.BeginAsync(context);
        var id = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            EXEC sys.sp_set_session_context @key=N'ERPFlow.GRN', @value={id};
            INSERT INTO GoodsReceipts(Id,Number,PurchaseOrderId,SupplierId,WarehouseId,CreatedByUserId,CreatedAt,Status)
            VALUES({id},N'GRN-WRONG-WAREHOUSE',{seed.Order.Id},{seed.Order.SupplierId},{seed.Procurement.Inventory.OtherWarehouseId},
                {seed.Procurement.Operator},SYSUTCDATETIME(),0);
            """));
        Assert.Equal(51020, error.Number);
    }
}
