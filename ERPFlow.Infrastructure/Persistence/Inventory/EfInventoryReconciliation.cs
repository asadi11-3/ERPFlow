using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed partial class EfInventoryReconciliation(ERPFlowDbContext context) : IInventoryReconciliation
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
        try
        {
            await InventoryAuthoritySession.EnterAsync(context, true, cancellationToken);
            if (context.ObservedInventoryAuthority != InventoryAuthorityMode.Warehouse)
                Fail("Warehouse reconciliation requires completed warehouse authority.");
            await VerifyAsync(context, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { context.ObservedInventoryAuthority = null; }
    }

    internal static async Task VerifyAsync(ERPFlowDbContext context, CancellationToken ct, Guid? preparationId = null)
    {
        var stocks = await context.WarehouseStocks.AsNoTracking().ToArrayAsync(ct);
        var reservations = await context.InventoryReservations.AsNoTracking().ToArrayAsync(ct);
        var movements = await context.StockMovements.AsNoTracking().ToArrayAsync(ct);
        var postings = await context.InventoryPostings.AsNoTracking().ToArrayAsync(ct);
        preparationId ??= await context.InventoryAuthorities.AsNoTracking().Select(x => x.PreparationId).SingleAsync(ct);
        var imports = await context.LegacyInventoryReservationImports.AsNoTracking()
            .Where(x => x.BatchId == preparationId).ToArrayAsync(ct);
        VerifyOwnerLedger(stocks, reservations, movements, postings, imports);
        await VerifyReceiptsAsync(context, stocks, postings, movements, ct);
        await VerifyTransfersAsync(context, stocks, postings, movements, ct);
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
        foreach (var reservation in reservations)
        {
            var stock = stocks.SingleOrDefault(x => x.Id == reservation.WarehouseStockId);
            if (stock is null || stock.ProductId != reservation.ProductId) Fail("Reservation stock identity is orphaned.");
            if (reservation.Status != InventoryReservationStatus.Active)
            {
                VerifyClosedOwner(reservation, stock, sales, parts, orders, jobcards, delivered);
                continue;
            }
            if (reservation.SalesOrderId.HasValue)
            {
                var owner = sales.SingleOrDefault(x => x.Id == reservation.Id);
                var line = lines.SingleOrDefault(x => x.SalesOrderId == reservation.SalesOrderId && x.ProductId == reservation.ProductId);
                if (owner is null || owner.SalesOrderId != reservation.SalesOrderId || owner.ProductId != reservation.ProductId ||
                    owner.IsReleased || owner.IsHistorical || owner.Quantity != reservation.Quantity ||
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
        foreach (var owner in sales)
            if (reservations.Count(x => x.Id == owner.Id && x.SalesOrderId == owner.SalesOrderId &&
                (x.Status == InventoryReservationStatus.Active) == !owner.IsReleased) != 1)
                Fail("A Sales obligation is missing its inventory owner.");
        foreach (var part in parts.Where(x => jobcards[x.JobcardId].Status is ERPFlow.Domain.Enums.JobcardStatus.Open or
            ERPFlow.Domain.Enums.JobcardStatus.PartsRequested or ERPFlow.Domain.Enums.JobcardStatus.InProgress))
            if (reservations.Count(x => x.JobcardItemId == part.Id && x.Status == InventoryReservationStatus.Active) != 1)
                Fail("An active Jobcard obligation is missing its inventory owner.");
    }

    private static void VerifyOwnerLedger(WarehouseStock[] stocks, InventoryReservation[] reservations,
        StockMovement[] movements, InventoryPosting[] postings, LegacyInventoryReservationImport[] imports)
    {
        var stocksById = stocks.ToDictionary(x => x.Id);
        var ownersById = reservations.ToDictionary(x => x.Id);
        var movementsByPosting = movements.ToLookup(x => x.PostingId);
        var postingsByOwner = postings.Where(x => x.ReservationId.HasValue).ToLookup(x => x.ReservationId!.Value);
        var importsByOwner = imports.ToDictionary(x => x.TargetReservationId);
        foreach (var posting in postings)
        {
            var effects = movementsByPosting[posting.Id].ToArray();
            if (effects.Length != 1 || effects[0].Kind != posting.Kind)
                Fail("Every posting must have exactly one movement of the same kind.");
            var effect = effects[0];
            if (!stocksById.ContainsKey(effect.WarehouseStockId)) Fail("A movement has no stock source.");
            if (posting.Kind == InventoryPostingKind.Opening)
            {
                if (posting.ReservationId.HasValue) Fail("An opening cannot claim a reservation event.");
                continue;
            }
            if (posting.Kind is InventoryPostingKind.Receipt or InventoryPostingKind.TransferOut or InventoryPostingKind.TransferIn) continue;
            var owner = posting.ReservationId.HasValue ? ownersById.GetValueOrDefault(posting.ReservationId.Value) : null;
            if (owner is null)
                Fail("A posting has no reservation owner.");
            if (effect.WarehouseStockId != owner.WarehouseStockId ||
                effect.ReservedDelta != (posting.Kind == InventoryPostingKind.Reserve ? (long)owner.Quantity : -(long)owner.Quantity) ||
                effect.PhysicalDelta != (posting.Kind == InventoryPostingKind.Consume ? -(long)owner.Quantity : 0))
                Fail("A posting movement does not match its exact reservation source and quantity.");
        }
        if (movements.Length != postings.Length) Fail("A movement has no unique posting.");
        foreach (var owner in reservations)
        {
            var history = postingsByOwner[owner.Id].ToArray();
            var reserves = history.Where(x => x.Kind == InventoryPostingKind.Reserve).ToArray();
            var closes = history.Where(x => x.Kind is InventoryPostingKind.Release or InventoryPostingKind.Consume).ToArray();
            var imported = importsByOwner.GetValueOrDefault(owner.Id);
            if (owner.IsLegacyImport != (imported is not null)) Fail("Reservation import provenance is missing or inconsistent.");
            if (imported is not null)
            {
                if (!stocksById.TryGetValue(owner.WarehouseStockId, out var stock) || stock.WarehouseId != imported.WarehouseId ||
                    owner.ProductId != imported.ProductId || owner.Quantity != imported.Quantity ||
                    owner.SalesOrderId != imported.SalesOrderId || owner.JobcardItemId != imported.JobcardItemId ||
                    owner.SourceCreatedAt != imported.SourceCreatedAt ||
                    (imported.SourceCreatedAt.HasValue && owner.CreatedAt != imported.SourceCreatedAt))
                    Fail("An imported owner does not match its approved preparation.");
                if (imported.Disposition != LegacyReservationDisposition.Active)
                {
                    if ((int)owner.Status != (int)imported.Disposition || owner.ClosedAt != imported.SourceClosedAt || history.Length != 0)
                        Fail("Closed imported history was changed or assigned fabricated inventory events.");
                    continue;
                }
            }
            if (reserves.Length != (owner.IsLegacyImport ? 0 : 1))
                Fail("An owner must have exactly one native reserve or one imported active opening, never both.");
            if (owner.Status == InventoryReservationStatus.Active)
            {
                if (closes.Length != 0 || owner.ClosedAt.HasValue) Fail("An active owner has a closure event.");
                continue;
            }
            var expected = owner.Status switch
            {
                InventoryReservationStatus.Released => InventoryPostingKind.Release,
                InventoryReservationStatus.Consumed => InventoryPostingKind.Consume,
                _ => throw new WorkflowConflictException("Inventory reconciliation failed: Unknown closure lacks closed import provenance.")
            };
            if (closes.Length != 1 || closes[0].Kind != expected || !owner.ClosedAt.HasValue ||
                (reserves.Length == 1 && movementsByPosting[reserves[0].Id].Single().Sequence >= movementsByPosting[closes[0].Id].Single().Sequence))
                Fail("A closed owner needs exactly one matching terminal event after its reservation.");
        }
        if (imports.Any(x => !ownersById.ContainsKey(x.TargetReservationId)))
            Fail("An approved imported owner is missing.");
    }

    private static void VerifyClosedOwner(InventoryReservation reservation, WarehouseStock stock,
        StockReservation[] sales, JobcardItem[] parts, Dictionary<Guid, SalesOrder> orders,
        Dictionary<Guid, Jobcard> jobcards, HashSet<Guid> delivered)
    {
        if (reservation.SalesOrderId.HasValue)
        {
            var mirror = sales.SingleOrDefault(x => x.Id == reservation.Id);
            var expectedClosure = reservation.Status switch
            {
                InventoryReservationStatus.Released => StockReservationClosureKind.Released,
                InventoryReservationStatus.Consumed => StockReservationClosureKind.Consumed,
                _ => (StockReservationClosureKind?)null
            };
            if (mirror is null || !mirror.IsReleased || mirror.SalesOrderId != reservation.SalesOrderId ||
                mirror.ProductId != reservation.ProductId || mirror.Quantity != reservation.Quantity ||
                mirror.WarehouseId != stock.WarehouseId || mirror.ClosureKind != expectedClosure)
                Fail("Closed Sales history disagrees with its original inventory owner.");
            if (!orders.TryGetValue(reservation.SalesOrderId.Value, out var order) ||
                (reservation.Status == InventoryReservationStatus.Consumed &&
                    (order.Status != OrderStatus.Approved || !delivered.Contains(order.Id))) ||
                (reservation.Status == InventoryReservationStatus.Released && !mirror.IsHistorical &&
                    order.Status is not (OrderStatus.Draft or OrderStatus.Rejected)))
                Fail("Closed Sales ownership has an inconsistent document state.");
        }
        else
        {
            var part = parts.SingleOrDefault(x => x.Id == reservation.JobcardItemId);
            if (part is null || part.ProductId != reservation.ProductId || part.Quantity != reservation.Quantity ||
                part.WarehouseId != stock.WarehouseId || !jobcards.TryGetValue(part.JobcardId, out var jobcard) ||
                (reservation.Status == InventoryReservationStatus.Consumed && jobcard.Status != JobcardStatus.Completed) ||
                (reservation.Status == InventoryReservationStatus.LegacyClosedUnknown &&
                    jobcard.Status is not (JobcardStatus.Completed or JobcardStatus.Cancelled)) ||
                reservation.Status == InventoryReservationStatus.Released)
                Fail("Closed Jobcard ownership has an inconsistent original part or document state.");
        }
    }

    [DoesNotReturn]
    private static void Fail(string detail) => throw new WorkflowConflictException("Inventory reconciliation failed: " + detail);
}
