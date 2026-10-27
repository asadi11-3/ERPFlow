using ERPFlow.Domain.Enums;

namespace ERPFlow.Application.Service;

public sealed record JobcardItemResult(
    Guid Id,
    Guid ProductId,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    Guid? WarehouseId);

public sealed record JobcardResult(
    Guid Id,
    string TicketNumber,
    Guid CustomerId,
    Guid? MachineAssetId,
    string TechnicianName,
    string ReportedFault,
    JobcardPriority Priority,
    JobcardStatus Status,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? TechnicianNotes,
    int? FinalMeterReading,
    IReadOnlyList<JobcardItemResult> Items);

public sealed record CreateJobcardCommand(
    Guid CustomerId,
    Guid? MachineAssetId,
    string TechnicianName,
    string ReportedFault,
    JobcardPriority Priority);

public sealed record JobcardPartRequestItem(Guid ProductId, int Quantity, Guid? WarehouseId = null);

public sealed record RequestJobcardPartsCommand(
    IReadOnlyList<JobcardPartRequestItem> Parts);

public sealed record CompleteJobcardCommand(
    string? TechnicianNotes,
    int? FinalMeterReading);

public interface IServiceWorkflow
{
    Task<IReadOnlyList<JobcardResult>> ListJobcardsAsync(
        JobcardStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<JobcardResult> GetJobcardByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<JobcardResult> CreateJobcardAsync(
        CreateJobcardCommand command,
        CancellationToken cancellationToken = default);

    Task<JobcardResult> RequestPartsAsync(
        Guid jobcardId,
        RequestJobcardPartsCommand command,
        CancellationToken cancellationToken = default);

    Task<JobcardResult> CompleteJobcardAsync(
        Guid jobcardId,
        CompleteJobcardCommand command,
        CancellationToken cancellationToken = default);
}
