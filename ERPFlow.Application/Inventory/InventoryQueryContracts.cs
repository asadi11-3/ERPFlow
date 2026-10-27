using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Inventory;

namespace ERPFlow.Application.Inventory;

public sealed record InventoryStockResult(
    Guid Id,
    Guid WarehouseId,
    string WarehouseCode,
    Guid ProductId,
    string ProductSku,
    string ProductName,
    int StockOnHand,
    int ReservedQuantity,
    int AvailableStock,
    long Sequence,
    byte[] RowVersion);

public sealed record InventoryMovementResult(
    Guid Id,
    Guid PostingId,
    Guid WarehouseStockId,
    Guid WarehouseId,
    Guid ProductId,
    long Sequence,
    InventoryPostingKind Kind,
    int PhysicalDelta,
    int ReservedDelta,
    int BeforeOnHand,
    int BeforeReserved,
    int AfterOnHand,
    int AfterReserved,
    Guid ActorId,
    DateTime CreatedAt,
    string SourceType,
    Guid? SourceDocumentId,
    string? SourceNumber,
    Guid? SourceItemId);

public sealed record StockQuery(int Page = 1, int PageSize = 20, Guid? WarehouseId = null, Guid? ProductId = null);

public sealed record MovementQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? WarehouseId = null,
    Guid? ProductId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

public interface IInventoryQueryWorkflow
{
    Task<PageResult<InventoryStockResult>> StocksAsync(StockQuery query, CancellationToken ct = default);
    Task<PageResult<InventoryMovementResult>> MovementsAsync(MovementQuery query, CancellationToken ct = default);
}
