using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ERPFlow.Tests;

public sealed class InventoryPostingIntegrationTests
{
    [Fact]
    public async Task ApplicationRegistrationRefusesWarehouseWritesWithoutTouchingInventory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(_ => "Server=unreachable;Database=Forbidden;Integrated Security=True");
        services.AddSingleton<ERPFlow.Application.Abstractions.ICurrentUser>(new InventoryTestDatabase.TestActor(Guid.NewGuid()));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var poster = scope.ServiceProvider.GetRequiredService<IInventoryPosting>();
        await Assert.ThrowsAsync<WorkflowConflictException>(() => poster.PostAsync(
            new InventoryPostingRequest("disabled", InventoryPostingKind.Opening, Guid.NewGuid(), Guid.NewGuid(), 1, new byte[8])));
    }

    [Fact]
    public async Task OpeningReserveConsumeAndReplayReconcileWithoutChangingLegacyProduct()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using (var context = database.Context())
        {
            var poster = InventoryTestDatabase.Poster(context, seed.ActorId);
            var opening = await poster.PostAsync(seed.Opening());
            var reservation = await poster.PostAsync(seed.Reserve(opening.RowVersion));
            var consumed = await poster.PostAsync(new InventoryPostingRequest("consume", InventoryPostingKind.Consume,
                seed.WarehouseId, seed.ProductId, 3, reservation.RowVersion, seed.SalesOrderId, ReservationId: reservation.ReservationId));
            Assert.Equal(7, consumed.StockOnHand);
            Assert.Equal(0, consumed.ReservedQuantity);
            var replay = await poster.PostAsync(seed.Opening());
            Assert.Equal(opening.PostingId, replay.PostingId);
            Assert.Equal(10, replay.StockOnHand);
            Assert.Equal(opening.RowVersion, replay.RowVersion);
            await Assert.ThrowsAsync<WorkflowConflictException>(() => poster.PostAsync(seed.Opening(11)));
        }
        await using var verification = database.Context();
        var stock = await verification.WarehouseStocks.SingleAsync(x => x.Id == seed.StockId);
        var movements = await verification.StockMovements.Where(x => x.WarehouseStockId == seed.StockId).OrderBy(x => x.Sequence).ToListAsync();
        Assert.Equal(stock.StockOnHand, movements.Sum(x => x.PhysicalDelta));
        Assert.Equal(stock.ReservedQuantity, movements.Sum(x => x.ReservedDelta));
        Assert.Equal(new long[] { 1, 2, 3 }, movements.Select(x => x.Sequence));
        Assert.Equal(InventoryReservationStatus.Consumed, (await verification.InventoryReservations.SingleAsync()).Status);
        var product = await verification.Products.SingleAsync();
        Assert.Equal(50, product.StockOnHand);
        Assert.Equal(6, product.ReservedQuantity);
        Assert.Equal(3, await verification.InventoryPostings.CountAsync());
    }

    [Fact]
    public async Task JobcardOwnerReleaseAndLaterSalesCycleRetainClosedHistory()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        var poster = InventoryTestDatabase.Poster(context, seed.ActorId);
        var opening = await poster.PostAsync(seed.Opening());
        var part = await poster.PostAsync(new InventoryPostingRequest("jobcard", InventoryPostingKind.Reserve,
            seed.WarehouseId, seed.ProductId, 3, opening.RowVersion, JobcardItemId: seed.JobcardItemId));
        var releasedPart = await poster.PostAsync(new InventoryPostingRequest("job-release", InventoryPostingKind.Release,
            seed.WarehouseId, seed.ProductId, 3, part.RowVersion, JobcardItemId: seed.JobcardItemId, ReservationId: part.ReservationId));
        var firstSales = await poster.PostAsync(seed.Reserve(releasedPart.RowVersion));
        var released = await poster.PostAsync(new InventoryPostingRequest("sales-release", InventoryPostingKind.Release,
            seed.WarehouseId, seed.ProductId, 3, firstSales.RowVersion, seed.SalesOrderId, ReservationId: firstSales.ReservationId));
        await poster.PostAsync(seed.Reserve(released.RowVersion, "sales-cycle-2"));
        Assert.Equal(3, await context.InventoryReservations.CountAsync());
        Assert.Equal(1, await context.InventoryReservations.CountAsync(x => x.Status == InventoryReservationStatus.Active));
        Assert.Equal(3, await context.InventoryReservations.Where(x => x.Status == InventoryReservationStatus.Active).SumAsync(x => x.Quantity));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReservationCannotBeConsumedFromAnotherWarehouseOrOwner(bool wrongWarehouse)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        var poster = InventoryTestDatabase.Poster(context, seed.ActorId);
        var opening = await poster.PostAsync(seed.Opening());
        var otherOpening = await poster.PostAsync(new InventoryPostingRequest("other-opening", InventoryPostingKind.Opening,
            seed.OtherWarehouseId, seed.ProductId, 10, seed.OtherVersion));
        var reservation = await poster.PostAsync(seed.Reserve(opening.RowVersion));
        var request = new InventoryPostingRequest("wrong", InventoryPostingKind.Consume,
            wrongWarehouse ? seed.OtherWarehouseId : seed.WarehouseId, seed.ProductId, 3,
            wrongWarehouse ? otherOpening.RowVersion : reservation.RowVersion,
            wrongWarehouse ? seed.SalesOrderId : seed.OtherOrderId, ReservationId: reservation.ReservationId);
        await Assert.ThrowsAsync<WorkflowConflictException>(() => poster.PostAsync(request));
        context.ChangeTracker.Clear();
        Assert.Equal(InventoryReservationStatus.Active, (await context.InventoryReservations.SingleAsync()).Status);
        Assert.All(await context.WarehouseStocks.ToListAsync(), x => Assert.Equal(10, x.StockOnHand));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task MovementFailureRollsBackStockPostingAndReplayClaim(int failureStage, bool reserve)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var request = seed.Opening();
        if (reserve)
        {
            await using var setup = database.Context();
            var opening = await InventoryTestDatabase.Poster(setup, seed.ActorId).PostAsync(request);
            request = seed.Reserve(opening.RowVersion);
        }
        await using (var failing = database.Context(new FailMovementSave(failureStage)))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                InventoryTestDatabase.Poster(failing, seed.ActorId).PostAsync(request));
        }
        await using var verification = database.Context();
        var stock = await verification.WarehouseStocks.SingleAsync(x => x.Id == seed.StockId);
        Assert.Equal(reserve ? 10 : 0, stock.StockOnHand);
        Assert.Equal(0, stock.ReservedQuantity);
        Assert.Equal(reserve ? 1 : 0, stock.Sequence);
        Assert.Equal(reserve ? 1 : 0, await verification.InventoryPostings.CountAsync());
        Assert.Equal(reserve ? 1 : 0, await verification.StockMovements.CountAsync());
        Assert.Empty(await verification.InventoryReservations.ToListAsync());
        var retried = await InventoryTestDatabase.Poster(verification, seed.ActorId).PostAsync(request);
        Assert.Equal(10, retried.StockOnHand);
    }

    [Fact]
    public async Task CallerTransactionCanRollBackTheEntirePosting()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using (var context = database.Context())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await InventoryTestDatabase.Poster(context, seed.ActorId).PostAsync(seed.Opening());
            await transaction.RollbackAsync();
        }
        await using var verification = database.Context();
        Assert.Empty(await verification.StockMovements.ToListAsync());
        Assert.Empty(await verification.InventoryPostings.ToListAsync());
        Assert.Equal(0, (await verification.WarehouseStocks.SingleAsync(x => x.Id == seed.StockId)).StockOnHand);
    }

    [Fact]
    public async Task OrdinaryEfSavesCannotBypassPostingOrRewriteHistory()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        await InventoryTestDatabase.Poster(context, seed.ActorId).PostAsync(seed.Opening());
        var stock = await context.WarehouseStocks.SingleAsync(x => x.Id == seed.StockId);
        stock.Apply(InventoryPostingKind.Reserve, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        var movement = await context.StockMovements.SingleAsync();
        context.StockMovements.Remove(movement);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        var posting = await context.InventoryPostings.SingleAsync();
        context.Entry(posting).Property(x => x.IdempotencyKey).CurrentValue = "rewritten";
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostingRejectsUnrecordedInventoryChangesAlreadyTrackedByItsCaller(bool warehouseChange)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using (var context = database.Context())
        {
            if (warehouseChange)
            {
                var warehouse = await context.Warehouses.SingleAsync(x => x.Id == seed.WarehouseId);
                context.Entry(warehouse).Property(x => x.IsActive).CurrentValue = false;
            }
            else
            {
                var other = await context.WarehouseStocks.SingleAsync(x => x.Id == seed.OtherStockId);
                other.Apply(InventoryPostingKind.Opening, 8);
            }
            await Assert.ThrowsAsync<WorkflowConflictException>(() =>
                InventoryTestDatabase.Poster(context, seed.ActorId).PostAsync(seed.Opening()));
        }
        await using var verification = database.Context();
        Assert.All(await verification.WarehouseStocks.ToListAsync(), x => Assert.Equal(0, x.StockOnHand));
        Assert.True((await verification.Warehouses.SingleAsync(x => x.Id == seed.WarehouseId)).IsActive);
        Assert.Empty(await verification.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task PostingRejectsPendingLegacyBalanceWrites()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using (var context = database.Context())
        {
            var product = await context.Products.SingleAsync();
            product.ReserveQuantity(1);
            await Assert.ThrowsAsync<WorkflowConflictException>(() =>
                InventoryTestDatabase.Poster(context, seed.ActorId).PostAsync(seed.Opening()));
        }
        await using var verification = database.Context();
        Assert.Equal(6, (await verification.Products.SingleAsync()).ReservedQuantity);
        Assert.Empty(await verification.StockMovements.ToListAsync());
    }

    private sealed class FailMovementSave(int stage) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (stage == 0 && eventData.Context!.ChangeTracker.Entries<WarehouseStock>().Any(x => x.State == EntityState.Modified))
                throw new InvalidOperationException("Injected failure before stock/reservation SQL.");
            if (stage == 1 && eventData.Context!.ChangeTracker.Entries<StockMovement>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Injected movement failure before SQL insertion.");
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (stage == 2 && eventData.Context!.ChangeTracker.Entries<StockMovement>().Any())
                throw new InvalidOperationException("Injected movement failure after SQL insertion.");
            return ValueTask.FromResult(result);
        }
    }
}
