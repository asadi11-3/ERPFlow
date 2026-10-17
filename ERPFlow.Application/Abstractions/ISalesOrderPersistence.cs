using ERPFlow.Domain.Entities;

namespace ERPFlow.Application.Abstractions;

public interface ISalesOrderPersistence
{
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task<Customer?> FindCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<bool> AppUserExistsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Product>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default);

    Task<SalesOrder?> GetOrderAsync(
        Guid orderId,
        bool tracking,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesOrder>> ListPendingOrdersAsync(CancellationToken cancellationToken = default);

    void AddOrder(SalesOrder order);

    void AddApprovalHistory(ApprovalHistory history);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
