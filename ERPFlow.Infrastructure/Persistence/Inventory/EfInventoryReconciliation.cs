using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed class EfInventoryReconciliation(ERPFlowDbContext context) : IInventoryReconciliation
{
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            await InventoryAuthoritySession.EnterAsync(context, true, cancellationToken);
            if (context.ObservedInventoryAuthority != InventoryAuthorityMode.Warehouse)
                Fail("Warehouse reconciliation requires completed warehouse authority.");
            await VerifyAsync(context, cancellationToken);
            return;
        }
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await InventoryAuthoritySession.EnterAsync(context, true, cancellationToken);
        if (context.ObservedInventoryAuthority != InventoryAuthorityMode.Warehouse)
            Fail("Warehouse reconciliation requires completed warehouse authority.");
        await VerifyAsync(context, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        context.ObservedInventoryAuthority = null;
    }

    internal static async Task VerifyAsync(ERPFlowDbContext context, CancellationToken ct)
    {
        var stocks = await context.WarehouseStocks.AsNoTracking().ToArrayAsync(ct);
        var reservations = await context.InventoryReservations.AsNoTracking().ToArrayAsync(ct);
        var movements = await context.StockMovements.AsNoTracking().ToArrayAsync(ct);
        var postings = await context.InventoryPostings.AsNoTracking().ToArrayAsync(ct);
        foreach (var posting in postings)
            if (movements.Count(x => x.PostingId == posting.Id) != 1)
                Fail("Every posting must have exactly one movement.");
        foreach (var stock in stocks)
        {
            var history = movements.Where(x => x.WarehouseStockId == stock.Id).OrderBy(x => x.Sequence).ToArray();
            var owners = reservations.Where(x => x.WarehouseStockId == stock.Id && x.Status == InventoryReservationStatus.Active).ToArray();
            if (stock.StockOnHand < stock.ReservedQuantity || stock.ReservedQuantity < 0 ||
                history.Sum(x => (long)x.PhysicalDelta) != stock.StockOnHand ||
                history.Sum(x => (long)x.ReservedDelta) != stock.ReservedQuantity ||
                owners.Sum(x => (long)x.Quantity) != stock.ReservedQuantity || history.LongLength != stock.Sequence)
                Fail("Stock, active owners and movement totals do not reconcile.");
            var onHand = 0;
            var reserved = 0;
            for (var i = 0; i < history.Length; i++)
            {
                var movement = history[i];
                if (movement.Sequence != i + 1L || movement.BeforeOnHand != onHand || movement.BeforeReserved != reserved)
                    Fail("Movement sequence or before/after continuity is inconsistent.");
                onHand = movement.AfterOnHand;
                reserved = movement.AfterReserved;
            }
        }
        var sales = await context.StockReservations.AsNoTracking().ToArrayAsync(ct);
        var lines = await context.SalesOrderItems.AsNoTracking().ToArrayAsync(ct);
        var orders = await context.SalesOrders.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var delivered = (await context.DeliveryNotes.AsNoTracking().Select(x => x.SalesOrderId).ToArrayAsync(ct)).ToHashSet();
        var parts = await context.JobcardItems.AsNoTracking().ToArrayAsync(ct);
        var jobcards = await context.Jobcards.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        foreach (var order in orders.Values)
        {
            if (delivered.Contains(order.Id) && order.Status != ERPFlow.Domain.Enums.OrderStatus.Approved)
                Fail("A delivered order has an inconsistent document state.");
            if (order.Status is ERPFlow.Domain.Enums.OrderStatus.PendingApproval or ERPFlow.Domain.Enums.OrderStatus.Approved &&
                !delivered.Contains(order.Id))
            {
                var currentLines = lines.Where(x => x.SalesOrderId == order.Id).ToArray();
                if (currentLines.Length == 0) Fail("A pending/approved order has no stock obligations.");
                foreach (var line in currentLines)
                {
                    var owner = sales.SingleOrDefault(x => x.SalesOrderId == order.Id && x.ProductId == line.ProductId && !x.IsReleased);
                    if (owner is null || owner.Quantity != line.Quantity || owner.WarehouseId != line.WarehouseId ||
                        reservations.Count(x => x.Id == owner.Id && x.Status == InventoryReservationStatus.Active) != 1)
                        Fail("A pending/approved line is missing its exact inventory obligation.");
                }
            }
        }
        foreach (var reservation in reservations.Where(x => x.Status == InventoryReservationStatus.Active))
        {
            var stock = stocks.SingleOrDefault(x => x.Id == reservation.WarehouseStockId);
            if (stock is null || stock.ProductId != reservation.ProductId) Fail("Reservation stock identity is orphaned.");
            if (reservation.SalesOrderId.HasValue)
            {
                var owner = sales.SingleOrDefault(x => x.Id == reservation.Id);
                var line = lines.SingleOrDefault(x => x.SalesOrderId == reservation.SalesOrderId && x.ProductId == reservation.ProductId);
                if (owner is null || owner.IsReleased || owner.IsHistorical || owner.Quantity != reservation.Quantity ||
                    owner.WarehouseId != stock!.WarehouseId || line is null || line.Quantity != reservation.Quantity ||
                    line.WarehouseId != stock.WarehouseId || !orders.TryGetValue(reservation.SalesOrderId.Value, out var order) ||
                    order.Status is not (ERPFlow.Domain.Enums.OrderStatus.PendingApproval or ERPFlow.Domain.Enums.OrderStatus.Approved) || delivered.Contains(order.Id))
                    Fail("Active Sales ownership or document state is inconsistent.");
            }
            else
            {
                var part = parts.SingleOrDefault(x => x.Id == reservation.JobcardItemId);
                if (part is null || part.ProductId != reservation.ProductId || part.Quantity != reservation.Quantity ||
                    part.WarehouseId != stock!.WarehouseId || !jobcards.TryGetValue(part.JobcardId, out var jobcard) ||
                    jobcard.Status is not (ERPFlow.Domain.Enums.JobcardStatus.Open or ERPFlow.Domain.Enums.JobcardStatus.PartsRequested or ERPFlow.Domain.Enums.JobcardStatus.InProgress))
                    Fail("Active Jobcard ownership or document state is inconsistent.");
            }
        }
        foreach (var owner in sales.Where(x => !x.IsReleased))
            if (reservations.Count(x => x.Id == owner.Id && x.Status == InventoryReservationStatus.Active) != 1)
                Fail("An active Sales obligation is missing its inventory owner.");
        foreach (var part in parts.Where(x => jobcards[x.JobcardId].Status is ERPFlow.Domain.Enums.JobcardStatus.Open or
            ERPFlow.Domain.Enums.JobcardStatus.PartsRequested or ERPFlow.Domain.Enums.JobcardStatus.InProgress))
            if (reservations.Count(x => x.JobcardItemId == part.Id && x.Status == InventoryReservationStatus.Active) != 1)
                Fail("An active Jobcard obligation is missing its inventory owner.");
    }

    private static void Fail(string detail) => throw new WorkflowConflictException("Inventory reconciliation failed: " + detail);
}
