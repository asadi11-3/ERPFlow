using ERPFlow.Application.Procurement;
using ERPFlow.Application.Receiving;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;

namespace ERPFlow.Application.Abstractions;

public interface IGoodsReceiptPersistence
{
    Task<IApplicationTransaction> BeginAsync(CancellationToken ct);
    bool WarehouseAuthority { get; }
    Task<AppUser?> FindActorAsync(Guid id, CancellationToken ct);
    Task<GoodsReceiptCommand?> LockCommandAsync(Guid actor, string key, CancellationToken ct);
    Task<PurchaseOrder?> LockOrderAsync(Guid id, CancellationToken ct);
    Task<GoodsReceipt?> FindReceiptAsync(Guid id, bool lockForUse, CancellationToken ct);
    Task EnsureSourcesAsync(PurchaseOrder order, CancellationToken ct);
    void AddReceipt(GoodsReceipt receipt);
    Task SaveAsync(CancellationToken ct);
    Task AdmitReceivingAsync(GoodsReceipt receipt, byte[] orderVersion, Guid actor, CancellationToken ct);
    Task EnterPostingScopeAsync(Guid receiptId, CancellationToken ct);
    Task LockStocksAsync(GoodsReceipt receipt, CancellationToken ct);
    Task FinalizePostingAsync(GoodsReceipt receipt, byte[] receiptVersion, Guid actor, CancellationToken ct);
    void AddCommand(GoodsReceiptCommand command);
    Task<GoodsReceiptResult> ResultAsync(Guid id, CancellationToken ct);
    Task<PageResult<GoodsReceiptResult>> ListAsync(int page, int size, Guid? orderId, GoodsReceiptStatus? status, CancellationToken ct);
}
