namespace ERPFlow.Domain.Inventory;

public sealed class InventoryReservation
{
    private InventoryReservation() { }

    public InventoryReservation(WarehouseStock stock, int quantity, Guid? salesOrderId, Guid? jobcardItemId, Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(stock);
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (salesOrderId == Guid.Empty || jobcardItemId == Guid.Empty || salesOrderId.HasValue == jobcardItemId.HasValue)
            throw new ArgumentException("Exactly one valid Sales Order or Jobcard Item owner is required.");

        if (id == Guid.Empty) throw new ArgumentException("Reservation identity cannot be empty.", nameof(id));
        Id = id ?? Guid.NewGuid();
        WarehouseStockId = stock.Id;
        ProductId = stock.ProductId;
        Quantity = quantity;
        SalesOrderId = salesOrderId;
        JobcardItemId = jobcardItemId;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid WarehouseStockId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public Guid? SalesOrderId { get; private set; }
    public Guid? JobcardItemId { get; private set; }
    public InventoryReservationStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public bool IsLegacyImport { get; private set; }
    public DateTime? SourceCreatedAt { get; private set; }

    public static InventoryReservation Import(WarehouseStock stock, LegacyInventoryReservationImport source)
    {
        var reservation = new InventoryReservation(stock, source.Quantity, source.SalesOrderId,
            source.JobcardItemId, source.TargetReservationId)
        {
            Status = source.Disposition switch
            {
                LegacyReservationDisposition.Active => InventoryReservationStatus.Active,
                LegacyReservationDisposition.Released => InventoryReservationStatus.Released,
                LegacyReservationDisposition.Consumed => InventoryReservationStatus.Consumed,
                LegacyReservationDisposition.LegacyClosedUnknown => InventoryReservationStatus.LegacyClosedUnknown,
                _ => throw new InvalidOperationException("Unsupported imported disposition.")
            },
            IsLegacyImport = true,
            SourceCreatedAt = source.SourceCreatedAt,
            CreatedAt = source.SourceCreatedAt ?? DateTime.UtcNow,
            ClosedAt = source.SourceClosedAt
        };
        return reservation;
    }

    public void Close(InventoryPostingKind kind)
    {
        if (Status != InventoryReservationStatus.Active)
            throw new InvalidOperationException("The reservation is already closed.");
        Status = kind switch
        {
            InventoryPostingKind.Release => InventoryReservationStatus.Released,
            InventoryPostingKind.Consume => InventoryReservationStatus.Consumed,
            _ => throw new InvalidOperationException("Reservations can only be released or consumed.")
        };
        ClosedAt = DateTime.UtcNow;
    }
}

public enum InventoryReservationStatus
{
    Active = 0,
    Released = 1,
    Consumed = 2,
    LegacyClosedUnknown = 3
}
