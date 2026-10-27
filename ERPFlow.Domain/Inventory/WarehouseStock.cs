namespace ERPFlow.Domain.Inventory;

public sealed class WarehouseStock
{
    private WarehouseStock() { }

    public WarehouseStock(Guid warehouseId, Guid productId)
    {
        if (warehouseId == Guid.Empty || productId == Guid.Empty)
            throw new ArgumentException("Warehouse and product IDs are required.");
        Id = Guid.NewGuid();
        WarehouseId = warehouseId;
        ProductId = productId;
    }

    public Guid Id { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ProductId { get; private set; }
    public int StockOnHand { get; private set; }
    public int ReservedQuantity { get; private set; }
    public int AvailableStock => StockOnHand - ReservedQuantity;
    public long Sequence { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public StockEffect Apply(InventoryPostingKind kind, int quantity)
    {
        if (quantity < 0 || (quantity == 0 && kind != InventoryPostingKind.Opening))
            throw new ArgumentOutOfRangeException(nameof(quantity));
        var (physical, reserved) = kind switch
        {
            InventoryPostingKind.Opening when Sequence == 0 && StockOnHand == 0 && ReservedQuantity == 0 => (quantity, 0),
            InventoryPostingKind.Reserve => (0, quantity),
            InventoryPostingKind.Release => (0, -quantity),
            InventoryPostingKind.Consume => (-quantity, -quantity),
            InventoryPostingKind.Receipt => (quantity, 0),
            InventoryPostingKind.TransferOut => (-quantity, 0),
            InventoryPostingKind.TransferIn => (quantity, 0),
            _ => throw new InvalidOperationException("Invalid posting kind or duplicate opening.")
        };
        if (Sequence == 0 && kind is not (InventoryPostingKind.Opening or InventoryPostingKind.Receipt or InventoryPostingKind.TransferIn))
            throw new InvalidOperationException("An opening is required before reservation operations.");

        var afterOnHand = checked(StockOnHand + physical);
        var afterReserved = checked(ReservedQuantity + reserved);
        if (afterOnHand < 0 || afterReserved < 0 || afterReserved > afterOnHand)
            throw new InvalidOperationException("Insufficient physical or available stock.");

        var effect = new StockEffect(StockOnHand, ReservedQuantity, afterOnHand, afterReserved, checked(Sequence + 1));
        StockOnHand = afterOnHand;
        ReservedQuantity = afterReserved;
        Sequence = effect.Sequence;
        return effect;
    }

    public StockEffect ImportOpening(int onHand, int reserved)
    {
        if (Sequence != 0 || StockOnHand != 0 || ReservedQuantity != 0 || reserved < 0 || onHand < reserved)
            throw new InvalidOperationException("Only an unused zero stock row can receive a conserved legacy opening.");
        StockOnHand = onHand;
        ReservedQuantity = reserved;
        Sequence = 1;
        return new StockEffect(0, 0, onHand, reserved, 1);
    }

    public static WarehouseStock ImportTarget(Guid id, Guid warehouseId, Guid productId)
    {
        if (id == Guid.Empty) throw new ArgumentException("An original target ID is required.", nameof(id));
        return new WarehouseStock(warehouseId, productId) { Id = id };
    }
}

public sealed record StockEffect(int BeforeOnHand, int BeforeReserved, int AfterOnHand, int AfterReserved, long Sequence);

public enum InventoryPostingKind
{
    Opening = 0,
    Reserve = 1,
    Release = 2,
    Consume = 3,
    Receipt = 4,
    TransferOut = 5,
    TransferIn = 6
}
