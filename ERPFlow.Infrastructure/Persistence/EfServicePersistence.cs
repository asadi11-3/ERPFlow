using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ERPFlow.Infrastructure.Persistence.Inventory;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class EfServicePersistence(ERPFlowDbContext context) : IServicePersistence
{
    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return await InventoryAuthoritySession.BeginAsync(context, cancellationToken);
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

    public Task<AppUser?> FindAppUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<MachineAsset?> FindMachineAssetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        return await context.MachineAssets.SingleOrDefaultAsync(m => m.Id == assetId, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default, bool lockForCatalog = false)
    {
        return await (lockForCatalog ? context.Products.FromSqlRaw("SELECT * FROM Products WITH (UPDLOCK,HOLDLOCK)") : context.Products)
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    public void AddJobcard(Jobcard jobcard)
    {
        context.Jobcards.Add(jobcard);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            var entityNames = string.Join(", ", exception.Entries.Select(entry => entry.Metadata.ClrType.Name));
            throw new ConcurrencyConflictException(
                $"The service operation conflicted while saving: {entityNames}. Retry the operation.",
                exception);
        }
        catch (DbUpdateException exception) when (FindSqlException(exception)?.Number is 2601 or 2627)
        {
            throw new WorkflowConflictException(
                "The service operation conflicts with an existing record.",
                exception);
        }
        catch (DbUpdateException exception) when (FindSqlException(exception)?.Number == 1205)
        {
            throw new ConcurrencyConflictException(
                "The service operation conflicted with another transaction. Retry the operation.",
                exception);
        }
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException)
            {
                return sqlException;
            }
        }

        return null;
    }
}
