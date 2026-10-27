using ERPFlow.Application.Common;

namespace ERPFlow.Application.Analytics;

public sealed record AnalyticsQuery(DateOnly? From = null, DateOnly? To = null, string GroupBy = "day");

public sealed record AnalyticsWindow(DateOnly From, DateOnly To, string GroupBy)
{
    public DateTime FromUtc => From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    public DateTime ToUtc => To.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public static AnalyticsWindow Create(AnalyticsQuery query, DateTime utcNow)
    {
        var to = query.To ?? DateOnly.FromDateTime(utcNow).AddDays(1);
        var from = query.From ?? DateOnly.FromDayNumber(Math.Max(0, to.DayNumber - 30));
        if (from >= to || to.DayNumber - from.DayNumber > 366)
            throw new WorkflowValidationException("From must precede To by at most 366 days; To is exclusive.");
        if (query.GroupBy is not ("day" or "week" or "month"))
            throw new WorkflowValidationException("GroupBy must be day, week, or month.");
        return new(from, to, query.GroupBy);
    }

    public DateOnly Bucket(DateOnly date) => GroupBy switch
    {
        "week" => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        "month" => new(date.Year, date.Month, 1),
        _ => date
    };

    public IReadOnlyList<DateOnly> Buckets()
    {
        var result = new List<DateOnly>();
        var last = Bucket(To.AddDays(-1));
        for (var date = Bucket(From);; date = GroupBy switch
        {
            "week" => date.AddDays(7), "month" => date.AddMonths(1), _ => date.AddDays(1)
        })
        {
            result.Add(date);
            if (date == last) return result;
        }
    }
}

public sealed record StatusCount(int Status, string Label, long Count);
public sealed record CountPoint(DateOnly Date, long Count);
public sealed record ValuePoint(DateOnly Date, long Count, decimal Amount);
public sealed record QuantityPoint(DateOnly Date, long Count, long Quantity);
public sealed record ProductSales(Guid ProductId, string Name, string Sku, long Quantity, decimal Amount);
public sealed record SupplierPurchases(Guid SupplierId, string Name, long OrderCount, decimal Amount);
public sealed record SalesAnalytics(AnalyticsWindow Window, IReadOnlyList<StatusCount> ByStatus,
    long OrderCount, decimal ApprovedOrderValue, IReadOnlyList<ValuePoint> Created,
    IReadOnlyList<ProductSales> TopProducts);
public sealed record ApprovalPoint(DateOnly Date, long Submitted, long Approved, long Rejected, long Returned);
public sealed record ApprovalsAnalytics(AnalyticsWindow Window, DateTime SnapshotAtUtc,
    long PendingSalesOrders, long PendingPurchaseOrders,
    IReadOnlyList<ApprovalPoint> SalesActivity, IReadOnlyList<ApprovalPoint> PurchaseActivity);
public sealed record ProcurementAnalytics(AnalyticsWindow Window, IReadOnlyList<StatusCount> ByStatus,
    long OrderCount, decimal CommittedOrderValue, long OrderedQuantity, long ReceivedQuantity,
    IReadOnlyList<ValuePoint> Created, IReadOnlyList<SupplierPurchases> TopSuppliers);
public sealed record ReceivingAnalytics(AnalyticsWindow Window, IReadOnlyList<StatusCount> ByStatus,
    IReadOnlyList<QuantityPoint> Posted);
public sealed record TransfersAnalytics(AnalyticsWindow Window, IReadOnlyList<StatusCount> ByStatus,
    IReadOnlyList<QuantityPoint> Posted);
public sealed record WarehouseBalance(Guid WarehouseId, string Code, string Name, long StockOnHand,
    long ReservedQuantity, long AvailableQuantity, long OutOfStockPositions);
public sealed record MovementPoint(DateOnly Date, long PhysicalIn, long PhysicalOut, long ReservedDelta);
public sealed record InventoryAnalytics(AnalyticsWindow Window, DateTime SnapshotAtUtc,
    IReadOnlyList<WarehouseBalance> ByWarehouse, IReadOnlyList<StatusCount> MovementsByKind,
    IReadOnlyList<MovementPoint> Movements);
public sealed record CarrierDispatches(string Carrier, long Count);
public sealed record AssetStatusCount(string Status, long Count);
public sealed record DispatchAnalytics(AnalyticsWindow Window, long Delivered, long AwaitingDelivery,
    IReadOnlyList<CountPoint> Dispatched, IReadOnlyList<CarrierDispatches> ByCarrier,
    IReadOnlyList<AssetStatusCount> InstalledAssetsByStatus);
public sealed record ServiceAnalytics(AnalyticsWindow Window, IReadOnlyList<StatusCount> ByStatus,
    IReadOnlyList<StatusCount> ByPriority, IReadOnlyList<CountPoint> Created,
    IReadOnlyList<QuantityPoint> Completed);

public interface IAnalyticsQueries
{
    Task<SalesAnalytics> SalesAsync(AnalyticsQuery query, CancellationToken ct = default);
    Task<ApprovalsAnalytics> ApprovalsAsync(AnalyticsQuery query, CancellationToken ct = default);
    Task<ProcurementAnalytics> ProcurementAsync(AnalyticsQuery query, CancellationToken ct = default);
    Task<ReceivingAnalytics> ReceivingAsync(AnalyticsQuery query, CancellationToken ct = default);
    Task<TransfersAnalytics> TransfersAsync(AnalyticsQuery query, CancellationToken ct = default);
    Task<InventoryAnalytics> InventoryAsync(AnalyticsQuery query, CancellationToken ct = default);
    Task<DispatchAnalytics> DispatchAsync(AnalyticsQuery query, CancellationToken ct = default);
    Task<ServiceAnalytics> ServiceAsync(AnalyticsQuery query, CancellationToken ct = default);
}
