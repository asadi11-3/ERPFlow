using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class InventoryCutoverSafetyTests
{
    [Fact]
    public async Task EmptyFreshDatabaseCanInitializeAuthorityWithoutInventingStock()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        Guid actor;
        await using (var setup = database.Context())
        {
            var user = new AppUser("Admin", "fresh-cutover@example.test", UserRole.Administrator, "test-hash");
            actor = user.Id;
            setup.AppUsers.Add(user);
            await setup.SaveChangesAsync();
        }
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, actor);
        await using var context = database.Context();
        var result = await InventoryCutoverTestSupport.Cutover(context, actor).RehearseAsync(approval);
        Assert.Equal(0, result.StockRows);
        Assert.Equal(0, result.ReservationRows);
        Assert.Empty(await context.StockMovements.ToListAsync());
        Assert.Equal(InventoryAuthorityMode.Warehouse, (await context.InventoryAuthorities.SingleAsync()).Mode);
    }

    [Fact]
    public async Task ConcurrentDuplicateCutoverExecutesOneImport()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using var first = database.Context();
        await using var second = database.Context();
        var results = await Task.WhenAll(InventoryCutoverTestSupport.Cutover(first, seed.ActorId).RehearseAsync(approval),
            InventoryCutoverTestSupport.Cutover(second, seed.ActorId).RehearseAsync(approval));
        Assert.Equal(results[0], results[1]);
        await using var verification = database.Context();
        Assert.Single(await verification.StockMovements.ToListAsync());
        Assert.Equal(2, await verification.InventoryReservations.CountAsync());
    }

    [Fact]
    public async Task SharedWorkflowFenceBlocksCutoverUntilTheLegacyTransactionFinishes()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using var legacy = database.Context();
        await using var transaction = await InventoryAuthoritySession.BeginAsync(legacy);
        await using var waiting = database.Context();
        var cutover = InventoryCutoverTestSupport.Cutover(waiting, seed.ActorId).RehearseAsync(approval);
        var committed = await legacy.InventoryAuthorities.AsNoTracking().SingleAsync();
        Assert.Equal(InventoryAuthorityMode.Product, committed.Mode);
        Assert.False(cutover.IsCompleted);
        await transaction.CommitAsync();
        await cutover.WaitAsync(TimeSpan.FromSeconds(30));
        await transaction.DisposeAsync();
        await using var subsequent = database.Context();
        await using var after = await InventoryAuthoritySession.BeginAsync(subsequent);
        Assert.True(InventoryCutoverTestSupport.Workflow(subsequent, seed.ActorId).UsesWarehouseStock);
    }

    [Fact]
    public async Task ReconciliationRejectsTamperedBalancesRatherThanRepairingThem()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var context = database.Context())
            await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        await using var tamper = database.Context();
        await using (var transaction = await InventoryAuthoritySession.BeginAsync(tamper))
        {
            await tamper.Database.ExecuteSqlRawAsync("UPDATE WarehouseStocks SET StockOnHand=StockOnHand+1 WHERE Sequence>0;");
            await transaction.CommitAsync();
        }
        await Assert.ThrowsAsync<WorkflowConflictException>(() => new EfInventoryReconciliation(tamper).ReconcileAsync());
        Assert.Equal(51, (await tamper.WarehouseStocks.SingleAsync(x => x.Sequence > 0)).StockOnHand);
        Assert.Single(await tamper.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task VersionMarkerAloneCannotBypassTheTransactionFence()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var context = database.Context())
            await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        await using var unfenced = database.Context();
        var exception = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => unfenced.Database.ExecuteSqlRawAsync(
            "EXEC sys.sp_set_session_context @key=N'ERPFlow.InventoryWriterVersion',@value=2; UPDATE WarehouseStocks SET StockOnHand=StockOnHand+1 WHERE Sequence>0;"));
        Assert.Equal(51002, exception.Number);
        Assert.Equal(50, (await unfenced.WarehouseStocks.SingleAsync(x => x.Sequence > 0)).StockOnHand);
    }

    [Fact]
    public async Task StaleTrackedAuthorityReplaysCompletedCutoverWithoutReimporting()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using var stale = database.Context();
        Assert.Equal(InventoryAuthorityMode.Product, (await stale.InventoryAuthorities.SingleAsync()).Mode);
        InventoryCutoverResult completed;
        await using (var context = database.Context())
            completed = await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        Assert.Equal(completed, await InventoryCutoverTestSupport.Cutover(stale, seed.ActorId).RehearseAsync(approval));
        Assert.Single(await stale.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task MatchingDatabaseNamePrefixDoesNotGrantRehearsalPermission()
    {
        var options = new DbContextOptionsBuilder<ERPFlowDbContext>().UseSqlServer(
            "Server=(localdb)\\MSSQLLocalDB;Database=ERPFlow_InventoryFoundation_NotOwned;Integrated Security=True;TrustServerCertificate=True").Options;
        await using var foreign = new ERPFlowDbContext(options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InventoryCutoverTestSupport.Cutover(foreign, Guid.NewGuid())
            .RehearseAsync(new(Guid.NewGuid(), new string('0', 64), LegacyDefaultWarehouse.Id, "not an owned fixture", 2)));
    }
}
