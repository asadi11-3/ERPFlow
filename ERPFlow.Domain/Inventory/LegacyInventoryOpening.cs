namespace ERPFlow.Domain.Inventory;

public sealed class LegacyInventoryOpening
{
    private LegacyInventoryOpening() { }

    public LegacyInventoryOpening(Guid batchId, Guid productId, Guid targetStockId, int onHand, int reserved, byte[] version)
    {
        if (batchId == Guid.Empty || productId == Guid.Empty || targetStockId == Guid.Empty ||
            reserved < 0 || onHand < reserved || version is not { Length: 8 })
            throw new ArgumentException("The prepared opening must contain valid identities and conserved balances.");
        BatchId = batchId;
        ProductId = productId;
        TargetWarehouseStockId = targetStockId;
        WarehouseId = LegacyDefaultWarehouse.Id;
        StockOnHand = onHand;
        ReservedQuantity = reserved;
        SourceProductRowVersion = version.ToArray();
    }

    public Guid BatchId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid TargetWarehouseStockId { get; private set; }
    public int StockOnHand { get; private set; }
    public int ReservedQuantity { get; private set; }
    public byte[] SourceProductRowVersion { get; private set; } = [];
}
