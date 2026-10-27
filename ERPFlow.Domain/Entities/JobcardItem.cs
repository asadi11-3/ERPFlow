namespace ERPFlow.Domain.Entities;

public sealed class JobcardItem
{
    public JobcardItem(Guid jobcardId, Guid productId, int quantity, decimal unitPrice, Guid? warehouseId = null)
    {
        if (jobcardId == Guid.Empty)
        {
            throw new ArgumentException("A jobcard ID is required.", nameof(jobcardId));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("A product ID is required.", nameof(productId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        if (warehouseId == Guid.Empty) throw new ArgumentException("Warehouse ID cannot be empty.", nameof(warehouseId));
        Id = Guid.NewGuid();
        JobcardId = jobcardId;
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        WarehouseId = warehouseId;
    }

    public Guid Id { get; private set; }

    public Guid JobcardId { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid? WarehouseId { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal LineTotal => Quantity * UnitPrice;
}
