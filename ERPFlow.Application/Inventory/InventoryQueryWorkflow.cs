using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Enums;

namespace ERPFlow.Application.Inventory;

public sealed class InventoryQueryWorkflow(
    IInventoryQueryPersistence persistence,
    ICurrentUser currentUser) : IInventoryQueryWorkflow
{
    private static readonly UserRole[] AllowedRoles =
    [
        UserRole.Administrator,
        UserRole.Approver,
        UserRole.Purchasing,
        UserRole.WarehouseOperator
    ];

    public async Task<PageResult<InventoryStockResult>> StocksAsync(StockQuery query, CancellationToken ct = default)
    {
        Validate(query.Page, query.PageSize, query.WarehouseId, query.ProductId);
        await AuthorizeAsync(ct);
        await using var transaction = await persistence.BeginReadAsync(ct);
        EnsureWarehouseAuthority();
        var result = await persistence.StocksAsync(query, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<PageResult<InventoryMovementResult>> MovementsAsync(MovementQuery query, CancellationToken ct = default)
    {
        Validate(query.Page, query.PageSize, query.WarehouseId, query.ProductId);
        if (query.From.HasValue && query.To.HasValue && query.From.Value >= query.To.Value)
            throw new WorkflowValidationException("From must be earlier than To.");
        await AuthorizeAsync(ct);
        await using var transaction = await persistence.BeginReadAsync(ct);
        EnsureWarehouseAuthority();
        var result = await persistence.MovementsAsync(query, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task AuthorizeAsync(CancellationToken ct)
    {
        var actor = await persistence.FindActorAsync(currentUser.UserId, ct);
        if (actor is null || !AllowedRoles.Contains(actor.Role))
            throw new WorkflowAuthorizationException("The persisted user is not authorized to read operational inventory.");
    }

    private void EnsureWarehouseAuthority()
    {
        if (!persistence.WarehouseAuthority)
            throw new WorkflowConflictException("Operational inventory is unavailable until warehouse authority is active.");
    }

    private static void Validate(int page, int pageSize, Guid? warehouseId, Guid? productId)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw new WorkflowValidationException("Page must be positive and pageSize must be between 1 and 100.");
        if (warehouseId == Guid.Empty || productId == Guid.Empty)
            throw new WorkflowValidationException("Warehouse and product filters must be nonempty IDs.");
    }
}
