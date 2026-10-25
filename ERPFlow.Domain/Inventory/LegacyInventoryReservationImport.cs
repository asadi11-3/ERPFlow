namespace ERPFlow.Domain.Inventory;

public sealed class LegacyInventoryReservationImport
{
    private LegacyInventoryReservationImport() { }

    public LegacyInventoryReservationImport(Guid batchId, LegacyReservationSource sourceKind, Guid sourceId,
        Guid ownerId, Guid productId, Guid targetReservationId, int quantity,
        LegacyReservationDisposition disposition, bool inferredWarehouse, bool historical,
        DateTime? sourceCreatedAt, DateTime? sourceClosedAt)
    {
        if (batchId == Guid.Empty || sourceId == Guid.Empty || ownerId == Guid.Empty || productId == Guid.Empty ||
            targetReservationId == Guid.Empty || quantity <= 0 || !Enum.IsDefined(sourceKind) || !Enum.IsDefined(disposition))
            throw new ArgumentException("Prepared reservation ownership is invalid.");
        BatchId = batchId;
        SourceKind = sourceKind;
        SourceId = sourceId;
        SalesOrderId = sourceKind == LegacyReservationSource.SalesReservation ? ownerId : null;
        JobcardItemId = sourceKind == LegacyReservationSource.JobcardItem ? ownerId : null;
        ProductId = productId;
        TargetReservationId = targetReservationId;
        WarehouseId = LegacyDefaultWarehouse.Id;
        Quantity = quantity;
        Disposition = disposition;
        WarehouseInferred = inferredWarehouse;
        IsHistorical = historical;
        SourceCreatedAt = sourceCreatedAt;
        SourceClosedAt = sourceClosedAt;
    }

    public Guid BatchId { get; private set; }
    public LegacyReservationSource SourceKind { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid? SalesOrderId { get; private set; }
    public Guid? JobcardItemId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid TargetReservationId { get; private set; }
    public int Quantity { get; private set; }
    public LegacyReservationDisposition Disposition { get; private set; }
    public bool WarehouseInferred { get; private set; }
    public bool IsHistorical { get; private set; }
    public DateTime? SourceCreatedAt { get; private set; }
    public DateTime? SourceClosedAt { get; private set; }
}

public enum LegacyReservationSource { SalesReservation = 0, JobcardItem = 1 }
public enum LegacyReservationDisposition { Active = 0, Released = 1, Consumed = 2, LegacyClosedUnknown = 3 }
