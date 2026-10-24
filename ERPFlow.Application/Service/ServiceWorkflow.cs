using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;

namespace ERPFlow.Application.Service;

public sealed class ServiceWorkflow(
    IServicePersistence persistence) : IServiceWorkflow
{
    public async Task<IReadOnlyList<JobcardResult>> ListJobcardsAsync(
        JobcardStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var list = await persistence.ListJobcardsAsync(status, cancellationToken);
        return list.Select(ToResult).ToArray();
    }

    public async Task<JobcardResult> GetJobcardByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var jobcard = await persistence.GetJobcardAsync(id, false, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Jobcard), id);
        return ToResult(jobcard);
    }

    public async Task<JobcardResult> CreateJobcardAsync(
        CreateJobcardCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.CustomerId == Guid.Empty)
        {
            throw new WorkflowValidationException("Customer ID is required.");
        }

        if (string.IsNullOrWhiteSpace(command.ReportedFault))
        {
            throw new WorkflowValidationException("Reported fault is required.");
        }

        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        var customer = await persistence.FindCustomerAsync(command.CustomerId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Customer), command.CustomerId);

        if (command.MachineAssetId.HasValue && command.MachineAssetId.Value != Guid.Empty)
        {
            var asset = await persistence.FindMachineAssetAsync(command.MachineAssetId.Value, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(MachineAsset), command.MachineAssetId.Value);
        }

        var ticketNumber = $"SRV-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}";
        var jobcard = new Jobcard(
            ticketNumber,
            command.CustomerId,
            command.MachineAssetId,
            command.TechnicianName,
            command.ReportedFault,
            command.Priority);

        persistence.AddJobcard(jobcard);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResult(jobcard);
    }

    public async Task<JobcardResult> RequestPartsAsync(
        Guid jobcardId,
        RequestJobcardPartsCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Parts is null || command.Parts.Count == 0)
        {
            throw new WorkflowValidationException("At least one part is required.");
        }

        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        var jobcard = await persistence.GetJobcardAsync(jobcardId, true, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Jobcard), jobcardId);

        var productIds = command.Parts.Select(p => p.ProductId).ToArray();
        var products = await persistence.GetProductsAsync(productIds, cancellationToken);
        var productsById = products.ToDictionary(p => p.Id);

        foreach (var p in command.Parts)
        {
            if (!productsById.TryGetValue(p.ProductId, out var product))
            {
                throw new EntityNotFoundException(nameof(Product), p.ProductId);
            }

            if (p.Quantity <= 0)
            {
                throw new WorkflowValidationException("Quantity must be positive.");
            }

            if (p.Quantity > product.AvailableStock)
            {
                throw new WorkflowConflictException($"Insufficient stock for product '{product.Name}'. Available: {product.AvailableStock}.");
            }

            // Reserve stock for the jobcard
            product.Reserve(jobcard.Id, p.Quantity);
            jobcard.AddPart(product, p.Quantity);
        }

        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResult(jobcard);
    }

    public async Task<JobcardResult> CompleteJobcardAsync(
        Guid jobcardId,
        CompleteJobcardCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        var jobcard = await persistence.GetJobcardAsync(jobcardId, true, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Jobcard), jobcardId);

        jobcard.Complete(command.TechnicianNotes, command.FinalMeterReading);

        if (jobcard.MachineAssetId.HasValue && command.FinalMeterReading.HasValue)
        {
            var asset = await persistence.FindMachineAssetAsync(jobcard.MachineAssetId.Value, cancellationToken);
            asset?.UpdateMeterReading(command.FinalMeterReading.Value);
        }

        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResult(jobcard);
    }

    private static JobcardResult ToResult(Jobcard j) =>
        new(
            j.Id,
            j.TicketNumber,
            j.CustomerId,
            j.MachineAssetId,
            j.TechnicianName,
            j.ReportedFault,
            j.Priority,
            j.Status,
            j.CreatedAt,
            j.CompletedAt,
            j.TechnicianNotes,
            j.FinalMeterReading,
            j.Items.Select(item => new JobcardItemResult(
                item.Id,
                item.ProductId,
                item.Quantity,
                item.UnitPrice,
                item.LineTotal)).ToArray());
}
