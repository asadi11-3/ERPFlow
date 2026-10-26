using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Tests;

internal static class InventoryCutoverTestSupport
{
    public static async Task<InventoryCutoverRequest> PrepareAsync(InventoryTestDatabase database, Guid actor)
    {
        await using var context = database.Context();
        var manifest = await new EfLegacyInventoryImportPreparation(context, new InventoryTestDatabase.TestActor(actor))
            .PrepareAsync(Guid.NewGuid());
        return new(manifest.PreparationId, manifest.SourceFingerprint, LegacyDefaultWarehouse.Id,
            "ISOLATED-TEST physical attribution approval", InventoryAuthority.SupportedWriterVersion);
    }

    public static EfInventoryCutover Cutover(ERPFlowDbContext context, Guid actor) =>
        new(context, new InventoryTestDatabase.TestActor(actor), new IsolatedPermission(context));

    public static EfWorkflowInventory Workflow(ERPFlowDbContext context, Guid actor) =>
        new(context, new InventoryTestDatabase.TestActor(actor), new WarehouseGate(context));

    internal sealed class WarehouseGate(ERPFlowDbContext context) : IInventoryWriteGate
    {
        public void EnsureWarehouseWritesAllowed()
        {
            if (!context.InventoryAuthorities.AsNoTracking().Any(x => x.Mode == InventoryAuthorityMode.Warehouse))
                throw new WorkflowConflictException("Rehearsal warehouse writes require a completed cutover.");
        }
    }

    private sealed class IsolatedPermission(ERPFlowDbContext context) : IInventoryCutoverPermission
    {
        public void EnsureOperatorExecutionAllowed()
        {
            if (!InventoryTestDatabase.Owns(context))
                throw new InvalidOperationException("This permission exists only for isolated fixture databases.");
        }
    }
}
