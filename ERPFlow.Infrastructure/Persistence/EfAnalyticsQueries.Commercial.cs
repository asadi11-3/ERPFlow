using ERPFlow.Application.Analytics;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Procurement;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class EfAnalyticsQueries
{
    public async Task<SalesAnalytics> SalesAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        var actor = await AuthorizeAsync(ct, SalesReaders);
        var window = Window(query);
        var orders = context.SalesOrders.AsNoTracking().Where(order =>
            order.CreatedAt >= window.FromUtc && order.CreatedAt < window.ToUtc &&
            (actor.Role != UserRole.Sales || order.CreatedByUserId == actor.Id));
        var statuses = await StatusesAsync(orders.Select(order => order.Status), ct);
        var daily = await orders.GroupBy(order => order.CreatedAt.Date).Select(group => new DailyValue(
            group.Key, group.LongCount(), group.Sum(order => order.Status == OrderStatus.Approved
                ? order.Items.Sum(item => item.Quantity * item.UnitPrice) : 0))).ToArrayAsync(ct);
        var top = await (from order in orders
            where order.Status == OrderStatus.Approved
            from item in order.Items
            join product in context.Products.AsNoTracking() on item.ProductId equals product.Id
            group item by new { product.Id, product.Name, product.Sku } into items
            select new { items.Key.Id, items.Key.Name, items.Key.Sku,
                Quantity = items.Sum(item => (long)item.Quantity), Amount = items.Sum(item => item.Quantity * item.UnitPrice) })
            .OrderByDescending(row => row.Amount).ThenBy(row => row.Id).Take(10)
            .Select(row => new ProductSales(row.Id, row.Name, row.Sku, row.Quantity, row.Amount)).ToArrayAsync(ct);
        return new(window, statuses, statuses.Sum(row => row.Count), daily.Sum(row => row.Amount), Values(window, daily), top);
    }

    public async Task<ProcurementAnalytics> ProcurementAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        var actor = await AuthorizeAsync(ct, CommercialReaders);
        var window = Window(query);
        var orders = context.PurchaseOrders.AsNoTracking().Where(order =>
            order.CreatedAt >= window.FromUtc && order.CreatedAt < window.ToUtc &&
            (actor.Role != UserRole.Purchasing || order.CreatedByUserId == actor.Id));
        var statuses = await StatusesAsync(orders.Select(order => order.Status), ct);
        var committed = orders.Where(order => order.Status == PurchaseOrderStatus.Approved ||
            order.Status == PurchaseOrderStatus.PartiallyReceived || order.Status == PurchaseOrderStatus.Received ||
            order.Status == PurchaseOrderStatus.ShortClosed);
        var daily = await orders.GroupBy(order => order.CreatedAt.Date).Select(group => new DailyValue(
            group.Key, group.LongCount(), group.Sum(order => order.Status == PurchaseOrderStatus.Approved ||
                order.Status == PurchaseOrderStatus.PartiallyReceived || order.Status == PurchaseOrderStatus.Received ||
                order.Status == PurchaseOrderStatus.ShortClosed ? order.TotalAmount : 0))).ToArrayAsync(ct);
        var quantities = await committed.SelectMany(order => order.Items).GroupBy(_ => 1)
            .Select(group => new { Ordered = group.Sum(item => (long)item.Quantity), Received = group.Sum(item => (long)item.ReceivedQuantity) })
            .SingleOrDefaultAsync(ct);
        var suppliers = await (from order in committed
            join supplier in context.Suppliers.AsNoTracking() on order.SupplierId equals supplier.Id
            group order by new { supplier.Id, supplier.Name } into groupOrders
            select new { groupOrders.Key.Id, groupOrders.Key.Name, Count = groupOrders.LongCount(),
                Amount = groupOrders.Sum(order => order.TotalAmount) })
            .OrderByDescending(row => row.Amount).ThenBy(row => row.Id).Take(10)
            .Select(row => new SupplierPurchases(row.Id, row.Name, row.Count, row.Amount)).ToArrayAsync(ct);
        return new(window, statuses, statuses.Sum(row => row.Count), daily.Sum(row => row.Amount),
            quantities?.Ordered ?? 0, quantities?.Received ?? 0, Values(window, daily), suppliers);
    }

    public async Task<ApprovalsAnalytics> ApprovalsAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        await AuthorizeAsync(ct, UserRole.Administrator, UserRole.Approver);
        var window = Window(query);
        var snapshot = DateTime.UtcNow;
        var pendingSales = await context.SalesOrders.AsNoTracking().LongCountAsync(order => order.Status == OrderStatus.PendingApproval, ct);
        var pendingPurchases = await context.PurchaseOrders.AsNoTracking().LongCountAsync(order => order.Status == PurchaseOrderStatus.PendingApproval, ct);
        var sales = await context.ApprovalHistories.AsNoTracking()
            .Where(entry => entry.CreatedAt >= window.FromUtc && entry.CreatedAt < window.ToUtc)
            .GroupBy(entry => new { Date = entry.CreatedAt.Date, entry.Action })
            .Select(group => new { group.Key.Date, group.Key.Action, Count = group.LongCount() }).ToArrayAsync(ct);
        var purchases = await context.PurchaseOrderHistories.AsNoTracking()
            .Where(entry => entry.CreatedAt >= window.FromUtc && entry.CreatedAt < window.ToUtc &&
                (entry.Action == PurchaseOrderAction.Submitted || entry.Action == PurchaseOrderAction.Approved || entry.Action == PurchaseOrderAction.Rejected))
            .GroupBy(entry => new { Date = entry.CreatedAt.Date, entry.Action })
            .Select(group => new { group.Key.Date, group.Key.Action, Count = group.LongCount() }).ToArrayAsync(ct);
        var salesBuckets = sales.GroupBy(row => window.Bucket(DateOnly.FromDateTime(row.Date)))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var purchaseBuckets = purchases.GroupBy(row => window.Bucket(DateOnly.FromDateTime(row.Date)))
            .ToDictionary(group => group.Key, group => group.ToArray());
        return new(window, snapshot, pendingSales, pendingPurchases,
            window.Buckets().Select(date => new ApprovalPoint(date,
                salesBuckets.GetValueOrDefault(date)?.Where(row => row.Action == ApprovalAction.Submitted).Sum(row => row.Count) ?? 0,
                salesBuckets.GetValueOrDefault(date)?.Where(row => row.Action == ApprovalAction.Approved).Sum(row => row.Count) ?? 0,
                salesBuckets.GetValueOrDefault(date)?.Where(row => row.Action == ApprovalAction.Rejected).Sum(row => row.Count) ?? 0,
                salesBuckets.GetValueOrDefault(date)?.Where(row => row.Action == ApprovalAction.Returned).Sum(row => row.Count) ?? 0)).ToArray(),
            window.Buckets().Select(date => new ApprovalPoint(date,
                purchaseBuckets.GetValueOrDefault(date)?.Where(row => row.Action == PurchaseOrderAction.Submitted).Sum(row => row.Count) ?? 0,
                purchaseBuckets.GetValueOrDefault(date)?.Where(row => row.Action == PurchaseOrderAction.Approved).Sum(row => row.Count) ?? 0,
                purchaseBuckets.GetValueOrDefault(date)?.Where(row => row.Action == PurchaseOrderAction.Rejected).Sum(row => row.Count) ?? 0, 0)).ToArray());
    }
}
