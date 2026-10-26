using System.Data.Common;
using ERPFlow.Application.Common;
using ERPFlow.Application.Service;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ERPFlow.Tests;

public sealed class InventoryCutoverOwnerRaceTests
{
    [Fact]
    public async Task ConcurrentWarehousePartsRequestsCannotDuplicateAnOwnerOrLeakReservedStock()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await database.SeedAsync();
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, seed.ActorId);
        await using (var context = database.Context())
            await InventoryCutoverTestSupport.Cutover(context, seed.ActorId).RehearseAsync(approval);
        Guid jobcardId;
        await using (var context = database.Context())
        {
            var customer = await context.Customers.SingleAsync();
            jobcardId = (await Workflow(context).CreateJobcardAsync(new(customer.Id, null, "Tech", "Owner race", JobcardPriority.Medium))).Id;
        }
        var barrier = new JobcardReadBarrier();
        await using var first = database.Context(barrier);
        await using var second = database.Context(barrier);
        var outcomes = await Task.WhenAll(Request(first), Request(second));
        Assert.Single(outcomes, x => x is null);
        Assert.Single(outcomes, x => x is ConcurrencyConflictException or WorkflowConflictException);
        await using var verification = database.Context();
        await new EfInventoryReconciliation(verification).ReconcileAsync();
        Assert.Single(await verification.JobcardItems.Where(x => x.JobcardId == jobcardId).ToListAsync());
        Assert.Equal(7, (await verification.WarehouseStocks.SingleAsync(x => x.WarehouseId == LegacyDefaultWarehouse.Id)).ReservedQuantity);
        Assert.Equal(2, await verification.StockMovements.CountAsync());

        ServiceWorkflow Workflow(ERPFlowDbContext context) => new(new EfServicePersistence(context),
            new InventoryTestDatabase.TestActor(seed.ActorId), new ProductAuthorityWarehouseSourcePolicy(context),
            InventoryCutoverTestSupport.Workflow(context, seed.ActorId));

        async Task<Exception?> Request(ERPFlowDbContext context)
        {
            try
            {
                await Workflow(context).RequestPartsAsync(jobcardId, new([new(seed.ProductId, 1, LegacyDefaultWarehouse.Id)]));
                return null;
            }
            catch (ConcurrencyConflictException exception) { return exception; }
            catch (WorkflowConflictException exception) { return exception; }
        }
    }

    private sealed class JobcardReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [Jobcards]", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
}
