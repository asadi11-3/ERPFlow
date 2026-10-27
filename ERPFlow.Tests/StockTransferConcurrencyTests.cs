using System.Text.Json;
using ERPFlow.Application.Common;
using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Transfers;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class StockTransferConcurrencyTests
{
    [Fact]
    public async Task IdenticalConcurrentCreatesProduceOneDocumentAndResponse()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        var barrier = new InventoryTransactionBarrier();
        await using var one = database.Context(barrier); await using var two = database.Context(barrier);
        var results = await Task.WhenAll(StockTransferTestSupport.Workflow(one, seed.Procurement.Operator).CreateAsync("same-create", seed.Draft()),
            StockTransferTestSupport.Workflow(two, seed.Procurement.Operator).CreateAsync("same-create", seed.Draft())).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(results[1]));
        await using var verification = database.Context();
        Assert.Single(await verification.StockTransfers.ToListAsync());
        Assert.Single(await verification.StockTransferCommands.ToListAsync());
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentPostsNeverDuplicateTheDocumentEffects(bool sameKey)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        StockTransferResult draft;
        await using (var setup = database.Context()) draft = await StockTransferTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync("draft", seed.Draft());
        var barrier = new InventoryTransactionBarrier();
        await using var one = database.Context(barrier); await using var two = database.Context(barrier);
        var results = await Task.WhenAll(Post(one, "post-1"), Post(two, sameKey ? "post-1" : "post-2")).WaitAsync(TimeSpan.FromSeconds(60));
        if (sameKey)
        {
            Assert.All(results, x => Assert.Null(x.Error));
            Assert.Equal(JsonSerializer.Serialize(results[0].Result), JsonSerializer.Serialize(results[1].Result));
        }
        else
        {
            Assert.Single(results, x => x.Error is null);
            Assert.Single(results, x => x.Error is ConcurrencyConflictException or WorkflowConflictException);
        }
        await using var verification = database.Context();
        Assert.Equal(2, await verification.StockMovements.CountAsync(x => x.Kind == InventoryPostingKind.TransferOut || x.Kind == InventoryPostingKind.TransferIn));
        await new EfInventoryReconciliation(verification).ReconcileAsync();
        async Task<(StockTransferResult? Result, Exception? Error)> Post(ERPFlowDbContext context, string key)
        {
            try { return (await StockTransferTestSupport.Workflow(context, seed.Procurement.Operator).PostAsync(draft.Id, key, new(draft.RowVersion)), null); }
            catch (ConcurrencyConflictException ex) { return (null, ex); }
            catch (WorkflowConflictException ex) { return (null, ex); }
        }
    }
    [Fact]
    public async Task CompetingTransfersCannotConsumeTheSameAvailableStock()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        StockTransferResult first, second;
        await using (var setup = database.Context())
        {
            var workflow = StockTransferTestSupport.Workflow(setup, seed.Procurement.Operator);
            first = await workflow.CreateAsync("draft-1", seed.Draft()); second = await workflow.CreateAsync("draft-2", seed.Draft());
        }
        var barrier = new InventoryTransactionBarrier();
        await using var one = database.Context(barrier); await using var two = database.Context(barrier);
        var outcomes = await Task.WhenAll(Post(one, first, "post-1"), Post(two, second, "post-2")).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Single(outcomes, x => x is null);
        Assert.Single(outcomes, x => x is WorkflowConflictException or ConcurrencyConflictException);
        await using var verification = database.Context();
        var source = await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Source);
        Assert.Equal((5, 3, 2), (source.StockOnHand, source.ReservedQuantity, source.AvailableStock));
        Assert.Single(await verification.StockTransfers.Where(x => x.Status == StockTransferStatus.Posted).ToListAsync());
        await new EfInventoryReconciliation(verification).ReconcileAsync();
        async Task<Exception?> Post(ERPFlowDbContext context, StockTransferResult document, string key)
        {
            try { await StockTransferTestSupport.Workflow(context, seed.Procurement.Operator).PostAsync(document.Id, key, new(document.RowVersion)); return null; }
            catch (WorkflowConflictException ex) { return ex; }
            catch (ConcurrencyConflictException ex) { return ex; }
        }
    }
    [Fact]
    public async Task MultiSkuOppositeTransfersIgnoreClientLineOrderAndKeepOneLockOrder()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        StockTransferResult outward, inward;
        Guid productId;
        await using (var setup = database.Context())
        {
            await using (var transaction = await InventoryAuthoritySession.BeginAsync(setup))
            {
                var product = new Product("Opposite SKU", "OPPOSITE-SKU", 20, 0);
                productId = product.Id;
                var a = new WarehouseStock(seed.Source, productId); var b = new WarehouseStock(seed.Destination, productId);
                setup.AddRange(product, a, b); await setup.SaveChangesAsync();
                var poster = new EfInventoryPosting(setup, new GoodsReceiptTestSupport.OwnedWarehouseGate(setup), new InventoryTestDatabase.TestActor(seed.Procurement.Inventory.ActorId));
                await poster.PostAsync(new("multi-a-opening", InventoryPostingKind.Opening, seed.Source, productId, 10, a.RowVersion));
                await poster.PostAsync(new("multi-b-opening", InventoryPostingKind.Opening, seed.Destination, productId, 10, b.RowVersion));
                await transaction.CommitAsync();
            }
            var workflow = StockTransferTestSupport.Workflow(setup, seed.Procurement.Operator);
            outward = await workflow.CreateAsync("multi-a-b", new(seed.Source, seed.Destination, [new(seed.Product, 2), new(productId, 2)]));
            inward = await workflow.CreateAsync("multi-b-a", new(seed.Destination, seed.Source, [new(productId, 1), new(seed.Product, 1)]));
        }
        var barrier = new InventoryTransactionBarrier();
        await using var one = database.Context(barrier); await using var two = database.Context(barrier);
        await Task.WhenAll(StockTransferTestSupport.Workflow(one, seed.Procurement.Operator).PostAsync(outward.Id, "multi-out", new(outward.RowVersion)),
            StockTransferTestSupport.Workflow(two, seed.Procurement.Operator).PostAsync(inward.Id, "multi-in", new(inward.RowVersion))).WaitAsync(TimeSpan.FromSeconds(60));
        await using var verification = database.Context();
        Assert.Equal(9, (await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Source && x.ProductId == productId)).StockOnHand);
        Assert.Equal(11, (await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Destination && x.ProductId == productId)).StockOnHand);
        Assert.Equal(8, await verification.StockMovements.CountAsync(x => x.Kind == InventoryPostingKind.TransferOut || x.Kind == InventoryPostingKind.TransferIn));
        await new EfInventoryReconciliation(verification).ReconcileAsync();
    }

    [Fact]
    public async Task OppositeDirectionsUseOneGlobalOrderAndConserveBothWarehouses()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        StockTransferResult outward, inward;
        await using (var setup = database.Context())
        {
            var workflow = StockTransferTestSupport.Workflow(setup, seed.Procurement.Operator);
            outward = await workflow.CreateAsync("a-to-b", seed.Draft());
            inward = await workflow.CreateAsync("b-to-a", new(seed.Destination, seed.Source, [new(seed.Product, 1)]));
        }
        var barrier = new InventoryTransactionBarrier();
        await using var one = database.Context(barrier); await using var two = database.Context(barrier);
        await Task.WhenAll(StockTransferTestSupport.Workflow(one, seed.Procurement.Operator).PostAsync(outward.Id, "out-post", new(outward.RowVersion)),
            StockTransferTestSupport.Workflow(two, seed.Procurement.Operator).PostAsync(inward.Id, "in-post", new(inward.RowVersion))).WaitAsync(TimeSpan.FromSeconds(60));
        await using var verification = database.Context();
        var source = await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Source);
        var destination = await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == seed.Destination);
        Assert.Equal((6, 3, 6, 0), (source.StockOnHand, source.ReservedQuantity, destination.StockOnHand, destination.ReservedQuantity));
        Assert.Equal(0, await verification.StockMovements.Where(x => x.Kind == InventoryPostingKind.TransferOut || x.Kind == InventoryPostingKind.TransferIn).SumAsync(x => (long)x.PhysicalDelta));
        await new EfInventoryReconciliation(verification).ReconcileAsync();
    }
}
