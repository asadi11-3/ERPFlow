using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;

namespace ERPFlow.Application.Equipment;

public sealed class EquipmentWorkflow(
    IEquipmentPersistence persistence,
    ICurrentUser currentUser) : IEquipmentWorkflow
{
    public const int MaxDispatchUnits = 1000;

    public async Task<DeliveryNoteResult> DispatchOrderAsync(
        Guid salesOrderId,
        DispatchOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Carrier?.Length > 100 || command.TrackingNumber?.Length > 100)
        {
            throw new WorkflowValidationException("Carrier and tracking number cannot exceed 100 characters.");
        }
        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        var actor = await GetActorAsync(cancellationToken);
        if (actor.Role is not (UserRole.Technician or UserRole.Administrator))
        {
            throw new WorkflowAuthorizationException("The current user is not authorized to dispatch equipment.");
        }

        var order = await persistence.GetOrderAsync(salesOrderId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SalesOrder), salesOrderId);

        if (order.Status != OrderStatus.Approved)
        {
            throw new WorkflowConflictException("Only approved orders can be dispatched.");
        }

        var totalUnits = 0;
        foreach (var item in order.Items)
        {
            if (item.Quantity <= 0 || item.Quantity > MaxDispatchUnits - totalUnits)
            {
                throw new WorkflowValidationException($"A dispatch cannot exceed {MaxDispatchUnits} units.");
            }

            totalUnits = checked(totalUnits + item.Quantity);
        }

        if (command.SerialNumbers?.Count > MaxDispatchUnits)
        {
            throw new WorkflowValidationException($"A dispatch cannot exceed {MaxDispatchUnits} serial numbers.");
        }

        var serials = BuildSerials(order, command.SerialNumbers);
        var products = await persistence.GetProductsAsync(
            order.Items.Select(item => item.ProductId).ToArray(),
            cancellationToken);

        try
        {
            order.Dispatch(products);
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkflowConflictException(exception.Message, exception);
        }

        var deliveryNumber = $"DN-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..20]}";
        var delivery = new DeliveryNote(
            order.Id,
            order.CustomerId,
            deliveryNumber,
            command.Carrier ?? string.Empty,
            command.TrackingNumber);

        persistence.AddDeliveryNote(delivery);

        foreach (var serial in serials)
        {
            var asset = new MachineAsset(
                $"TAG-{Guid.NewGuid():N}",
                serial.SerialNumber,
                serial.ProductId,
                order.CustomerId,
                order.Id,
                DateTime.UtcNow.AddYears(1));
            persistence.AddMachineAsset(asset);
        }

        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResult(delivery);
    }

    public async Task<IReadOnlyList<DeliveryNoteResult>> ListDeliveryNotesAsync(
        CancellationToken cancellationToken = default)
    {
        var list = await persistence.ListDeliveryNotesAsync(cancellationToken);
        return list.Select(ToResult).ToArray();
    }

    public async Task<IReadOnlyList<MachineAssetResult>> ListMachineAssetsAsync(
        CancellationToken cancellationToken = default)
    {
        var list = await persistence.ListMachineAssetsAsync(cancellationToken);
        return list.Select(ToResult).ToArray();
    }

    public async Task<MachineAssetResult> GetMachineAssetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var asset = await persistence.FindMachineAssetAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(MachineAsset), id);
        return ToResult(asset);
    }

    private static DeliveryNoteResult ToResult(DeliveryNote d) =>
        new(d.Id, d.SalesOrderId, d.CustomerId, d.DeliveryNumber, d.Carrier, d.TrackingNumber, d.DispatchedAt, d.DeliveredAt);

    private static MachineAssetResult ToResult(MachineAsset m) =>
        new(m.Id, m.TagNumber, m.SerialNumber, m.ProductId, m.CustomerId, m.SalesOrderId, m.InstalledAt, m.WarrantyExpiresAt, m.Status, m.MeterReading);

    private async Task<AppUser> GetActorAsync(CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId;
        var actor = actorId == Guid.Empty
            ? null
            : await persistence.FindAppUserAsync(actorId, cancellationToken);
        return actor ?? throw new EntityNotFoundException(nameof(AppUser), actorId);
    }

    private static IReadOnlyList<MachineSerialInput> BuildSerials(
        SalesOrder order,
        IReadOnlyList<MachineSerialInput>? suppliedSerials)
    {
        if (suppliedSerials is null || suppliedSerials.Count == 0)
        {
            return order.Items
                .SelectMany(item => Enumerable.Range(0, item.Quantity)
                    .Select(_ => new MachineSerialInput(item.ProductId, $"SN-{Guid.NewGuid():N}")))
                .ToArray();
        }

        var normalized = new List<MachineSerialInput>(suppliedSerials.Count);
        var serialNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var supplied in suppliedSerials)
        {
            if (supplied is null)
            {
                throw new WorkflowValidationException("Serial number entries cannot be null.");
            }

            var serialNumber = supplied.SerialNumber?.Trim().ToUpperInvariant();
            if (supplied.ProductId == Guid.Empty ||
                string.IsNullOrEmpty(serialNumber) ||
                serialNumber.Length > 100 ||
                !serialNumbers.Add(serialNumber))
            {
                throw new WorkflowValidationException(
                    "Serials require a product, a unique non-empty value no longer than 100 characters, and exact order coverage.");
            }

            normalized.Add(new MachineSerialInput(supplied.ProductId, serialNumber));
        }

        var expected = order.Items.ToDictionary(item => item.ProductId, item => item.Quantity);
        var actual = normalized
            .GroupBy(serial => serial.ProductId)
            .ToDictionary(group => group.Key, group => group.Count());
        if (expected.Count != actual.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var count) || count != pair.Value))
        {
            throw new WorkflowValidationException("Serial numbers must exactly cover every ordered product and quantity.");
        }

        return normalized;
    }
}
