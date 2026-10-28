using ERPFlow.Domain.Enums;

namespace ERPFlow.Domain.Entities;

public sealed class ApprovalHistory
{
    public ApprovalHistory(Guid salesOrderId, Guid userId, ApprovalAction action, string? comments = null)
    {
        if (salesOrderId == Guid.Empty)
        {
            throw new ArgumentException("A sales order ID is required.", nameof(salesOrderId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A user ID is required.", nameof(userId));
        }

        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        Id = Guid.NewGuid();
        SalesOrderId = salesOrderId;
        UserId = userId;
        Action = action;
        Comments = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim();
        CreatedAt = BusinessClock.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid SalesOrderId { get; private set; }

    public Guid UserId { get; private set; }

    public ApprovalAction Action { get; private set; }

    public string? Comments { get; private set; }

    public DateTime CreatedAt { get; private set; }
}
