namespace ERPFlow.Domain.Inventory;

public sealed class InventoryAuthority
{
    public const int SupportedWriterVersion = 2;
    private InventoryAuthority() { }

    public int Id { get; private set; } = 1;
    public InventoryAuthorityMode Mode { get; private set; }
    public int MinimumWriterVersion { get; private set; } = 1;
    public Guid? PreparationId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public Guid? DefaultWarehouseId { get; private set; }
    public string? PhysicalAttributionApproval { get; private set; }
    public byte[]? SourceFingerprint { get; private set; }
    public byte[]? CommandHash { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public int ImportedStocks { get; private set; }
    public int ImportedReservations { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Complete(Guid preparationId, Guid actorId, string approval, byte[] fingerprint,
        byte[] commandHash, int stocks, int reservations)
    {
        if (Mode != InventoryAuthorityMode.Product || preparationId == Guid.Empty || actorId == Guid.Empty ||
            string.IsNullOrWhiteSpace(approval) || approval.Length > 200 || fingerprint.Length != 32 || commandHash.Length != 32)
            throw new InvalidOperationException("A valid explicit approval and unchanged legacy authority are required.");
        Mode = InventoryAuthorityMode.Warehouse;
        MinimumWriterVersion = SupportedWriterVersion;
        PreparationId = preparationId;
        ApprovedByUserId = actorId;
        DefaultWarehouseId = LegacyDefaultWarehouse.Id;
        PhysicalAttributionApproval = approval;
        SourceFingerprint = fingerprint.ToArray();
        CommandHash = commandHash.ToArray();
        CompletedAt = BusinessClock.UtcNow;
        ImportedStocks = stocks;
        ImportedReservations = reservations;
    }
}

public enum InventoryAuthorityMode { Product = 0, Warehouse = 1 }
