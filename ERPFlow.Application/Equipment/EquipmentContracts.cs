namespace ERPFlow.Application.Equipment;

public sealed record DeliveryNoteResult(
    Guid Id,
    Guid SalesOrderId,
    Guid CustomerId,
    string DeliveryNumber,
    string Carrier,
    string? TrackingNumber,
    DateTime DispatchedAt,
    DateTime? DeliveredAt);

public sealed record MachineAssetResult(
    Guid Id,
    string TagNumber,
    string SerialNumber,
    Guid ProductId,
    Guid CustomerId,
    Guid? SalesOrderId,
    DateTime InstalledAt,
    DateTime WarrantyExpiresAt,
    string Status,
    int MeterReading);

public sealed record MachineSerialInput(Guid ProductId, string SerialNumber);

public sealed record DispatchOrderCommand(
    string Carrier,
    string? TrackingNumber,
    IReadOnlyList<MachineSerialInput>? SerialNumbers);

public interface IEquipmentWorkflow
{
    Task<DeliveryNoteResult> DispatchOrderAsync(
        Guid salesOrderId,
        DispatchOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeliveryNoteResult>> ListDeliveryNotesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MachineAssetResult>> ListMachineAssetsAsync(
        CancellationToken cancellationToken = default);

    Task<MachineAssetResult> GetMachineAssetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
