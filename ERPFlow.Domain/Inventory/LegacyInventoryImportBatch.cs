namespace ERPFlow.Domain.Inventory;

public sealed class LegacyInventoryImportBatch
{
    private LegacyInventoryImportBatch() { }

    public LegacyInventoryImportBatch(Guid id, Guid actorId, byte[] fingerprint)
    {
        if (id == Guid.Empty || actorId == Guid.Empty || fingerprint is not { Length: 32 })
            throw new ArgumentException("A preparation ID, actor and SHA-256 fingerprint are required.");
        Id = id;
        PreparedByUserId = actorId;
        SourceFingerprint = fingerprint.ToArray();
        DefaultWarehouseId = LegacyDefaultWarehouse.Id;
        FormatVersion = 1;
        PreparedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid PreparedByUserId { get; private set; }
    public Guid DefaultWarehouseId { get; private set; }
    public byte[] SourceFingerprint { get; private set; } = [];
    public int FormatVersion { get; private set; }
    public DateTime PreparedAt { get; private set; }
}
