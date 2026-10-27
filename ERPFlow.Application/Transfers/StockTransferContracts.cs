using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Transfers;

namespace ERPFlow.Application.Transfers;

public sealed record StockTransferLineCommand(Guid ProductId, int Quantity);
public sealed record CreateStockTransferCommand(Guid SourceWarehouseId, Guid DestinationWarehouseId,
    IReadOnlyCollection<StockTransferLineCommand> Items);
public sealed record PostStockTransferCommand(byte[] RowVersion);
public sealed record CancelStockTransferCommand(byte[] RowVersion, string Reason);
public sealed record StockTransferItemResult(Guid Id, Guid ProductId, int Quantity, Guid? SourcePostingId,
    Guid? SourceMovementId, Guid? DestinationPostingId, Guid? DestinationMovementId);
public sealed record StockTransferResult(Guid Id, string Number, Guid SourceWarehouseId, Guid DestinationWarehouseId,
    StockTransferStatus Status, Guid CreatedByUserId, DateTime CreatedAt, Guid? PostedByUserId, DateTime? PostedAt,
    Guid? CancelledByUserId, DateTime? CancelledAt, string? CancellationReason, byte[] RowVersion,
    IReadOnlyList<StockTransferItemResult> Items);

public interface IStockTransferWorkflow
{
    Task<StockTransferResult> CreateAsync(string key, CreateStockTransferCommand command, CancellationToken ct = default);
    Task<StockTransferResult> PostAsync(Guid id, string key, PostStockTransferCommand command, CancellationToken ct = default);
    Task<StockTransferResult> CancelAsync(Guid id, CancelStockTransferCommand command, CancellationToken ct = default);
    Task<StockTransferResult> GetAsync(Guid id, CancellationToken ct = default);
    Task<PageResult<StockTransferResult>> ListAsync(int page, int pageSize, Guid? warehouseId, Guid? productId,
        StockTransferStatus? status, CancellationToken ct = default);
}
