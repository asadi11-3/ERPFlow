namespace ERPFlow.Domain.Entities;

public sealed class MachineAsset
{
    public MachineAsset(
        string tagNumber,
        string serialNumber,
        Guid productId,
        Guid customerId,
        Guid? salesOrderId,
        DateTime warrantyExpiresAt,
        int meterReading = 0)
    {
        if (string.IsNullOrWhiteSpace(tagNumber))
        {
            throw new ArgumentException("Tag number is required.", nameof(tagNumber));
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new ArgumentException("Serial number is required.", nameof(serialNumber));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product ID is required.", nameof(productId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Customer ID is required.", nameof(customerId));
        }

        Id = Guid.NewGuid();
        TagNumber = tagNumber.Trim().ToUpperInvariant();
        SerialNumber = serialNumber.Trim().ToUpperInvariant();
        ProductId = productId;
        CustomerId = customerId;
        SalesOrderId = salesOrderId;
        InstalledAt = BusinessClock.UtcNow;
        WarrantyExpiresAt = warrantyExpiresAt;
        Status = "Active";
        MeterReading = Math.Max(0, meterReading);
    }

    public Guid Id { get; private set; }

    public string TagNumber { get; private set; }

    public string SerialNumber { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid? SalesOrderId { get; private set; }

    public DateTime InstalledAt { get; private set; }

    public DateTime WarrantyExpiresAt { get; private set; }

    public string Status { get; private set; }

    public int MeterReading { get; private set; }

    public void UpdateMeterReading(int reading)
    {
        if (reading >= MeterReading)
        {
            MeterReading = reading;
        }
    }

    public void SetStatus(string status)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            Status = status.Trim();
        }
    }
}
