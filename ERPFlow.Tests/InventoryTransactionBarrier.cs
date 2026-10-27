using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERPFlow.Tests;

internal sealed class InventoryTransactionBarrier : DbCommandInterceptor
{
    private readonly ConcurrentDictionary<Guid, byte> contexts = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("ERPFlow.Inventory.Authority.v2", StringComparison.Ordinal) &&
            contexts.TryAdd(eventData.Context!.ContextId.InstanceId, 0))
        {
            if (contexts.Count == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        return result;
    }
}
