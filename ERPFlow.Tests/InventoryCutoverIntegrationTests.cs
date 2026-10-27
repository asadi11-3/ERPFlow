using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ERPFlow.Tests;

public sealed class InventoryCutoverIntegrationTests
{
    [Fact]
    public void ProductionOperatorGateCannotBeEnabledByAnApprovalPayload()
    {
        Assert.Throws<WorkflowConflictException>(() => new DisabledInventoryCutoverPermission().EnsureOperatorExecutionAllowed());
        Assert.Throws<WorkflowConflictException>(() => new ProductAuthorityWriteGate().EnsureWarehouseWritesAllowed());
    }

    [Fact]
    public async Task AtomicImportCarriesReservedOpeningAndOriginalOwnersExactlyOnce()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        Guid reservationId;
        byte[] version;
        await using (var before = database.Context())
        {
            reservationId = (await before.StockReservations.SingleAsync()).Id;
            version = (await before.Products.SingleAsync()).RowVersion.ToArray();
        }
        InventoryCutoverResult result;
        await using (var context = database.Context())
            result = await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        await using (var replay = database.Context())
            Assert.Equal(result, await InventoryCutoverTestSupport.Cutover(replay, seed.ActorId).RehearseAsync(approval));
        await using var verification = database.Context();
        var imported = await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == LegacyDefaultWarehouse.Id);
        Assert.Equal(50, imported.StockOnHand);
        Assert.Equal(6, imported.ReservedQuantity);
        Assert.Equal(1, imported.Sequence);
        var movement = Assert.Single(await verification.StockMovements.ToListAsync());
        Assert.Equal(50, movement.PhysicalDelta);
        Assert.Equal(6, movement.ReservedDelta);
        var owners = await verification.InventoryReservations.ToArrayAsync();
        Assert.Equal(2, owners.Length);
        Assert.Equal(reservationId, Assert.Single(owners, x => x.SalesOrderId.HasValue).Id);
        Assert.Equal(seed.JobcardItemId, Assert.Single(owners, x => x.JobcardItemId.HasValue).JobcardItemId);
        Assert.Equal(6, owners.Sum(x => x.Quantity));
        Assert.Equal(version, (await verification.Products.SingleAsync()).RowVersion);
        Assert.Equal(6, (await verification.Products.SingleAsync()).ReservedQuantity);
        Assert.All(await verification.SalesOrderItems.ToListAsync(), x => Assert.Equal(LegacyDefaultWarehouse.Id, x.WarehouseId));
        Assert.Equal(InventoryAuthorityMode.Warehouse, (await verification.InventoryAuthorities.SingleAsync()).Mode);
        await new EfInventoryReconciliation(verification).ReconcileAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FailureAtAnyCutoverStageRollsBackOpeningsOwnersOriginsAndAuthority(int stage)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var failing = database.Context(new FailCutover(stage)))
            await Assert.ThrowsAsync<InvalidOperationException>(() => InventoryCutoverTestSupport.Cutover(failing, seed.ActorId).RehearseAsync(approval));
        await using (var verification = database.Context())
        {
            Assert.Equal(InventoryAuthorityMode.Product, (await verification.InventoryAuthorities.SingleAsync()).Mode);
            Assert.Empty(await verification.InventoryReservations.ToListAsync());
            Assert.Empty(await verification.InventoryPostings.ToListAsync());
            Assert.Empty(await verification.StockMovements.ToListAsync());
            Assert.All(await verification.WarehouseStocks.ToListAsync(), x => Assert.Equal(0, x.Sequence));
            Assert.All(await verification.SalesOrderItems.ToListAsync(), x => Assert.Null(x.WarehouseId));
            Assert.Equal(6, (await verification.Products.SingleAsync()).ReservedQuantity);
        }
        await using var retried = database.Context();
        await InventoryCutoverTestSupport.Cutover(retried, seed.ActorId).RehearseAsync(approval);
    }

    [Fact]
    public async Task StaleSourceAndTamperedManifestNeverApplyCutover()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var change = database.Context())
        {
            (await change.Products.SingleAsync()).SetStockOnHand(49);
            await change.SaveChangesAsync();
        }
        await using (var stale = database.Context())
            await Assert.ThrowsAsync<WorkflowConflictException>(() => InventoryCutoverTestSupport.Cutover(stale, seed.ActorId).RehearseAsync(approval));
        var fresh = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var tamper = database.Context())
            await tamper.Database.ExecuteSqlInterpolatedAsync($"UPDATE LegacyInventoryOpenings SET StockOnHand=999 WHERE BatchId={fresh.PreparationId}");
        await using var context = database.Context();
        await Assert.ThrowsAsync<WorkflowConflictException>(() => InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(fresh));
        Assert.Equal(InventoryAuthorityMode.Product, (await context.InventoryAuthorities.SingleAsync()).Mode);
        Assert.Empty(await context.InventoryPostings.ToListAsync());
    }

    [Fact]
    public async Task MissingPhysicalApprovalAndIncompatibleVersionsAreRejected()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var context = database.Context())
        {
            await Assert.ThrowsAsync<WorkflowValidationException>(() => InventoryCutoverTestSupport.Cutover(context, seed.ActorId)
                .RehearseAsync(approval with { PhysicalAttributionApproval = "" }));
            await Assert.ThrowsAsync<WorkflowValidationException>(() => InventoryCutoverTestSupport.Cutover(context, seed.ActorId)
                .RehearseAsync(approval with { ApplicationWriterVersion = 1 }));
            await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        }
        await using var incompatible = database.Context();
        await using var transaction = await incompatible.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<WorkflowConflictException>(() => InventoryAuthoritySession.EnterAsync(incompatible, false, applicationWriterVersion: 1));
        await transaction.RollbackAsync();
        var sql = await Assert.ThrowsAsync<SqlException>(() => incompatible.Database.ExecuteSqlRawAsync(
            "EXEC sys.sp_set_session_context @key=N'ERPFlow.InventoryWriterVersion',@value=1; UPDATE Jobcards SET Revision=Revision+1;"));
        Assert.Equal(51002, sql.Number);
    }

    [Fact]
    public async Task ProductWritersCannotRunAfterCutoverEvenWithCurrentProtocol()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var context = database.Context())
            await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        await using (var context = database.Context())
        {
            await using var transaction = await InventoryAuthoritySession.BeginAsync(context);
            (await context.Products.SingleAsync()).ReserveQuantity(1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
        await using var raw = database.Context();
        var sql = await Assert.ThrowsAsync<SqlException>(() => raw.Database.ExecuteSqlRawAsync(
            "EXEC sys.sp_set_session_context @key=N'ERPFlow.InventoryWriterVersion',@value=2; UPDATE Products SET StockOnHand=StockOnHand+1;"));
        Assert.Equal(51001, sql.Number);
    }

    private sealed class FailCutover(int stage) : SaveChangesInterceptor
    {
        private int saves;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref saves) == stage + 1)
                throw new InvalidOperationException("Injected atomic cutover failure.");
            return ValueTask.FromResult(result);
        }
    }
}
