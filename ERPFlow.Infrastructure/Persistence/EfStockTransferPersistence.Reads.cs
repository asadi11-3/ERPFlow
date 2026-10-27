using ERPFlow.Application.Common;
using ERPFlow.Application.Procurement;
using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class EfStockTransferPersistence
{
    public async Task<StockTransferResult> ResultAsync(Guid id, CancellationToken ct) =>
        (await ResultsAsync([id], ct)).SingleOrDefault() ?? throw new EntityNotFoundException(nameof(StockTransfer), id);
    public async Task<PageResult<StockTransferResult>> ListAsync(int page, int size, Guid? warehouseId, Guid? productId, StockTransferStatus? status, CancellationToken ct)
    {
        var query = context.StockTransfers.AsNoTracking().Where(x => (!warehouseId.HasValue || x.SourceWarehouseId == warehouseId || x.DestinationWarehouseId == warehouseId) &&
            (!productId.HasValue || x.Items.Any(i => i.ProductId == productId)) && (!status.HasValue || x.Status == status));
        var count = await query.CountAsync(ct);
        var offset = ((long)page - 1) * size;
        var ids = offset >= count ? [] : await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((int)offset).Take(size).Select(x => x.Id).ToArrayAsync(ct);
        var rows = await ResultsAsync(ids, ct);
        return new(ids.Select(id => rows.Single(x => x.Id == id)).ToArray(), page, size, count, count == 0 ? 0 : (count - 1) / size + 1);
    }
    private async Task<StockTransferResult[]> ResultsAsync(Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var documents = await context.StockTransfers.AsNoTracking().Where(x => ids.Contains(x.Id)).Include(x => x.Items).AsSplitQuery().ToArrayAsync(ct);
        var lineIds = documents.SelectMany(x => x.Items).Select(x => x.Id).ToArray();
        var postings = await context.InventoryPostings.AsNoTracking().Where(x => x.StockTransferItemId.HasValue && lineIds.Contains(x.StockTransferItemId.Value))
            .Select(x => new { x.Id, x.Kind, ItemId = x.StockTransferItemId!.Value }).ToArrayAsync(ct);
        var postingIds = postings.Select(x => x.Id).ToArray();
        var movements = await context.StockMovements.AsNoTracking().Where(x => postingIds.Contains(x.PostingId)).Select(x => new { x.Id, x.PostingId }).ToArrayAsync(ct);
        return documents.Select(x => new StockTransferResult(x.Id, x.Number, x.SourceWarehouseId, x.DestinationWarehouseId, x.Status,
            x.CreatedByUserId, x.CreatedAt, x.PostedByUserId, x.PostedAt, x.CancelledByUserId, x.CancelledAt, x.CancellationReason, x.RowVersion,
            x.Items.OrderBy(i => i.ProductId).Select(i =>
            {
                var source = postings.SingleOrDefault(p => p.ItemId == i.Id && p.Kind == InventoryPostingKind.TransferOut);
                var destination = postings.SingleOrDefault(p => p.ItemId == i.Id && p.Kind == InventoryPostingKind.TransferIn);
                return new StockTransferItemResult(i.Id, i.ProductId, i.Quantity, source?.Id,
                    source is null ? null : movements.SingleOrDefault(m => m.PostingId == source.Id)?.Id, destination?.Id,
                    destination is null ? null : movements.SingleOrDefault(m => m.PostingId == destination.Id)?.Id);
            }).ToArray())).ToArray();
    }
}
