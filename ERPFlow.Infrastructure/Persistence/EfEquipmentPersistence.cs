using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ERPFlow.Infrastructure.Persistence.Inventory;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class EfEquipmentPersistence(ERPFlowDbContext context) : IEquipmentPersistence
{
    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return await InventoryAuthoritySession.BeginAsync(context, cancellationToken);
    }

    public async Task<SalesOrder?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await context.SalesOrders
            .Include(o => o.Items)
            .Include(o => o.Reservations)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        return await context.Products
            .Where(product => productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<AppUser?> FindAppUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.AppUsers.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<DeliveryNote>> ListDeliveryNotesAsync(CancellationToken cancellationToken = default)
    {
        return await context.DeliveryNotes
            .AsNoTracking()
            .OrderByDescending(d => d.DispatchedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MachineAsset>> ListMachineAssetsAsync(CancellationToken cancellationToken = default)
    {
        return await context.MachineAssets
            .AsNoTracking()
            .OrderByDescending(m => m.InstalledAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<MachineAsset?> FindMachineAssetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.MachineAssets
            .SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public void AddDeliveryNote(DeliveryNote deliveryNote)
    {
        context.DeliveryNotes.Add(deliveryNote);
    }

    public void AddMachineAsset(MachineAsset asset)
    {
        context.MachineAssets.Add(asset);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new WorkflowConflictException(
                "The equipment dispatch conflicted with another request.",
                exception);
        }
        catch (DbUpdateException exception) when (FindSqlException(exception)?.Number is 2601 or 2627)
        {
            throw new WorkflowConflictException(
                "The sales order has already been dispatched or an equipment identifier is already in use.",
                exception);
        }
        catch (DbUpdateException exception) when (FindSqlException(exception)?.Number == 1205)
        {
            throw new WorkflowConflictException(
                "The equipment dispatch conflicted with another transaction.",
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
