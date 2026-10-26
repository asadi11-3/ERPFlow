using System.Security.Cryptography;
using System.Text.Json;
using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed partial class EfInventoryPosting
{
    public async Task<InventoryPostingResult> PostReceiptAsync(InventoryReceiptRequest request, CancellationToken cancellationToken = default)
    {
        writeGate.EnsureWarehouseWritesAllowed();
        ArgumentNullException.ThrowIfNull(request);
        if (context.Database.CurrentTransaction is null || context.ObservedInventoryAuthority != InventoryAuthorityMode.Warehouse || !context.GoodsReceiptWriteInProgress)
            throw new WorkflowConflictException("Receiving needs a completed-cutover GRN workflow transaction.");
        if (request.GoodsReceiptItemId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128 || request.IdempotencyKey.Any(char.IsWhiteSpace))
            throw new WorkflowValidationException("Receipt posting identity/key is invalid.");
        var actor = await context.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == currentUser.UserId, cancellationToken);
        if (actor is null || actor.Role is not (UserRole.Administrator or UserRole.WarehouseOperator))
            throw new WorkflowAuthorizationException("Receiving stock requires a persisted WarehouseOperator or Administrator.");
        var item = await context.GoodsReceiptItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.GoodsReceiptItemId, cancellationToken)
            ?? throw new WorkflowConflictException("The original GRN item does not exist.");
        var header = await context.GoodsReceipts.AsNoTracking().SingleAsync(x => x.Id == item.GoodsReceiptId, cancellationToken);
        if (header.Status != GoodsReceiptStatus.Draft || !await context.PurchaseOrders.AsNoTracking().AnyAsync(x => x.Id == header.PurchaseOrderId &&
            (x.Status == PurchaseOrderStatus.Approved || x.Status == PurchaseOrderStatus.PartiallyReceived) && x.WarehouseId == header.WarehouseId, cancellationToken))
            throw new WorkflowConflictException("Only Draft receipts against an approved/partially received exact-source PO can post.");
        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Version = "grn-stock-v1", Item = item.Id, header.WarehouseId, item.ProductId, item.Quantity }));
        var replay = await ReplayAsync(actor.Id, request.IdempotencyKey, hash, cancellationToken);
        if (replay is not null) return replay;
        if (!await context.Warehouses.AsNoTracking().AnyAsync(x => x.Id == header.WarehouseId && x.IsActive, cancellationToken))
            throw new WorkflowConflictException("The receipt destination warehouse is inactive.");
        var resource = $"ERPFlow.Inventory.Stock.{header.WarehouseId:N}.{item.ProductId:N}";
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=30000;
            IF @result<0 THROW 51000, 'Receipt stock lock could not be acquired.', 1;
            """, cancellationToken);
        var stock = await context.WarehouseStocks.FromSqlInterpolated($"""
            SELECT * FROM WarehouseStocks WITH (UPDLOCK,HOLDLOCK) WHERE WarehouseId={header.WarehouseId} AND ProductId={item.ProductId}
            """).SingleOrDefaultAsync(cancellationToken);
        context.InventoryPostingInProgress = true;
        try
        {
            if (stock is null)
            {
                stock = new WarehouseStock(header.WarehouseId, item.ProductId);
                context.WarehouseStocks.Add(stock);
            }
            StockEffect effect;
            try { effect = stock.Apply(InventoryPostingKind.Receipt, item.Quantity); }
            catch (OverflowException) { throw new WorkflowValidationException("Receipt stock arithmetic exceeds Int32 capacity."); }
            await context.SaveChangesAsync(cancellationToken);
            var envelope = new InventoryPosting(actor.Id, request.IdempotencyKey, hash, InventoryPostingKind.Receipt, null, item.Id);
            var movement = new StockMovement(envelope, stock, effect);
            context.InventoryPostings.Add(envelope);
            context.StockMovements.Add(movement);
            await context.SaveChangesAsync(cancellationToken);
            return Result(envelope, movement);
        }
        catch (Exception exception) when (IsConflict(exception))
        { throw new ConcurrencyConflictException("Receipt stock conflicted; roll back the entire GRN and retry.", exception); }
        finally { context.InventoryPostingInProgress = false; }
    }
}
