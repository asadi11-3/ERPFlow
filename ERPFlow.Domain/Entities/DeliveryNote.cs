namespace ERPFlow.Domain.Entities;

public sealed class DeliveryNote
{
    public DeliveryNote(
        Guid salesOrderId,
        Guid customerId,
        string deliveryNumber,
        string carrier,
        string? trackingNumber = null)
    {
        if (salesOrderId == Guid.Empty)
        {
            throw new ArgumentException("Sales order ID is required.", nameof(salesOrderId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Customer ID is required.", nameof(customerId));
        }

        if (string.IsNullOrWhiteSpace(deliveryNumber))
        {
            throw new ArgumentException("Delivery number is required.", nameof(deliveryNumber));
        }

        Id = Guid.NewGuid();
        SalesOrderId = salesOrderId;
        CustomerId = customerId;
        DeliveryNumber = deliveryNumber.Trim();
        Carrier = string.IsNullOrWhiteSpace(carrier) ? "Standard Dispatch" : carrier.Trim();
        TrackingNumber = trackingNumber?.Trim();
        DispatchedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid SalesOrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public string DeliveryNumber { get; private set; }

    public string Carrier { get; private set; }

    public string? TrackingNumber { get; private set; }

    public DateTime DispatchedAt { get; private set; }

    public DateTime? DeliveredAt { get; private set; }

    public void MarkDelivered()
    {
        DeliveredAt = DateTime.UtcNow;
    }
}
