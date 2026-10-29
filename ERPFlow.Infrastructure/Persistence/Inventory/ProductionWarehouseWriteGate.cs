using ERPFlow.Application.Inventory;
using ERPFlow.Infrastructure.Deployment;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed class ProductionWarehouseWriteGate(ERPFlowDbContext context, ProductionInventoryAdmission admission)
    : IInventoryWriteGate
{
    public void EnsureWarehouseWritesAllowed() => admission.EnsureAllowed(context);
}
