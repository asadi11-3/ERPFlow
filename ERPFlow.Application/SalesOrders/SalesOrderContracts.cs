using ERPFlow.Domain.Enums;

namespace ERPFlow.Application.SalesOrders;

public sealed record CreateSalesOrderItem(Guid ProductId, int Quantity, Guid? WarehouseId = null);

public sealed record SalesOrderPage(
    IReadOnlyList<SalesOrderResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record CreateSalesOrderCommand(
    Guid CustomerId,
    IReadOnlyCollection<CreateSalesOrderItem> Items);

public sealed record UpdateSalesOrderCommand(
    Guid CustomerId,
    IReadOnlyCollection<CreateSalesOrderItem> Items,
    byte[] RowVersion);

public sealed record SalesOrderItemResult(
    Guid Id,
    Guid ProductId,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    Guid? WarehouseId);

public sealed record StockReservationResult(
    Guid Id,
    Guid ProductId,
    int Quantity,
    bool IsReleased,
    DateTime CreatedAt,
    DateTime? ReleasedAt,
    Guid? WarehouseId);

public sealed record ApprovalHistoryResult(
    ApprovalAction Action,
    Guid ActorId,
    DateTime TimestampUtc,
    string? Remarks);

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
    IReadOnlyList<StockReservationResult> Reservations)
{
    public byte[] RowVersion { get; init; } = [];
}

public interface ISalesOrderWorkflow
{
    Task<SalesOrderPage> ListAsync(
        int page,
        int pageSize,
        OrderStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<SalesOrderResult> CreateDraftAsync(
        CreateSalesOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<SalesOrderResult> SubmitAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<SalesOrderResult> UpdateDraftAsync(
        Guid orderId,
        UpdateSalesOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<SalesOrderResult> ApproveAsync(
        Guid orderId,
        string? comments = null,
        CancellationToken cancellationToken = default);

    Task<SalesOrderResult> RejectAsync(
        Guid orderId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<SalesOrderResult> ReturnAsync(
        Guid orderId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<SalesOrderResult> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalHistoryResult>> GetApprovalHistoryAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesOrderResult>> ListPendingApprovalsAsync(
        CancellationToken cancellationToken = default);
}
