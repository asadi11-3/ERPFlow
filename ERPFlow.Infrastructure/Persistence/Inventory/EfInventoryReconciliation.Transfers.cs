using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed partial class EfInventoryReconciliation
{
    private static async Task VerifyTransfersAsync(ERPFlowDbContext db, WarehouseStock[] stocks, InventoryPosting[] postings, StockMovement[] movements, CancellationToken ct)
    {
        var documents = await db.StockTransfers.AsNoTracking().Include(x => x.Items).AsSplitQuery().ToArrayAsync(ct);
        var items = documents.SelectMany(x => x.Items).ToDictionary(x => x.Id);
        foreach (var posting in postings.Where(x => x.Kind is InventoryPostingKind.TransferOut or InventoryPostingKind.TransferIn))
        {
            var item = posting.StockTransferItemId.HasValue ? items.GetValueOrDefault(posting.StockTransferItemId.Value) : null;
            if (item is null) Fail("A transfer posting has no exact original line.");
            var document = documents.Single(x => x.Id == item.StockTransferId);
            if (document.Status != StockTransferStatus.Posted || document.PostedByUserId != posting.ActorId)
                Fail("Transfer posting status or actor disagrees with its document.");
            var movement = movements.Single(x => x.PostingId == posting.Id);
            var stock = stocks.SingleOrDefault(x => x.Id == movement.WarehouseStockId);
            var outgoing = posting.Kind == InventoryPostingKind.TransferOut;
            if (stock is null || stock.ProductId != item.ProductId || stock.WarehouseId != (outgoing ? document.SourceWarehouseId : document.DestinationWarehouseId) ||
                movement.PhysicalDelta != (outgoing ? -(long)item.Quantity : item.Quantity) || movement.ReservedDelta != 0)
                Fail("Transfer movement source, product, quantity or reservation delta is inconsistent.");
        }
        foreach (var document in documents)
        {
            if (document.Items.Count == 0 || document.SourceWarehouseId == document.DestinationWarehouseId) Fail("Invalid transfer document identity/lines.");
            foreach (var item in document.Items)
            {
                var legs = postings.Where(x => x.StockTransferItemId == item.Id).ToArray();
                if (document.Status != StockTransferStatus.Posted)
                {
                    if (legs.Length != 0) Fail("An unposted transfer has inventory effects.");
                    continue;
                }
                if (legs.Length != 2 || legs.Count(x => x.Kind == InventoryPostingKind.TransferOut) != 1 || legs.Count(x => x.Kind == InventoryPostingKind.TransferIn) != 1 ||
                    movements.Where(x => legs.Any(p => p.Id == x.PostingId)).Sum(x => (long)x.PhysicalDelta) != 0)
                    Fail("A posted transfer requires one conserved outgoing/incoming pair per item.");
            }
        }
    }
}
