using ERPFlow.Application.Abstractions;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class EfServicePersistence(ERPFlowDbContext context) : IServicePersistence
{
    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        return new EfApplicationTransaction(transaction);
    }

    public async Task<IReadOnlyList<Jobcard>> ListJobcardsAsync(
        JobcardStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = context.Jobcards
            .Include(j => j.Items)
            .AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(j => j.Status == status.Value);
        }

        return await query
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Jobcard?> GetJobcardAsync(Guid id, bool tracking, CancellationToken cancellationToken = default)
    {
        var query = tracking ? context.Jobcards.AsTracking() : context.Jobcards.AsNoTracking();
        return await query
            .Include(j => j.Items)
            .SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
    }

    public async Task<Customer?> FindCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        return await context.Customers.SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken);
    }

    public async Task<MachineAsset?> FindMachineAssetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        return await context.MachineAssets.SingleOrDefaultAsync(m => m.Id == assetId, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        return await context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    public void AddJobcard(Jobcard jobcard)
    {
        context.Jobcards.Add(jobcard);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await context.SaveChangesAsync(cancellationToken);
    }
}
