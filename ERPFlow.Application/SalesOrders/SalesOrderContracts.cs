using ERPFlow.Domain.Enums;

namespace ERPFlow.Application.SalesOrders;

public sealed record CreateSalesOrderItem(Guid ProductId, int Quantity);

public sealed record CreateSalesOrderCommand(
    Guid CustomerId,
    IReadOnlyCollection<CreateSalesOrderItem> Items);

public sealed record SalesOrderItemResult(
    Guid Id,
    Guid ProductId,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record StockReservationResult(
    Guid Id,
    Guid ProductId,
    int Quantity,
    bool IsReleased,
    DateTime CreatedAt,
    DateTime? ReleasedAt);

public sealed record SalesOrderResult(
    Guid Id,
    Guid CustomerId,
    Guid CreatedByUserId,
    OrderStatus Status,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? DecidedAt,
    decimal TotalAmount,
    IReadOnlyList<SalesOrderItemResult> Items,
    IReadOnlyList<StockReservationResult> Reservations);

public interface ISalesOrderWorkflow
{
    Task<SalesOrderResult> CreateDraftAsync(
        CreateSalesOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<SalesOrderResult> SubmitAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<SalesOrderResult> ApproveAsync(
        Guid orderId,
        string? comments = null,
        CancellationToken cancellationToken = default);

    Task<SalesOrderResult> RejectAsync(
        Guid orderId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<SalesOrderResult> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesOrderResult>> ListPendingApprovalsAsync(
        CancellationToken cancellationToken = default);
}
