using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;

namespace ERPFlow.Application.Service;

public sealed class ServiceWorkflow(
    IServicePersistence persistence,
    ICurrentUser currentUser,
    IWarehouseSourcePolicy warehouseSourcePolicy,
    IWorkflowInventory inventory) : IServiceWorkflow
{
    public async Task<IReadOnlyList<JobcardResult>> ListJobcardsAsync(
        JobcardStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value))
        {
            throw new WorkflowValidationException("Status must be a defined JobcardStatus value.");
        }

        var list = await persistence.ListJobcardsAsync(status, cancellationToken);
        return list.Select(ToResult).ToArray();
    }

    public async Task<JobcardResult> GetJobcardByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ValidateJobcardId(id);
        var jobcard = await persistence.GetJobcardAsync(id, false, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Jobcard), id);
        return ToResult(jobcard);
    }

    public async Task<JobcardResult> CreateJobcardAsync(
        CreateJobcardCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCreateCommand(command);

        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        await EnsureAuthorizedActorAsync(cancellationToken);
        _ = await persistence.FindCustomerAsync(command.CustomerId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Customer), command.CustomerId);

        if (command.MachineAssetId.HasValue)
        {
            var asset = await persistence.FindMachineAssetAsync(command.MachineAssetId.Value, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(MachineAsset), command.MachineAssetId.Value);
            if (asset.CustomerId != command.CustomerId)
            {
                throw new WorkflowConflictException("The machine asset does not belong to the selected customer.");
            }
        }

        var ticketNumber = $"SRV-{BusinessClock.UtcNow:yyMMdd}-{Guid.NewGuid():N}";
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
        ValidateJobcardId(jobcardId);
        ValidatePartsCommand(command);

        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        await EnsureAuthorizedActorAsync(cancellationToken);
        var jobcard = await persistence.GetJobcardAsync(jobcardId, true, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Jobcard), jobcardId);

        if (jobcard.Status is not (JobcardStatus.Open or JobcardStatus.PartsRequested or JobcardStatus.InProgress))
        {
            throw new WorkflowConflictException("Parts can only be requested for an active jobcard.");
        }

        var productIds = command.Parts.Select(p => p.ProductId).ToArray();
        if (jobcard.Items.Any(item => productIds.Contains(item.ProductId)))
        {
            throw new WorkflowConflictException("A requested product already exists on this jobcard.");
        }

        var products = await persistence.GetProductsAsync(productIds, cancellationToken, lockForCatalog: true);
        var missingProductId = productIds.FirstOrDefault(id => products.All(product => product.Id != id));
        if (missingProductId != Guid.Empty)
        {
            throw new EntityNotFoundException(nameof(Product), missingProductId);
        }

        var productsById = products.ToDictionary(p => p.Id);

        if (products.Any(product => !product.IsActive))
            throw new WorkflowConflictException("Inactive products cannot be selected for new parts requests.");

        foreach (var p in command.Parts)
        {
            var product = productsById[p.ProductId];
            if (!inventory.UsesWarehouseStock && p.Quantity > product.AvailableStock)
            {
                throw new WorkflowConflictException($"Insufficient stock for product '{product.Name}'. Available: {product.AvailableStock}.");
            }
        }

        var warehouseIds = new Dictionary<Guid, Guid>();
        foreach (var part in command.Parts)
            warehouseIds.Add(part.ProductId, await warehouseSourcePolicy.ResolveAsync(part.WarehouseId, cancellationToken: cancellationToken));

        var priorItemIds = jobcard.Items.Select(x => x.Id).ToHashSet();
        try
        {
            foreach (var part in command.Parts)
            {
                var product = productsById[part.ProductId];
                if (!inventory.UsesWarehouseStock) product.ReserveQuantity(part.Quantity);
                jobcard.AddPart(product, part.Quantity, warehouseIds[part.ProductId]);
            }
            if (inventory.UsesWarehouseStock)
                await inventory.ReservePartsAsync(jobcard, jobcard.Items.Where(x => !priorItemIds.Contains(x.Id)).Select(x => x.Id).ToArray(), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkflowConflictException(exception.Message, exception);
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
        ValidateJobcardId(jobcardId);
        ValidateCompleteCommand(command);

        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        await EnsureAuthorizedActorAsync(cancellationToken);
        var jobcard = await persistence.GetJobcardAsync(jobcardId, true, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Jobcard), jobcardId);

        if (jobcard.Status is not (JobcardStatus.Open or JobcardStatus.PartsRequested or JobcardStatus.InProgress))
        {
            throw new WorkflowConflictException("Only an active jobcard can be completed.");
        }

        MachineAsset? asset = null;
        if (jobcard.MachineAssetId.HasValue && command.FinalMeterReading.HasValue)
        {
            asset = await persistence.FindMachineAssetAsync(jobcard.MachineAssetId.Value, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(MachineAsset), jobcard.MachineAssetId.Value);
            if (command.FinalMeterReading.Value < asset.MeterReading)
            {
                throw new WorkflowConflictException("Final meter reading cannot be lower than the asset's current meter reading.");
            }
        }

        var productIds = jobcard.Items.Select(item => item.ProductId).ToArray();
        var products = await persistence.GetProductsAsync(productIds, cancellationToken);
        var productsById = products.ToDictionary(product => product.Id);
        var missingProductId = productIds.FirstOrDefault(id => !productsById.ContainsKey(id));
        if (missingProductId != Guid.Empty)
        {
            throw new EntityNotFoundException(nameof(Product), missingProductId);
        }

        foreach (var item in jobcard.Items)
        {
            await warehouseSourcePolicy.ResolveAsync(item.WarehouseId, cancellationToken: cancellationToken);
            var product = productsById[item.ProductId];
            if (!inventory.UsesWarehouseStock && (product.ReservedQuantity < item.Quantity || product.StockOnHand < item.Quantity))
            {
                throw new WorkflowConflictException($"Reserved stock for product '{product.Name}' is inconsistent with this jobcard.");
            }
        }

        try
        {
            if (inventory.UsesWarehouseStock) await inventory.ConsumePartsAsync(jobcard, cancellationToken);
            jobcard.Complete(command.TechnicianNotes, command.FinalMeterReading);
            foreach (var item in jobcard.Items.Where(_ => !inventory.UsesWarehouseStock))
            {
                productsById[item.ProductId].ConsumeReservedQuantity(item.Quantity);
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkflowConflictException(exception.Message, exception);
        }

        if (asset is not null)
        {
            asset.UpdateMeterReading(command.FinalMeterReading!.Value);
        }

        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResult(jobcard);
    }

    private async Task EnsureAuthorizedActorAsync(CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId;
        var actor = actorId == Guid.Empty
            ? null
            : await persistence.FindAppUserAsync(actorId, cancellationToken);
        if (actor is null)
        {
            throw new EntityNotFoundException(nameof(AppUser), actorId);
        }

        if (actor.Role is not (UserRole.Technician or UserRole.Administrator))
        {
            throw new WorkflowAuthorizationException("Only technicians and administrators can access service operations.");
        }
    }

    private static void ValidateJobcardId(Guid jobcardId)
    {
        if (jobcardId == Guid.Empty)
        {
            throw new WorkflowValidationException("A jobcard ID is required.");
        }
    }

    private static void ValidateCreateCommand(CreateJobcardCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CustomerId == Guid.Empty)
        {
            throw new WorkflowValidationException("Customer ID is required.");
        }

        if (command.MachineAssetId == Guid.Empty)
        {
            throw new WorkflowValidationException("Machine asset ID cannot be empty.");
        }

        if (command.TechnicianName?.Length > 150)
        {
            throw new WorkflowValidationException("Technician name cannot exceed 150 characters.");
        }

        if (string.IsNullOrWhiteSpace(command.ReportedFault) || command.ReportedFault.Length > 1000)
        {
            throw new WorkflowValidationException("Reported fault is required and cannot exceed 1000 characters.");
        }

        if (!Enum.IsDefined(command.Priority))
        {
            throw new WorkflowValidationException("Priority must be a defined JobcardPriority value.");
        }
    }

    private static void ValidatePartsCommand(RequestJobcardPartsCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Parts is null || command.Parts.Count == 0)
        {
            throw new WorkflowValidationException("At least one part is required.");
        }

        if (command.Parts.Any(part => part is null || part.ProductId == Guid.Empty || part.Quantity <= 0))
        {
            throw new WorkflowValidationException("Every part must have a product ID and a positive quantity.");
        }

        if (command.Parts.Select(part => part.ProductId).Distinct().Count() != command.Parts.Count)
        {
            throw new WorkflowValidationException("A product can only appear once in a parts request.");
        }
    }

    private static void ValidateCompleteCommand(CompleteJobcardCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TechnicianNotes?.Length > 2000)
        {
            throw new WorkflowValidationException("Technician notes cannot exceed 2000 characters.");
        }

        if (command.FinalMeterReading < 0)
        {
            throw new WorkflowValidationException("Final meter reading cannot be negative.");
        }
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
                item.LineTotal,
                item.WarehouseId)).ToArray());
}
