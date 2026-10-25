namespace ERPFlow.Application.Inventory;

public interface ILegacyInventoryImportPreparation
{
    Task<InventoryImportPreflight> PreflightAsync(Guid? preparationId = null, CancellationToken cancellationToken = default);
    Task<InventoryImportPreparationResult> PrepareAsync(Guid preparationId, CancellationToken cancellationToken = default);
}

public sealed record InventoryImportIssue(string Code, Guid? SourceId, string Detail);
public sealed record InventoryImportProduct(Guid ProductId, int StockOnHand, int ReservedQuantity,
    long ActiveSalesQuantity, long ActiveServiceQuantity);
public sealed record InventoryImportPreflight(bool CanPrepare, string SourceFingerprint,
    IReadOnlyList<InventoryImportIssue> Issues, IReadOnlyList<InventoryImportProduct> Products);
public sealed record InventoryImportPreparationResult(Guid PreparationId, string SourceFingerprint,
    int ProductCount, int SalesReservationCount, int JobcardItemCount);
