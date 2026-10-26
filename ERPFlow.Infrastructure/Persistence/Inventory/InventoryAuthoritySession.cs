using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public static class InventoryAuthoritySession
{
    public static async Task<IApplicationTransaction> BeginAsync(ERPFlowDbContext context, CancellationToken ct = default)
    {
        var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            await EnterAsync(context, false, ct);
            return new EfApplicationTransaction(transaction, context);
        }
        catch
        {
            await transaction.DisposeAsync();
            context.ObservedInventoryAuthority = null;
            throw;
        }
    }

    public static async Task EnterAsync(ERPFlowDbContext context, bool exclusive, CancellationToken ct = default,
        int applicationWriterVersion = InventoryAuthority.SupportedWriterVersion)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Inventory authority fencing requires a transaction.");
        var mode = exclusive ? "Exclusive" : "Shared";
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'ERPFlow.Inventory.Authority.v2',
                @LockMode={mode}, @LockOwner=N'Transaction', @LockTimeout=30000;
            IF @result < 0 THROW 51000, 'Inventory authority lock could not be acquired.', 1;
            EXEC sys.sp_set_session_context @key=N'ERPFlow.InventoryWriterVersion', @value={applicationWriterVersion};
            """, ct);
        var authority = await context.InventoryAuthorities.AsNoTracking().SingleAsync(ct);
        if (authority.MinimumWriterVersion > applicationWriterVersion)
            throw new WorkflowConflictException("This application version is incompatible with the inventory authority.");
        context.ObservedInventoryAuthority = authority.Mode;
    }
}
