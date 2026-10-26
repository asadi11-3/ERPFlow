using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProcurementDraftEditingTests
{
    [Fact]
    public async Task DraftCanRemoveAndReplaceProductsWithoutDeletingDocumentHistory()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        Guid replacementId;
        await using (var setup = database.Context())
        {
            var replacement = new Product("Replacement", "PO-REPLACE", 77m, 10);
            replacementId = replacement.Id;
            setup.Products.Add(replacement);
            await setup.SaveChangesAsync();
        }
        await using var context = database.Context();
        var workflow = ProcurementTestSupport.Orders(context, seed.Purchasing);
        var draft = await workflow.CreateAsync(seed.Draft());
        var updated = await workflow.UpdateAsync(draft.Id, new(draft.SupplierId, draft.WarehouseId,
            [new(replacementId, 5, 3.1234m)], "Replacement specification", draft.RowVersion));
        Assert.Equal(replacementId, Assert.Single(updated.Items).ProductId);
        Assert.Equal(15.6170m, updated.TotalAmount);
        Assert.Equal(draft.PONumber, updated.PONumber);
        Assert.Single(await context.PurchaseOrderItems.AsNoTracking().Where(x => x.PurchaseOrderId == draft.Id).ToListAsync());
        var submitted = await workflow.SubmitAsync(draft.Id, new(updated.RowVersion));
        Assert.Single(await workflow.HistoryAsync(submitted.Id));
        Assert.Equal(77m, (await context.Products.AsNoTracking().SingleAsync(x => x.Id == replacementId)).UnitPrice);
    }
}
