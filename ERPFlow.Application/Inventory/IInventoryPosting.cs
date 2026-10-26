using ERPFlow.Domain.Inventory;

namespace ERPFlow.Application.Inventory;

public interface IInventoryPosting
{
    Task<InventoryPostingResult> PostAsync(InventoryPostingRequest request, CancellationToken cancellationToken = default);
    Task<InventoryPostingResult> PostReceiptAsync(InventoryReceiptRequest request, CancellationToken cancellationToken = default);
}

public sealed record InventoryReceiptRequest(Guid GoodsReceiptItemId, string IdempotencyKey);

public interface IInventoryWriteGate
{
    void EnsureWarehouseWritesAllowed();
}

public sealed record InventoryPostingRequest(
    string IdempotencyKey,
    InventoryPostingKind Kind,
    Guid WarehouseId,
    Guid ProductId,
    int Quantity,
    byte[] ExpectedRowVersion,
    Guid? SalesOrderId = null,
    Guid? JobcardItemId = null,
    Guid? ReservationId = null,
    Guid? NewReservationId = null);

public sealed record InventoryPostingResult(
    Guid PostingId,
    Guid MovementId,
    Guid? ReservationId,
    int StockOnHand,
    int ReservedQuantity,
    byte[] RowVersion);
