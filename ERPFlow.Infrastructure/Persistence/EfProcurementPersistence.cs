using System.Data;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using ERPFlow.Domain.Transfers;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class EfProcurementPersistence(ERPFlowDbContext context) : IProcurementPersistence
{
    public async Task<IApplicationTransaction> BeginAsync(bool exclusiveWarehouseChange = false, CancellationToken ct = default)
    {
        context.ChangeTracker.DetectChanges();
        if (context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Procurement requires a clean unit of work.");
        context.ChangeTracker.Clear();
        if (!exclusiveWarehouseChange) return await ConflictAsync(() => InventoryAuthoritySession.BeginAsync(context, ct));
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await ConflictAsync(async () => { await InventoryAuthoritySession.EnterAsync(context, true, ct); return true; });
            return new EfApplicationTransaction(transaction, context);
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    public Task<AppUser?> FindActorAsync(Guid id, CancellationToken ct) => context.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<PageResult<Warehouse>> ListWarehousesAsync(int page, int size, bool? active, CancellationToken ct) =>
        PageAsync(context.Warehouses.AsNoTracking().Where(x => !active.HasValue || x.IsActive == active.Value).OrderBy(x => x.Code).ThenBy(x => x.Id), page, size, ct);
    public Task<Warehouse?> FindWarehouseAsync(Guid id, bool lockForUse, CancellationToken ct) => ConflictAsync(() => lockForUse
        ? context.Warehouses.FromSqlInterpolated($"SELECT * FROM Warehouses WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync(ct)
        : context.Warehouses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct));

    public async Task<bool> HasWarehouseCommitmentsAsync(Guid id, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Warehouse dependency checks require an exclusive serializable transaction.");
        return await context.WarehouseStocks.AnyAsync(x => x.WarehouseId == id && (x.StockOnHand != 0 || x.ReservedQuantity != 0), ct) ||
            await context.StockTransfers.AnyAsync(x => (x.SourceWarehouseId == id || x.DestinationWarehouseId == id) && x.Status == StockTransferStatus.Draft, ct) ||
            await context.GoodsReceipts.AnyAsync(x => x.WarehouseId == id && x.Status == GoodsReceiptStatus.Draft, ct) ||
            await context.InventoryReservations.AnyAsync(x => x.Status == InventoryReservationStatus.Active &&
                context.WarehouseStocks.Any(s => s.Id == x.WarehouseStockId && s.WarehouseId == id), ct) ||
            await context.StockReservations.AnyAsync(x => !x.IsReleased && (x.WarehouseId == id || (id == LegacyDefaultWarehouse.Id && x.WarehouseId == null)), ct) ||
            await context.SalesOrderItems.AnyAsync(x => (x.WarehouseId == id || (id == LegacyDefaultWarehouse.Id && x.WarehouseId == null)) &&
                context.SalesOrders.Any(o => o.Id == x.SalesOrderId && (o.Status == OrderStatus.Draft || o.Status == OrderStatus.PendingApproval ||
                    (o.Status == OrderStatus.Approved && !context.DeliveryNotes.Any(d => d.SalesOrderId == o.Id)))), ct) ||
            await context.JobcardItems.AnyAsync(x => (x.WarehouseId == id || (id == LegacyDefaultWarehouse.Id && x.WarehouseId == null)) &&
                context.Jobcards.Any(j => j.Id == x.JobcardId && (j.Status == JobcardStatus.Open || j.Status == JobcardStatus.PartsRequested || j.Status == JobcardStatus.InProgress)), ct) ||
            await context.PurchaseOrders.AnyAsync(x => x.WarehouseId == id && (x.Status == PurchaseOrderStatus.Draft ||
                x.Status == PurchaseOrderStatus.PendingApproval || x.Status == PurchaseOrderStatus.Approved || x.Status == PurchaseOrderStatus.PartiallyReceived), ct);
    }

    public Task<PageResult<Supplier>> ListSuppliersAsync(int page, int size, string? search, bool? active, CancellationToken ct)
    {
        var query = context.Suppliers.AsNoTracking().Where(x => !active.HasValue || x.IsActive == active.Value);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Name.Contains(search.Trim()) || x.SupplierCode.Contains(search.Trim()));
        return PageAsync(query.OrderBy(x => x.SupplierCode).ThenBy(x => x.Id), page, size, ct);
    }
    public Task<Supplier?> FindSupplierAsync(Guid id, bool lockForUse, CancellationToken ct) => ConflictAsync(() => lockForUse
        ? context.Suppliers.FromSqlInterpolated($"SELECT * FROM Suppliers WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync(ct)
        : context.Suppliers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct));
    public async Task<Guid?> FindMissingProductAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct, bool requireActive = false)
    {
        var found = await context.Products.FromSqlRaw("SELECT * FROM Products WITH (HOLDLOCK)")
            .AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.IsActive }).ToArrayAsync(ct);
        if (requireActive && found.Any(x => !x.IsActive))
            throw new WorkflowConflictException("Inactive products cannot be selected or submitted in new purchase commitments.");
        return ids.Except(found.Select(x => x.Id)).Select(x => (Guid?)x).FirstOrDefault();
    }
    public Task<PageResult<PurchaseOrder>> ListOrdersAsync(Guid? owner, int page, int size, PurchaseOrderStatus? status, bool receivingOnly, CancellationToken ct)
    {
        var query = context.PurchaseOrders.AsNoTracking().Where(x => (!owner.HasValue || x.CreatedByUserId == owner.Value) &&
            (!status.HasValue || x.Status == status.Value));
        if (receivingOnly) query = query.Where(x => x.Status == PurchaseOrderStatus.Approved || x.Status == PurchaseOrderStatus.PartiallyReceived ||
            x.Status == PurchaseOrderStatus.Received || x.Status == PurchaseOrderStatus.ShortClosed);
        return PageAsync(query.Include(x => x.Items).AsSplitQuery().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id), page, size, ct);
    }
    public Task<PurchaseOrder?> FindOrderAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? context.PurchaseOrders : context.PurchaseOrders.AsNoTracking();
        return ConflictAsync(() => query.Include(x => x.Items).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct));
    }
    public async Task<IReadOnlyList<PurchaseOrderHistory>> ListHistoryAsync(Guid id, CancellationToken ct) =>
        await context.PurchaseOrderHistories.AsNoTracking().Where(x => x.PurchaseOrderId == id).OrderBy(x => x.Revision).ToListAsync(ct);
    public void AddWarehouse(Warehouse warehouse) => context.Warehouses.Add(warehouse);
    public void TouchWarehouse(Warehouse warehouse) => context.Entry(warehouse).Property(x => x.Name).IsModified = true;
    public void AddSupplier(Supplier supplier) => context.Suppliers.Add(supplier);
    public void TouchSupplier(Supplier supplier) => context.Entry(supplier).Property(x => x.Name).IsModified = true;
    public void AddOrder(PurchaseOrder order) => context.PurchaseOrders.Add(order);
    public void RemoveDraftItems(IReadOnlyCollection<PurchaseOrderItem> items) => context.PurchaseOrderItems.RemoveRange(items);
    public async Task TransitionAsync(PurchaseOrder order, PurchaseOrderHistory history, byte[] expectedVersion, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("PO transitions require the existing workflow transaction.");
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                EXEC dbo.TransitionPurchaseOrder @Id={order.Id}, @ExpectedVersion={expectedVersion},
                    @ActorId={history.ActorId}, @Action={(int)history.Action}, @Comments={history.Comments},
                    @HistoryId={history.Id}, @At={history.CreatedAt};
                """, ct);
            await context.Entry(order).ReloadAsync(ct);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51013)
        { throw new ConcurrencyConflictException("PO transition version or transaction conflicted. Reload before retrying.", exception); }
        catch (SqlException exception) when (exception.Number is 547 or 2601 or 2627 or 51011 or 51012)
        { throw new WorkflowConflictException("PO transition or audit admission was rejected.", exception); }
    }
    public async Task SaveAsync(CancellationToken ct)
    {
        context.ChangeTracker.DetectChanges();
        if (context.ChangeTracker.Entries().Any(x => (x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            x.Entity is not (Warehouse or Supplier or PurchaseOrder or PurchaseOrderItem or PurchaseOrderHistory)))
            throw new InvalidOperationException("Procurement cannot save inventory or unrelated workflow mutations.");
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException exception) { throw new ConcurrencyConflictException("Procurement resource changed. Reload and retry.", exception); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new WorkflowConflictException("Duplicate warehouse/supplier code, PO number or document line.", exception); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 })
        { throw new ConcurrencyConflictException("Procurement transaction conflicted; retry with a fresh context.", exception); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 547 or 51010 or 51011 or 51012 })
        { throw new WorkflowConflictException("Procurement data violates a protected relationship or immutable document rule.", exception); }
    }
    private static async Task<PageResult<T>> PageAsync<T>(IQueryable<T> query, int page, int size, CancellationToken ct)
    {
        var count = await query.CountAsync(ct);
        var offset = ((long)page - 1) * size;
        var items = offset >= count ? [] : await query.Skip((int)offset).Take(size).ToArrayAsync(ct);
        return new(items, page, size, count, count == 0 ? 0 : (count - 1) / size + 1);
    }
    private static async Task<T> ConflictAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        { throw new ConcurrencyConflictException("Procurement lock admission conflicted. Retry the complete operation.", exception); }
    }
}
