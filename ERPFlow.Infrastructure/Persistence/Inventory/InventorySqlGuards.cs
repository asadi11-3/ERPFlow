using Microsoft.EntityFrameworkCore.Migrations;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

internal static class InventorySqlGuards
{
    public static void Create(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "SalesOrders", "SalesOrderItems", "StockReservations", "Jobcards", "JobcardItems", "WarehouseStocks" })
            migrationBuilder.Sql($"""
                CREATE TRIGGER [TR_{table}_InventoryWriterProtocol] ON [{table}] AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS(SELECT 1 FROM InventoryAuthorities WHERE Id=1 AND Mode=1)
                    BEGIN
                        IF ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'ERPFlow.InventoryWriterVersion')),0) <> 2
                            THROW 51002, 'Incompatible inventory writer protocol.', 1;
                        DECLARE @authorityLock nvarchar(32)=N'NoLock';
                        BEGIN TRY
                            SET @authorityLock=APPLOCK_MODE(N'public',N'ERPFlow.Inventory.Authority.v2',N'Transaction');
                        END TRY
                        BEGIN CATCH
                            THROW 51002, 'Inventory writes require an explicit fenced transaction.', 1;
                        END CATCH;
                        IF ISNULL(@authorityLock,N'NoLock') NOT IN (N'Shared',N'Exclusive')
                            THROW 51002, 'Inventory transaction does not hold the authority fence.', 1;
                    END;
                END;
                """);
        migrationBuilder.Sql("""
            CREATE TRIGGER [TR_Products_FrozenLegacyBalances] ON [Products] AFTER INSERT, UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM InventoryAuthorities WHERE Id=1 AND Mode=1)
                   AND EXISTS(SELECT 1 FROM inserted i FULL OUTER JOIN deleted d ON i.Id=d.Id
                       WHERE ISNULL(i.StockOnHand,0)<>ISNULL(d.StockOnHand,0)
                          OR ISNULL(i.ReservedQuantity,0)<>ISNULL(d.ReservedQuantity,0))
                    THROW 51001, 'Legacy Product balances are immutable after inventory authority cutover.', 1;
            END;
            """);
        migrationBuilder.Sql("""
            CREATE TRIGGER [TR_InventoryAuthorities_ExplicitTransition] ON [InventoryAuthorities] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted WHERE Mode=1)
                    THROW 51003, 'Completed inventory authority metadata is immutable.', 1;
                IF NOT EXISTS(SELECT 1 FROM inserted)
                    THROW 51003, 'Inventory authority metadata cannot be deleted.', 1;
                IF ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'ERPFlow.InventoryCutoverPermit')),0)<>2
                    THROW 51003, 'An explicit cutover operator context is required.', 1;
                DECLARE @authorityLock nvarchar(32)=N'NoLock';
                BEGIN TRY
                    SET @authorityLock=APPLOCK_MODE(N'public',N'ERPFlow.Inventory.Authority.v2',N'Transaction');
                END TRY
                BEGIN CATCH
                    THROW 51003, 'Authority transition requires an explicit operator transaction.', 1;
                END CATCH;
                IF ISNULL(@authorityLock,N'NoLock')<>N'Exclusive'
                    THROW 51003, 'Authority transition requires the exclusive transaction fence.', 1;
            END;
            """);
    }
}
