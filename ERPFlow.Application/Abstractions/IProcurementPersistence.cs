using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;

namespace ERPFlow.Application.Abstractions;

public interface IProcurementPersistence
{
    Task<IApplicationTransaction> BeginAsync(bool exclusiveWarehouseChange = false, CancellationToken ct = default);
    Task<AppUser?> FindActorAsync(Guid id, CancellationToken ct);
    Task<PageResult<Warehouse>> ListWarehousesAsync(int page, int size, bool? active, CancellationToken ct);
    Task<Warehouse?> FindWarehouseAsync(Guid id, bool lockForUse, CancellationToken ct);
    Task<bool> HasWarehouseCommitmentsAsync(Guid id, CancellationToken ct);
    Task<PageResult<Supplier>> ListSuppliersAsync(int page, int size, string? search, bool? active, CancellationToken ct);
    Task<Supplier?> FindSupplierAsync(Guid id, bool lockForUse, CancellationToken ct);
    Task<Guid?> FindMissingProductAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct, bool requireActive = false);
    Task<PageResult<PurchaseOrder>> ListOrdersAsync(Guid? owner, int page, int size, PurchaseOrderStatus? status, bool receivingOnly, CancellationToken ct);
    Task<PurchaseOrder?> FindOrderAsync(Guid id, bool tracking, CancellationToken ct);
    Task<IReadOnlyList<PurchaseOrderHistory>> ListHistoryAsync(Guid id, CancellationToken ct);
    void AddWarehouse(Warehouse warehouse);
    void TouchWarehouse(Warehouse warehouse);
    void AddSupplier(Supplier supplier);
    void TouchSupplier(Supplier supplier);
    void AddOrder(PurchaseOrder order);
    void RemoveDraftItems(IReadOnlyCollection<PurchaseOrderItem> items);
    Task TransitionAsync(PurchaseOrder order, PurchaseOrderHistory history, byte[] expectedVersion, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}
