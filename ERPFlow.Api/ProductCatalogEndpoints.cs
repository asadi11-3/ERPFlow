using ERPFlow.Application.Products;
using Microsoft.AspNetCore.Mvc;

namespace ERPFlow.Api;

public static class ProductCatalogEndpoints
{
    public static void MapProductCatalogEndpoints(this RouteGroupBuilder api)
    {
        var products = api.MapGroup("/products").WithTags("Product Catalog").RequireAuthorization();
        Errors(products.MapGet("/{id:guid}", (Guid id, IProductCatalog workflow, CancellationToken ct) => workflow.GetAsync(id, ct)));
        Errors(products.MapPost("", async (CreateProductCommand command, IProductCatalog workflow, CancellationToken ct) =>
        {
            var result = await workflow.CreateAsync(command, ct);
            return Results.Created($"/api/products/{result.Id}", result);
        }).RequireAuthorization(ProcurementPolicies.Administrator).Produces<ProductDetails>(201));
        Errors(products.MapPut("/{id:guid}", (Guid id, UpdateProductCommand command, IProductCatalog workflow, CancellationToken ct) =>
            workflow.UpdateAsync(id, command, ct)).RequireAuthorization(ProcurementPolicies.Administrator));
        Errors(products.MapPost("/{id:guid}/activate", (Guid id, ProductVersionCommand command, IProductCatalog workflow, CancellationToken ct) =>
            workflow.SetActiveAsync(id, command, true, ct)).RequireAuthorization(ProcurementPolicies.Administrator));
        Errors(products.MapPost("/{id:guid}/deactivate", (Guid id, ProductVersionCommand command, IProductCatalog workflow, CancellationToken ct) =>
            workflow.SetActiveAsync(id, command, false, ct)).RequireAuthorization(ProcurementPolicies.Administrator));
    }
    private static RouteHandlerBuilder Errors(RouteHandlerBuilder endpoint) => endpoint
        .Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(403)
        .Produces<ProblemDetails>(404).Produces<ProblemDetails>(409);
}
