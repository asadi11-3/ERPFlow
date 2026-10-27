using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Api;

public sealed class DevelopmentInventory(ERPFlowDbContext context, IHostEnvironment environment)
    : IInventoryWriteGate, IInventoryCutoverPermission
{
    public void EnsureOperatorExecutionAllowed()
    {
        if (!environment.IsDevelopment())
            throw new WorkflowConflictException("Warehouse activation is available only in Development.");
    }

    public void EnsureWarehouseWritesAllowed()
    {
        EnsureOperatorExecutionAllowed();
        if (!context.InventoryAuthorities.AsNoTracking().Any(a => a.Mode == InventoryAuthorityMode.Warehouse))
            throw new WorkflowConflictException("Activate development warehouse inventory before posting stock.");
    }

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        EnsureOperatorExecutionAllowed();
        if (await context.InventoryAuthorities.AsNoTracking().AnyAsync(a => a.Mode == InventoryAuthorityMode.Warehouse, ct))
        {
            await new EfInventoryReconciliation(context).ReconcileAsync(ct);
            return;
        }
        var administrator = await context.AppUsers.AsNoTracking()
            .Where(u => u.Role == UserRole.Administrator).OrderBy(u => u.Id).Select(u => u.Id).FirstAsync(ct);
        var actor = new Operator(administrator);
        var preparation = await new EfLegacyInventoryImportPreparation(context, actor).PrepareAsync(Guid.NewGuid(), ct);
        await new EfInventoryCutover(context, actor, this).RehearseAsync(new InventoryCutoverRequest(
            preparation.PreparationId, preparation.SourceFingerprint, LegacyDefaultWarehouse.Id,
            "Development database activation approved by local operator command.", InventoryAuthority.SupportedWriterVersion), ct);
    }

    private sealed record Operator(Guid UserId) : ICurrentUser;
}
