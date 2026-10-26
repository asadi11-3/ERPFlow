namespace ERPFlow.Application.Inventory;

public interface IInventoryCutover
{
    Task<InventoryCutoverResult> RehearseAsync(InventoryCutoverRequest request, CancellationToken cancellationToken = default);
}

public interface IInventoryCutoverPermission
{
    void EnsureOperatorExecutionAllowed();
}

public sealed record InventoryCutoverRequest(Guid PreparationId, string ExpectedFingerprint,
    Guid DefaultWarehouseId, string PhysicalAttributionApproval, int ApplicationWriterVersion);
public sealed record InventoryCutoverResult(Guid PreparationId, int StockRows, int ReservationRows, DateTime CompletedAt);

public interface IInventoryReconciliation
{
    Task ReconcileAsync(CancellationToken cancellationToken = default);
}
