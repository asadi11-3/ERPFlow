using ERPFlow.Application.Inventory;
using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Entities;

namespace ERPFlow.Application.Abstractions;

public interface IInventoryQueryPersistence
{
    Task<IApplicationTransaction> BeginReadAsync(CancellationToken ct = default);
    bool WarehouseAuthority { get; }
    Task<AppUser?> FindActorAsync(Guid id, CancellationToken ct = default);
    Task<PageResult<InventoryStockResult>> StocksAsync(StockQuery query, CancellationToken ct = default);
    Task<PageResult<InventoryMovementResult>> MovementsAsync(MovementQuery query, CancellationToken ct = default);
}
