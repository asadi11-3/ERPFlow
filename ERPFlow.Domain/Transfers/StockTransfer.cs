using System.Collections.ObjectModel;

namespace ERPFlow.Domain.Transfers;

public enum StockTransferStatus { Draft = 0, Posted = 1, Cancelled = 2 }

public sealed class StockTransfer
{
    private readonly List<StockTransferItem> _items = [];
    private readonly ReadOnlyCollection<StockTransferItem> _view;

    private StockTransfer() { _view = _items.AsReadOnly(); }

    public StockTransfer(Guid sourceWarehouseId, Guid destinationWarehouseId, Guid actor,
        IReadOnlyCollection<(Guid ProductId, int Quantity)> items) : this()
    {
        if (sourceWarehouseId == Guid.Empty || destinationWarehouseId == Guid.Empty || actor == Guid.Empty)
            throw new ArgumentException("Transfer warehouses and actor are required.");
        if (sourceWarehouseId == destinationWarehouseId)
            throw new ArgumentException("Transfer source and destination warehouses must differ.");
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is < 1 or > 100 || items.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0) ||
            items.Select(x => x.ProductId).Distinct().Count() != items.Count)
            throw new ArgumentException("A transfer requires 1-100 distinct products with positive quantities.");

        Id = Guid.NewGuid();
        var now = BusinessClock.UtcNow;
        Number = $"TRF-{now:yyyyMMdd}-{Id:N}";
        SourceWarehouseId = sourceWarehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        CreatedByUserId = actor;
        CreatedAt = now;
        foreach (var item in items) _items.Add(new StockTransferItem(Id, item.ProductId, item.Quantity));
    }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public Guid SourceWarehouseId { get; private set; }
    public Guid DestinationWarehouseId { get; private set; }
    public StockTransferStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? PostedByUserId { get; private set; }
    public DateTime? PostedAt { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyList<StockTransferItem> Items => _view;

    public void EnsurePostable()
    {
        if (Status != StockTransferStatus.Draft)
            throw new InvalidOperationException("Only a Draft stock transfer can be posted.");
    }

    public void Post(Guid actor)
    {
        EnsurePostable();
        if (actor == Guid.Empty) throw new ArgumentException("A posting actor is required.");
        PostedByUserId = actor;
        PostedAt = BusinessClock.UtcNow;
        Status = StockTransferStatus.Posted;
    }

    public void Cancel(Guid actor, string reason)
    {
        EnsurePostable();
        if (actor == Guid.Empty) throw new ArgumentException("A cancellation actor is required.");
        CancellationReason = Text(reason, 1000) ?? throw new ArgumentException("A cancellation reason is required.");
        CancelledByUserId = actor;
        CancelledAt = BusinessClock.UtcNow;
        Status = StockTransferStatus.Cancelled;
    }

    private static string? Text(string? value, int maximum)
    {
        if (value?.Length > maximum || value?.Any(char.IsControl) == true)
            throw new ArgumentException($"Transfer text must have at most {maximum} characters and no control characters.");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed class StockTransferItem
{
    private StockTransferItem() { }

    internal StockTransferItem(Guid stockTransferId, Guid productId, int quantity)
    {
        if (stockTransferId == Guid.Empty || productId == Guid.Empty || quantity <= 0)
            throw new ArgumentException("Transfer lines require ownership, a product and positive quantity.");
        Id = Guid.NewGuid();
        StockTransferId = stockTransferId;
        ProductId = productId;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid StockTransferId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
}

public sealed class StockTransferCommand
{
    private StockTransferCommand() { }

    public StockTransferCommand(Guid actor, string key, int operation, byte[] hash, Guid stockTransferId, string responseJson)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (actor == Guid.Empty || stockTransferId == Guid.Empty || hash.Length != 32 || operation is < 0 or > 1 ||
            string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(x => char.IsWhiteSpace(x) || char.IsControl(x)) ||
            string.IsNullOrWhiteSpace(responseJson))
            throw new ArgumentException("Transfer idempotency command is invalid.");
        Id = Guid.NewGuid();
        ActorId = actor;
        IdempotencyKey = key;
        Operation = operation;
        RequestHash = hash.ToArray();
        StockTransferId = stockTransferId;
        ResponseJson = responseJson;
        CreatedAt = BusinessClock.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int Operation { get; private set; }
    public byte[] RequestHash { get; private set; } = [];
    public Guid StockTransferId { get; private set; }
    public string ResponseJson { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
}
