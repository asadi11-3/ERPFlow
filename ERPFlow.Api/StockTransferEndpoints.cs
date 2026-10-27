using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Transfers;
using Microsoft.AspNetCore.Mvc;

namespace ERPFlow.Api;

public static class StockTransferEndpoints
{
    public const string Policy = "StockTransferOperations";
    public static void MapStockTransferEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/stock-transfers").WithTags("Stock Transfers").RequireAuthorization(Policy);
        group.MapGet("/", (IStockTransferWorkflow workflow, CancellationToken ct, int page = 1, int pageSize = 20,
            Guid? warehouseId = null, Guid? productId = null, StockTransferStatus? status = null) => workflow.ListAsync(page, pageSize, warehouseId, productId, status, ct)).Errors();
        group.MapGet("/{id:guid}", (Guid id, IStockTransferWorkflow workflow, CancellationToken ct) => workflow.GetAsync(id, ct)).Errors();
        group.MapPost("/", async ([FromHeader(Name = "Idempotency-Key")] string key, CreateStockTransferCommand command, IStockTransferWorkflow workflow, CancellationToken ct) =>
        {
            var result = await workflow.CreateAsync(key, command, ct);
            return Results.Created($"/api/stock-transfers/{result.Id}", result);
        }).Produces<StockTransferResult>(201).Errors();
        group.MapPost("/{id:guid}/post", (Guid id, [FromHeader(Name = "Idempotency-Key")] string key, PostStockTransferCommand command,
            IStockTransferWorkflow workflow, CancellationToken ct) => workflow.PostAsync(id, key, command, ct)).Errors();
        group.MapPost("/{id:guid}/cancel", (Guid id, CancelStockTransferCommand command, IStockTransferWorkflow workflow, CancellationToken ct) =>
            workflow.CancelAsync(id, command, ct)).Errors();
    }
    private static RouteHandlerBuilder Errors(this RouteHandlerBuilder endpoint) => endpoint.Produces<ProblemDetails>(400)
        .Produces<ProblemDetails>(401).Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409);
}
