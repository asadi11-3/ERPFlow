using System.Collections.ObjectModel;
using ERPFlow.Domain.Enums;

namespace ERPFlow.Domain.Entities;

public sealed class Jobcard
{
    private readonly List<JobcardItem> _items = [];
    private readonly ReadOnlyCollection<JobcardItem> _readOnlyItems;

    public Jobcard(
        string ticketNumber,
        Guid customerId,
        Guid? machineAssetId,
        string technicianName,
        string reportedFault,
        JobcardPriority priority = JobcardPriority.Medium)
    {
        if (string.IsNullOrWhiteSpace(ticketNumber))
        {
            throw new ArgumentException("Ticket number is required.", nameof(ticketNumber));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Customer ID is required.", nameof(customerId));
        }

        if (string.IsNullOrWhiteSpace(reportedFault))
        {
            throw new ArgumentException("Reported fault is required.", nameof(reportedFault));
        }

        Id = Guid.NewGuid();
        TicketNumber = ticketNumber.Trim().ToUpperInvariant();
        CustomerId = customerId;
        MachineAssetId = machineAssetId;
        TechnicianName = string.IsNullOrWhiteSpace(technicianName) ? "Unassigned" : technicianName.Trim();
        ReportedFault = reportedFault.Trim();
        Priority = priority;
        Status = JobcardStatus.Open;
        CreatedAt = DateTime.UtcNow;
        _readOnlyItems = _items.AsReadOnly();
    }

    public Guid Id { get; private set; }

    public string TicketNumber { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid? MachineAssetId { get; private set; }

    public string TechnicianName { get; private set; }

    public string ReportedFault { get; private set; }

    public JobcardPriority Priority { get; private set; }

    public JobcardStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public string? TechnicianNotes { get; private set; }

    public int? FinalMeterReading { get; private set; }

    public int Revision { get; private set; }

    public IReadOnlyList<JobcardItem> Items => _readOnlyItems;

    public void AddPart(Product product, int quantity)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (Status is not (JobcardStatus.Open or JobcardStatus.PartsRequested or JobcardStatus.InProgress))
        {
            throw new InvalidOperationException("Parts can only be requested for an active jobcard.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (_items.Any(i => i.ProductId == product.Id))
        {
            throw new InvalidOperationException("This product has already been requested on this jobcard.");
        }

        _items.Add(new JobcardItem(Id, product.Id, quantity, product.UnitPrice));
        if (Status == JobcardStatus.Open)
        {
            Status = JobcardStatus.PartsRequested;
        }

        Revision++;
    }

    public void StartWork()
    {
        if (Status is not (JobcardStatus.Open or JobcardStatus.PartsRequested))
        {
            throw new InvalidOperationException("Work can only start on an open jobcard or one with requested parts.");
        }

        Status = JobcardStatus.InProgress;
        Revision++;
    }

    public void Complete(string? notes, int? meterReading = null)
    {
        if (Status is not (JobcardStatus.Open or JobcardStatus.PartsRequested or JobcardStatus.InProgress))
        {
            throw new InvalidOperationException("Only an active jobcard can be completed.");
        }

        if (meterReading < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(meterReading), "Meter reading cannot be negative.");
        }

        Status = JobcardStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        TechnicianNotes = notes?.Trim();
        FinalMeterReading = meterReading;
        Revision++;
    }
}
