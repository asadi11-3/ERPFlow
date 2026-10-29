using ERPFlow.Application.Common;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ERPFlow.Infrastructure.Deployment;

/// <summary>Read-only runtime contract check. Schema installation belongs to the operator process.</summary>
public sealed class DatabaseSchemaVerifier(ERPFlowDbContext context)
{
    private static readonly string[] RequiredTriggers =
    [
        "TR_SalesOrders_InventoryWriterProtocol", "TR_SalesOrderItems_InventoryWriterProtocol",
        "TR_StockReservations_InventoryWriterProtocol", "TR_Jobcards_InventoryWriterProtocol",
        "TR_JobcardItems_InventoryWriterProtocol", "TR_WarehouseStocks_InventoryWriterProtocol",
        "TR_Products_FrozenLegacyBalances", "TR_InventoryAuthorities_ExplicitTransition",
        "TR_Suppliers_MasterIdentity", "TR_Warehouses_MasterIdentity", "TR_PurchaseOrders_ImmutableTerms",
        "TR_PurchaseOrderItems_DraftOnly", "TR_PurchaseOrderHistories_AppendOnly",
        "TR_GoodsReceipts_Lifecycle", "TR_GoodsReceiptItems_Immutable", "TR_GoodsReceiptCommands_Immutable",
        "TR_InventoryPostings_GoodsReceipt", "TR_StockMovements_GoodsReceipt",
        "TR_StockTransfers_Lifecycle", "TR_StockTransferItems_Immutable", "TR_StockTransferCommands_Immutable",
        "TR_InventoryPostings_Transfer", "TR_StockMovements_Transfer"
    ];

    public async Task VerifyAsync(CancellationToken ct = default)
    {
        var expected = context.Database.GetMigrations().ToArray();
        var applied = (await context.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        if (expected.Length == 0 || !expected.SequenceEqual(applied, StringComparer.Ordinal)) Fail();

        var columns = (await context.Database.SqlQueryRaw<string>("""
            SELECT SCHEMA_NAME(t.schema_id)+'.'+t.name+'.'+c.name AS Value
            FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
            """).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var checks = (await context.Database.SqlQueryRaw<string>("""
            SELECT SCHEMA_NAME(t.schema_id)+'.'+t.name+'.'+c.name AS Value
            FROM sys.check_constraints c JOIN sys.tables t ON t.object_id=c.parent_object_id
            WHERE c.is_disabled=0 AND c.is_not_trusted=0
            """).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var indexes = (await context.Database.SqlQueryRaw<string>("""
            SELECT SCHEMA_NAME(t.schema_id)+'.'+t.name+'.'+i.name+':'+CONVERT(varchar(1),i.is_unique) AS Value
            FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id WHERE i.is_disabled=0
            """).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var foreignKeys = (await context.Database.SqlQueryRaw<string>("""
            SELECT SCHEMA_NAME(t.schema_id)+'.'+t.name+'.'+f.name AS Value
            FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id=f.parent_object_id
            WHERE f.is_disabled=0 AND f.is_not_trusted=0
            """).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        foreach (var table in context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables)
        {
            var prefix = $"{table.Schema ?? "dbo"}.{table.Name}.";
            if (table.Columns.Any(column => !columns.Contains(prefix + column.Name)) ||
                table.CheckConstraints.Any(check => !checks.Contains(prefix + check.Name)) ||
                table.Indexes.Any(index => !indexes.Contains(prefix + index.Name + (index.IsUnique ? ":1" : ":0"))) ||
                table.ForeignKeyConstraints.Any(key => !foreignKeys.Contains(prefix + key.Name))) Fail();
        }
        var triggers = (await GuardQuery(context).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var procedures = (await context.Database.SqlQueryRaw<string>("""
            SELECT SCHEMA_NAME(schema_id)+'.'+name AS Value FROM sys.procedures
            """).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        if (RequiredTriggers.Any(name => !triggers.Contains(name)) ||
            new[] { "dbo.TransitionPurchaseOrder", "dbo.AdmitGoodsReceipt", "dbo.PostGoodsReceipt" }
                .Any(name => !procedures.Contains(name))) Fail();
    }

    internal static void VerifyWriteGuards(ERPFlowDbContext context)
    {
        var triggers = GuardQuery(context).ToHashSet(StringComparer.Ordinal);
        if (RequiredTriggers.Any(name => !triggers.Contains(name))) Fail();
    }

    private static IQueryable<string> GuardQuery(ERPFlowDbContext context) => context.Database.SqlQueryRaw<string>("""
        SELECT tr.name AS Value FROM sys.triggers tr JOIN sys.tables t ON tr.parent_id=t.object_id
        WHERE tr.parent_class=1 AND tr.is_disabled=0 AND tr.is_not_for_replication=0 AND SCHEMA_NAME(t.schema_id)='dbo'
          AND (tr.name='TR_'+t.name+'_InventoryWriterProtocol' OR tr.name IN (
            'TR_'+t.name+'_FrozenLegacyBalances','TR_'+t.name+'_ExplicitTransition',
            'TR_'+t.name+'_MasterIdentity','TR_'+t.name+'_ImmutableTerms','TR_'+t.name+'_DraftOnly',
            'TR_'+t.name+'_AppendOnly','TR_'+t.name+'_Lifecycle','TR_'+t.name+'_Immutable',
            'TR_'+t.name+'_GoodsReceipt','TR_'+t.name+'_Transfer'))
        """);

    private static void Fail() => throw new WorkflowConflictException(
        "The database schema is incompatible. Apply the reviewed migration release with the operator process.");
}
