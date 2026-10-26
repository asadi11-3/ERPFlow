using ERPFlow.Application.SalesOrders;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class InventoryCutoverReaderTests
{
    [Fact]
    public async Task CatalogAvailabilityDoesNotReadArchivedProductQuantitiesAfterCutover()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var context = database.Context())
            await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        await using (var context = database.Context())
        {
            var workflow = new SalesOrderWorkflow(new EfSalesOrderPersistence(context), new InventoryTestDatabase.TestActor(seed.ActorId),
                new ProductAuthorityWarehouseSourcePolicy(context), InventoryCutoverTestSupport.Workflow(context, seed.ActorId));
            await workflow.SubmitAsync(seed.OtherOrderId);
        }
        await using var verification = database.Context();
        var archived = await verification.Products.SingleAsync();
        Assert.Equal(44, archived.AvailableStock);
        var reference = Assert.Single(await new EfReferenceDataQueries(verification).ListProductsAsync());
        Assert.Equal(41, reference.AvailableStock);
        var current = await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == LegacyDefaultWarehouse.Id);
        Assert.Equal(current.AvailableStock, reference.AvailableStock);
    }
}
