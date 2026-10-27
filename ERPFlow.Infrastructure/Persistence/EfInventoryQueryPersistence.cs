using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Transfers;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class EfInventoryQueryPersistence(ERPFlowDbContext context) : IInventoryQueryPersistence
{
    public bool WarehouseAuthority => context.ObservedInventoryAuthority == InventoryAuthorityMode.Warehouse;

    public Task<IApplicationTransaction> BeginReadAsync(CancellationToken ct = default) =>
        InventoryAuthoritySession.BeginAsync(context, ct);

    public Task<AppUser?> FindActorAsync(Guid id, CancellationToken ct = default) =>
        context.AppUsers.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id, ct);

    public async Task<PageResult<InventoryStockResult>> StocksAsync(StockQuery query, CancellationToken ct = default)
    {
        var filtered =
            from stock in context.WarehouseStocks.AsNoTracking()
            join warehouse in context.Warehouses.AsNoTracking() on stock.WarehouseId equals warehouse.Id
            join product in context.Products.AsNoTracking() on stock.ProductId equals product.Id
            where (!query.WarehouseId.HasValue || stock.WarehouseId == query.WarehouseId) &&
                  (!query.ProductId.HasValue || stock.ProductId == query.ProductId)
            select new { Stock = stock, WarehouseCode = warehouse.Code, ProductSku = product.Sku, ProductName = product.Name };

        var count = await filtered.CountAsync(ct);
        var offset = ((long)query.Page - 1) * query.PageSize;
        var items = offset >= count
            ? []
            : await filtered.OrderBy(row => row.Stock.WarehouseId)
                .ThenBy(row => row.Stock.ProductId)
                .Skip((int)offset)
                .Take(query.PageSize)
                .Select(row => new InventoryStockResult(row.Stock.Id, row.Stock.WarehouseId, row.WarehouseCode, row.Stock.ProductId,
                    row.ProductSku, row.ProductName, row.Stock.StockOnHand, row.Stock.ReservedQuantity,
                    row.Stock.StockOnHand-row.Stock.ReservedQuantity, row.Stock.Sequence, row.Stock.RowVersion))
                .ToArrayAsync(ct);

        return Page(items, query.Page, query.PageSize, count);
    }

    public async Task<PageResult<InventoryMovementResult>> MovementsAsync(MovementQuery query, CancellationToken ct = default)
    {
        var fromUtc = query.From?.UtcDateTime;
        var toUtc = query.To?.UtcDateTime;
        var filtered =
            from movement in context.StockMovements.AsNoTracking()
            join posting in context.InventoryPostings.AsNoTracking() on movement.PostingId equals posting.Id
            join stock in context.WarehouseStocks.AsNoTracking() on movement.WarehouseStockId equals stock.Id
            where (!query.WarehouseId.HasValue || stock.WarehouseId == query.WarehouseId) &&
                  (!query.ProductId.HasValue || stock.ProductId == query.ProductId) &&
                  (!fromUtc.HasValue || posting.CreatedAt >= fromUtc.Value) &&
                  (!toUtc.HasValue || posting.CreatedAt < toUtc.Value)
            select new
            {
                movement.Id, PostingId=posting.Id, movement.WarehouseStockId, stock.WarehouseId, stock.ProductId,
                movement.Sequence, movement.Kind, movement.PhysicalDelta, movement.ReservedDelta, movement.BeforeOnHand,
                movement.BeforeReserved, movement.AfterOnHand, movement.AfterReserved, posting.ActorId, posting.CreatedAt,
                posting.GoodsReceiptItemId, posting.ReservationId, posting.StockTransferItemId
            };

        var count = await filtered.CountAsync(ct);
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (offset >= count) return Page<InventoryMovementResult>([], query.Page, query.PageSize, count);

        var rows = await filtered.OrderByDescending(movement => movement.CreatedAt)
            .ThenByDescending(movement => movement.Id)
            .Skip((int)offset)
            .Take(query.PageSize)
            .Select(row => new MovementRow(row.Id, row.PostingId, row.WarehouseStockId, row.WarehouseId, row.ProductId, row.Sequence,
                row.Kind, row.PhysicalDelta, row.ReservedDelta, row.BeforeOnHand, row.BeforeReserved, row.AfterOnHand, row.AfterReserved,
                row.ActorId, row.CreatedAt, row.GoodsReceiptItemId, row.ReservationId, row.StockTransferItemId))
            .ToArrayAsync(ct);

        var receiptItemIds = rows.Where(row => row.GoodsReceiptItemId.HasValue)
            .Select(row => row.GoodsReceiptItemId!.Value).Distinct().ToArray();
        var receiptSources = await (
            from item in context.GoodsReceiptItems.AsNoTracking()
            join receipt in context.GoodsReceipts.AsNoTracking() on item.GoodsReceiptId equals receipt.Id
            where receiptItemIds.Contains(item.Id)
            select new SourceRow(item.Id, "GoodsReceipt", receipt.Id, receipt.Number, item.Id))
            .ToDictionaryAsync(source => source.Key, ct);

        var reservationIds = rows.Where(row => row.ReservationId.HasValue)
            .Select(row => row.ReservationId!.Value).Distinct().ToArray();
        var reservationSources = await (
            from reservation in context.InventoryReservations.AsNoTracking()
            join item in context.JobcardItems.AsNoTracking() on reservation.JobcardItemId equals (Guid?)item.Id into jobcardItems
            from item in jobcardItems.DefaultIfEmpty()
            join jobcard in context.Jobcards.AsNoTracking() on item.JobcardId equals jobcard.Id into jobcards
            from jobcard in jobcards.DefaultIfEmpty()
            where reservationIds.Contains(reservation.Id)
            select new ReservationSource(
                reservation.Id,
                reservation.SalesOrderId,
                reservation.JobcardItemId,
                jobcard == null ? null : jobcard.Id,
                jobcard == null ? null : jobcard.TicketNumber))
            .ToDictionaryAsync(source => source.Id, ct);

        var transferItemIds = rows.Where(row => row.StockTransferItemId.HasValue)
            .Select(row => row.StockTransferItemId!.Value).Distinct().ToArray();
        var transferSources = await (
            from item in context.Set<StockTransferItem>().AsNoTracking()
            join transfer in context.Set<StockTransfer>().AsNoTracking() on item.StockTransferId equals transfer.Id
            where transferItemIds.Contains(item.Id)
            select new SourceRow(item.Id, "StockTransfer", transfer.Id, transfer.Number, item.Id))
            .ToDictionaryAsync(source => source.Key, ct);

        var items = rows.Select(row =>
        {
            var source = Source(row, receiptSources, reservationSources, transferSources);
            return new InventoryMovementResult(
                row.Id, row.PostingId, row.WarehouseStockId, row.WarehouseId, row.ProductId, row.Sequence,
                row.Kind, row.PhysicalDelta, row.ReservedDelta, row.BeforeOnHand, row.BeforeReserved,
                row.AfterOnHand, row.AfterReserved, row.ActorId, row.CreatedAt, source.SourceType,
                source.DocumentId, source.Number, source.ItemId);
        }).ToArray();

        return Page(items, query.Page, query.PageSize, count);
    }

    private static MovementSource Source(
        MovementRow row,
        IReadOnlyDictionary<Guid, SourceRow> receipts,
        IReadOnlyDictionary<Guid, ReservationSource> reservations,
        IReadOnlyDictionary<Guid, SourceRow> transfers)
    {
        if (row.GoodsReceiptItemId is { } receiptItemId && receipts.TryGetValue(receiptItemId, out var receipt))
            return new(receipt.SourceType, receipt.DocumentId, receipt.Number, receipt.ItemId);
        if (row.StockTransferItemId is { } transferItemId && transfers.TryGetValue(transferItemId, out var transfer))
            return new(transfer.SourceType, transfer.DocumentId, transfer.Number, transfer.ItemId);
        if (row.ReservationId is { } reservationId && reservations.TryGetValue(reservationId, out var reservation))
        {
            if (reservation.SalesOrderId is Guid salesOrderId)
                return new("SalesReservation", salesOrderId, salesOrderId.ToString(), reservationId);
            if (reservation.JobcardItemId is Guid jobcardItemId)
                return new("Jobcard", reservation.JobcardId, reservation.JobcardNumber, jobcardItemId);
        }
        return new("Opening", null, null, null);
    }

    private static PageResult<T> Page<T>(IReadOnlyList<T> items, int page, int pageSize, int count) =>
        new(items, page, pageSize, count, count == 0 ? 0 : (count - 1) / pageSize + 1);

    private sealed record MovementRow(
        Guid Id, Guid PostingId, Guid WarehouseStockId, Guid WarehouseId, Guid ProductId, long Sequence,
        InventoryPostingKind Kind, int PhysicalDelta, int ReservedDelta, int BeforeOnHand, int BeforeReserved,
        int AfterOnHand, int AfterReserved, Guid ActorId, DateTime CreatedAt, Guid? GoodsReceiptItemId,
        Guid? ReservationId, Guid? StockTransferItemId);

    private sealed record SourceRow(Guid Key, string SourceType, Guid DocumentId, string Number, Guid ItemId);
    private sealed record ReservationSource(
        Guid Id, Guid? SalesOrderId, Guid? JobcardItemId, Guid? JobcardId, string? JobcardNumber);
    private sealed record MovementSource(string SourceType, Guid? DocumentId, string? Number, Guid? ItemId);
}
