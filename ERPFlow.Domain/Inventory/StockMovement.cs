namespace ERPFlow.Domain.Inventory;

public sealed class StockMovement
{
    private StockMovement() { }

    public StockMovement(InventoryPosting posting, WarehouseStock stock, StockEffect effect)
    {
        ArgumentNullException.ThrowIfNull(posting);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(effect);
        if (stock.RowVersion.Length != 8 || stock.Sequence != effect.Sequence ||
            stock.StockOnHand != effect.AfterOnHand || stock.ReservedQuantity != effect.AfterReserved)
            throw new InvalidOperationException("The movement must snapshot the persisted stock posting result.");
        Id = Guid.NewGuid();
        PostingId = posting.Id;
        WarehouseStockId = stock.Id;
        Kind = posting.Kind;
        Sequence = effect.Sequence;
        BeforeOnHand = effect.BeforeOnHand;
        BeforeReserved = effect.BeforeReserved;
        AfterOnHand = effect.AfterOnHand;
        AfterReserved = effect.AfterReserved;
        PhysicalDelta = checked(AfterOnHand - BeforeOnHand);
        ReservedDelta = checked(AfterReserved - BeforeReserved);
        ResultRowVersion = stock.RowVersion.ToArray();
    }

    public Guid Id { get; private set; }
    public Guid PostingId { get; private set; }
    public Guid WarehouseStockId { get; private set; }
    public InventoryPostingKind Kind { get; private set; }
    public long Sequence { get; private set; }
    public int BeforeOnHand { get; private set; }
    public int BeforeReserved { get; private set; }
    public int AfterOnHand { get; private set; }
    public int AfterReserved { get; private set; }
    public int PhysicalDelta { get; private set; }
    public int ReservedDelta { get; private set; }
    public byte[] ResultRowVersion { get; private set; } = [];
}
