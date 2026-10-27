using ERPFlow.Domain.Procurement;

namespace ERPFlow.Application.Procurement;

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record VersionCommand(byte[] RowVersion);
public sealed record CreateWarehouseCommand(string Code, string Name);
public sealed record UpdateWarehouseCommand(string Name, byte[] RowVersion);
public sealed record WarehouseResult(Guid Id, string Code, string Name, bool IsActive, DateTime CreatedAt, byte[] RowVersion);
public sealed record CreateSupplierCommand(string SupplierCode, string Name, string? ContactName = null,
    string? Email = null, string? Phone = null, string? Address = null);
public sealed record UpdateSupplierCommand(string Name, string? ContactName, string? Email,
    string? Phone, string? Address, byte[] RowVersion);
public sealed record SupplierResult(Guid Id, string SupplierCode, string Name, string? ContactName,
    string? Email, string? Phone, string? Address, bool IsActive, DateTime CreatedAt, byte[] RowVersion);
public sealed record PurchaseOrderLineCommand(Guid ProductId, int Quantity, decimal UnitCost);
public sealed record CreatePurchaseOrderCommand(Guid SupplierId, Guid WarehouseId,
    IReadOnlyCollection<PurchaseOrderLineCommand> Items, string? Notes = null);
public sealed record UpdatePurchaseOrderCommand(Guid SupplierId, Guid WarehouseId,
    IReadOnlyCollection<PurchaseOrderLineCommand> Items, string? Notes, byte[] RowVersion);
public sealed record PurchaseOrderDecisionCommand(byte[] RowVersion, string? Comments = null);
public sealed record PurchaseOrderReasonCommand(byte[] RowVersion, string Reason);
public sealed record PurchaseOrderItemResult(Guid Id, Guid ProductId, int Quantity, decimal UnitCost, decimal LineTotal,
    int ReceivedQuantity, int RemainingQuantity);
public sealed record PurchaseOrderResult(Guid Id, string PONumber, Guid SupplierId, Guid WarehouseId,
    Guid CreatedByUserId, PurchaseOrderStatus Status, DateTime CreatedAt, DateTime? SubmittedAt,
    DateTime? DecidedAt, string? Notes, decimal TotalAmount, IReadOnlyList<PurchaseOrderItemResult> Items, byte[] RowVersion);
public sealed record PurchaseOrderOperationalItem(Guid ProductId, int Quantity, Guid PurchaseOrderItemId, int ReceivedQuantity, int RemainingQuantity);
public sealed record PurchaseOrderOperationalResult(Guid Id, string PONumber, Guid SupplierId, Guid WarehouseId,
    PurchaseOrderStatus Status, DateTime CreatedAt, IReadOnlyList<PurchaseOrderOperationalItem> Items, byte[] RowVersion);
public sealed record PurchaseOrderHistoryResult(Guid Id, Guid ActorId, PurchaseOrderAction Action,
    PurchaseOrderStatus FromStatus, PurchaseOrderStatus ToStatus, string? Comments, int Revision, DateTime CreatedAt);

public interface IProcurementMasters
{
    Task<PageResult<WarehouseResult>> ListWarehousesAsync(int page, int pageSize, bool? active, CancellationToken ct = default);
    Task<WarehouseResult> GetWarehouseAsync(Guid id, CancellationToken ct = default);
    Task<WarehouseResult> CreateWarehouseAsync(CreateWarehouseCommand command, CancellationToken ct = default);
    Task<WarehouseResult> UpdateWarehouseAsync(Guid id, UpdateWarehouseCommand command, CancellationToken ct = default);
    Task<WarehouseResult> SetWarehouseActiveAsync(Guid id, VersionCommand command, bool active, CancellationToken ct = default);
    Task<PageResult<SupplierResult>> ListSuppliersAsync(int page, int pageSize, string? search, bool? active, CancellationToken ct = default);
    Task<SupplierResult> GetSupplierAsync(Guid id, CancellationToken ct = default);
    Task<SupplierResult> CreateSupplierAsync(CreateSupplierCommand command, CancellationToken ct = default);
    Task<SupplierResult> UpdateSupplierAsync(Guid id, UpdateSupplierCommand command, CancellationToken ct = default);
    Task<SupplierResult> SetSupplierActiveAsync(Guid id, VersionCommand command, bool active, CancellationToken ct = default);
}

public interface IPurchaseOrderWorkflow
{
    Task<PageResult<PurchaseOrderResult>> ListAsync(int page, int pageSize, PurchaseOrderStatus? status, CancellationToken ct = default);
    Task<PurchaseOrderResult> GetAsync(Guid id, CancellationToken ct = default);
    Task<PageResult<PurchaseOrderOperationalResult>> ListOperationalAsync(int page, int pageSize, PurchaseOrderStatus? status, CancellationToken ct = default);
    Task<PurchaseOrderOperationalResult> GetOperationalAsync(Guid id, CancellationToken ct = default);
    Task<PurchaseOrderResult> CreateAsync(CreatePurchaseOrderCommand command, CancellationToken ct = default);
    Task<PurchaseOrderResult> UpdateAsync(Guid id, UpdatePurchaseOrderCommand command, CancellationToken ct = default);
    Task<PurchaseOrderResult> SubmitAsync(Guid id, VersionCommand command, CancellationToken ct = default);
    Task<PurchaseOrderResult> ApproveAsync(Guid id, PurchaseOrderDecisionCommand command, CancellationToken ct = default);
    Task<PurchaseOrderResult> RejectAsync(Guid id, PurchaseOrderReasonCommand command, CancellationToken ct = default);
    Task<PurchaseOrderResult> CancelAsync(Guid id, PurchaseOrderReasonCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<PurchaseOrderHistoryResult>> HistoryAsync(Guid id, CancellationToken ct = default);
}
