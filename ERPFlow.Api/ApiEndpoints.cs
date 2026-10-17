using ERPFlow.Application.Abstractions;
using ERPFlow.Application.SalesOrders;
using Microsoft.AspNetCore.Mvc;

namespace ERPFlow.Api;

public static class ApiPolicies
{
    public const string Sales = "Sales";
    public const string Approver = "Approver";
}

public sealed record LoginRequest(string Email, string Password);

public sealed record RejectOrderRequest(string Reason);

public sealed record ApproveOrderRequest(string? Comments);

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost("/auth/login", LoginAsync)
            .AllowAnonymous()
            .WithTags("Authentication")
            .Produces<AccessTokenResult>()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        var references = api.MapGroup("/reference")
            .RequireAuthorization()
            .WithTags("Reference Data");
        references.MapGet("/customers", (IReferenceDataQueries queries, CancellationToken ct) =>
            queries.ListActiveCustomersAsync(ct));
        references.MapGet("/products", (IReferenceDataQueries queries, CancellationToken ct) =>
            queries.ListProductsAsync(ct));

        var orders = api.MapGroup("/sales-orders").WithTags("Sales Orders");
        orders.MapPost("/", async (
                CreateSalesOrderCommand command,
                ISalesOrderWorkflow workflow,
                CancellationToken ct) =>
            {
                var order = await workflow.CreateDraftAsync(command, ct);
                return Results.Created($"/api/sales-orders/{order.Id}", order);
            })
            .RequireAuthorization(ApiPolicies.Sales)
            .Produces<SalesOrderResult>(StatusCodes.Status201Created);
        orders.MapGet("/{id:guid}", (
                Guid id,
                ISalesOrderWorkflow workflow,
                CancellationToken ct) => workflow.GetByIdAsync(id, ct))
            .RequireAuthorization();
        orders.MapPost("/{id:guid}/submit", (
                Guid id,
                ISalesOrderWorkflow workflow,
                CancellationToken ct) => workflow.SubmitAsync(id, ct))
            .RequireAuthorization(ApiPolicies.Sales);
        orders.MapGet("/pending", (
                ISalesOrderWorkflow workflow,
                CancellationToken ct) => workflow.ListPendingApprovalsAsync(ct))
            .RequireAuthorization(ApiPolicies.Approver);
        orders.MapPost("/{id:guid}/approve", (
                Guid id,
                ApproveOrderRequest request,
                ISalesOrderWorkflow workflow,
                CancellationToken ct) => workflow.ApproveAsync(id, request.Comments, ct))
            .RequireAuthorization(ApiPolicies.Approver);
        orders.MapPost("/{id:guid}/reject", (
                Guid id,
                RejectOrderRequest request,
                ISalesOrderWorkflow workflow,
                CancellationToken ct) => workflow.RejectAsync(id, request.Reason, ct))
            .RequireAuthorization(ApiPolicies.Approver);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IUserDirectory users,
        JwtTokenService tokens,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            request.Email.Length > 320 ||
            string.IsNullOrEmpty(request.Password) ||
            request.Password.Length > 256)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = ["Email and password are required."]
            });
        }

        var user = await users.FindByEmailAsync(request.Email, cancellationToken);
        if (user is null || !users.VerifyPassword(user, request.Password))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                detail: "The supplied email or password is invalid.");
        }

        return Results.Ok(tokens.Create(user));
    }
}
