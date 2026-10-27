using ERPFlow.Application.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace ERPFlow.Api;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder api)
    {
        var analytics = api.MapGroup("/analytics").RequireAuthorization().WithTags("Analytics");
        analytics.MapGet("/sales", ([AsParameters] AnalyticsParameters parameters, IAnalyticsQueries queries, CancellationToken ct) =>
            queries.SalesAsync(parameters.Query(), ct));
        analytics.MapGet("/approvals", ([AsParameters] AnalyticsParameters parameters, IAnalyticsQueries queries, CancellationToken ct) =>
            queries.ApprovalsAsync(parameters.Query(), ct));
        analytics.MapGet("/procurement", ([AsParameters] AnalyticsParameters parameters, IAnalyticsQueries queries, CancellationToken ct) =>
            queries.ProcurementAsync(parameters.Query(), ct));
        analytics.MapGet("/receiving", ([AsParameters] AnalyticsParameters parameters, IAnalyticsQueries queries, CancellationToken ct) =>
            queries.ReceivingAsync(parameters.Query(), ct));
        analytics.MapGet("/transfers", ([AsParameters] AnalyticsParameters parameters, IAnalyticsQueries queries, CancellationToken ct) =>
            queries.TransfersAsync(parameters.Query(), ct));
        analytics.MapGet("/inventory", ([AsParameters] AnalyticsParameters parameters, IAnalyticsQueries queries, CancellationToken ct) =>
            queries.InventoryAsync(parameters.Query(), ct));
        analytics.MapGet("/dispatch", ([AsParameters] AnalyticsParameters parameters, IAnalyticsQueries queries, CancellationToken ct) =>
            queries.DispatchAsync(parameters.Query(), ct));
        analytics.MapGet("/service", ([AsParameters] AnalyticsParameters parameters, IAnalyticsQueries queries, CancellationToken ct) =>
            queries.ServiceAsync(parameters.Query(), ct));
        return api;
    }

    public sealed record AnalyticsParameters(DateOnly? From = null, DateOnly? To = null, string GroupBy = "day")
    {
        public AnalyticsQuery Query() => new(From, To, GroupBy);
    }
}
