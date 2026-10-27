using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed class EfInventoryCutover(ERPFlowDbContext context, ICurrentUser actor,
    IInventoryCutoverPermission permission) : IInventoryCutover
{
    public async Task<InventoryCutoverResult> RehearseAsync(InventoryCutoverRequest request, CancellationToken cancellationToken = default)
    {
        permission.EnsureOperatorExecutionAllowed();
        if (request.PreparationId == Guid.Empty || request.DefaultWarehouseId != LegacyDefaultWarehouse.Id ||
            request.ApplicationWriterVersion != InventoryAuthority.SupportedWriterVersion ||
            string.IsNullOrWhiteSpace(request.PhysicalAttributionApproval) || request.PhysicalAttributionApproval.Length > 200 ||
            request.ExpectedFingerprint is null || request.ExpectedFingerprint.Length != 64 ||
            request.ExpectedFingerprint.Any(x => !Uri.IsHexDigit(x)))
            throw new WorkflowValidationException("Cutover requires a valid manifest, explicit default physical attribution approval and compatible application version.");
        context.ChangeTracker.DetectChanges();
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.Entries()
            .Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new WorkflowConflictException("Cutover requires a clean context and exclusive transaction ownership.");
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await InventoryAuthoritySession.EnterAsync(context, true, cancellationToken);
            if (!await context.AppUsers.AsNoTracking().AnyAsync(x => x.Id == actor.UserId && x.Role == UserRole.Administrator, cancellationToken))
                throw new WorkflowAuthorizationException("Only a persisted Administrator may approve cutover rehearsal.");
            var commandHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request));
            var authority = await context.InventoryAuthorities.SingleAsync(cancellationToken);
            await context.Entry(authority).ReloadAsync(cancellationToken);
            if (authority.Mode == InventoryAuthorityMode.Warehouse)
            {
                if (authority.PreparationId != request.PreparationId || authority.ApprovedByUserId != actor.UserId ||
                    !authority.CommandHash!.SequenceEqual(commandHash))
                    throw new WorkflowConflictException("The completed authority transition cannot be replaced or repeated with another approval.");
                await EfInventoryReconciliation.VerifyAsync(context, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Result(authority);
            }
            var batch = await context.LegacyInventoryImportBatches.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == request.PreparationId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(LegacyInventoryImportBatch), request.PreparationId);
            var snapshot = await LegacyInventorySnapshot.ReadAsync(context, cancellationToken);
            var preflight = LegacyInventoryPreflight.Check(snapshot);
            if (!preflight.CanPrepare || !batch.SourceFingerprint.SequenceEqual(snapshot.Fingerprint()) ||
                !batch.SourceFingerprint.SequenceEqual(Convert.FromHexString(request.ExpectedFingerprint)))
                throw new WorkflowConflictException("Frozen source preflight or preparation fingerprint failed. No cutover effects were applied.");
            var openings = await context.LegacyInventoryOpenings.AsNoTracking().Where(x => x.BatchId == batch.Id).ToArrayAsync(cancellationToken);
            var owners = await context.LegacyInventoryReservationImports.AsNoTracking().Where(x => x.BatchId == batch.Id).ToArrayAsync(cancellationToken);
            ValidateManifest(snapshot, batch.Id, openings, owners);
            context.InventoryCutoverInProgress = true;
            await context.Database.ExecuteSqlRawAsync(
                "EXEC sys.sp_set_session_context @key=N'ERPFlow.InventoryCutoverPermit', @value=2;", cancellationToken);
            var stocks = new Dictionary<Guid, WarehouseStock>();
            var effects = new Dictionary<Guid, StockEffect>();
            foreach (var opening in openings.OrderBy(x => x.ProductId))
            {
                var stock = await context.WarehouseStocks.SingleOrDefaultAsync(x => x.Id == opening.TargetWarehouseStockId, cancellationToken);
                if (stock is null)
                {
                    stock = WarehouseStock.ImportTarget(opening.TargetWarehouseStockId, opening.WarehouseId, opening.ProductId);
                    context.WarehouseStocks.Add(stock);
                }
                stocks.Add(opening.ProductId, stock);
                effects.Add(opening.ProductId, stock.ImportOpening(opening.StockOnHand, opening.ReservedQuantity));
            }
            foreach (var owner in owners)
                context.InventoryReservations.Add(InventoryReservation.Import(stocks[owner.ProductId], owner));
            foreach (var line in await context.SalesOrderItems.Where(x => x.WarehouseId == null).ToListAsync(cancellationToken))
                context.Entry(line).Property(x => x.WarehouseId).CurrentValue = LegacyDefaultWarehouse.Id;
            foreach (var line in await context.StockReservations.Where(x => x.WarehouseId == null).ToListAsync(cancellationToken))
                context.Entry(line).Property(x => x.WarehouseId).CurrentValue = LegacyDefaultWarehouse.Id;
            foreach (var line in await context.JobcardItems.Where(x => x.WarehouseId == null).ToListAsync(cancellationToken))
                context.Entry(line).Property(x => x.WarehouseId).CurrentValue = LegacyDefaultWarehouse.Id;
            await context.SaveChangesAsync(cancellationToken);
            foreach (var opening in openings)
            {
                var stock = stocks[opening.ProductId];
                var posting = new InventoryPosting(actor.UserId, $"cutover:{batch.Id:N}:{opening.ProductId:N}",
                    batch.SourceFingerprint, InventoryPostingKind.Opening, null);
                context.InventoryPostings.Add(posting);
                context.StockMovements.Add(new StockMovement(posting, stock, effects[opening.ProductId]));
            }
            await context.SaveChangesAsync(cancellationToken);
            await EfInventoryReconciliation.VerifyAsync(context, cancellationToken, batch.Id);
            authority.Complete(batch.Id, actor.UserId, request.PhysicalAttributionApproval, batch.SourceFingerprint,
                commandHash, stocks.Count, owners.Length);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result(authority);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            context.InventoryCutoverInProgress = false;
            context.ObservedInventoryAuthority = null;
            await transaction.DisposeAsync();
            await context.Database.ExecuteSqlRawAsync(
                "EXEC sys.sp_set_session_context @key=N'ERPFlow.InventoryCutoverPermit', @value=NULL;", CancellationToken.None);
            await context.Database.CloseConnectionAsync();
        }
    }

    private static InventoryCutoverResult Result(InventoryAuthority authority) =>
        new(authority.PreparationId!.Value, authority.ImportedStocks, authority.ImportedReservations, authority.CompletedAt!.Value);

    private static void ValidateManifest(LegacyInventorySnapshot snapshot, Guid batchId,
        LegacyInventoryOpening[] openings, LegacyInventoryReservationImport[] owners)
    {
        var expectedOpenings = snapshot.Products.Select(p => new LegacyInventoryOpening(batchId, p.Id,
            snapshot.StockTargets.SingleOrDefault(x => x.WarehouseId == LegacyDefaultWarehouse.Id && x.ProductId == p.Id)?.Id
                ?? LegacyInventorySnapshot.StableTarget("stock", LegacyDefaultWarehouse.Id, p.Id), p.OnHand, p.Reserved, p.Version)).ToArray();
        var expectedOwners = snapshot.Reservations.Select(r => new LegacyInventoryReservationImport(batchId,
            LegacyReservationSource.SalesReservation, r.Id, r.OrderId, r.ProductId, r.Id, r.Quantity,
            !r.Closed ? LegacyReservationDisposition.Active : r.ClosureKind switch
            {
                1 => LegacyReservationDisposition.Released, 2 => LegacyReservationDisposition.Consumed,
                _ => LegacyReservationDisposition.LegacyClosedUnknown
            }, !r.WarehouseId.HasValue, r.Historical, r.CreatedAt, r.ClosedAt)).Concat(
            snapshot.JobcardLines.Select(p => new LegacyInventoryReservationImport(batchId, LegacyReservationSource.JobcardItem,
                p.Id, p.Id, p.ProductId, LegacyInventorySnapshot.StableTarget("jobcard-reservation", p.Id), p.Quantity,
                snapshot.Jobcards.Single(j => j.Id == p.JobcardId).Status is 0 or 1 or 2
                    ? LegacyReservationDisposition.Active : LegacyReservationDisposition.LegacyClosedUnknown,
                !p.WarehouseId.HasValue, false, null, snapshot.Jobcards.Single(j => j.Id == p.JobcardId).CompletedAt))).ToArray();
        if (JsonSerializer.Serialize(expectedOpenings.OrderBy(x => x.ProductId)) != JsonSerializer.Serialize(openings.OrderBy(x => x.ProductId)) ||
            JsonSerializer.Serialize(expectedOwners.OrderBy(x => x.SourceKind).ThenBy(x => x.SourceId)) !=
            JsonSerializer.Serialize(owners.OrderBy(x => x.SourceKind).ThenBy(x => x.SourceId)))
            throw new WorkflowConflictException("Prepared opening/ownership rows do not match the frozen source. No repair or inflation is permitted.");
    }
}
