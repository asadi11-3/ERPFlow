using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;

namespace ERPFlow.Application.Abstractions;

public interface IServicePersistence
{
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Jobcard>> ListJobcardsAsync(JobcardStatus? status, CancellationToken cancellationToken = default);
    Task<Jobcard?> GetJobcardAsync(Guid id, bool tracking, CancellationToken cancellationToken = default);
    Task<Customer?> FindCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<AppUser?> FindAppUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<MachineAsset?> FindMachineAssetAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> GetProductsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default, bool lockForCatalog = false);
    void AddJobcard(Jobcard jobcard);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
