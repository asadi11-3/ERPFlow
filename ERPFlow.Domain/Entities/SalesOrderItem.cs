namespace ERPFlow.Domain.Entities;

public sealed class SalesOrderItem
{
    public SalesOrderItem(Guid salesOrderId, Guid productId, int quantity, decimal unitPrice, Guid? warehouseId = null)
    {
        if (salesOrderId == Guid.Empty)
        {
            throw new ArgumentException("A sales order ID is required.", nameof(salesOrderId));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("A product ID is required.", nameof(productId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        }

        if (warehouseId == Guid.Empty) throw new ArgumentException("Warehouse ID cannot be empty.", nameof(warehouseId));
        Id = Guid.NewGuid();
        SalesOrderId = salesOrderId;
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        WarehouseId = warehouseId;
    }

    public Guid Id { get; private set; }

    public Guid SalesOrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid? WarehouseId { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal LineTotal => Quantity * UnitPrice;

    internal void Revise(int quantity, decimal unitPrice, Guid? warehouseId)
    {
        if (quantity <= 0 || unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive and price non-negative.");
        }

        Quantity = quantity;
        UnitPrice = unitPrice;
        WarehouseId = warehouseId;
    }
}
