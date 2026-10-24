using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Equipment;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Application.Service;
using Microsoft.AspNetCore.Mvc;

namespace ERPFlow.Api;

public static class ApiPolicies
{
    public const string Sales = "Sales";
    public const string Approver = "Approver";
    public const string Technician = "Technician";
}

public sealed record LoginRequest(string Email, string Password);

public sealed record RejectOrderRequest(string Reason);

public sealed record ReturnOrderRequest(string Reason);

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
        orders.MapGet("/", (
                ISalesOrderWorkflow workflow,
                CancellationToken ct,
                int page = 1,
                int pageSize = 10,
                int? status = null) => workflow.ListAsync(
                    page, pageSize, status.HasValue ? (ERPFlow.Domain.Enums.OrderStatus)status.Value : null, ct))
            .RequireAuthorization();
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
        orders.MapGet("/{id:guid}/history", (
                Guid id,
                ISalesOrderWorkflow workflow,
                CancellationToken ct) => workflow.GetApprovalHistoryAsync(id, ct))
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
        orders.MapPost("/{id:guid}/return", (
                Guid id,
                ReturnOrderRequest request,
                ISalesOrderWorkflow workflow,
                CancellationToken ct) => workflow.ReturnAsync(id, request.Reason, ct))
            .RequireAuthorization(ApiPolicies.Approver);

        var equipment = api.MapGroup("/equipment").WithTags("Equipment & Delivery").RequireAuthorization();
        equipment.MapPost("/sales-orders/{id:guid}/dispatch", (
                Guid id,
                DispatchOrderCommand command,
                IEquipmentWorkflow workflow,
                CancellationToken ct) => workflow.DispatchOrderAsync(id, command, ct))
            .RequireAuthorization(ApiPolicies.Technician);
        equipment.MapGet("/delivery-notes", (
                IEquipmentWorkflow workflow,
                CancellationToken ct) => workflow.ListDeliveryNotesAsync(ct));
        equipment.MapGet("/machine-assets", (
                IEquipmentWorkflow workflow,
                CancellationToken ct) => workflow.ListMachineAssetsAsync(ct));
        equipment.MapGet("/machine-assets/{id:guid}", (
                Guid id,
                IEquipmentWorkflow workflow,
                CancellationToken ct) => workflow.GetMachineAssetByIdAsync(id, ct));

        var service = api.MapGroup("/service").WithTags("Service Operations").RequireAuthorization();
        service.MapGet("/jobcards", (
                IServiceWorkflow workflow,
                CancellationToken ct,
                ERPFlow.Domain.Enums.JobcardStatus? status = null) => workflow.ListJobcardsAsync(status, ct));
        service.MapGet("/jobcards/{id:guid}", (
                Guid id,
                IServiceWorkflow workflow,
                CancellationToken ct) => workflow.GetJobcardByIdAsync(id, ct));
        service.MapPost("/jobcards", (
                CreateJobcardCommand command,
                IServiceWorkflow workflow,
                CancellationToken ct) => workflow.CreateJobcardAsync(command, ct))
            .RequireAuthorization(ApiPolicies.Technician);
        service.MapPost("/jobcards/{id:guid}/request-parts", (
                Guid id,
                RequestJobcardPartsCommand command,
                IServiceWorkflow workflow,
                CancellationToken ct) => workflow.RequestPartsAsync(id, command, ct))
            .RequireAuthorization(ApiPolicies.Technician);
        service.MapPost("/jobcards/{id:guid}/complete", (
                Guid id,
                CompleteJobcardCommand command,
                IServiceWorkflow workflow,
                CancellationToken ct) => workflow.CompleteJobcardAsync(id, command, ct))
            .RequireAuthorization(ApiPolicies.Technician);

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
