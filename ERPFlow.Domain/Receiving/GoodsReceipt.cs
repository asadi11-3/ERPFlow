using System.Collections.ObjectModel;

namespace ERPFlow.Domain.Receiving;

public enum GoodsReceiptStatus { Draft = 0, Posted = 1, Cancelled = 2 }

public sealed class GoodsReceipt
{
    private readonly List<GoodsReceiptItem> _items = [];
    private readonly ReadOnlyCollection<GoodsReceiptItem> _view;
    private GoodsReceipt() { _view = _items.AsReadOnly(); }

    public GoodsReceipt(Guid purchaseOrderId, Guid supplierId, Guid warehouseId, Guid actorId,
        IReadOnlyCollection<(Guid PurchaseOrderItemId, Guid ProductId, int Quantity)> lines,
        string? supplierDeliveryReference = null, string? notes = null) : this()
    {
        if (purchaseOrderId == Guid.Empty || supplierId == Guid.Empty || warehouseId == Guid.Empty || actorId == Guid.Empty)
            throw new ArgumentException("Receipt order, supplier, warehouse and actor are required.");
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count is < 1 or > 100 || lines.Select(x => x.PurchaseOrderItemId).Distinct().Count() != lines.Count ||
            lines.Select(x => x.ProductId).Distinct().Count() != lines.Count)
            throw new ArgumentException("A receipt requires 1-100 distinct PO lines/products.");
        Id = Guid.NewGuid();
        var now = BusinessClock.UtcNow;
        Number = $"GRN-{now:yyyyMMdd}-{Id:N}";
        PurchaseOrderId = purchaseOrderId;
        SupplierId = supplierId;
        WarehouseId = warehouseId;
        CreatedByUserId = actorId;
        CreatedAt = now;
        SupplierDeliveryReference = Text(supplierDeliveryReference, 100)?.ToUpperInvariant();
        Notes = Text(notes, 1000);
        foreach (var line in lines) _items.Add(new GoodsReceiptItem(Id, purchaseOrderId, line.PurchaseOrderItemId, line.ProductId, line.Quantity));
    }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public Guid PurchaseOrderId { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string? SupplierDeliveryReference { get; private set; }
    public string? Notes { get; private set; }
    public GoodsReceiptStatus Status { get; private set; }
    public Guid? PostedByUserId { get; private set; }
    public DateTime? PostedAt { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyList<GoodsReceiptItem> Items => _view;

    public void EnsurePostable()
    {
        if (Status != GoodsReceiptStatus.Draft) throw new InvalidOperationException("Only a Draft GRN can be posted. Posted stock is immutable.");
    }
    public void Post(Guid actor)
    {
        EnsurePostable();
        if (actor == Guid.Empty) throw new ArgumentException("A posting actor is required.");
        PostedByUserId = actor;
        PostedAt = BusinessClock.UtcNow;
        Status = GoodsReceiptStatus.Posted;
    }
    public void Cancel(Guid actor, string reason)
    {
        EnsurePostable();
        if (actor == Guid.Empty) throw new ArgumentException("A cancellation actor is required.");
        CancellationReason = Text(reason, 1000) ?? throw new ArgumentException("A cancellation reason is required.");
        CancelledByUserId = actor;
        CancelledAt = BusinessClock.UtcNow;
        Status = GoodsReceiptStatus.Cancelled;
    }
    public static string? Text(string? value, int maximum)
    {
        if (value?.Length > maximum || value?.Any(char.IsControl) == true)
            throw new ArgumentException($"Receipt text must have at most {maximum} characters and no control characters.");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed class GoodsReceiptItem
{
    private GoodsReceiptItem() { }
    internal GoodsReceiptItem(Guid receiptId, Guid orderId, Guid orderItemId, Guid productId, int quantity)
    {
        if (orderItemId == Guid.Empty || productId == Guid.Empty || quantity <= 0)
            throw new ArgumentException("Receipt lines require an original PO line/product and positive quantity.");
        Id = Guid.NewGuid();
        GoodsReceiptId = receiptId;
        PurchaseOrderId = orderId;
        PurchaseOrderItemId = orderItemId;
        ProductId = productId;
        Quantity = quantity;
    }
    public Guid Id { get; private set; }
    public Guid GoodsReceiptId { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public Guid PurchaseOrderItemId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
}

public sealed class GoodsReceiptCommand
{
    private GoodsReceiptCommand() { }
    public GoodsReceiptCommand(Guid actor, string key, int operation, byte[] hash, Guid receiptId, string responseJson)
    {
        if (actor == Guid.Empty || receiptId == Guid.Empty || hash.Length != 32 || operation is < 0 or > 1 ||
            string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsWhiteSpace) || string.IsNullOrWhiteSpace(responseJson))
            throw new ArgumentException("Receipt idempotency command is invalid.");
        Id = Guid.NewGuid();
        ActorId = actor;
        IdempotencyKey = key;
        Operation = operation;
        RequestHash = hash.ToArray();
        GoodsReceiptId = receiptId;
        ResponseJson = responseJson;
        CreatedAt = BusinessClock.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int Operation { get; private set; }
    public byte[] RequestHash { get; private set; } = [];
    public Guid GoodsReceiptId { get; private set; }
    public string ResponseJson { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
}
