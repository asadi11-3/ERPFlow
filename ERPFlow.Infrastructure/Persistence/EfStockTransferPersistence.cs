using System.Security.Cryptography;
using System.Text;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Transfers;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class EfStockTransferPersistence(ERPFlowDbContext context) : IStockTransferPersistence
{
    public bool WarehouseAuthority => context.ObservedInventoryAuthority == InventoryAuthorityMode.Warehouse;
    public async Task<IApplicationTransaction> BeginAsync(CancellationToken ct)
    {
        context.ChangeTracker.DetectChanges();
        if (context.ChangeTracker.HasChanges()) throw new WorkflowConflictException("Transfers require a clean unit of work.");
        context.ChangeTracker.Clear();
        await context.Database.OpenConnectionAsync(ct);
        try
        {
            var inner = await InventoryAuthoritySession.BeginAsync(context, ct);
            context.StockTransferWriteInProgress = true;
            return new TransferTransaction(inner, context);
        }
        catch { await context.Database.CloseConnectionAsync(); throw; }
    }
    public Task<AppUser?> FindActorAsync(Guid id, CancellationToken ct) => context.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<StockTransferCommand?> LockCommandAsync(Guid actor, string key, CancellationToken ct)
    {
        var resource = "ERPFlow.Transfer.Command." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{actor:N}|{key}")));
        return await ConflictAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=30000;
                IF @result<0 THROW 51000, 'Transfer command lock could not be acquired.', 1;
                """, ct);
            return await context.StockTransferCommands.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor && x.IdempotencyKey == key, ct);
        });
    }
    public Task<StockTransfer?> FindAsync(Guid id, bool lockForUse, CancellationToken ct)
    {
        var query = lockForUse ? context.StockTransfers.FromSqlInterpolated($"SELECT * FROM StockTransfers WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}") : context.StockTransfers.AsNoTracking();
        return ConflictAsync(() => query.Include(x => x.Items).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct));
    }
    public async Task EnsureWarehousesAsync(Guid source, Guid destination, IReadOnlyCollection<Guid> products, CancellationToken ct, bool requireActive = false)
    {
        foreach (var id in new[] { source, destination }.Distinct().OrderBy(x => x.ToString("N"), StringComparer.Ordinal))
        {
            var warehouse = await ConflictAsync(() => context.Warehouses.FromSqlInterpolated($"SELECT * FROM Warehouses WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}")
                .AsNoTracking().SingleOrDefaultAsync(ct));
            if (warehouse is null) throw new EntityNotFoundException(nameof(Warehouse), id);
            if (!warehouse.IsActive) throw new WorkflowConflictException("Both transfer warehouses must be active.");
        }
        var found = await context.Products.FromSqlRaw("SELECT * FROM Products WITH (HOLDLOCK)")
            .AsNoTracking().Where(x => products.Contains(x.Id)).Select(x => new { x.Id, x.IsActive }).ToArrayAsync(ct);
        if (requireActive && found.Any(x => !x.IsActive))
            throw new WorkflowConflictException("Inactive products cannot be selected for new stock transfers.");
        var missing = products.Except(found.Select(x => x.Id)).Select(x => (Guid?)x).FirstOrDefault();
        if (missing.HasValue) throw new EntityNotFoundException(nameof(Product), missing.Value);
    }
    public void AddTransfer(StockTransfer transfer) => context.StockTransfers.Add(transfer);
    public void AddCommand(StockTransferCommand command) => context.StockTransferCommands.Add(command);
    public async Task EnterScopeAsync(Guid id, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Transfer scope requires the existing transaction.");
        await context.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_set_session_context @key=N'ERPFlow.Transfer', @value={id};", ct);
    }
    public async Task SaveAsync(CancellationToken ct)
    {
        context.ChangeTracker.DetectChanges();
        if (context.ChangeTracker.Entries().Any(x => (x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            x.Entity is not (StockTransfer or StockTransferItem or StockTransferCommand)))
            throw new InvalidOperationException("Transfer document saves cannot write unrelated entities or balances.");
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException exception) { throw new ConcurrencyConflictException("Transfer changed. Reload before retrying.", exception); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 547 or 2601 or 2627 or 51030 })
        { throw new WorkflowConflictException("Transfer identity, ledger pair or immutable relationship conflicts.", exception); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 })
        { throw new ConcurrencyConflictException("Transfer conflicted. Retry the complete operation.", exception); }
    }
    private static async Task<T> ConflictAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        { throw new ConcurrencyConflictException("Transfer lock conflicted. Retry the complete command.", exception); }
    }
    private sealed class TransferTransaction(IApplicationTransaction inner, ERPFlowDbContext db) : IApplicationTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);
        public async ValueTask DisposeAsync()
        {
            try
            {
                await inner.DisposeAsync();
                await db.Database.ExecuteSqlRawAsync("EXEC sys.sp_set_session_context @key=N'ERPFlow.Transfer', @value=NULL;", CancellationToken.None);
            }
            finally
            {
                db.StockTransferWriteInProgress = false;
                db.ObservedInventoryAuthority = null;
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
