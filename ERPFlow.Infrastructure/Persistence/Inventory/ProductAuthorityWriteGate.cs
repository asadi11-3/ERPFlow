using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed class ProductAuthorityWriteGate : IInventoryWriteGate
{
    public void EnsureWarehouseWritesAllowed() => throw new WorkflowConflictException(
        "Product inventory remains authoritative. Warehouse inventory cutover has not been approved or activated.");
}
