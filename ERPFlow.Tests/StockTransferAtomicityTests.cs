using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ERPFlow.Tests;

public sealed class StockTransferAtomicityTests
{
    [Fact]
    public async Task MultiProductFailureRollsBackEarlierPairsAndNewDestinationRows()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        Guid productId;
        ERPFlow.Application.Transfers.StockTransferResult draft;
        await using (var setup = database.Context())
        {
            await using (var transaction = await InventoryAuthoritySession.BeginAsync(setup))
            {
                var product = new Product("Other transferred SKU", "TRANSFER-ATOMIC", 20m, 0);
                productId = product.Id;
                var stock = new WarehouseStock(seed.Source, product.Id);
                setup.AddRange(product, stock);
                await setup.SaveChangesAsync();
                await new EfInventoryPosting(setup, new GoodsReceiptTestSupport.OwnedWarehouseGate(setup), new InventoryTestDatabase.TestActor(seed.Procurement.Inventory.ActorId))
                    .PostAsync(new("atomic-sku-opening", InventoryPostingKind.Opening, seed.Source, productId, 10, stock.RowVersion));
                await transaction.CommitAsync();
            }
            draft = await StockTransferTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync("multi-create",
                new(seed.Source, seed.Destination, [new(seed.Product, 2), new(productId, 3)]));
        }
        await using (var failing = database.Context(new FailAfterFirstPair()))
            await Assert.ThrowsAsync<InjectedFailure>(() => StockTransferTestSupport.Workflow(failing, seed.Procurement.Operator)
                .PostAsync(draft.Id, "multi-post", new(draft.RowVersion)));
        await using var verification = database.Context();
        Assert.Empty(await verification.InventoryPostings.Where(x => x.StockTransferItemId != null).ToListAsync());
        Assert.False(await verification.WarehouseStocks.AnyAsync(x => x.WarehouseId == seed.Destination && x.ProductId == productId));
        Assert.Equal(10, (await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Source && x.ProductId == productId)).StockOnHand);
        Assert.Equal(10, (await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Source && x.ProductId == seed.Product)).StockOnHand);
        var result = await StockTransferTestSupport.Workflow(verification, seed.Procurement.Operator).PostAsync(draft.Id, "multi-post", new(draft.RowVersion));
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(4, await verification.StockMovements.CountAsync(x => x.Kind == InventoryPostingKind.TransferOut || x.Kind == InventoryPostingKind.TransferIn));
        await new EfInventoryReconciliation(verification).ReconcileAsync();
    }
    [Fact]
    public async Task TransfersDoNotRewriteHistoricalImportedOwnersOrProductEvidence()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var owners = await context.InventoryReservations.AsNoTracking().Where(x => x.IsLegacyImport).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ProductId, x.Quantity, x.Status, x.SourceCreatedAt, x.ClosedAt, x.WarehouseStockId }).ToArrayAsync();
        var product = await context.Products.AsNoTracking().SingleAsync();
        var workflow = StockTransferTestSupport.Workflow(context, seed.Procurement.Operator);
        var draft = await workflow.CreateAsync("legacy-stock", new(LegacyDefaultWarehouse.Id, seed.Destination, [new(seed.Product, 5)]));
        await workflow.PostAsync(draft.Id, "legacy-stock-post", new(draft.RowVersion));
        var retained = await context.InventoryReservations.AsNoTracking().Where(x => x.IsLegacyImport).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ProductId, x.Quantity, x.Status, x.SourceCreatedAt, x.ClosedAt, x.WarehouseStockId }).ToArrayAsync();
        Assert.Equal(owners, retained);
        Assert.Equal(product.RowVersion, (await context.Products.AsNoTracking().SingleAsync()).RowVersion);
        Assert.Equal(6, (await context.WarehouseStocks.SingleAsync(x => x.WarehouseId == LegacyDefaultWarehouse.Id)).ReservedQuantity);
        await new EfInventoryReconciliation(context).ReconcileAsync();
    }
    private sealed class InjectedFailure : Exception;
    private sealed class FailAfterFirstPair : SaveChangesInterceptor
    {
        private int count;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (++count == 2) throw new InjectedFailure();
            return ValueTask.FromResult(result);
        }
    }
}
