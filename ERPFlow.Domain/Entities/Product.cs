namespace ERPFlow.Domain.Entities;

public sealed class Product
{
    public Product(string name, string sku, decimal unitPrice, int stockOnHand)
    {
        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        }

        if (stockOnHand < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stockOnHand), "Stock cannot be negative.");
        }

        Id = Guid.NewGuid();
        Name = RequireText(name, nameof(name));
        Sku = RequireText(sku, nameof(sku));
        UnitPrice = unitPrice;
        StockOnHand = stockOnHand;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Sku { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int StockOnHand { get; private set; }

    public int ReservedQuantity { get; private set; }

    public int AvailableStock => StockOnHand - ReservedQuantity;

    public DateTime CreatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public StockReservation Reserve(Guid salesOrderId, int quantity, Guid? warehouseId = null)
    {
        if (salesOrderId == Guid.Empty)
        {
            throw new ArgumentException("A sales order ID is required.", nameof(salesOrderId));
        }

        var reservation = new StockReservation(salesOrderId, Id, quantity, warehouseId);
        ReserveQuantity(quantity);
        return reservation;
    }

    public void ReserveQuantity(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (quantity > AvailableStock)
        {
            throw new InvalidOperationException("Insufficient available stock.");
        }

        ReservedQuantity += quantity;
    }

    public void ConsumeReservedQuantity(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (quantity > ReservedQuantity)
        {
            throw new InvalidOperationException("The product does not contain the reserved quantity.");
        }

        if (quantity > StockOnHand)
        {
            throw new InvalidOperationException("The product does not contain enough physical stock.");
        }

        StockOnHand -= quantity;
        ReservedQuantity -= quantity;
    }

    public void SetStockOnHand(int quantity)
    {
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Stock cannot be negative.");
        }

        if (quantity < ReservedQuantity)
        {
            throw new InvalidOperationException("Stock cannot be lower than the reserved quantity.");
        }

        StockOnHand = quantity;
    }

    public void ReleaseReservedQuantity(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (quantity > ReservedQuantity)
        {
            throw new InvalidOperationException("The product does not contain the reserved quantity.");
        }

        ReservedQuantity -= quantity;
    }

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }
}
