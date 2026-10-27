using ERPFlow.Application.Analytics;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Receiving;
using ERPFlow.Domain.Transfers;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class EfAnalyticsQueries
{
    public async Task<ReceivingAnalytics> ReceivingAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        var actor = await AuthorizeAsync(ct, InventoryReaders);
        var window = Window(query);
        var receipts = context.GoodsReceipts.AsNoTracking().Where(receipt => actor.Role != Domain.Enums.UserRole.Purchasing ||
            context.PurchaseOrders.Any(order => order.Id == receipt.PurchaseOrderId && order.CreatedByUserId == actor.Id));
        var statuses = await StatusesAsync(receipts.Where(receipt => receipt.CreatedAt >= window.FromUtc && receipt.CreatedAt < window.ToUtc)
            .Select(receipt => receipt.Status), ct);
        var posted = await receipts.Where(receipt => receipt.Status == GoodsReceiptStatus.Posted &&
                receipt.PostedAt >= window.FromUtc && receipt.PostedAt < window.ToUtc)
            .GroupBy(receipt => receipt.PostedAt!.Value.Date)
            .Select(group => new DailyQuantity(group.Key, group.LongCount(), group.Sum(receipt => receipt.Items.Sum(item => (long)item.Quantity))))
            .ToArrayAsync(ct);
        return new(window, statuses, Quantities(window, posted));
    }

    public async Task<TransfersAnalytics> TransfersAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        await AuthorizeAsync(ct, Domain.Enums.UserRole.Administrator, Domain.Enums.UserRole.WarehouseOperator);
        var window = Window(query);
        var transfers = context.StockTransfers.AsNoTracking();
        var statuses = await StatusesAsync(transfers.Where(transfer => transfer.CreatedAt >= window.FromUtc && transfer.CreatedAt < window.ToUtc)
            .Select(transfer => transfer.Status), ct);
        var posted = await transfers.Where(transfer => transfer.Status == StockTransferStatus.Posted &&
                transfer.PostedAt >= window.FromUtc && transfer.PostedAt < window.ToUtc)
            .GroupBy(transfer => transfer.PostedAt!.Value.Date)
            .Select(group => new DailyQuantity(group.Key, group.LongCount(), group.Sum(transfer => transfer.Items.Sum(item => (long)item.Quantity))))
            .ToArrayAsync(ct);
        return new(window, statuses, Quantities(window, posted));
    }

    public async Task<InventoryAnalytics> InventoryAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        await AuthorizeAsync(ct, InventoryReaders);
        var window = Window(query);
        await using var transaction = await InventoryAuthoritySession.BeginAsync(context, ct);
        if (context.ObservedInventoryAuthority != InventoryAuthorityMode.Warehouse)
            throw new WorkflowConflictException("Operational inventory analytics are unavailable until warehouse authority is active.");
        var snapshot = DateTime.UtcNow;
        var balances = await (from stock in context.WarehouseStocks.AsNoTracking()
            join warehouse in context.Warehouses.AsNoTracking() on stock.WarehouseId equals warehouse.Id
            group stock by new { warehouse.Id, warehouse.Code, warehouse.Name } into stocks
            orderby stocks.Key.Code, stocks.Key.Id
            select new WarehouseBalance(stocks.Key.Id, stocks.Key.Code, stocks.Key.Name,
                stocks.Sum(stock => (long)stock.StockOnHand), stocks.Sum(stock => (long)stock.ReservedQuantity),
                stocks.Sum(stock => (long)stock.StockOnHand - stock.ReservedQuantity),
                stocks.LongCount(stock => stock.StockOnHand - stock.ReservedQuantity <= 0))).ToArrayAsync(ct);
        var movements = from movement in context.StockMovements.AsNoTracking()
            join posting in context.InventoryPostings.AsNoTracking() on movement.PostingId equals posting.Id
            where posting.CreatedAt >= window.FromUtc && posting.CreatedAt < window.ToUtc
            select new { posting.CreatedAt, movement.Kind, movement.PhysicalDelta, movement.ReservedDelta };
        var kinds = await StatusesAsync(movements.Select(row => row.Kind), ct);
        var daily = await movements.GroupBy(row => row.CreatedAt.Date).Select(group => new
        {
            Date = group.Key,
            PhysicalIn = group.Sum(row => row.PhysicalDelta > 0 ? (long)row.PhysicalDelta : 0),
            PhysicalOut = group.Sum(row => row.PhysicalDelta < 0 ? -(long)row.PhysicalDelta : 0),
            ReservedDelta = group.Sum(row => (long)row.ReservedDelta)
        }).ToArrayAsync(ct);
        var buckets = daily.GroupBy(row => window.Bucket(DateOnly.FromDateTime(row.Date))).ToDictionary(group => group.Key,
            group => new { PhysicalIn = group.Sum(row => row.PhysicalIn), PhysicalOut = group.Sum(row => row.PhysicalOut), ReservedDelta = group.Sum(row => row.ReservedDelta) });
        var series = window.Buckets().Select(date => new MovementPoint(date, buckets.GetValueOrDefault(date)?.PhysicalIn ?? 0,
            buckets.GetValueOrDefault(date)?.PhysicalOut ?? 0, buckets.GetValueOrDefault(date)?.ReservedDelta ?? 0)).ToArray();
        await transaction.CommitAsync(ct);
        return new(window, snapshot, balances, kinds, series);
    }

    public async Task<DispatchAnalytics> DispatchAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        await AuthorizeAsync(ct, SalesReaders);
        var window = Window(query);
        var deliveries = context.DeliveryNotes.AsNoTracking().Where(delivery => delivery.DispatchedAt >= window.FromUtc && delivery.DispatchedAt < window.ToUtc);
        var daily = await deliveries.GroupBy(delivery => delivery.DispatchedAt.Date)
            .Select(group => new DailyCount(group.Key, group.LongCount())).ToArrayAsync(ct);
        var delivered = await deliveries.LongCountAsync(delivery => delivery.DeliveredAt != null, ct);
        var carriers = await deliveries.GroupBy(delivery => delivery.Carrier)
            .OrderByDescending(group => group.LongCount()).ThenBy(group => group.Key)
            .Select(group => new CarrierDispatches(group.Key, group.LongCount())).ToArrayAsync(ct);
        var assets = await context.MachineAssets.AsNoTracking().Where(asset => asset.InstalledAt >= window.FromUtc && asset.InstalledAt < window.ToUtc)
            .GroupBy(asset => asset.Status).OrderBy(group => group.Key)
            .Select(group => new AssetStatusCount(group.Key, group.LongCount())).ToArrayAsync(ct);
        return new(window, delivered, daily.Sum(row => row.Count) - delivered, Counts(window, daily), carriers, assets);
    }

    public async Task<ServiceAnalytics> ServiceAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        await AuthorizeAsync(ct, SalesReaders);
        var window = Window(query);
        var created = context.Jobcards.AsNoTracking().Where(job => job.CreatedAt >= window.FromUtc && job.CreatedAt < window.ToUtc);
        var statuses = await StatusesAsync(created.Select(job => job.Status), ct);
        var priorities = await StatusesAsync(created.Select(job => job.Priority), ct);
        var daily = await created.GroupBy(job => job.CreatedAt.Date).Select(group => new DailyCount(group.Key, group.LongCount())).ToArrayAsync(ct);
        var completed = await context.Jobcards.AsNoTracking().Where(job => job.Status == Domain.Enums.JobcardStatus.Completed &&
                job.CompletedAt >= window.FromUtc && job.CompletedAt < window.ToUtc)
            .GroupBy(job => job.CompletedAt!.Value.Date)
            .Select(group => new DailyQuantity(group.Key, group.LongCount(), group.Sum(job => job.Items.Sum(item => (long)item.Quantity))))
            .ToArrayAsync(ct);
        return new(window, statuses, priorities, Counts(window, daily), Quantities(window, completed));
    }
}
