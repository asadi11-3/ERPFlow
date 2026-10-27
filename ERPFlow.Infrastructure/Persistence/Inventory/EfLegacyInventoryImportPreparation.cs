using System.Data;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed class EfLegacyInventoryImportPreparation(ERPFlowDbContext context, ICurrentUser currentUser)
    : ILegacyInventoryImportPreparation
{
    public async Task<InventoryImportPreflight> PreflightAsync(Guid? preparationId = null, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken);
        EnsureCleanContext();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await InventoryAuthoritySession.EnterAsync(context, false, cancellationToken);
        if (context.ObservedInventoryAuthority != InventoryAuthorityMode.Product)
            throw new WorkflowConflictException("Legacy preparation cannot run after warehouse authority is active.");
        var snapshot = await LegacyInventorySnapshot.ReadAsync(context, cancellationToken);
        var report = LegacyInventoryPreflight.Check(snapshot);
        if (preparationId.HasValue)
        {
            var prepared = await context.LegacyInventoryImportBatches.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == preparationId.Value, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(LegacyInventoryImportBatch), preparationId.Value);
            if (!prepared.SourceFingerprint.SequenceEqual(snapshot.Fingerprint()))
                report = report with { CanPrepare = false, Issues = report.Issues.Append(
                    new InventoryImportIssue("PreparationStale", prepared.Id, "Legacy inventory has changed; do not apply this preparation.")).ToArray() };
        }
        await transaction.CommitAsync(cancellationToken);
        return report;
    }

    public async Task<InventoryImportPreparationResult> PrepareAsync(Guid preparationId, CancellationToken cancellationToken = default)
    {
        if (preparationId == Guid.Empty) throw new WorkflowValidationException("A preparation ID is required.");
        await EnsureAdministratorAsync(cancellationToken);
        EnsureCleanContext();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await InventoryAuthoritySession.EnterAsync(context, false, cancellationToken);
        if (context.ObservedInventoryAuthority != InventoryAuthorityMode.Product)
            throw new WorkflowConflictException("Legacy preparation cannot run after warehouse authority is active.");
        var existing = await context.LegacyInventoryImportBatches.FromSqlInterpolated(
            $"SELECT * FROM LegacyInventoryImportBatches WITH (UPDLOCK, HOLDLOCK) WHERE Id={preparationId}")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var snapshot = await LegacyInventorySnapshot.ReadAsync(context, cancellationToken);
        var report = LegacyInventoryPreflight.Check(snapshot);
        if (!report.CanPrepare)
            throw new WorkflowConflictException("Import preflight failed: " + string.Join(", ", report.Issues.Select(x => x.Code).Distinct()));
        var fingerprint = snapshot.Fingerprint();
        if (existing is not null)
        {
            if (!existing.SourceFingerprint.SequenceEqual(fingerprint))
                throw new WorkflowConflictException("This preparation ID belongs to a different source snapshot. The existing preparation was preserved.");
            var replay = await ResultAsync(existing, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var batch = new LegacyInventoryImportBatch(preparationId, currentUser.UserId, fingerprint);
        var openings = snapshot.Products.Select(product => new LegacyInventoryOpening(batch.Id, product.Id,
            snapshot.StockTargets.SingleOrDefault(x => x.WarehouseId == LegacyDefaultWarehouse.Id && x.ProductId == product.Id)?.Id
                ?? LegacyInventorySnapshot.StableTarget("stock", LegacyDefaultWarehouse.Id, product.Id),
            product.OnHand, product.Reserved, product.Version)).ToArray();
        var sales = snapshot.Reservations.Select(r => new LegacyInventoryReservationImport(batch.Id,
            LegacyReservationSource.SalesReservation, r.Id, r.OrderId, r.ProductId, r.Id, r.Quantity,
            !r.Closed ? LegacyReservationDisposition.Active : r.ClosureKind switch
            {
                1 => LegacyReservationDisposition.Released,
                2 => LegacyReservationDisposition.Consumed,
                _ => LegacyReservationDisposition.LegacyClosedUnknown
            }, !r.WarehouseId.HasValue, r.Historical, r.CreatedAt, r.ClosedAt));
        var jobcards = snapshot.Jobcards.ToDictionary(x => x.Id);
        var parts = snapshot.JobcardLines.Select(part => new LegacyInventoryReservationImport(batch.Id,
            LegacyReservationSource.JobcardItem, part.Id, part.Id, part.ProductId,
            LegacyInventorySnapshot.StableTarget("jobcard-reservation", part.Id), part.Quantity,
            jobcards[part.JobcardId].Status is 0 or 1 or 2 ? LegacyReservationDisposition.Active : LegacyReservationDisposition.LegacyClosedUnknown,
            !part.WarehouseId.HasValue, false, null, jobcards[part.JobcardId].CompletedAt));
        context.ImportPreparationInProgress = true;
        try
        {
            context.LegacyInventoryImportBatches.Add(batch);
            context.LegacyInventoryOpenings.AddRange(openings);
            context.LegacyInventoryReservationImports.AddRange(sales.Concat(parts));
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            context.ImportPreparationInProgress = false;
        }
        var result = await ResultAsync(batch, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task EnsureAdministratorAsync(CancellationToken ct)
    {
        if (currentUser.UserId == Guid.Empty || !await context.AppUsers.AsNoTracking()
            .AnyAsync(x => x.Id == currentUser.UserId && x.Role == UserRole.Administrator, ct))
            throw new WorkflowAuthorizationException("Only a persisted Administrator can prepare legacy inventory imports.");
    }

    private void EnsureCleanContext()
    {
        context.ChangeTracker.DetectChanges();
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.Entries()
            .Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new WorkflowConflictException("Preparation requires its own transaction and a clean DbContext.");
    }

    private async Task<InventoryImportPreparationResult> ResultAsync(LegacyInventoryImportBatch batch, CancellationToken ct) =>
        new(batch.Id, Convert.ToHexString(batch.SourceFingerprint),
            await context.LegacyInventoryOpenings.CountAsync(x => x.BatchId == batch.Id, ct),
            await context.LegacyInventoryReservationImports.CountAsync(x => x.BatchId == batch.Id && x.SourceKind == LegacyReservationSource.SalesReservation, ct),
            await context.LegacyInventoryReservationImports.CountAsync(x => x.BatchId == batch.Id && x.SourceKind == LegacyReservationSource.JobcardItem, ct));
}
