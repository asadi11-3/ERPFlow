using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Application.SalesOrders;

namespace ERPFlow.Application.Abstractions;

public interface ISalesOrderPersistence
{
    Task<SalesOrderPage> ListOrdersAsync(
        Guid? ownerId,
        int page,
        int pageSize,
        OrderStatus? status,
        CancellationToken cancellationToken = default);

    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task<Customer?> FindCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<AppUser?> FindAppUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Product>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default);

    Task<SalesOrder?> GetOrderAsync(
        Guid orderId,
        bool tracking,
        CancellationToken cancellationToken = default);

    Task<Guid?> FindOrderOwnerIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesOrder>> ListPendingOrdersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalHistory>> ListApprovalHistoryAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    void AddOrder(SalesOrder order);

    void MarkOrderUpdated(SalesOrder order);

    void AddApprovalHistory(ApprovalHistory history);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
