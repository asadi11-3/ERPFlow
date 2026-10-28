using System.Security.Cryptography;
using System.Text;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Procurement;
using ERPFlow.Application.Receiving;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class EfGoodsReceiptPersistence(ERPFlowDbContext context) : IGoodsReceiptPersistence
{
    public bool WarehouseAuthority => context.ObservedInventoryAuthority == InventoryAuthorityMode.Warehouse;
    public async Task<IApplicationTransaction> BeginAsync(CancellationToken ct)
    {
        context.ChangeTracker.DetectChanges();
        if (context.ChangeTracker.HasChanges()) throw new WorkflowConflictException("GRN commands require a clean unit of work.");
        context.ChangeTracker.Clear();
        await context.Database.OpenConnectionAsync(ct);
        try
        {
            var inner = await InventoryAuthoritySession.BeginAsync(context, ct);
            context.GoodsReceiptWriteInProgress = true;
            return new ReceiptTransaction(inner, context);
        }
        catch { await context.Database.CloseConnectionAsync(); throw; }
    }
    public Task<AppUser?> FindActorAsync(Guid id, CancellationToken ct) => context.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<GoodsReceiptCommand?> LockCommandAsync(Guid actor, string key, CancellationToken ct)
    {
        var resource = "ERPFlow.GRN.Command." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{actor:N}|{key}")));
        return await ConflictAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=30000;
                IF @result<0 THROW 51000, 'GRN command lock could not be acquired.', 1;
                """, ct);
            return await context.GoodsReceiptCommands.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor && x.IdempotencyKey == key, ct);
        });
    }
    public Task<PurchaseOrder?> LockOrderAsync(Guid id, CancellationToken ct) => ConflictAsync(() => context.PurchaseOrders
        .FromSqlInterpolated($"SELECT * FROM PurchaseOrders WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}")
        .Include(x => x.Items).AsSplitQuery().SingleOrDefaultAsync(ct));
    public Task<GoodsReceipt?> FindReceiptAsync(Guid id, bool lockForUse, CancellationToken ct)
    {
        var query = lockForUse ? context.GoodsReceipts.FromSqlInterpolated($"SELECT * FROM GoodsReceipts WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}") : context.GoodsReceipts.AsNoTracking();
        return ConflictAsync(() => query.Include(x => x.Items).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct));
    }
    public async Task EnsureSourcesAsync(PurchaseOrder order, CancellationToken ct)
    {
        var warehouse = await context.Warehouses.FromSqlInterpolated($"SELECT * FROM Warehouses WITH (UPDLOCK,HOLDLOCK) WHERE Id={order.WarehouseId}").AsNoTracking().SingleOrDefaultAsync(ct);
        var supplier = await context.Suppliers.FromSqlInterpolated($"SELECT * FROM Suppliers WITH (UPDLOCK,HOLDLOCK) WHERE Id={order.SupplierId}").AsNoTracking().SingleOrDefaultAsync(ct);
        if (warehouse is null || supplier is null || !warehouse.IsActive || !supplier.IsActive)
            throw new WorkflowConflictException("GRN supplier and exact destination warehouse must remain active.");
    }
    public void AddReceipt(GoodsReceipt receipt) => context.GoodsReceipts.Add(receipt);
    public void AddCommand(GoodsReceiptCommand command) => context.GoodsReceiptCommands.Add(command);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException exception) { throw new ConcurrencyConflictException("GRN changed. Reload before retrying.", exception); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 or 547 or 51020 })
        { throw new WorkflowConflictException("GRN identity, supplier reference or immutable relationship conflicts.", exception); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 })
        { throw new ConcurrencyConflictException("GRN transaction conflicted. Retry the complete command.", exception); }
    }
    public async Task EnterPostingScopeAsync(Guid receiptId, CancellationToken ct)
    {
        if (!WarehouseAuthority || context.Database.CurrentTransaction is null)
            throw new WorkflowConflictException("GRN changes require a completed-cutover fenced transaction.");
        await context.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_set_session_context @key=N'ERPFlow.GRN', @value={receiptId};", ct);
    }
    public Task LockStocksAsync(GoodsReceipt receipt, CancellationToken ct) =>
        InventoryStockLocks.AcquireAsync(context, receipt.Items.Select(x => (receipt.WarehouseId, x.ProductId)), ct);
    public Task AdmitReceivingAsync(GoodsReceipt receipt, byte[] orderVersion, Guid actor, DateTime at, CancellationToken ct) => ExecuteAsync($"""
        EXEC dbo.AdmitGoodsReceipt @ReceiptId={receipt.Id}, @OrderVersion={orderVersion}, @ActorId={actor}, @At={at};
        """, ct);
    public async Task FinalizePostingAsync(GoodsReceipt receipt, byte[] receiptVersion, Guid actor, DateTime at, CancellationToken ct)
    {
        await ExecuteAsync($"EXEC dbo.PostGoodsReceipt @ReceiptId={receipt.Id}, @ReceiptVersion={receiptVersion}, @ActorId={actor}, @At={at};", ct);
        await context.Entry(receipt).ReloadAsync(ct);
    }
    private async Task ExecuteAsync(FormattableString sql, CancellationToken ct) => await ConflictAsync(async () =>
    {
        await context.Database.ExecuteSqlInterpolatedAsync(sql, ct);
        return true;
    });
    private static async Task<T> ConflictAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000 or 51013)
        { throw new ConcurrencyConflictException("GRN version or lock conflict. Retry the complete command.", exception); }
        catch (SqlException exception) when (exception.Number is 51011 or 51012 or 51020 or 547 or 2601 or 2627)
        { throw new WorkflowConflictException("GRN receiving/state/quantity admission failed.", exception); }
    }
    private sealed class ReceiptTransaction(IApplicationTransaction inner, ERPFlowDbContext db) : IApplicationTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);
        public async ValueTask DisposeAsync()
        {
            try
            {
                await inner.DisposeAsync();
                await db.Database.ExecuteSqlRawAsync("EXEC sys.sp_set_session_context @key=N'ERPFlow.GRN', @value=NULL;", CancellationToken.None);
            }
            finally
            {
                db.GoodsReceiptWriteInProgress = false;
                db.ObservedInventoryAuthority = null;
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
