using Microsoft.EntityFrameworkCore.Migrations;

namespace ERPFlow.Infrastructure.Persistence;

internal static partial class ReceivingSqlGuards
{
    public static void Create(MigrationBuilder migration)
    {
        Documents(migration);
        Ledger(migration);
        PurchaseOrderProtocol(migration);
        Procedures(migration);
    }
    private static void Documents(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_GoodsReceipts_Lifecycle ON GoodsReceipts AFTER INSERT,UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL OR d.Status<>0)
                    THROW 51020, 'Receipt history cannot be replaced, deleted or reversed.', 1;
                IF NOT EXISTS(SELECT 1 FROM InventoryAuthorities WHERE Id=1 AND Mode=1)
                    OR APPLOCK_MODE(N'public',N'ERPFlow.Inventory.Authority.v2',N'Transaction') NOT IN (N'Shared',N'Exclusive')
                    THROW 51020, 'Receipts require completed cutover and the workflow fence.', 1;
                IF EXISTS(SELECT 1 FROM inserted i WHERE i.Id<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN')))
                    OR TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN')) IS NULL
                    THROW 51020, 'Receipt workflow scope is required.', 1;
                IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN AppUsers a ON a.Id=COALESCE(i.PostedByUserId,i.CancelledByUserId,i.CreatedByUserId)
                    WHERE a.Role NOT IN (0,5) OR a.Id IS NULL)
                    THROW 51020, 'Receipt actor is not authorized.', 1;
                IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id JOIN PurchaseOrders p ON p.Id=i.PurchaseOrderId
                    WHERE i.SupplierId<>p.SupplierId OR i.WarehouseId<>p.WarehouseId OR (d.Id IS NULL AND (i.Status<>0 OR p.Status NOT IN (2,5))))
                    THROW 51020, 'Only Draft GRNs against open approved POs may be created.', 1;
                IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE i.Status NOT IN (1,2)
                    OR i.Number<>d.Number OR i.PurchaseOrderId<>d.PurchaseOrderId OR i.WarehouseId<>d.WarehouseId
                    OR i.SupplierId<>d.SupplierId OR i.CreatedByUserId<>d.CreatedByUserId OR i.CreatedAt<>d.CreatedAt
                    OR ISNULL(i.SupplierDeliveryReference,N'')<>ISNULL(d.SupplierDeliveryReference,N'')
                    OR ISNULL(i.Notes,N'')<>ISNULL(d.Notes,N''))
                    THROW 51020, 'Receipt identity and lines are immutable.', 1;
                IF EXISTS(SELECT 1 FROM inserted i WHERE i.Status=2 AND EXISTS(
                    SELECT 1 FROM GoodsReceiptItems l JOIN InventoryPostings p ON p.GoodsReceiptItemId=l.Id WHERE l.GoodsReceiptId=i.Id))
                    THROW 51020, 'A receipt with posted inventory cannot be cancelled.', 1;
                IF EXISTS(SELECT 1 FROM inserted i WHERE i.Status=1 AND
                    (NOT EXISTS(SELECT 1 FROM GoodsReceiptItems WHERE GoodsReceiptId=i.Id) OR EXISTS(
                    SELECT 1 FROM GoodsReceiptItems l WHERE l.GoodsReceiptId=i.Id AND NOT EXISTS(
                        SELECT 1 FROM InventoryPostings p JOIN StockMovements m ON m.PostingId=p.Id
                        JOIN WarehouseStocks s ON s.Id=m.WarehouseStockId
                        WHERE p.GoodsReceiptItemId=l.Id AND p.Kind=4 AND m.Kind=4 AND m.PhysicalDelta=l.Quantity
                            AND m.ReservedDelta=0 AND s.WarehouseId=i.WarehouseId AND s.ProductId=l.ProductId))))
                    THROW 51020, 'A Posted GRN needs every exact physical receipt movement.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_GoodsReceiptItems_Immutable ON GoodsReceiptItems AFTER INSERT,UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted) THROW 51020, 'GRN lines are immutable.', 1;
                IF NOT EXISTS(SELECT 1 FROM InventoryAuthorities WHERE Id=1 AND Mode=1)
                    OR TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN')) IS NULL
                    THROW 51020, 'GRN line creation requires warehouse authority and workflow scope.', 1;
                IF EXISTS(SELECT 1 FROM inserted l JOIN GoodsReceipts g ON g.Id=l.GoodsReceiptId
                    JOIN PurchaseOrderItems p ON p.Id=l.PurchaseOrderItemId
                    WHERE g.Status<>0 OR g.Id<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN'))
                        OR CONVERT(bigint,l.Quantity)+p.ReceivedQuantity>p.Quantity)
                    THROW 51020, 'Invalid or excessive GRN draft quantity.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_GoodsReceiptCommands_Immutable ON GoodsReceiptCommands AFTER INSERT,UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted) THROW 51020, 'GRN command results are immutable.', 1;
                IF TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN')) IS NULL OR EXISTS(
                    SELECT 1 FROM inserted c JOIN GoodsReceipts g ON g.Id=c.GoodsReceiptId JOIN AppUsers a ON a.Id=c.ActorId
                    WHERE c.GoodsReceiptId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN')) OR a.Role NOT IN (0,5)
                        OR (c.Operation=0 AND g.Status<>0) OR (c.Operation=1 AND g.Status<>1)
                        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(c.ResponseJson,'$.Id'))<>c.GoodsReceiptId)
                    THROW 51020, 'GRN replay result must match its committed workflow.', 1;
            END;
            """);
    }
    private static void Ledger(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_InventoryPostings_GoodsReceipt ON InventoryPostings AFTER INSERT,UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted WHERE Kind=4) OR EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE i.Kind=4)
                    THROW 51020, 'Receipt postings are immutable.', 1;
                IF EXISTS(SELECT 1 FROM inserted WHERE Kind=4)
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM InventoryAuthorities WHERE Id=1 AND Mode=1)
                        OR APPLOCK_MODE(N'public',N'ERPFlow.Inventory.Authority.v2',N'Transaction') NOT IN (N'Shared',N'Exclusive')
                        THROW 51020, 'Receipt posting requires warehouse authority and the transaction fence.', 1;
                    IF TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN')) IS NULL OR EXISTS(
                        SELECT 1 FROM inserted p JOIN GoodsReceiptItems l ON l.Id=p.GoodsReceiptItemId
                        JOIN GoodsReceipts g ON g.Id=l.GoodsReceiptId JOIN AppUsers a ON a.Id=p.ActorId
                        WHERE p.Kind=4 AND (g.Status<>0 OR g.Id<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN')) OR a.Role NOT IN (0,5)))
                        THROW 51020, 'Receipt posting owner or actor is invalid.', 1;
                END;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_StockMovements_GoodsReceipt ON StockMovements AFTER INSERT,UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted WHERE Kind=4) OR EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE i.Kind=4)
                    THROW 51020, 'Receipt movements are immutable.', 1;
                IF EXISTS(SELECT 1 FROM inserted WHERE Kind=4) AND
                    (TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN')) IS NULL OR EXISTS(
                        SELECT 1 FROM inserted m JOIN InventoryPostings p ON p.Id=m.PostingId
                        JOIN GoodsReceiptItems l ON l.Id=p.GoodsReceiptItemId JOIN GoodsReceipts g ON g.Id=l.GoodsReceiptId
                        JOIN WarehouseStocks s ON s.Id=m.WarehouseStockId
                        WHERE m.Kind=4 AND (g.Status<>0 OR g.Id<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN'))
                            OR m.PhysicalDelta<>l.Quantity OR m.ReservedDelta<>0 OR s.ProductId<>l.ProductId OR s.WarehouseId<>g.WarehouseId)))
                    THROW 51020, 'Receipt movement does not match its exact GRN source.', 1;
            END;
            """);
    }
}
