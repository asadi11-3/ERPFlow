using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;

namespace ERPFlow.Application.Receiving;

public sealed record GoodsReceiptLineCommand(Guid PurchaseOrderItemId, int Quantity);
public sealed record CreateGoodsReceiptCommand(string IdempotencyKey, Guid PurchaseOrderId, byte[] PurchaseOrderRowVersion,
    IReadOnlyCollection<GoodsReceiptLineCommand> Items, string? SupplierDeliveryReference = null, string? Notes = null);
public sealed record PostGoodsReceiptCommand(string IdempotencyKey, byte[] RowVersion);
public sealed record CancelGoodsReceiptCommand(byte[] RowVersion, string Reason);
public sealed record GoodsReceiptItemResult(Guid Id, Guid PurchaseOrderItemId, Guid ProductId, int Quantity,
    Guid? InventoryPostingId, Guid? StockMovementId);
public sealed record GoodsReceiptResult(Guid Id, string Number, Guid PurchaseOrderId, Guid SupplierId, Guid WarehouseId,
    GoodsReceiptStatus Status, Guid CreatedByUserId, DateTime CreatedAt, string? SupplierDeliveryReference, string? Notes,
    Guid? PostedByUserId, DateTime? PostedAt, Guid? CancelledByUserId, DateTime? CancelledAt, string? CancellationReason,
    byte[] RowVersion, PurchaseOrderStatus PurchaseOrderStatus, byte[] PurchaseOrderRowVersion, IReadOnlyList<GoodsReceiptItemResult> Items);

public interface IGoodsReceiptWorkflow
{
    Task<GoodsReceiptResult> CreateAsync(CreateGoodsReceiptCommand command, CancellationToken ct = default);
    Task<GoodsReceiptResult> PostAsync(Guid id, PostGoodsReceiptCommand command, CancellationToken ct = default);
    Task<GoodsReceiptResult> CancelAsync(Guid id, CancelGoodsReceiptCommand command, CancellationToken ct = default);
    Task<GoodsReceiptResult> GetAsync(Guid id, CancellationToken ct = default);
    Task<PageResult<GoodsReceiptResult>> ListAsync(int page, int pageSize, Guid? purchaseOrderId, GoodsReceiptStatus? status, CancellationToken ct = default);
}
