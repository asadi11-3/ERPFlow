using ERPFlow.Application.Common;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Deployment;

/// <summary>A process-local admission established only after fenced schema, provenance and ledger verification.</summary>
public sealed class ProductionInventoryAdmission
{
    private Admission? admitted;

    public async Task InitializeAsync(ERPFlowDbContext context, CancellationToken ct = default, Action? schemaVerified = null)
    {
        Volatile.Write(ref admitted, null);
        await new DatabaseSchemaVerifier(context).VerifyAsync(ct);
        schemaVerified?.Invoke();
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            await InventoryAuthoritySession.EnterAsync(context, true, ct);
            var authority = await context.InventoryAuthorities.AsNoTracking().SingleAsync(ct);
            if (authority.Mode == InventoryAuthorityMode.Warehouse)
            {
                ValidateAuthority(authority);
                var batch = await context.LegacyInventoryImportBatches.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == authority.PreparationId, ct);
                if (batch is null || !batch.SourceFingerprint.SequenceEqual(authority.SourceFingerprint!) ||
                    !await context.Warehouses.AnyAsync(x => x.Id == LegacyDefaultWarehouse.Id && x.IsActive, ct) ||
                    await context.LegacyInventoryOpenings.CountAsync(x => x.BatchId == batch.Id, ct) != authority.ImportedStocks ||
                    await context.LegacyInventoryReservationImports.CountAsync(x => x.BatchId == batch.Id, ct) != authority.ImportedReservations)
                    throw Refused();
                await new EfInventoryReconciliation(context).ReconcileAsync(ct);
                await transaction.CommitAsync(ct);
                Volatile.Write(ref admitted, new Admission(DatabaseIdentity(context), authority.RowVersion.ToArray()));
            }
            else if (authority.Mode == InventoryAuthorityMode.Product)
                await transaction.CommitAsync(ct);
            else throw Refused();
        }
        finally { context.ObservedInventoryAuthority = null; }
    }

    public void EnsureAllowed(ERPFlowDbContext context)
    {
        var proof = Volatile.Read(ref admitted);
        if (proof is null || proof.Database != DatabaseIdentity(context)) throw Refused();
        var authority = context.InventoryAuthorities.AsNoTracking().SingleOrDefault();
        if (authority is null) throw Refused();
        ValidateAuthority(authority);
        if (!authority.RowVersion.SequenceEqual(proof.Version) ||
            context.Database.CurrentTransaction is not null && context.ObservedInventoryAuthority != InventoryAuthorityMode.Warehouse)
            throw Refused();
        DatabaseSchemaVerifier.VerifyWriteGuards(context);
    }

    public async Task<bool> IsReadyAsync(ERPFlowDbContext context, CancellationToken ct)
    {
        var proof = Volatile.Read(ref admitted);
        if (proof is null || proof.Database != DatabaseIdentity(context)) return false;
        var authority = await context.InventoryAuthorities.AsNoTracking().SingleOrDefaultAsync(ct);
        return authority is not null && authority.Mode == InventoryAuthorityMode.Warehouse &&
            authority.MinimumWriterVersion == InventoryAuthority.SupportedWriterVersion && authority.RowVersion.SequenceEqual(proof.Version);
    }

    private static void ValidateAuthority(InventoryAuthority authority)
    {
        if (authority.Id != 1 || authority.Mode != InventoryAuthorityMode.Warehouse ||
            authority.MinimumWriterVersion != InventoryAuthority.SupportedWriterVersion ||
            authority.PreparationId is null || authority.PreparationId == Guid.Empty ||
            authority.ApprovedByUserId is null || authority.ApprovedByUserId == Guid.Empty ||
            authority.DefaultWarehouseId != LegacyDefaultWarehouse.Id || authority.CompletedAt is null ||
            string.IsNullOrWhiteSpace(authority.PhysicalAttributionApproval) ||
            authority.SourceFingerprint is not { Length: 32 } || authority.CommandHash is not { Length: 32 } ||
            authority.RowVersion.Length != 8 || authority.ImportedStocks < 0 || authority.ImportedReservations < 0)
            throw Refused();
    }

    private static string DatabaseIdentity(ERPFlowDbContext context)
    {
        var connection = new SqlConnectionStringBuilder(context.Database.GetConnectionString());
        return $"{connection.DataSource.Trim().ToUpperInvariant()}|{connection.InitialCatalog.Trim().ToUpperInvariant()}";
    }
    private static WorkflowConflictException Refused() => new(
        "Production warehouse writes require verified schema, completed authority and reconciled inventory. Complete the controlled operator procedure and restart the application.");
    private sealed record Admission(string Database, byte[] Version);
}
