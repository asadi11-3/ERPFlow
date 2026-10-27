using Microsoft.EntityFrameworkCore.Migrations;

namespace ERPFlow.Infrastructure.Persistence;

internal static class TransferSqlGuards
{
    public static void Create(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_StockTransfers_Lifecycle ON StockTransfers AFTER INSERT,UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL OR d.Status<>0)
                    THROW 51030, 'Transfer history is immutable and cannot be deleted or reversed.', 1;
                IF TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.Transfer')) IS NULL OR EXISTS(
                    SELECT 1 FROM inserted WHERE Id<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.Transfer')))
                    THROW 51030, 'Transfer document scope is required.', 1;
                DECLARE @fence nvarchar(32)=N'NoLock';
                BEGIN TRY
                    SET @fence=APPLOCK_MODE(N'public',N'ERPFlow.Inventory.Authority.v2',N'Transaction');
                END TRY
                BEGIN CATCH
                    THROW 51030, 'Transfer changes require an explicit fenced transaction.', 1;
                END CATCH;
                IF ISNULL(@fence,N'NoLock') NOT IN (N'Shared',N'Exclusive') THROW 51030, 'Transfer authority fence is absent.', 1;
                IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN AppUsers a ON a.Id=COALESCE(i.PostedByUserId,i.CancelledByUserId,i.CreatedByUserId)
                    WHERE a.Id IS NULL OR a.Role NOT IN (0,5)) THROW 51030, 'Transfer actor is not authorized.', 1;
                IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND i.Status<>0)
                    THROW 51030, 'Transfers must be created as Draft.', 1;
                IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE i.Status NOT IN (1,2)
                    OR i.Number<>d.Number OR i.SourceWarehouseId<>d.SourceWarehouseId OR i.DestinationWarehouseId<>d.DestinationWarehouseId
                    OR i.CreatedByUserId<>d.CreatedByUserId OR i.CreatedAt<>d.CreatedAt)
                    THROW 51030, 'Transfer identity is immutable.', 1;
                IF EXISTS(SELECT 1 FROM inserted i WHERE i.Status=2 AND EXISTS(
                    SELECT 1 FROM StockTransferItems l JOIN InventoryPostings p ON p.StockTransferItemId=l.Id WHERE l.StockTransferId=i.Id))
                    THROW 51030, 'Transfers with inventory effects cannot be cancelled.', 1;
                IF EXISTS(SELECT 1 FROM inserted WHERE Status=1)
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM InventoryAuthorities WHERE Id=1 AND Mode=1)
                        THROW 51030, 'Transfer posting requires completed Warehouse authority.', 1;
                    IF EXISTS(SELECT 1 FROM inserted t WHERE t.Status=1 AND
                        (NOT EXISTS(SELECT 1 FROM StockTransferItems WHERE StockTransferId=t.Id) OR EXISTS(
                        SELECT 1 FROM StockTransferItems l WHERE l.StockTransferId=t.Id AND
                            ((SELECT COUNT(*) FROM InventoryPostings p WHERE p.StockTransferItemId=l.Id)<>2
                             OR NOT EXISTS(SELECT 1 FROM InventoryPostings p JOIN StockMovements m ON m.PostingId=p.Id JOIN WarehouseStocks s ON s.Id=m.WarehouseStockId
                                WHERE p.StockTransferItemId=l.Id AND p.Kind=5 AND p.ActorId=t.PostedByUserId AND m.Kind=5
                                    AND m.PhysicalDelta=-l.Quantity AND m.ReservedDelta=0 AND s.WarehouseId=t.SourceWarehouseId AND s.ProductId=l.ProductId)
                             OR NOT EXISTS(SELECT 1 FROM InventoryPostings p JOIN StockMovements m ON m.PostingId=p.Id JOIN WarehouseStocks s ON s.Id=m.WarehouseStockId
                                WHERE p.StockTransferItemId=l.Id AND p.Kind=6 AND p.ActorId=t.PostedByUserId AND m.Kind=6
                                    AND m.PhysicalDelta=l.Quantity AND m.ReservedDelta=0 AND s.WarehouseId=t.DestinationWarehouseId AND s.ProductId=l.ProductId)))))
                        THROW 51030, 'A Posted transfer requires every exact conserved movement pair.', 1;
                END;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_StockTransferItems_Immutable ON StockTransferItems AFTER INSERT,UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted) THROW 51030, 'Transfer lines are immutable.', 1;
                IF TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.Transfer')) IS NULL OR EXISTS(
                    SELECT 1 FROM inserted l JOIN StockTransfers t ON t.Id=l.StockTransferId
                    WHERE t.Status<>0 OR t.Id<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.Transfer')))
                    THROW 51030, 'Transfer line scope is invalid.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_StockTransferCommands_Immutable ON StockTransferCommands AFTER INSERT,UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted) THROW 51030, 'Transfer command results are immutable.', 1;
                IF TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.Transfer')) IS NULL OR EXISTS(
                    SELECT 1 FROM inserted c JOIN StockTransfers t ON t.Id=c.StockTransferId JOIN AppUsers a ON a.Id=c.ActorId
                    WHERE t.Id<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.Transfer')) OR a.Role NOT IN (0,5)
                        OR (c.Operation=0 AND t.Status<>0) OR (c.Operation=1 AND t.Status<>1)
                        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(c.ResponseJson,'$.Id')) IS NULL
                        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(c.ResponseJson,'$.Id'))<>t.Id)
                    THROW 51030, 'Transfer replay response does not match its committed document.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_InventoryPostings_Transfer ON InventoryPostings AFTER INSERT,UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted WHERE Kind IN (5,6)) OR EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE i.Kind IN (5,6))
                    THROW 51030, 'Transfer postings are immutable.', 1;
                IF EXISTS(SELECT 1 FROM inserted WHERE Kind IN (5,6))
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM InventoryAuthorities WHERE Id=1 AND Mode=1)
                        OR TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.Transfer')) IS NULL
                        OR APPLOCK_MODE(N'public',N'ERPFlow.Inventory.Authority.v2',N'Transaction') NOT IN (N'Shared',N'Exclusive')
                        THROW 51030, 'Transfer posting requires Warehouse authority and workflow scope.', 1;
                    IF EXISTS(SELECT 1 FROM inserted p JOIN StockTransferItems l ON l.Id=p.StockTransferItemId
                        JOIN StockTransfers t ON t.Id=l.StockTransferId JOIN AppUsers a ON a.Id=p.ActorId
                        WHERE p.Kind IN (5,6) AND (t.Status<>0 OR t.Id<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.Transfer')) OR a.Role NOT IN (0,5)))
                        THROW 51030, 'Transfer posting owner or actor is invalid.', 1;
                END;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_StockMovements_Transfer ON StockMovements AFTER INSERT,UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted WHERE Kind IN (5,6)) OR EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE i.Kind IN (5,6))
                    THROW 51030, 'Transfer movements are immutable.', 1;
                IF EXISTS(SELECT 1 FROM inserted WHERE Kind IN (5,6)) AND
                    (TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.Transfer')) IS NULL OR EXISTS(
                        SELECT 1 FROM inserted m JOIN InventoryPostings p ON p.Id=m.PostingId JOIN StockTransferItems l ON l.Id=p.StockTransferItemId
                        JOIN StockTransfers t ON t.Id=l.StockTransferId JOIN WarehouseStocks s ON s.Id=m.WarehouseStockId
                        WHERE m.Kind IN (5,6) AND (t.Status<>0 OR t.Id<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.Transfer'))
                            OR s.ProductId<>l.ProductId OR m.ReservedDelta<>0
                            OR (m.Kind=5 AND (s.WarehouseId<>t.SourceWarehouseId OR m.PhysicalDelta<>-l.Quantity))
                            OR (m.Kind=6 AND (s.WarehouseId<>t.DestinationWarehouseId OR m.PhysicalDelta<>l.Quantity)))))
                    THROW 51030, 'Transfer movement does not match its exact document leg.', 1;
            END;
            """);
    }
}
