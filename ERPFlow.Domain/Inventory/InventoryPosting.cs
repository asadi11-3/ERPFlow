namespace ERPFlow.Domain.Inventory;

public sealed class InventoryPosting
{
    private InventoryPosting() { }

    public InventoryPosting(Guid actorId, string idempotencyKey, byte[] requestHash,
        InventoryPostingKind kind, Guid? reservationId, Guid? goodsReceiptItemId = null, Guid? stockTransferItemId = null)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("An actor is required.", nameof(actorId));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new ArgumentException("An idempotency key of at most 128 characters is required.", nameof(idempotencyKey));
        if (requestHash is not { Length: 32 }) throw new ArgumentException("A SHA-256 request hash is required.", nameof(requestHash));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Id = Guid.NewGuid();
        ActorId = actorId;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash.ToArray();
        Kind = kind;
        ReservationId = reservationId;
        GoodsReceiptItemId = goodsReceiptItemId;
        StockTransferItemId = stockTransferItemId;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public byte[] RequestHash { get; private set; } = [];
    public InventoryPostingKind Kind { get; private set; }
    public Guid? ReservationId { get; private set; }
    public Guid? GoodsReceiptItemId { get; private set; }
    public Guid? StockTransferItemId { get; private set; }
    public DateTime CreatedAt { get; private set; }
}
