using ERPFlow.Application.Common;
using ERPFlow.Application.Procurement;
using ERPFlow.Application.Receiving;
using ERPFlow.Domain.Receiving;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class EfGoodsReceiptPersistence
{
    public async Task<GoodsReceiptResult> ResultAsync(Guid id, CancellationToken ct) =>
        (await ResultsAsync([id], ct)).SingleOrDefault() ?? throw new EntityNotFoundException(nameof(GoodsReceipt), id);
    public async Task<PageResult<GoodsReceiptResult>> ListAsync(int page, int size, Guid? orderId, GoodsReceiptStatus? status, CancellationToken ct)
    {
        var query = context.GoodsReceipts.AsNoTracking().Where(x => (!orderId.HasValue || x.PurchaseOrderId == orderId) && (!status.HasValue || x.Status == status));
        var count = await query.CountAsync(ct);
        var offset = ((long)page - 1) * size;
        var ids = offset >= count ? [] : await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((int)offset).Take(size).Select(x => x.Id).ToArrayAsync(ct);
        var results = await ResultsAsync(ids, ct);
        return new(ids.Select(id => results.Single(x => x.Id == id)).ToArray(), page, size, count, count == 0 ? 0 : (count - 1) / size + 1);
    }
    private async Task<GoodsReceiptResult[]> ResultsAsync(Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var receipts = await context.GoodsReceipts.AsNoTracking().Where(x => ids.Contains(x.Id)).Include(x => x.Items).AsSplitQuery().ToArrayAsync(ct);
        var orders = receipts.Select(x => x.PurchaseOrderId).Distinct().ToArray();
        var parents = await context.PurchaseOrders.AsNoTracking().Where(x => orders.Contains(x.Id)).Select(x => new { x.Id, x.Status, x.RowVersion }).ToDictionaryAsync(x => x.Id, ct);
        var itemIds = receipts.SelectMany(x => x.Items).Select(x => x.Id).ToArray();
        var postings = await context.InventoryPostings.AsNoTracking().Where(x => x.GoodsReceiptItemId.HasValue && itemIds.Contains(x.GoodsReceiptItemId.Value))
            .Select(x => new { x.Id, ItemId = x.GoodsReceiptItemId!.Value }).ToArrayAsync(ct);
        var postingIds = postings.Select(x => x.Id).ToArray();
        var movements = await context.StockMovements.AsNoTracking().Where(x => postingIds.Contains(x.PostingId)).Select(x => new { x.Id, x.PostingId }).ToArrayAsync(ct);
        return receipts.Select(x => new GoodsReceiptResult(x.Id, x.Number, x.PurchaseOrderId, x.SupplierId, x.WarehouseId, x.Status,
            x.CreatedByUserId, x.CreatedAt, x.SupplierDeliveryReference, x.Notes, x.PostedByUserId, x.PostedAt, x.CancelledByUserId,
            x.CancelledAt, x.CancellationReason, x.RowVersion, parents[x.PurchaseOrderId].Status, parents[x.PurchaseOrderId].RowVersion,
            x.Items.OrderBy(i => i.ProductId).Select(i =>
            {
                var posting = postings.SingleOrDefault(p => p.ItemId == i.Id);
                return new GoodsReceiptItemResult(i.Id, i.PurchaseOrderItemId, i.ProductId, i.Quantity, posting?.Id,
                    posting is null ? null : movements.SingleOrDefault(m => m.PostingId == posting.Id)?.Id);
            }).ToArray())).ToArray();
    }
}
