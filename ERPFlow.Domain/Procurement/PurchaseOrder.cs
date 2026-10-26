using System.Collections.ObjectModel;

namespace ERPFlow.Domain.Procurement;

public enum PurchaseOrderStatus
{
    Draft = 0, PendingApproval = 1, Approved = 2, Rejected = 3, Cancelled = 4,
    PartiallyReceived = 5, Received = 6, ShortClosed = 7
}

public enum PurchaseOrderAction { Submitted = 0, Approved = 1, Rejected = 2, Cancelled = 3, ReceivingStarted = 4, ReceiptPosted = 5 }

public sealed class PurchaseOrder
{
    public const int MaxLines = 100;
    private readonly List<PurchaseOrderItem> _items = [];
    private readonly ReadOnlyCollection<PurchaseOrderItem> _view;

    private PurchaseOrder() { _view = _items.AsReadOnly(); }

    public PurchaseOrder(Guid supplierId, Guid warehouseId, Guid creatorId,
        IReadOnlyCollection<(Guid ProductId, int Quantity, decimal UnitCost)> items, string? notes = null) : this()
    {
        if (creatorId == Guid.Empty) throw new ArgumentException("PO creator is required.");
        Id = Guid.NewGuid();
        PONumber = $"PO-{DateTime.UtcNow:yyyyMMdd}-{Id:N}";
        CreatedByUserId = creatorId;
        CreatedAt = DateTime.UtcNow;
        Edit(supplierId, warehouseId, items, notes);
    }

    public Guid Id { get; private set; }
    public string PONumber { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? Notes { get; private set; }
    public decimal TotalAmount { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public int Revision { get; private set; }
    public DateTime? ReceivingStartedAt { get; private set; }
    public IReadOnlyList<PurchaseOrderItem> Items => _view;

    public PurchaseOrderStatus ValidateReceipt(IReadOnlyCollection<(Guid PurchaseOrderItemId, int Quantity)> lines)
    {
        if (Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived))
            throw new InvalidOperationException("Only Approved or PartiallyReceived POs accept goods receipts.");
        if (lines.Count is < 1 or > MaxLines || lines.Select(x => x.PurchaseOrderItemId).Distinct().Count() != lines.Count)
            throw new ArgumentException("A receipt needs distinct original PO lines.");
        foreach (var line in lines)
        {
            var item = _items.SingleOrDefault(x => x.Id == line.PurchaseOrderItemId)
                ?? throw new ArgumentException("The receipt line does not belong to this PO.");
            if (line.Quantity <= 0 || (long)item.ReceivedQuantity + line.Quantity > item.Quantity)
                throw new InvalidOperationException("Receipt quantity exceeds the remaining ordered quantity.");
        }
        return _items.All(item => (long)item.ReceivedQuantity + lines.Where(x => x.PurchaseOrderItemId == item.Id).Sum(x => (long)x.Quantity) == item.Quantity)
            ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
    }

    public void Edit(Guid supplierId, Guid warehouseId,
        IReadOnlyCollection<(Guid ProductId, int Quantity, decimal UnitCost)> items, string? notes)
    {
        if (Status != PurchaseOrderStatus.Draft) throw new InvalidOperationException("Only Draft purchase orders can be edited.");
        if (supplierId == Guid.Empty || warehouseId == Guid.Empty) throw new ArgumentException("Supplier and warehouse are required.");
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is < 1 or > MaxLines || items.Select(x => x.ProductId).Distinct().Count() != items.Count)
            throw new ArgumentException($"A PO requires 1-{MaxLines} distinct product lines.");
        var normalizedNotes = ValidateText(notes, false);
        var replacements = items.Select(x => new PurchaseOrderItem(Id, x.ProductId, x.Quantity, x.UnitCost)).ToArray();
        var total = replacements.Aggregate(0m, (sum, x) => checked(sum + x.LineTotal));
        var retained = replacements.Select(x => x.ProductId).ToHashSet();
        _items.RemoveAll(x => !retained.Contains(x.ProductId));
        foreach (var item in replacements)
        {
            var existing = _items.SingleOrDefault(x => x.ProductId == item.ProductId);
            if (existing is null) _items.Add(item);
            else existing.Revise(item.Quantity, item.UnitCost);
        }
        (SupplierId, WarehouseId, Notes, TotalAmount) = (supplierId, warehouseId, normalizedNotes, total);
        Revision = checked(Revision + 1);
    }

    public PurchaseOrderHistory Submit(Guid actor)
    {
        if (Status != PurchaseOrderStatus.Draft) throw new InvalidOperationException("Only a Draft PO can be submitted.");
        var history = Change(actor, PurchaseOrderAction.Submitted, null, PurchaseOrderStatus.PendingApproval);
        SubmittedAt = history.CreatedAt;
        return history;
    }

    public PurchaseOrderHistory Approve(Guid actor, string? comments)
    {
        PendingDecision(actor);
        var history = Change(actor, PurchaseOrderAction.Approved, ValidateText(comments, false), PurchaseOrderStatus.Approved);
        DecidedAt = history.CreatedAt;
        return history;
    }

    public PurchaseOrderHistory Reject(Guid actor, string reason)
    {
        PendingDecision(actor);
        var history = Change(actor, PurchaseOrderAction.Rejected, ValidateText(reason, true), PurchaseOrderStatus.Rejected);
        DecidedAt = history.CreatedAt;
        return history;
    }

    public PurchaseOrderHistory Cancel(Guid actor, string reason)
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval or PurchaseOrderStatus.Approved) || ReceivingStartedAt.HasValue)
            throw new InvalidOperationException("Only an unreceived Draft, Pending or Approved PO can be cancelled.");
        return Change(actor, PurchaseOrderAction.Cancelled, ValidateText(reason, true), PurchaseOrderStatus.Cancelled);
    }

    private void PendingDecision(Guid actor)
    {
        if (Status != PurchaseOrderStatus.PendingApproval) throw new InvalidOperationException("Only a pending PO accepts a decision.");
        if (actor == CreatedByUserId) throw new InvalidOperationException("A creator cannot approve or reject their own PO.");
    }

    private PurchaseOrderHistory Change(Guid actor, PurchaseOrderAction action, string? comments, PurchaseOrderStatus target)
    {
        var history = new PurchaseOrderHistory(Id, actor, action, Status, target, comments, checked(Revision + 1));
        Status = target;
        Revision = history.Revision;
        return history;
    }

    public static string? ValidateText(string? text, bool required)
    {
        if ((required && string.IsNullOrWhiteSpace(text)) || text?.Length > 1000 || text?.Any(char.IsControl) == true)
            throw new ArgumentException("PO text must be at most 1000 characters; a reason is required for rejection/cancellation.");
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}

public sealed class PurchaseOrderItem
{
    public const decimal MaxUnitCost = 999999999999.9999m;
    private PurchaseOrderItem() { }
    internal PurchaseOrderItem(Guid orderId, Guid productId, int quantity, decimal unitCost)
    {
        if (orderId == Guid.Empty || productId == Guid.Empty) throw new ArgumentException("PO line ownership/product is required.");
        Id = Guid.NewGuid();
        PurchaseOrderId = orderId;
        ProductId = productId;
        Revise(quantity, unitCost);
    }
    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public int ReceivedQuantity { get; private set; }
    public int RemainingQuantity => checked(Quantity - ReceivedQuantity);
    public decimal LineTotal => checked(Quantity * UnitCost);
    internal void Revise(int quantity, decimal unitCost)
    {
        if (quantity <= 0 || unitCost < 0 || unitCost > MaxUnitCost || decimal.Round(unitCost, 4) != unitCost)
            throw new ArgumentException("PO quantities must be positive; unit cost must be nonnegative, bounded and have at most four decimal places.");
        (Quantity, UnitCost) = (quantity, unitCost);
    }
}

public sealed class PurchaseOrderHistory
{
    private PurchaseOrderHistory() { }
    internal PurchaseOrderHistory(Guid orderId, Guid actorId, PurchaseOrderAction action,
        PurchaseOrderStatus from, PurchaseOrderStatus to, string? comments, int revision)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("A persisted actor is required.");
        Id = Guid.NewGuid();
        PurchaseOrderId = orderId;
        ActorId = actorId;
        Action = action;
        FromStatus = from;
        ToStatus = to;
        Comments = comments;
        Revision = revision;
        CreatedAt = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public Guid ActorId { get; private set; }
    public PurchaseOrderAction Action { get; private set; }
    public PurchaseOrderStatus FromStatus { get; private set; }
    public PurchaseOrderStatus ToStatus { get; private set; }
    public string? Comments { get; private set; }
    public int Revision { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? GoodsReceiptId { get; private set; }
}
