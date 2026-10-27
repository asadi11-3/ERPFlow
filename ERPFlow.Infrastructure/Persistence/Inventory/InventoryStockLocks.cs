using ERPFlow.Application.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public static class InventoryStockLocks
{
    public static string Key(Guid warehouseId, Guid productId) => $"{warehouseId:N}.{productId:N}";

    public static async Task AcquireAsync(ERPFlowDbContext context,
        IEnumerable<(Guid WarehouseId, Guid ProductId)> affected, CancellationToken ct = default)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Inventory locks require the caller transaction.");
        var keys = affected.Distinct().OrderBy(x => Key(x.WarehouseId, x.ProductId), StringComparer.Ordinal).ToArray();
        try
        {
            foreach (var id in keys.Select(x => x.WarehouseId).Distinct().OrderBy(x => x.ToString("N"), StringComparer.Ordinal))
                await context.Warehouses.FromSqlInterpolated($"SELECT * FROM Warehouses WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}")
                    .AsNoTracking().Select(x => x.Id).SingleOrDefaultAsync(ct);
            foreach (var key in keys)
            {
                var resource = "ERPFlow.Inventory.Stock." + Key(key.WarehouseId, key.ProductId);
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    DECLARE @result int;
                    EXEC @result=sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=30000;
                    IF @result<0 THROW 51000, 'Inventory stock lock could not be acquired.', 1;
                    """, ct);
            }
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        { throw new ConcurrencyConflictException("Inventory lock conflict. Roll back and retry the complete command.", exception); }
    }
}
