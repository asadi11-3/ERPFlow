namespace ERPFlow.Domain.Entities;

public sealed class StockReservation
{
    internal StockReservation(Guid salesOrderId, Guid productId, int quantity, Guid? warehouseId = null)
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

        if (warehouseId == Guid.Empty) throw new ArgumentException("Warehouse ID cannot be empty.", nameof(warehouseId));
        Id = Guid.NewGuid();
        SalesOrderId = salesOrderId;
        ProductId = productId;
        Quantity = quantity;
        WarehouseId = warehouseId;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid SalesOrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid? WarehouseId { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Indicates that the reservation is closed, whether released or consumed.</summary>
    public bool IsReleased { get; private set; }

    public bool IsHistorical { get; private set; }

    public StockReservationClosureKind? ClosureKind { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? ReleasedAt { get; private set; }

    public void Release(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (product.Id != ProductId)
        {
            throw new InvalidOperationException("The reservation belongs to a different product.");
        }

        if (IsReleased)
        {
            throw new InvalidOperationException("The reservation has already been released.");
        }

        product.ReleaseReservedQuantity(Quantity);
        IsReleased = true;
        ReleasedAt = DateTime.UtcNow;
        ClosureKind = StockReservationClosureKind.Released;
    }

    public void Consume(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (product.Id != ProductId)
        {
            throw new InvalidOperationException("The reservation belongs to a different product.");
        }

        if (IsReleased)
        {
            throw new InvalidOperationException("The reservation has already been closed.");
        }

        product.ConsumeReservedQuantity(Quantity);
        IsReleased = true;
        ReleasedAt = DateTime.UtcNow;
        ClosureKind = StockReservationClosureKind.Consumed;
    }

    internal void Archive()
    {
        if (!IsReleased)
        {
            throw new InvalidOperationException("Only closed reservations can be archived.");
        }

        IsHistorical = true;
    }

    internal void CloseWarehouse(StockReservationClosureKind kind)
    {
        if (IsReleased || IsHistorical || !WarehouseId.HasValue || !Enum.IsDefined(kind))
            throw new InvalidOperationException("Only an active exact-source warehouse reservation can close.");
        IsReleased = true;
        ReleasedAt = DateTime.UtcNow;
        ClosureKind = kind;
    }
}

public enum StockReservationClosureKind
{
    Released = 1,
    Consumed = 2
}
