using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Analytics;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class EfAnalyticsQueries(ERPFlowDbContext context, ICurrentUser currentUser) : IAnalyticsQueries
{
    private static readonly UserRole[] CommercialReaders = [UserRole.Administrator, UserRole.Approver, UserRole.Purchasing];
    private static readonly UserRole[] InventoryReaders = [.. CommercialReaders, UserRole.WarehouseOperator];
    private static readonly UserRole[] SalesReaders = [UserRole.Administrator, UserRole.Approver, UserRole.Sales, UserRole.Technician];

    private async Task<AppUser> AuthorizeAsync(CancellationToken ct, params UserRole[] roles)
    {
        var actor = await context.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == currentUser.UserId, ct);
        if (actor is null || !roles.Contains(actor.Role))
            throw new WorkflowAuthorizationException("The persisted user is not authorized for these analytics.");
        return actor;
    }

    private static AnalyticsWindow Window(AnalyticsQuery query) => AnalyticsWindow.Create(query, DateTime.UtcNow);

    private static async Task<IReadOnlyList<StatusCount>> StatusesAsync<T>(IQueryable<T> query, CancellationToken ct)
        where T : struct, Enum
    {
        var rows = await query.GroupBy(status => status)
            .Select(group => new { Status = group.Key, Count = group.LongCount() }).ToArrayAsync(ct);
        var counts = rows.ToDictionary(row => row.Status, row => row.Count);
        return Enum.GetValues<T>().Select(status => new StatusCount(Convert.ToInt32(status), status.ToString(),
            counts.GetValueOrDefault(status))).ToArray();
    }

    private static IReadOnlyList<CountPoint> Counts(AnalyticsWindow window, IEnumerable<DailyCount> rows)
    {
        var grouped = rows.GroupBy(row => window.Bucket(DateOnly.FromDateTime(row.Date)))
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Count));
        return window.Buckets().Select(date => new CountPoint(date, grouped.GetValueOrDefault(date))).ToArray();
    }

    private static IReadOnlyList<ValuePoint> Values(AnalyticsWindow window, IEnumerable<DailyValue> rows)
    {
        var grouped = rows.GroupBy(row => window.Bucket(DateOnly.FromDateTime(row.Date)))
            .ToDictionary(group => group.Key, group => new { Count = group.Sum(row => row.Count), Amount = group.Sum(row => row.Amount) });
        return window.Buckets().Select(date => new ValuePoint(date, grouped.GetValueOrDefault(date)?.Count ?? 0,
            grouped.GetValueOrDefault(date)?.Amount ?? 0)).ToArray();
    }

    private static IReadOnlyList<QuantityPoint> Quantities(AnalyticsWindow window, IEnumerable<DailyQuantity> rows)
    {
        var grouped = rows.GroupBy(row => window.Bucket(DateOnly.FromDateTime(row.Date)))
            .ToDictionary(group => group.Key, group => new { Count = group.Sum(row => row.Count), Quantity = group.Sum(row => row.Quantity) });
        return window.Buckets().Select(date => new QuantityPoint(date, grouped.GetValueOrDefault(date)?.Count ?? 0,
            grouped.GetValueOrDefault(date)?.Quantity ?? 0)).ToArray();
    }

    private sealed record DailyCount(DateTime Date, long Count);
    private sealed record DailyValue(DateTime Date, long Count, decimal Amount);
    private sealed record DailyQuantity(DateTime Date, long Count, long Quantity);
}
