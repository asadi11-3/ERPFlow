using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ERPFlow.Domain.Inventory;
using Xunit;

namespace ERPFlow.Tests;

public sealed partial class LegacyImportPreparationIntegrationTests
{
    [Fact]
    public async Task FailureAfterManifestSqlRollsBackAllPreparationRowsAndPreservesSource()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var id = Guid.NewGuid();
        await using (var context = database.Context(new FailManifestSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context, seed.ActorId).PrepareAsync(id));
        await using var verification = database.Context();
        Assert.Empty(await verification.LegacyInventoryImportBatches.ToListAsync());
        Assert.Empty(await verification.LegacyInventoryOpenings.ToListAsync());
        Assert.Empty(await verification.LegacyInventoryReservationImports.ToListAsync());
        Assert.Equal(6, (await verification.Products.SingleAsync()).ReservedQuantity);
        Assert.Single(await verification.StockReservations.ToListAsync());
        Assert.Empty(await verification.StockMovements.ToListAsync());
        Assert.Equal(id, (await Service(verification, seed.ActorId).PrepareAsync(id)).PreparationId);
    }

    private sealed class FailManifestSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<LegacyInventoryImportBatch>().Any())
                throw new InvalidOperationException("Injected failure after preparation SQL.");
            return ValueTask.FromResult(result);
        }
    }
}
