using ERPFlow.Application.Abstractions;
using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class EfEquipmentPersistence(ERPFlowDbContext context) : IEquipmentPersistence
{
    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        return new EfApplicationTransaction(transaction);
    }

    public async Task<SalesOrder?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await context.SalesOrders
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
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
        await context.SaveChangesAsync(cancellationToken);
    }
}
