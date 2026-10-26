using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed class EfWorkflowInventory(ERPFlowDbContext context, ICurrentUser actor, IInventoryWriteGate gate) : IWorkflowInventory
{
    public bool UsesWarehouseStock => context.ObservedInventoryAuthority switch
    {
        InventoryAuthorityMode.Product => false,
        InventoryAuthorityMode.Warehouse => true,
        _ => throw new WorkflowConflictException("A fenced inventory transaction is required before selecting authority.")
    };

    public async Task ReserveSalesAsync(SalesOrder order, CancellationToken cancellationToken = default)
    {
        RequireWarehouse();
        await SaveOwnersAsync(cancellationToken);
        foreach (var reservation in order.Reservations.OrderBy(x => x.ProductId))
            await PostAsync(InventoryPostingKind.Reserve, reservation.WarehouseId, reservation.ProductId,
                reservation.Quantity, order.Id, null, reservation.Id, cancellationToken);
    }

    public async Task ReleaseSalesAsync(SalesOrder order, CancellationToken cancellationToken = default)
    {
        RequireWarehouse();
        foreach (var reservation in order.Reservations.OrderBy(x => x.ProductId))
            await PostAsync(InventoryPostingKind.Release, reservation.WarehouseId, reservation.ProductId,
                reservation.Quantity, order.Id, null, reservation.Id, cancellationToken);
    }

    public async Task ConsumeSalesAsync(SalesOrder order, CancellationToken cancellationToken = default)
    {
        RequireWarehouse();
        foreach (var reservation in order.Reservations.OrderBy(x => x.ProductId))
            await PostAsync(InventoryPostingKind.Consume, reservation.WarehouseId, reservation.ProductId,
                reservation.Quantity, order.Id, null, reservation.Id, cancellationToken);
    }

    public async Task ReservePartsAsync(Jobcard jobcard, IReadOnlyCollection<Guid> newItemIds, CancellationToken cancellationToken = default)
    {
        RequireWarehouse();
        await SaveOwnersAsync(cancellationToken);
        foreach (var item in jobcard.Items.Where(x => newItemIds.Contains(x.Id)).OrderBy(x => x.ProductId))
            await PostAsync(InventoryPostingKind.Reserve, item.WarehouseId, item.ProductId, item.Quantity,
                null, item.Id, LegacyInventorySnapshot.StableTarget("jobcard-reservation", item.Id), cancellationToken);
    }

    public async Task ConsumePartsAsync(Jobcard jobcard, CancellationToken cancellationToken = default)
    {
        RequireWarehouse();
        foreach (var item in jobcard.Items.OrderBy(x => x.ProductId))
        {
            var reservation = await context.InventoryReservations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.JobcardItemId == item.Id, cancellationToken)
                ?? throw new WorkflowConflictException("The Jobcard part has no original inventory reservation.");
            await PostAsync(InventoryPostingKind.Consume, item.WarehouseId, item.ProductId,
                item.Quantity, null, item.Id, reservation.Id, cancellationToken);
        }
    }

    private void RequireWarehouse()
    {
        if (!UsesWarehouseStock || context.Database.CurrentTransaction is null)
            throw new WorkflowConflictException("Warehouse posting requires a fenced warehouse-authority transaction.");
        gate.EnsureWarehouseWritesAllowed();
    }

    private async Task SaveOwnersAsync(CancellationToken ct)
    {
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The reservation owner changed. Roll back and retry the complete workflow.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 or 2601 or 2627 })
        {
            throw new ConcurrencyConflictException("The reservation owner conflicted with another workflow. Roll back the complete transaction.", exception);
        }
    }

    private async Task PostAsync(InventoryPostingKind kind, Guid? warehouseId, Guid productId, int quantity,
        Guid? orderId, Guid? itemId, Guid reservationId, CancellationToken ct)
    {
        if (!warehouseId.HasValue)
            throw new WorkflowConflictException("Warehouse authority requires an explicit persisted origin.");
        var stock = await context.WarehouseStocks.SingleOrDefaultAsync(x => x.WarehouseId == warehouseId && x.ProductId == productId, ct)
            ?? throw new WorkflowConflictException("No stock row exists in the original warehouse. No fallback is permitted.");
        var key = $"workflow:{kind}:{reservationId:N}";
        await new EfInventoryPosting(context, gate, actor).PostAsync(new InventoryPostingRequest(key, kind,
            warehouseId.Value, productId, quantity, stock.RowVersion.ToArray(), orderId, itemId,
            kind == InventoryPostingKind.Reserve ? null : reservationId,
            kind == InventoryPostingKind.Reserve ? reservationId : null), ct);
    }
}
