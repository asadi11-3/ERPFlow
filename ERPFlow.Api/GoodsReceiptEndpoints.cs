using ERPFlow.Application.Receiving;
using ERPFlow.Domain.Receiving;
using Microsoft.AspNetCore.Mvc;

namespace ERPFlow.Api;

public static class GoodsReceiptEndpoints
{
    public const string Policy = "GoodsReceiptWarehouseOperations";
    public static void MapGoodsReceiptEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/goods-receipts").WithTags("Goods Receipts").RequireAuthorization(Policy);
        group.MapGet("/", (IGoodsReceiptWorkflow workflow, CancellationToken ct, int page = 1, int pageSize = 20,
            Guid? purchaseOrderId = null, GoodsReceiptStatus? status = null) => workflow.ListAsync(page, pageSize, purchaseOrderId, status, ct)).Errors();
        group.MapGet("/{id:guid}", (Guid id, IGoodsReceiptWorkflow workflow, CancellationToken ct) => workflow.GetAsync(id, ct)).Errors();
        group.MapPost("/", async (CreateGoodsReceiptCommand command, IGoodsReceiptWorkflow workflow, CancellationToken ct) =>
        {
            var receipt = await workflow.CreateAsync(command, ct);
            return Results.Created($"/api/goods-receipts/{receipt.Id}", receipt);
        }).Produces<GoodsReceiptResult>(201).Errors();
        group.MapPost("/{id:guid}/post", (Guid id, PostGoodsReceiptCommand command, IGoodsReceiptWorkflow workflow, CancellationToken ct) =>
            workflow.PostAsync(id, command, ct)).Errors();
        group.MapPost("/{id:guid}/cancel", (Guid id, CancelGoodsReceiptCommand command, IGoodsReceiptWorkflow workflow, CancellationToken ct) =>
            workflow.CancelAsync(id, command, ct)).Errors();
    }
    private static RouteHandlerBuilder Errors(this RouteHandlerBuilder endpoint) => endpoint.Produces<ProblemDetails>(400)
        .Produces<ProblemDetails>(401).Produces<ProblemDetails>(403).Produces<ProblemDetails>(404).Produces<ProblemDetails>(409);
}
