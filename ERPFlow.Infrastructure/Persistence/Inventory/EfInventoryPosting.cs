using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed class EfInventoryPosting(
    ERPFlowDbContext context,
    IInventoryWriteGate writeGate,
    ICurrentUser currentUser) : IInventoryPosting
{
    public async Task<InventoryPostingResult> PostAsync(InventoryPostingRequest request, CancellationToken cancellationToken = default)
    {
        writeGate.EnsureWarehouseWritesAllowed();
        Validate(request);
        context.ChangeTracker.DetectChanges();
        if (context.ChangeTracker.Entries().Any(entry =>
            (entry.Entity is Warehouse or WarehouseStock or InventoryReservation or InventoryPosting or StockMovement &&
             entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) ||
            (entry.Entity is Product && entry.State == EntityState.Modified &&
             (entry.Property(nameof(Product.StockOnHand)).IsModified || entry.Property(nameof(Product.ReservedQuantity)).IsModified))))
            throw new WorkflowConflictException("Inventory must be clean before posting. Roll back and discard failed contexts.");
        var actorId = currentUser.UserId;
        if (actorId == Guid.Empty || !await context.AppUsers.AsNoTracking().AnyAsync(x => x.Id == actorId, cancellationToken))
            throw new WorkflowAuthorizationException("A persisted current actor is required for inventory posting.");
        var hash = Hash(request);

        if (context.Database.CurrentTransaction is not null)
            return await ExecuteAsync(request, actorId, hash, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await ExecuteAsync(request, actorId, hash, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            context.ChangeTracker.Clear();
            var replay = await ReplayAsync(actorId, request.IdempotencyKey, hash, cancellationToken);
            return replay ?? throw new ConcurrencyConflictException("The inventory posting conflicted. Reload and retry the complete operation.", exception);
        }
    }

    private async Task<InventoryPostingResult> ExecuteAsync(
        InventoryPostingRequest request, Guid actorId, byte[] hash, CancellationToken ct)
    {
        var replay = await ReplayAsync(actorId, request.IdempotencyKey, hash, ct);
        if (replay is not null) return replay;
        if (!await context.Warehouses.AsNoTracking().AnyAsync(x => x.Id == request.WarehouseId && x.IsActive, ct))
            throw new WorkflowConflictException("The selected warehouse does not exist or is inactive.");
        var stock = await context.WarehouseStocks.SingleOrDefaultAsync(
            x => x.WarehouseId == request.WarehouseId && x.ProductId == request.ProductId, ct)
            ?? throw new WorkflowConflictException("Stock must be explicitly provisioned in the selected warehouse. No fallback is permitted.");
        if (!stock.RowVersion.SequenceEqual(request.ExpectedRowVersion))
            throw new ConcurrencyConflictException("The selected warehouse stock version is stale.");

        InventoryReservation? reservation = null;
        if (request.Kind == InventoryPostingKind.Reserve)
        {
            await ValidateOwnerAsync(request, ct);
            reservation = new InventoryReservation(stock, request.Quantity, request.SalesOrderId, request.JobcardItemId);
        }
        else if (request.Kind is InventoryPostingKind.Release or InventoryPostingKind.Consume)
        {
            reservation = await context.InventoryReservations.SingleOrDefaultAsync(x => x.Id == request.ReservationId, ct)
                ?? throw new WorkflowConflictException("The inventory reservation does not exist.");
            if (reservation.WarehouseStockId != stock.Id || reservation.ProductId != request.ProductId ||
                reservation.Quantity != request.Quantity || reservation.SalesOrderId != request.SalesOrderId ||
                reservation.JobcardItemId != request.JobcardItemId)
                throw new WorkflowConflictException("The reservation must match the exact owner, warehouse, product and quantity.");
            if (reservation.Status != InventoryReservationStatus.Active)
                throw new WorkflowConflictException("The inventory reservation is already closed.");
        }

        StockEffect effect;
        try
        {
            effect = stock.Apply(request.Kind, request.Quantity);
            if (request.Kind is InventoryPostingKind.Release or InventoryPostingKind.Consume)
                reservation!.Close(request.Kind);
        }
        catch (OverflowException)
        {
            throw new WorkflowValidationException("Inventory quantity arithmetic exceeds the supported range.");
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkflowConflictException(exception.Message, exception);
        }

        context.InventoryPostingInProgress = true;
        try
        {
            if (request.Kind == InventoryPostingKind.Reserve) context.InventoryReservations.Add(reservation!);
            await context.SaveChangesAsync(ct);
            var posting = new InventoryPosting(actorId, request.IdempotencyKey, hash, request.Kind, reservation?.Id);
            var movement = new StockMovement(posting, stock, effect);
            context.InventoryPostings.Add(posting);
            context.StockMovements.Add(movement);
            await context.SaveChangesAsync(ct);
            return Result(posting, movement);
        }
        catch (Exception exception) when (IsConflict(exception) && context.Database.CurrentTransaction is not null)
        {
            throw new ConcurrencyConflictException("Inventory concurrency conflict; roll back the complete transaction before retrying.", exception);
        }
        finally
        {
            context.InventoryPostingInProgress = false;
        }
    }

    private async Task ValidateOwnerAsync(InventoryPostingRequest request, CancellationToken ct)
    {
        var matches = request.SalesOrderId.HasValue
            ? await context.SalesOrderItems.AsNoTracking().AnyAsync(x => x.SalesOrderId == request.SalesOrderId &&
                x.ProductId == request.ProductId && x.Quantity == request.Quantity, ct)
            : await context.JobcardItems.AsNoTracking().AnyAsync(x => x.Id == request.JobcardItemId &&
                x.ProductId == request.ProductId && x.Quantity == request.Quantity, ct);
        if (!matches) throw new WorkflowConflictException("The reservation must match a persisted owner line.");
    }

    private async Task<InventoryPostingResult?> ReplayAsync(Guid actorId, string key, byte[] hash, CancellationToken ct)
    {
        var posting = await context.InventoryPostings.AsNoTracking().SingleOrDefaultAsync(
            x => x.ActorId == actorId && x.IdempotencyKey == key, ct);
        if (posting is null) return null;
        if (!posting.RequestHash.SequenceEqual(hash))
            throw new WorkflowConflictException("The idempotency key was already used for a different inventory request.");
        var movement = await context.StockMovements.AsNoTracking().SingleAsync(x => x.PostingId == posting.Id, ct);
        return Result(posting, movement);
    }

    private static InventoryPostingResult Result(InventoryPosting posting, StockMovement movement) =>
        new(posting.Id, movement.Id, posting.ReservationId, movement.AfterOnHand,
            movement.AfterReserved, movement.ResultRowVersion.ToArray());

    private static void Validate(InventoryPostingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Kind) || request.WarehouseId == Guid.Empty || request.ProductId == Guid.Empty ||
            request.Quantity < 0 || (request.Quantity == 0 && request.Kind != InventoryPostingKind.Opening) ||
            request.ExpectedRowVersion is not { Length: 8 } || string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 128 || request.IdempotencyKey.Any(char.IsWhiteSpace))
            throw new WorkflowValidationException("Inventory posting requires valid IDs, bounded key, quantity and stock row version.");
        var hasOwner = request.SalesOrderId.HasValue != request.JobcardItemId.HasValue;
        var closing = request.Kind is InventoryPostingKind.Release or InventoryPostingKind.Consume;
        if (request.SalesOrderId == Guid.Empty || request.JobcardItemId == Guid.Empty || request.ReservationId == Guid.Empty ||
            (request.Kind == InventoryPostingKind.Opening && (hasOwner || request.SalesOrderId.HasValue || request.ReservationId.HasValue)) ||
            (request.Kind != InventoryPostingKind.Opening && !hasOwner) || closing != request.ReservationId.HasValue)
            throw new WorkflowValidationException("Inventory posting ownership or reservation identity is invalid.");
    }

    private static byte[] Hash(InventoryPostingRequest request) => SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
        "inventory-v1", ((int)request.Kind).ToString(CultureInfo.InvariantCulture), request.WarehouseId.ToString("N"),
        request.ProductId.ToString("N"), request.Quantity.ToString(CultureInfo.InvariantCulture),
        Convert.ToBase64String(request.ExpectedRowVersion), request.SalesOrderId?.ToString("N") ?? "-",
        request.JobcardItemId?.ToString("N") ?? "-", request.ReservationId?.ToString("N") ?? "-")));

    private static bool IsConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException || current is SqlException { Number: 2601 or 2627 or 1205 }) return true;
        return false;
    }
}
