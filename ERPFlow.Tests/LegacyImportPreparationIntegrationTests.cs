using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed partial class LegacyImportPreparationIntegrationTests
{
    [Fact]
    public async Task PreparationStagesBalancedOpeningsAndExplicitOwnersWithoutApplyingInventory()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        byte[] version;
        Guid sourceReservation;
        await using (var before = database.Context())
        {
            version = (await before.Products.SingleAsync()).RowVersion.ToArray();
            sourceReservation = (await before.StockReservations.SingleAsync()).Id;
        }
        var id = Guid.NewGuid();
        await using (var context = database.Context())
        {
            var service = Service(context, seed.ActorId);
            var preflight = await service.PreflightAsync();
            Assert.True(preflight.CanPrepare);
            var balance = Assert.Single(preflight.Products);
            Assert.Equal(6, balance.ReservedQuantity);
            Assert.Equal(3, balance.ActiveSalesQuantity);
            Assert.Equal(3, balance.ActiveServiceQuantity);
            var prepared = await service.PrepareAsync(id);
            Assert.Equal(preflight.SourceFingerprint, prepared.SourceFingerprint);
            Assert.Equal(prepared, await service.PrepareAsync(id));
        }
        await using var verification = database.Context();
        var opening = Assert.Single(await verification.LegacyInventoryOpenings.ToListAsync());
        Assert.Equal(50, opening.StockOnHand);
        Assert.Equal(6, opening.ReservedQuantity);
        Assert.Equal(version, opening.SourceProductRowVersion);
        Assert.Equal(LegacyDefaultWarehouse.Id, opening.WarehouseId);
        var owners = await verification.LegacyInventoryReservationImports.ToListAsync();
        Assert.Equal(2, owners.Count);
        Assert.Equal(6, owners.Where(x => x.Disposition == LegacyReservationDisposition.Active).Sum(x => x.Quantity));
        var sales = Assert.Single(owners, x => x.SourceKind == LegacyReservationSource.SalesReservation);
        Assert.Equal(sourceReservation, sales.TargetReservationId);
        Assert.Equal(seed.SalesOrderId, sales.SalesOrderId);
        Assert.Null(sales.JobcardItemId);
        var part = Assert.Single(owners, x => x.SourceKind == LegacyReservationSource.JobcardItem);
        Assert.Equal(seed.JobcardItemId, part.JobcardItemId);
        Assert.Null(part.SalesOrderId);
        Assert.Null(part.SourceCreatedAt);
        Assert.All(owners, x => Assert.True(x.WarehouseInferred));
        Assert.Single(await verification.LegacyInventoryImportBatches.ToListAsync());
        Assert.Equal(version, (await verification.Products.SingleAsync()).RowVersion);
        Assert.All(await verification.WarehouseStocks.ToListAsync(), x => Assert.Equal(0, x.Sequence));
        Assert.Empty(await verification.InventoryReservations.ToListAsync());
        Assert.Empty(await verification.InventoryPostings.ToListAsync());
        Assert.Empty(await verification.StockMovements.ToListAsync());
        Assert.Null((await verification.StockReservations.SingleAsync()).WarehouseId);
    }

    [Fact]
    public async Task SourceMismatchAbortsPreparationInsteadOfRepairingBalances()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        (await context.Products.SingleAsync()).ReserveQuantity(1);
        await context.SaveChangesAsync();
        var service = Service(context, seed.ActorId);
        var report = await service.PreflightAsync();
        Assert.False(report.CanPrepare);
        Assert.Contains(report.Issues, x => x.Code == "ReservationTotalMismatch");
        await Assert.ThrowsAsync<WorkflowConflictException>(() => service.PrepareAsync(Guid.NewGuid()));
        Assert.Empty(await context.LegacyInventoryImportBatches.ToListAsync());
        Assert.Empty(await context.LegacyInventoryOpenings.ToListAsync());
        Assert.Equal(7, (await context.Products.SingleAsync()).ReservedQuantity);
    }

    [Fact]
    public async Task AggregateEqualityDoesNotHideMissingSalesOwnership()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE StockReservations SET IsReleased=1,ReleasedAt=SYSUTCDATETIME() WHERE SalesOrderId={seed.SalesOrderId}");
        (await context.Products.SingleAsync()).ReleaseReservedQuantity(3);
        await context.SaveChangesAsync();
        var report = await Service(context, seed.ActorId).PreflightAsync();
        Assert.DoesNotContain(report.Issues, x => x.Code == "ReservationTotalMismatch");
        Assert.Contains(report.Issues, x => x.Code == "MissingOrMismatchedSalesReservation");
        await Assert.ThrowsAsync<WorkflowConflictException>(() => Service(context, seed.ActorId).PrepareAsync(Guid.NewGuid()));
        Assert.Empty(await context.LegacyInventoryImportBatches.ToListAsync());
    }

    [Fact]
    public async Task ChangedSourceMakesExistingPreparationStaleWithoutOverwritingIt()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var id = Guid.NewGuid();
        await using (var context = database.Context())
        {
            var service = Service(context, seed.ActorId);
            var first = await service.PrepareAsync(id);
            (await context.Products.SingleAsync()).SetStockOnHand(49);
            await context.SaveChangesAsync();
            var report = await service.PreflightAsync(id);
            Assert.False(report.CanPrepare);
            Assert.Contains(report.Issues, x => x.Code == "PreparationStale");
            await Assert.ThrowsAsync<WorkflowConflictException>(() => service.PrepareAsync(id));
            var replacement = await service.PrepareAsync(Guid.NewGuid());
            Assert.NotEqual(first.SourceFingerprint, replacement.SourceFingerprint);
        }
        await using var verification = database.Context();
        Assert.Equal(50, (await verification.LegacyInventoryOpenings.SingleAsync(x => x.BatchId == id)).StockOnHand);
        Assert.Equal(2, await verification.LegacyInventoryImportBatches.CountAsync());
        Assert.Empty(await verification.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentPreparationWithSameIdHasOneImmutableManifest()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var id = Guid.NewGuid();
        await using var first = database.Context();
        await using var second = database.Context();
        var results = await Task.WhenAll(Service(first, seed.ActorId).PrepareAsync(id), Service(second, seed.ActorId).PrepareAsync(id));
        Assert.Equal(results[0], results[1]);
        await using var verification = database.Context();
        Assert.Single(await verification.LegacyInventoryImportBatches.ToListAsync());
        Assert.Single(await verification.LegacyInventoryOpenings.ToListAsync());
        Assert.Equal(2, await verification.LegacyInventoryReservationImports.CountAsync());
    }

    [Fact]
    public async Task OnlyPersistedAdministratorCanPrepareOrReadPreflight()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        await database.SeedAsync();
        await using var context = database.Context();
        var sales = new AppUser("Sales", "prepare-sales@example.test", UserRole.Sales, "hash");
        context.AppUsers.Add(sales);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() => Service(context, sales.Id).PreflightAsync());
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() => Service(context, sales.Id).PrepareAsync(Guid.NewGuid()));
        Assert.Empty(await context.LegacyInventoryImportBatches.ToListAsync());
    }

    private static EfLegacyInventoryImportPreparation Service(ERPFlowDbContext context, Guid actor) =>
        new(context, new InventoryTestDatabase.TestActor(actor));
}
