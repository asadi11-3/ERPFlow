using System.Data.Common;
using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ERPFlow.Tests;

public sealed class InventoryConcurrencyIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentReservationsDoNotOversellAndIdenticalRequestsReplay(bool sameKey)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        InventoryPostingResult opening;
        await using (var context = database.Context())
            opening = await InventoryTestDatabase.Poster(context, seed.ActorId).PostAsync(seed.Opening(5));
        var barrier = new StockReadBarrier();
        await using var first = database.Context(barrier);
        await using var second = database.Context(barrier);
        var request = seed.Reserve(opening.RowVersion, "first");
        var competing = sameKey ? request : seed.Reserve(opening.RowVersion, "second", seed.OtherOrderId);
        var outcomes = await Task.WhenAll(
            CaptureAsync(InventoryTestDatabase.Poster(first, seed.ActorId), request),
            CaptureAsync(InventoryTestDatabase.Poster(second, seed.ActorId), competing));
        if (sameKey)
        {
            Assert.All(outcomes, x => Assert.Null(x.Error));
            Assert.Equal(outcomes[0].Result!.PostingId, outcomes[1].Result!.PostingId);
            Assert.Equal(outcomes[0].Result!.ReservationId, outcomes[1].Result!.ReservationId);
        }
        else
        {
            Assert.Single(outcomes, x => x.Error is null);
            Assert.Single(outcomes, x => x.Error is ConcurrencyConflictException);
        }
        await using var verification = database.Context();
        var stock = await verification.WarehouseStocks.SingleAsync(x => x.Id == seed.StockId);
        Assert.Equal(5, stock.StockOnHand);
        Assert.Equal(3, stock.ReservedQuantity);
        Assert.Single(await verification.InventoryReservations.ToListAsync());
        Assert.Equal(2, await verification.StockMovements.CountAsync());
        Assert.Equal(2, await verification.InventoryPostings.CountAsync());
    }

    [Fact]
    public async Task StaleVersionAndUnprovisionedWarehouseNeverMutateAnotherStockRow()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        await using var context = database.Context();
        var poster = InventoryTestDatabase.Poster(context, seed.ActorId);
        await poster.PostAsync(seed.Opening());
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => poster.PostAsync(seed.Reserve(seed.Version)));
        await Assert.ThrowsAsync<WorkflowConflictException>(() => poster.PostAsync(
            seed.Reserve(seed.Version) with { WarehouseId = Guid.NewGuid() }));
        Assert.Empty(await context.InventoryReservations.ToListAsync());
        Assert.Single(await context.StockMovements.ToListAsync());
    }

    private static async Task<Outcome> CaptureAsync(IInventoryPosting poster, InventoryPostingRequest request)
    {
        try { return new Outcome(await poster.PostAsync(request), null); }
        catch (ConcurrencyConflictException exception) { return new Outcome(null, exception); }
    }

    private sealed record Outcome(InventoryPostingResult? Result, Exception? Error);

    private sealed class StockReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [WarehouseStocks]", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref arrivals) == 2) completion.TrySetResult();
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
}
