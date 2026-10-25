using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed class ProductAuthorityWarehouseSourcePolicy(ERPFlowDbContext context) : IWarehouseSourcePolicy
{
    public async Task<Guid> ResolveAsync(Guid? requestedWarehouseId, Guid? existingWarehouseId = null,
        CancellationToken cancellationToken = default)
    {
        if (requestedWarehouseId == Guid.Empty || existingWarehouseId == Guid.Empty)
            throw new WorkflowValidationException("Warehouse IDs cannot be empty.");
        var selected = requestedWarehouseId ?? existingWarehouseId ?? LegacyDefaultWarehouse.Id;
        if (selected != LegacyDefaultWarehouse.Id)
            throw new WorkflowConflictException("Non-default warehouse inventory requires the separately approved atomic cutover. No fallback is permitted.");
        if (!await context.Warehouses.AsNoTracking().AnyAsync(x => x.Id == selected && x.IsActive, cancellationToken))
            throw new WorkflowConflictException("The legacy default warehouse is unavailable. No fallback is permitted.");
        return selected;
    }
}
