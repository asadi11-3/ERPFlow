namespace ERPFlow.Application.Inventory;

public interface IWarehouseSourcePolicy
{
    Task<Guid> ResolveAsync(Guid? requestedWarehouseId, Guid? existingWarehouseId = null,
        CancellationToken cancellationToken = default);
}
