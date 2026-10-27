using System.Security.Cryptography;
using System.Text.Json;
using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Transfers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed partial class EfInventoryPosting
{
    public async Task<InventoryTransferResult> PostTransferAsync(InventoryTransferRequest request, CancellationToken cancellationToken = default)
    {
        writeGate.EnsureWarehouseWritesAllowed();
        ArgumentNullException.ThrowIfNull(request);
        if (context.Database.CurrentTransaction is null || context.ObservedInventoryAuthority != InventoryAuthorityMode.Warehouse || !context.StockTransferWriteInProgress)
            throw new WorkflowConflictException("Transfer posting requires a completed-cutover document transaction.");
        var actor = await context.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == currentUser.UserId, cancellationToken);
        if (actor is null || actor.Role is not (UserRole.Administrator or UserRole.WarehouseOperator))
            throw new WorkflowAuthorizationException("A persisted WarehouseOperator or Administrator is required.");
        var transfer = await context.StockTransfers.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == request.StockTransferId, cancellationToken)
            ?? throw new WorkflowConflictException("The original transfer does not exist.");
        if (transfer.Status != StockTransferStatus.Draft || transfer.SourceWarehouseId == transfer.DestinationWarehouseId || transfer.Items.Count == 0)
            throw new WorkflowConflictException("Only a distinct-warehouse Draft transfer can post.");
        var keys = transfer.Items.SelectMany(x => new[] { (WarehouseId: transfer.SourceWarehouseId, x.ProductId),
            (WarehouseId: transfer.DestinationWarehouseId, x.ProductId) }).ToArray();
        await InventoryStockLocks.AcquireAsync(context, keys, cancellationToken);
        if (await context.Warehouses.AsNoTracking().CountAsync(x => (x.Id == transfer.SourceWarehouseId || x.Id == transfer.DestinationWarehouseId) && x.IsActive, cancellationToken) != 2)
            throw new WorkflowConflictException("Both exact transfer warehouses must be active.");
        var stocks = new Dictionary<string, WarehouseStock>();
        var additions = new List<WarehouseStock>();
        foreach (var key in keys.OrderBy(x => InventoryStockLocks.Key(x.WarehouseId, x.ProductId), StringComparer.Ordinal))
        {
            var stock = await context.WarehouseStocks.FromSqlInterpolated($"""
                SELECT * FROM WarehouseStocks WITH (UPDLOCK,HOLDLOCK) WHERE WarehouseId={key.WarehouseId} AND ProductId={key.ProductId}
                """).SingleOrDefaultAsync(cancellationToken);
            if (stock is null)
            {
                if (key.WarehouseId == transfer.SourceWarehouseId) throw new WorkflowConflictException("The exact source stock row does not exist.");
                stock = new WarehouseStock(key.WarehouseId, key.ProductId);
                additions.Add(stock);
            }
            stocks.Add(InventoryStockLocks.Key(key.WarehouseId, key.ProductId), stock);
        }
        foreach (var item in transfer.Items)
        {
            var source = stocks[InventoryStockLocks.Key(transfer.SourceWarehouseId, item.ProductId)];
            var destination = stocks[InventoryStockLocks.Key(transfer.DestinationWarehouseId, item.ProductId)];
            if (source.AvailableStock < item.Quantity) throw new WorkflowConflictException("Insufficient unreserved stock in the exact source warehouse.");
            if ((long)destination.StockOnHand + item.Quantity > int.MaxValue) throw new WorkflowConflictException("Destination stock capacity would overflow.");
        }
        context.InventoryPostingInProgress = true;
        try
        {
            context.WarehouseStocks.AddRange(additions);
            var effects = new Dictionary<Guid, (StockEffect Out, StockEffect In)>();
            foreach (var item in transfer.Items)
            {
                var source = stocks[InventoryStockLocks.Key(transfer.SourceWarehouseId, item.ProductId)];
                var destination = stocks[InventoryStockLocks.Key(transfer.DestinationWarehouseId, item.ProductId)];
                effects.Add(item.Id, (source.Apply(InventoryPostingKind.TransferOut, item.Quantity), destination.Apply(InventoryPostingKind.TransferIn, item.Quantity)));
            }
            await context.SaveChangesAsync(cancellationToken);
            var result = new List<InventoryTransferItemResult>();
            foreach (var item in transfer.Items.OrderBy(x => x.ProductId))
            {
                var source = stocks[InventoryStockLocks.Key(transfer.SourceWarehouseId, item.ProductId)];
                var destination = stocks[InventoryStockLocks.Key(transfer.DestinationWarehouseId, item.ProductId)];
                var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Version = "transfer-v1", Transfer = transfer.Id, Item = item.Id,
                    transfer.SourceWarehouseId, transfer.DestinationWarehouseId, item.ProductId, item.Quantity }));
                var outgoing = new InventoryPosting(actor.Id, $"transfer:{transfer.Id:N}:{item.Id:N}:out", hash, InventoryPostingKind.TransferOut, null, null, item.Id);
                var incoming = new InventoryPosting(actor.Id, $"transfer:{transfer.Id:N}:{item.Id:N}:in", hash, InventoryPostingKind.TransferIn, null, null, item.Id);
                var outMovement = new StockMovement(outgoing, source, effects[item.Id].Out);
                var inMovement = new StockMovement(incoming, destination, effects[item.Id].In);
                context.InventoryPostings.AddRange(outgoing, incoming);
                context.StockMovements.AddRange(outMovement, inMovement);
                await context.SaveChangesAsync(cancellationToken);
                result.Add(new(item.Id, Result(outgoing, outMovement), Result(incoming, inMovement)));
            }
            return new(result);
        }
        catch (Exception exception) when (IsConflict(exception))
        { throw new ConcurrencyConflictException("Transfer stock conflicted. Roll back the complete document.", exception); }
        catch (OverflowException)
        { throw new WorkflowConflictException("Transfer stock or sequence capacity would overflow."); }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51000)
        { throw new ConcurrencyConflictException("Transfer stock lock conflicted. Roll back the complete document.", exception); }
        finally { context.InventoryPostingInProgress = false; }
    }
}
