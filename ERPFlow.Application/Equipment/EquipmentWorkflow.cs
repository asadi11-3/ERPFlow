using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;

namespace ERPFlow.Application.Equipment;

public sealed class EquipmentWorkflow(
    IEquipmentPersistence persistence) : IEquipmentWorkflow
{
    public async Task<DeliveryNoteResult> DispatchOrderAsync(
        Guid salesOrderId,
        DispatchOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        var order = await persistence.GetOrderAsync(salesOrderId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SalesOrder), salesOrderId);

        if (order.Status != OrderStatus.Approved)
        {
            throw new WorkflowConflictException("Only approved orders can be dispatched.");
        }

        var deliveryNumber = $"DN-{DateTime.UtcNow:yyyyMMdd}-{order.Id.ToString()[..4].ToUpperInvariant()}";
        var delivery = new DeliveryNote(
            order.Id,
            order.CustomerId,
            deliveryNumber,
            command.Carrier,
            command.TrackingNumber);

        persistence.AddDeliveryNote(delivery);

        // Auto-register machine assets if serial numbers are supplied, or default generated for line items
        var counter = 1;
        if (command.SerialNumbers is not null && command.SerialNumbers.Count > 0)
        {
            foreach (var s in command.SerialNumbers)
            {
                var tagNumber = $"TAG-{DateTime.UtcNow:yyyy}-{counter++:D3}-{s.SerialNumber[..Math.Min(4, s.SerialNumber.Length)]}";
                var asset = new MachineAsset(
                    tagNumber,
                    s.SerialNumber,
                    s.ProductId,
                    order.CustomerId,
                    order.Id,
                    DateTime.UtcNow.AddYears(1));
                persistence.AddMachineAsset(asset);
            }
        }
        else
        {
            foreach (var item in order.Items)
            {
                for (var i = 0; i < item.Quantity; i++)
                {
                    var serial = $"SN-{DateTime.UtcNow:yyMM}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
                    var tagNumber = $"TAG-{DateTime.UtcNow:yyyy}-{counter++:D3}";
                    var asset = new MachineAsset(
                        tagNumber,
                        serial,
                        item.ProductId,
                        order.CustomerId,
                        order.Id,
                        DateTime.UtcNow.AddYears(1));
                    persistence.AddMachineAsset(asset);
                }
            }
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
}
