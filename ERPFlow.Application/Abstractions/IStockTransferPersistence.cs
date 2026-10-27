using ERPFlow.Application.Procurement;
using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Transfers;

namespace ERPFlow.Application.Abstractions;

public interface IStockTransferPersistence
{
    Task<IApplicationTransaction> BeginAsync(CancellationToken ct);
    bool WarehouseAuthority { get; }
    Task<AppUser?> FindActorAsync(Guid id, CancellationToken ct);
    Task<StockTransferCommand?> LockCommandAsync(Guid actor, string key, CancellationToken ct);
    Task<StockTransfer?> FindAsync(Guid id, bool lockForUse, CancellationToken ct);
    Task EnsureWarehousesAsync(Guid source, Guid dest, IReadOnlyCollection<Guid> products, CancellationToken ct);
    void AddTransfer(StockTransfer transfer);
    Task EnterScopeAsync(Guid id, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
    void AddCommand(StockTransferCommand command);
    Task<StockTransferResult> ResultAsync(Guid id, CancellationToken ct);
    Task<PageResult<StockTransferResult>> ListAsync(int page, int size, Guid? warehouseId, Guid? productId,
        StockTransferStatus? status, CancellationToken ct);
}
