using ERPFlow.Application.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace ERPFlow.Api;

public static class InventoryQueryEndpoints
{
    public const string Policy = "InventoryOperationalRead";

    public static void MapInventoryQueryEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/inventory").WithTags("Inventory").RequireAuthorization(Policy);

        group.MapGet("/stocks", (IInventoryQueryWorkflow workflow, CancellationToken ct,
                int page = 1, int pageSize = 20, Guid? warehouseId = null, Guid? productId = null) =>
                workflow.StocksAsync(new StockQuery(page, pageSize, warehouseId, productId), ct))
            .Errors();

        group.MapGet("/movements", (IInventoryQueryWorkflow workflow, CancellationToken ct,
                int page = 1, int pageSize = 20, Guid? warehouseId = null, Guid? productId = null,
                DateTimeOffset? from = null, DateTimeOffset? to = null) =>
                workflow.MovementsAsync(new MovementQuery(page, pageSize, warehouseId, productId, from, to), ct))
            .Errors();
    }

    private static RouteHandlerBuilder Errors(this RouteHandlerBuilder endpoint) => endpoint
        .Produces<ProblemDetails>(400)
        .Produces<ProblemDetails>(401)
        .Produces<ProblemDetails>(403)
        .Produces<ProblemDetails>(409);
}
