using Microsoft.EntityFrameworkCore.Migrations;

namespace ERPFlow.Infrastructure.Persistence;

internal static partial class ReceivingSqlGuards
{
    private static void PurchaseOrderProtocol(MigrationBuilder migration)
    {
        migration.Sql("""
            ALTER TRIGGER [TR_PurchaseOrders_ImmutableTerms] ON [PurchaseOrders] AFTER UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL
                    OR i.PONumber<>d.PONumber OR i.CreatedByUserId<>d.CreatedByUserId OR i.CreatedAt<>d.CreatedAt)
                    THROW 51011, 'PO identity/history cannot be replaced or deleted.', 1;
                IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.Status<>0 AND
                    (i.SupplierId<>d.SupplierId OR i.WarehouseId<>d.WarehouseId OR i.TotalAmount<>d.TotalAmount
                        OR ISNULL(i.Notes,N'') COLLATE Latin1_General_100_BIN2<>ISNULL(d.Notes,N'') COLLATE Latin1_General_100_BIN2))
                    THROW 51011, 'Submitted commercial terms are immutable.', 1;
                IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE
                    (i.Status=4 AND (d.ReceivingStartedAt IS NOT NULL OR d.Status IN (5,6,7)))
                    OR (d.ReceivingStartedAt IS NOT NULL AND (i.ReceivingStartedAt IS NULL OR i.ReceivingStartedAt<>d.ReceivingStartedAt)))
                    THROW 51011, 'Receiving admission cannot be cleared or cancelled.', 1;
                IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id
                    WHERE (i.Status<>d.Status OR (i.Revision<>d.Revision AND NOT(d.Status=0 AND i.Status=0 AND i.Revision=d.Revision+1))) AND
                    (i.Revision<>d.Revision+1 OR NOT EXISTS(
                        SELECT 1 FROM PurchaseOrderHistories h LEFT JOIN GoodsReceipts g ON g.Id=h.GoodsReceiptId
                        WHERE h.PurchaseOrderId=i.Id AND h.Revision=i.Revision AND h.FromStatus=d.Status AND h.ToStatus=i.Status AND
                        ((h.Action=0 AND d.Status=0 AND i.Status=1 AND i.SubmittedAt=h.CreatedAt AND i.DecidedAt IS NULL)
                        OR (h.Action IN (1,2) AND d.Status=1 AND i.Status=CASE h.Action WHEN 1 THEN 2 ELSE 3 END AND i.DecidedAt=h.CreatedAt AND i.SubmittedAt=d.SubmittedAt)
                        OR (h.Action=3 AND d.Status IN (0,1,2) AND i.Status=4)
                        OR (h.Action=4 AND d.Status=2 AND i.Status=2 AND d.ReceivingStartedAt IS NULL AND i.ReceivingStartedAt=h.CreatedAt AND g.Status=0 AND g.PurchaseOrderId=i.Id)
                        OR (h.Action=5 AND d.Status IN (2,5) AND i.Status IN (5,6) AND g.Status=1 AND g.PurchaseOrderId=i.Id AND i.ReceivingStartedAt IS NOT NULL)))))
                    THROW 51011, 'PO state/revision change requires a matching audit event.', 1;
                IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE
                    (ISNULL(i.SubmittedAt,'0001-01-01')<>ISNULL(d.SubmittedAt,'0001-01-01') AND NOT(d.Status=0 AND i.Status=1))
                    OR (ISNULL(i.DecidedAt,'0001-01-01')<>ISNULL(d.DecidedAt,'0001-01-01') AND NOT(d.Status=1 AND i.Status IN (2,3))))
                    THROW 51011, 'PO timestamps cannot change outside their decision transition.', 1;
                IF EXISTS(SELECT 1 FROM inserted i WHERE
                    (i.Status=6 AND EXISTS(SELECT 1 FROM PurchaseOrderItems p WHERE p.PurchaseOrderId=i.Id AND p.ReceivedQuantity<>p.Quantity))
                    OR (i.Status=5 AND (NOT EXISTS(SELECT 1 FROM PurchaseOrderItems p WHERE p.PurchaseOrderId=i.Id AND p.ReceivedQuantity>0)
                        OR NOT EXISTS(SELECT 1 FROM PurchaseOrderItems p WHERE p.PurchaseOrderId=i.Id AND p.ReceivedQuantity<p.Quantity))))
                    THROW 51011, 'PO receiving state disagrees with line quantities.', 1;
            END;
            """);
        migration.Sql("""
            ALTER TRIGGER [TR_PurchaseOrderItems_DraftOnly] ON [PurchaseOrderItems] AFTER INSERT,UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id JOIN PurchaseOrders p ON p.Id=i.PurchaseOrderId
                    WHERE p.Status<>0 AND (d.Id IS NULL OR i.ProductId<>d.ProductId OR i.PurchaseOrderId<>d.PurchaseOrderId OR i.Quantity<>d.Quantity OR i.UnitCost<>d.UnitCost))
                    OR EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id JOIN PurchaseOrders p ON p.Id=d.PurchaseOrderId WHERE i.Id IS NULL AND p.Status<>0)
                    THROW 51011, 'Submitted PO lines are immutable.', 1;
                IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id
                    WHERE i.ReceivedQuantity<>ISNULL(d.ReceivedQuantity,0) AND NOT EXISTS(
                        SELECT 1 FROM GoodsReceiptItems l JOIN GoodsReceipts g ON g.Id=l.GoodsReceiptId
                        WHERE g.Id=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN')) AND g.Status=1
                            AND l.PurchaseOrderItemId=i.Id AND d.Id IS NOT NULL AND CONVERT(bigint,d.ReceivedQuantity)+l.Quantity=i.ReceivedQuantity))
                    THROW 51020, 'Received quantities require their posted GRN.', 1;
            END;
            """);
        migration.Sql("""
            ALTER TRIGGER [TR_PurchaseOrderHistories_AppendOnly] ON [PurchaseOrderHistories] AFTER INSERT,UPDATE,DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted) THROW 51012, 'PO audit history is append-only.', 1;
                IF TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.POTransitionEvent')) IS NULL OR
                    EXISTS(SELECT 1 FROM inserted WHERE Id<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.POTransitionEvent')))
                    THROW 51012, 'PO audit scope is required.', 1;
                DECLARE @fence nvarchar(32)=N'NoLock';
                BEGIN TRY
                    SET @fence=APPLOCK_MODE(N'public',N'ERPFlow.Inventory.Authority.v2',N'Transaction');
                END TRY
                BEGIN CATCH
                    THROW 51012, 'PO audit needs an explicit workflow transaction.', 1;
                END CATCH;
                IF ISNULL(@fence,N'NoLock') NOT IN (N'Shared',N'Exclusive') THROW 51012, 'PO authority fence is absent.', 1;
                IF EXISTS(SELECT 1 FROM inserted h JOIN PurchaseOrders p ON p.Id=h.PurchaseOrderId JOIN AppUsers a WITH(HOLDLOCK) ON a.Id=h.ActorId
                    LEFT JOIN GoodsReceipts g ON g.Id=h.GoodsReceiptId
                    WHERE h.FromStatus<>p.Status OR h.Revision<>p.Revision+1
                    OR (h.Action IN (1,2) AND (a.Role NOT IN (0,2) OR a.Id=p.CreatedByUserId))
                    OR (h.Action IN (0,3) AND (a.Role NOT IN (0,4) OR (a.Role=4 AND a.Id<>p.CreatedByUserId)))
                    OR (h.Action=3 AND (p.ReceivingStartedAt IS NOT NULL OR (p.Status=2 AND a.Role<>0)))
                    OR (h.Action IN (2,3) AND LEN(LTRIM(RTRIM(ISNULL(h.Comments,N''))))=0)
                    OR (h.Action IN (4,5) AND (a.Role NOT IN (0,5) OR g.Id IS NULL OR g.PurchaseOrderId<>p.Id
                        OR g.Id<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN'))
                        OR (h.Action=4 AND (g.Status<>0 OR p.Status<>2 OR p.ReceivingStartedAt IS NOT NULL))
                        OR (h.Action=5 AND (g.Status<>1 OR g.PostedByUserId<>a.Id)))))
                    THROW 51012, 'PO audit actor/ownership/receiving admission is invalid.', 1;
            END;
            """);
    }
}
