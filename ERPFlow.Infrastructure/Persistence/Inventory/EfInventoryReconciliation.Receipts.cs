using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed partial class EfInventoryReconciliation
{
    private static async Task VerifyReceiptsAsync(ERPFlowDbContext db, WarehouseStock[] stocks, InventoryPosting[] postings, StockMovement[] movements, CancellationToken ct)
    {
        var receipts = await db.GoodsReceipts.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var lines = await db.GoodsReceiptItems.AsNoTracking().ToArrayAsync(ct);
        var orders = await db.PurchaseOrders.AsNoTracking().Include(x => x.Items).AsSplitQuery().ToArrayAsync(ct);
        foreach (var posting in postings.Where(x => x.Kind == InventoryPostingKind.Receipt))
        {
            var line = lines.SingleOrDefault(x => x.Id == posting.GoodsReceiptItemId);
            var receipt = line is null ? null : receipts.GetValueOrDefault(line.GoodsReceiptId);
            if (line is null || receipt is null || receipt.Status != GoodsReceiptStatus.Posted ||
                receipt.PostedByUserId != posting.ActorId) Fail("Receipt posting does not belong to its posted GRN and actor.");
            var movement = movements.Single(x => x.PostingId == posting.Id);
            var stock = stocks.SingleOrDefault(x => x.Id == movement.WarehouseStockId);
            if (stock is null || stock.ProductId != line.ProductId || stock.WarehouseId != receipt.WarehouseId ||
                movement.PhysicalDelta != line.Quantity || movement.ReservedDelta != 0)
                Fail("Receipt physical movement disagrees with its exact GRN source and quantity.");
        }
        foreach (var receipt in receipts.Values)
        {
            var items = lines.Where(x => x.GoodsReceiptId == receipt.Id).ToArray();
            if (items.Length == 0) Fail("A GRN has no lines.");
            var parent = orders.SingleOrDefault(x => x.Id == receipt.PurchaseOrderId);
            if (parent is null || parent.SupplierId != receipt.SupplierId || parent.WarehouseId != receipt.WarehouseId || !parent.ReceivingStartedAt.HasValue)
                Fail("Receipt original PO/source or receiving admission is inconsistent.");
            foreach (var item in items)
            {
                var original = parent.Items.SingleOrDefault(x => x.Id == item.PurchaseOrderItemId);
                if (original is null || original.ProductId != item.ProductId || item.PurchaseOrderId != parent.Id)
                    Fail("Receipt line has an orphaned original PO product.");
                var effects = postings.Count(x => x.GoodsReceiptItemId == item.Id && x.Kind == InventoryPostingKind.Receipt);
                if (effects != (receipt.Status == GoodsReceiptStatus.Posted ? 1 : 0))
                    Fail("Posted GRNs need exactly one immutable receipt posting per line; other GRNs need none.");
            }
        }
        foreach (var order in orders)
        {
            foreach (var item in order.Items)
            {
                var total = lines.Where(x => x.PurchaseOrderItemId == item.Id && receipts[x.GoodsReceiptId].Status == GoodsReceiptStatus.Posted).Sum(x => (long)x.Quantity);
                if (total != item.ReceivedQuantity || total < 0 || total > item.Quantity) Fail("PO received counters do not equal posted GRN quantities.");
            }
            var any = order.Items.Any(x => x.ReceivedQuantity > 0);
            var full = order.Items.Count > 0 && order.Items.All(x => x.ReceivedQuantity == x.Quantity);
            if ((any && order.Status is not (PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received or PurchaseOrderStatus.ShortClosed)) ||
                (order.Status == PurchaseOrderStatus.Received && !full) || (order.Status == PurchaseOrderStatus.PartiallyReceived && (!any || full)))
                Fail("PO status and posted receiving totals disagree.");
        }
    }
}
