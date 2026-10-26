using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Procurement;
using Microsoft.AspNetCore.Mvc;

namespace ERPFlow.Api;

public static class ProcurementPolicies
{
    public const string Administrator = "ProcurementAdministrator";
    public const string Write = "ProcurementWrite";
    public const string CommercialRead = "ProcurementCommercialRead";
    public const string OperationalRead = "ProcurementOperationalRead";
}

public static class ProcurementEndpoints
{
    public static RouteGroupBuilder MapProcurementEndpoints(this RouteGroupBuilder api)
    {
        var warehouses = api.MapGroup("/warehouses").WithTags("Warehouses").RequireAuthorization();
        warehouses.MapGet("/", (IProcurementMasters workflow, CancellationToken ct, int page = 1, int pageSize = 20, bool? active = null) =>
            workflow.ListWarehousesAsync(page, pageSize, active, ct)).Errors();
        warehouses.MapGet("/{id:guid}", (Guid id, IProcurementMasters workflow, CancellationToken ct) => workflow.GetWarehouseAsync(id, ct)).Errors();
        warehouses.MapPost("/", async (CreateWarehouseCommand command, IProcurementMasters workflow, CancellationToken ct) =>
        {
            var result = await workflow.CreateWarehouseAsync(command, ct);
            return Results.Created($"/api/warehouses/{result.Id}", result);
        }).RequireAuthorization(ProcurementPolicies.Administrator).Produces<WarehouseResult>(201).Errors();
        warehouses.MapPut("/{id:guid}", (Guid id, UpdateWarehouseCommand command, IProcurementMasters workflow, CancellationToken ct) =>
            workflow.UpdateWarehouseAsync(id, command, ct)).RequireAuthorization(ProcurementPolicies.Administrator).Errors();
        warehouses.MapPost("/{id:guid}/activate", (Guid id, VersionCommand command, IProcurementMasters workflow, CancellationToken ct) =>
            workflow.SetWarehouseActiveAsync(id, command, true, ct)).RequireAuthorization(ProcurementPolicies.Administrator).Errors();
        warehouses.MapPost("/{id:guid}/deactivate", (Guid id, VersionCommand command, IProcurementMasters workflow, CancellationToken ct) =>
            workflow.SetWarehouseActiveAsync(id, command, false, ct)).RequireAuthorization(ProcurementPolicies.Administrator).Errors();

        var suppliers = api.MapGroup("/suppliers").WithTags("Suppliers").RequireAuthorization(ProcurementPolicies.CommercialRead);
        suppliers.MapGet("/", (IProcurementMasters workflow, CancellationToken ct, int page = 1, int pageSize = 20, string? search = null, bool? active = null) =>
            workflow.ListSuppliersAsync(page, pageSize, search, active, ct)).Errors();
        suppliers.MapGet("/{id:guid}", (Guid id, IProcurementMasters workflow, CancellationToken ct) => workflow.GetSupplierAsync(id, ct)).Errors();
        suppliers.MapPost("/", async (CreateSupplierCommand command, IProcurementMasters workflow, CancellationToken ct) =>
        {
            var result = await workflow.CreateSupplierAsync(command, ct);
            return Results.Created($"/api/suppliers/{result.Id}", result);
        }).RequireAuthorization(ProcurementPolicies.Write).Produces<SupplierResult>(201).Errors();
        suppliers.MapPut("/{id:guid}", (Guid id, UpdateSupplierCommand command, IProcurementMasters workflow, CancellationToken ct) =>
            workflow.UpdateSupplierAsync(id, command, ct)).RequireAuthorization(ProcurementPolicies.Write).Errors();
        suppliers.MapPost("/{id:guid}/activate", (Guid id, VersionCommand command, IProcurementMasters workflow, CancellationToken ct) =>
            workflow.SetSupplierActiveAsync(id, command, true, ct)).RequireAuthorization(ProcurementPolicies.Write).Errors();
        suppliers.MapPost("/{id:guid}/deactivate", (Guid id, VersionCommand command, IProcurementMasters workflow, CancellationToken ct) =>
            workflow.SetSupplierActiveAsync(id, command, false, ct)).RequireAuthorization(ProcurementPolicies.Write).Errors();

        var operational = api.MapGroup("/purchase-orders/operational").WithTags("Purchase Order Operations").RequireAuthorization(ProcurementPolicies.OperationalRead);
        operational.MapGet("/", (IPurchaseOrderWorkflow workflow, CancellationToken ct, int page = 1, int pageSize = 20, PurchaseOrderStatus? status = null) =>
            workflow.ListOperationalAsync(page, pageSize, status, ct)).Errors();
        operational.MapGet("/{id:guid}", (Guid id, IPurchaseOrderWorkflow workflow, CancellationToken ct) => workflow.GetOperationalAsync(id, ct)).Errors();

        var orders = api.MapGroup("/purchase-orders").WithTags("Purchase Orders").RequireAuthorization(ProcurementPolicies.CommercialRead);
        orders.MapGet("/", (IPurchaseOrderWorkflow workflow, CancellationToken ct, int page = 1, int pageSize = 20, PurchaseOrderStatus? status = null) =>
            workflow.ListAsync(page, pageSize, status, ct)).Errors();
        orders.MapGet("/{id:guid}", (Guid id, IPurchaseOrderWorkflow workflow, CancellationToken ct) => workflow.GetAsync(id, ct)).Errors();
        orders.MapGet("/{id:guid}/history", (Guid id, IPurchaseOrderWorkflow workflow, CancellationToken ct) => workflow.HistoryAsync(id, ct)).Errors();
        orders.MapPost("/", async (CreatePurchaseOrderCommand command, IPurchaseOrderWorkflow workflow, CancellationToken ct) =>
        {
            var result = await workflow.CreateAsync(command, ct);
            return Results.Created($"/api/purchase-orders/{result.Id}", result);
        }).RequireAuthorization(ProcurementPolicies.Write).Produces<PurchaseOrderResult>(201).Errors();
        orders.MapPut("/{id:guid}", (Guid id, UpdatePurchaseOrderCommand command, IPurchaseOrderWorkflow workflow, CancellationToken ct) =>
            workflow.UpdateAsync(id, command, ct)).RequireAuthorization(ProcurementPolicies.Write).Errors();
        orders.MapPost("/{id:guid}/submit", (Guid id, VersionCommand command, IPurchaseOrderWorkflow workflow, CancellationToken ct) =>
            workflow.SubmitAsync(id, command, ct)).RequireAuthorization(ProcurementPolicies.Write).Errors();
        orders.MapPost("/{id:guid}/approve", (Guid id, PurchaseOrderDecisionCommand command, IPurchaseOrderWorkflow workflow, CancellationToken ct) =>
            workflow.ApproveAsync(id, command, ct)).RequireAuthorization(ApiPolicies.Approver).Errors();
        orders.MapPost("/{id:guid}/reject", (Guid id, PurchaseOrderReasonCommand command, IPurchaseOrderWorkflow workflow, CancellationToken ct) =>
            workflow.RejectAsync(id, command, ct)).RequireAuthorization(ApiPolicies.Approver).Errors();
        orders.MapPost("/{id:guid}/cancel", (Guid id, PurchaseOrderReasonCommand command, IPurchaseOrderWorkflow workflow, CancellationToken ct) =>
            workflow.CancelAsync(id, command, ct)).RequireAuthorization(ProcurementPolicies.Write).Errors();
        return api;
    }

    private static RouteHandlerBuilder Errors(this RouteHandlerBuilder endpoint) => endpoint
        .Produces<ProblemDetails>(400).Produces<ProblemDetails>(401).Produces<ProblemDetails>(403)
        .Produces<ProblemDetails>(404).Produces<ProblemDetails>(409);
}
