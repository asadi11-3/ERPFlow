using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERPFlow.Infrastructure.Persistence;

internal sealed class EfApplicationTransaction(IDbContextTransaction transaction, ERPFlowDbContext? context = null) : IApplicationTransaction
{
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            throw new ConcurrencyConflictException(
                "The sales order operation conflicted with another transaction. Retry the operation.",
                exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        if (context is not null) context.ObservedInventoryAuthority = null;
    }
}
