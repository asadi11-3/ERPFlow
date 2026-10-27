using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Transfers;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class StockTransferSafetyTests
{
    [Fact]
    public async Task ReservedStockCannotBeTransferredAndFailureLeavesZeroEffects()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = StockTransferTestSupport.Workflow(context, seed.Procurement.Operator);
        var draft = await workflow.CreateAsync("insufficient-create", seed.Draft(8));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.PostAsync(draft.Id, "insufficient-post", new(draft.RowVersion)));
        var source = await context.WarehouseStocks.AsNoTracking().SingleAsync(x => x.WarehouseId == seed.Source);
        var destination = await context.WarehouseStocks.AsNoTracking().SingleAsync(x => x.WarehouseId == seed.Destination);
        Assert.Equal((10, 3, 2, 0), (source.StockOnHand, source.ReservedQuantity, destination.StockOnHand, destination.ReservedQuantity));
        Assert.Empty(await context.InventoryPostings.Where(x => x.StockTransferItemId != null).ToListAsync());
        Assert.Equal(StockTransferStatus.Draft, (await workflow.GetAsync(draft.Id)).Status);
        Assert.Single(await context.StockTransferCommands.ToListAsync());
        await new EfInventoryReconciliation(context).ReconcileAsync();
    }
    [Fact]
    public async Task DraftCanExistBeforeCutoverButPostCannotUseProductOrPermissiveGate()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database, cutover: false);
        await using var context = database.Context();
        var before = (await context.Products.AsNoTracking().SingleAsync()).RowVersion;
        var workflow = StockTransferTestSupport.Workflow(context, seed.Procurement.Operator, new PermissiveGate());
        var draft = await workflow.CreateAsync("precutover-create", seed.Draft());
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.PostAsync(draft.Id, "precutover-post", new(draft.RowVersion)));
        Assert.Equal(StockTransferStatus.Draft, (await workflow.GetAsync(draft.Id)).Status);
        Assert.All(await context.WarehouseStocks.ToListAsync(), x => Assert.Equal((0, 0L), (x.StockOnHand, x.Sequence)));
        Assert.Empty(await context.StockMovements.ToListAsync());
        Assert.Equal(before, (await context.Products.AsNoTracking().SingleAsync()).RowVersion);
    }
    [Fact]
    public async Task ProductionGateCannotPostEvenWithACompletedCutoverAndValidDocument()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = StockTransferTestSupport.Workflow(context, seed.Procurement.Operator, new ProductAuthorityWriteGate());
        var draft = await workflow.CreateAsync("production-draft", seed.Draft());
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.PostAsync(draft.Id, "production-post", new(draft.RowVersion)));
        Assert.Empty(await context.InventoryPostings.Where(x => x.StockTransferItemId != null).ToListAsync());
    }
    [Fact]
    public async Task CancelledDocumentsRejectPostingAndTheirOldVersionIsStale()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = StockTransferTestSupport.Workflow(context, seed.Procurement.Operator);
        var draft = await workflow.CreateAsync("cancel-create", seed.Draft());
        var cancelled = await workflow.CancelAsync(draft.Id, new(draft.RowVersion, "Wrong shipment"));
        Assert.Equal(StockTransferStatus.Cancelled, cancelled.Status);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => workflow.PostAsync(draft.Id, "stale-post", new(draft.RowVersion)));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.PostAsync(draft.Id, "cancelled-post", new(cancelled.RowVersion)));
        Assert.Empty(await context.InventoryPostings.Where(x => x.StockTransferItemId != null).ToListAsync());
    }
    [Fact]
    public async Task InactiveAndMissingWarehousesRejectCreationAndSourceStockNeverFallsBack()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var masters = ProcurementTestSupport.Masters(context, seed.Procurement.Inventory.ActorId);
        var unused = await masters.CreateWarehouseAsync(new("TRANSFER-INACTIVE", "Inactive"));
        await masters.SetWarehouseActiveAsync(unused.Id, new(unused.RowVersion), false);
        var workflow = StockTransferTestSupport.Workflow(context, seed.Procurement.Operator);
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.CreateAsync("inactive", seed.Draft() with { DestinationWarehouseId = unused.Id }));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => workflow.CreateAsync("missing-wh", seed.Draft() with { SourceWarehouseId = Guid.NewGuid() }));
        var empty = await masters.CreateWarehouseAsync(new("TRANSFER-EMPTY", "No source stock"));
        var draft = await workflow.CreateAsync("missing-source", seed.Draft() with { SourceWarehouseId = empty.Id });
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.PostAsync(draft.Id, "post-missing-source", new(draft.RowVersion)));
        Assert.False(await context.WarehouseStocks.AnyAsync(x => x.WarehouseId == empty.Id));
        Assert.Empty(await context.InventoryPostings.Where(x => x.StockTransferItemId != null).ToListAsync());
    }
    [Fact]
    public async Task PersistedRoleRevocationRejectsMutationsAndCachedReplay()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = StockTransferTestSupport.Workflow(context, seed.Procurement.Operator);
        var draft = await workflow.CreateAsync("revoked-create", seed.Draft());
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE AppUsers SET Role=1 WHERE Id={seed.Procurement.Operator}");
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() => workflow.PostAsync(draft.Id, "revoked-post", new(draft.RowVersion)));
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() => workflow.CreateAsync("revoked-create", seed.Draft()));
        Assert.Empty(await context.InventoryPostings.Where(x => x.StockTransferItemId != null).ToListAsync());
    }
    [Fact]
    public async Task PostedDocumentAndPairedMovementsCannotBeModifiedOrDeleted()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var workflow = StockTransferTestSupport.Workflow(context, seed.Procurement.Operator);
        var draft = await workflow.CreateAsync("immutable-create", seed.Draft());
        var posted = await workflow.PostAsync(draft.Id, "immutable-post", new(draft.RowVersion));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.CancelAsync(draft.Id, new(posted.RowVersion, "Not a reversal")));
        Assert.Equal(51030, (await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE StockTransfers SET Number=N'CHANGED' WHERE Id={draft.Id}"))).Number);
        Assert.Equal(51030, (await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE StockMovements SET ResultRowVersion=0x0000000000000000 WHERE Id={posted.Items.Single().SourceMovementId}"))).Number);
        await new EfInventoryReconciliation(context).ReconcileAsync();
    }
    [Fact]
    public async Task DraftTransferBlocksBothWarehouseDeactivationCommitments()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await StockTransferTestSupport.SeedAsync(database, cutover: false);
        await using var context = database.Context();
        await StockTransferTestSupport.Workflow(context, seed.Procurement.Operator).CreateAsync("commitment", seed.Draft());
        var masters = ProcurementTestSupport.Masters(context, seed.Procurement.Inventory.ActorId);
        foreach (var id in new[] { seed.Source, seed.Destination })
        {
            var warehouse = await masters.GetWarehouseAsync(id);
            await Assert.ThrowsAsync<WorkflowConflictException>(() => masters.SetWarehouseActiveAsync(id, new(warehouse.RowVersion), false));
        }
    }
    private sealed class PermissiveGate : IInventoryWriteGate { public void EnsureWarehouseWritesAllowed() { } }
}
