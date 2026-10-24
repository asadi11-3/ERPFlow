using ERPFlow.Domain.Entities;

namespace ERPFlow.Application.Abstractions;

public interface IEquipmentPersistence
{
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<SalesOrder?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default);
    Task<AppUser?> FindAppUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryNote>> ListDeliveryNotesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MachineAsset>> ListMachineAssetsAsync(CancellationToken cancellationToken = default);
    Task<MachineAsset?> FindMachineAssetAsync(Guid id, CancellationToken cancellationToken = default);
    void AddDeliveryNote(DeliveryNote deliveryNote);
    void AddMachineAsset(MachineAsset asset);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
