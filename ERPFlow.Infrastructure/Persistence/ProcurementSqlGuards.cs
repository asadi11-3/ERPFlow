using Microsoft.EntityFrameworkCore.Migrations;

namespace ERPFlow.Infrastructure.Persistence;

internal static class ProcurementSqlGuards
{
    public static void Create(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER [TR_Suppliers_MasterIdentity] ON [Suppliers] AFTER UPDATE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id
                    WHERE i.SupplierCode COLLATE Latin1_General_100_BIN2 <> d.SupplierCode COLLATE Latin1_General_100_BIN2)
                    THROW 51010, 'Supplier codes are immutable.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER [TR_Warehouses_MasterIdentity] ON [Warehouses] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id
                    WHERE i.Code COLLATE Latin1_General_100_BIN2 <> d.Code COLLATE Latin1_General_100_BIN2)
                    THROW 51010, 'Warehouse codes are immutable.', 1;
                IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id
                    WHERE (d.Id='0f96d350-19ce-4d10-92d9-b1f06be839f1' OR d.Code=N'LEGACY-DEFAULT')
                      AND (i.Id IS NULL OR i.IsActive=0 OR i.Code<>N'LEGACY-DEFAULT'))
                    THROW 51010, 'Legacy default warehouse identity and active state are protected.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER [TR_PurchaseOrders_ImmutableTerms] ON [PurchaseOrders] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id
                    WHERE i.Id IS NULL OR i.PONumber<>d.PONumber OR i.CreatedByUserId<>d.CreatedByUserId OR i.CreatedAt<>d.CreatedAt)
                    THROW 51011, 'PO identity/history cannot be replaced or deleted.', 1;
                IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.Status<>0 AND
                    (i.SupplierId<>d.SupplierId OR i.WarehouseId<>d.WarehouseId OR i.TotalAmount<>d.TotalAmount
                     OR ISNULL(i.Notes,N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(d.Notes,N'') COLLATE Latin1_General_100_BIN2))
                    THROW 51011, 'Submitted PO commercial terms are immutable.', 1;
                IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id
                    WHERE i.Status=4 AND (d.ReceivingStartedAt IS NOT NULL OR d.Status IN (5,6,7)))
                    THROW 51011, 'A PO with receiving activity cannot be cancelled.', 1;
                IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE i.Status<>d.Status AND
                    (i.Revision<>d.Revision+1 OR NOT EXISTS(
                        SELECT 1 FROM PurchaseOrderHistories h WHERE h.PurchaseOrderId=i.Id AND h.Revision=i.Revision
                        AND h.FromStatus=d.Status AND h.ToStatus=i.Status
                        AND ((h.Action=0 AND d.Status=0 AND i.Status=1 AND i.SubmittedAt=h.CreatedAt AND i.DecidedAt IS NULL)
                          OR (h.Action IN (1,2) AND d.Status=1 AND i.Status=CASE h.Action WHEN 1 THEN 2 ELSE 3 END
                              AND i.DecidedAt=h.CreatedAt AND i.SubmittedAt=d.SubmittedAt)
                          OR (h.Action=3 AND d.Status IN (0,1,2) AND i.Status=4
                              AND ISNULL(i.SubmittedAt,'0001-01-01')=ISNULL(d.SubmittedAt,'0001-01-01')
                              AND ISNULL(i.DecidedAt,'0001-01-01')=ISNULL(d.DecidedAt,'0001-01-01'))))))
                    THROW 51011, 'PO state changes require a matching atomic audit transition.', 1;
                IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE i.Status=d.Status AND
                    ((i.Revision<>d.Revision AND (d.Status<>0 OR i.Revision<>d.Revision+1))
                     OR ISNULL(i.SubmittedAt,'0001-01-01')<>ISNULL(d.SubmittedAt,'0001-01-01')
                     OR ISNULL(i.DecidedAt,'0001-01-01')<>ISNULL(d.DecidedAt,'0001-01-01')))
                    THROW 51011, 'PO transition metadata cannot change without an audit transition.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER [TR_PurchaseOrderItems_DraftOnly] ON [PurchaseOrderItems] AFTER INSERT, UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM (SELECT PurchaseOrderId FROM inserted UNION SELECT PurchaseOrderId FROM deleted) x
                    JOIN PurchaseOrders p ON p.Id=x.PurchaseOrderId WHERE p.Status<>0)
                    THROW 51011, 'Submitted PO lines are immutable.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER [TR_PurchaseOrderHistories_AppendOnly] ON [PurchaseOrderHistories] AFTER INSERT, UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted) THROW 51012, 'PO approval history is append-only.', 1;
                IF EXISTS(SELECT 1 FROM inserted WHERE Id<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.POTransitionEvent')))
                    OR TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.POTransitionEvent')) IS NULL
                    THROW 51012, 'PO audit insertion requires the atomic transition scope.', 1;
                DECLARE @fence nvarchar(32)=N'NoLock';
                BEGIN TRY
                    SET @fence=APPLOCK_MODE(N'public',N'ERPFlow.Inventory.Authority.v2',N'Transaction');
                END TRY
                BEGIN CATCH
                    THROW 51012, 'PO audit insertion requires an explicit fenced transaction.', 1;
                END CATCH;
                IF ISNULL(@fence,N'NoLock') NOT IN (N'Shared',N'Exclusive')
                    THROW 51012, 'PO audit insertion requires the workflow transaction fence.', 1;
                IF EXISTS(SELECT 1 FROM inserted h JOIN PurchaseOrders p ON p.Id=h.PurchaseOrderId
                    JOIN AppUsers a WITH (HOLDLOCK) ON a.Id=h.ActorId
                    WHERE h.FromStatus<>p.Status OR h.Revision<>p.Revision+1
                    OR (h.Action IN (1,2) AND (a.Role NOT IN (0,2) OR h.ActorId=p.CreatedByUserId))
                    OR (h.Action IN (0,3) AND (a.Role NOT IN (0,4) OR (a.Role=4 AND h.ActorId<>p.CreatedByUserId)))
                    OR (h.Action=3 AND (p.ReceivingStartedAt IS NOT NULL OR (p.Status=2 AND a.Role<>0)))
                    OR (h.Action IN (2,3) AND LEN(LTRIM(RTRIM(ISNULL(h.Comments,N''))))=0))
                    THROW 51012, 'PO audit actor, ownership or transition admission is invalid.', 1;
            END;
            """);
        migration.Sql("""
            CREATE PROCEDURE dbo.TransitionPurchaseOrder
                @Id uniqueidentifier, @ExpectedVersion binary(8), @ActorId uniqueidentifier, @Action int,
                @Comments nvarchar(1000), @HistoryId uniqueidentifier, @At datetime2(7)
            AS
            BEGIN
                SET NOCOUNT ON;
                IF @@TRANCOUNT=0 THROW 51011, 'PO transitions require a caller-owned transaction.', 1;
                DECLARE @state int, @revision int, @creator uniqueidentifier, @supplier uniqueidentifier,
                    @warehouse uniqueidentifier, @receiving datetime2(7), @role int, @target int;
                SELECT @state=Status, @revision=Revision, @creator=CreatedByUserId, @supplier=SupplierId,
                    @warehouse=WarehouseId, @receiving=ReceivingStartedAt
                FROM PurchaseOrders WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id AND RowVersion=@ExpectedVersion;
                IF @state IS NULL THROW 51013, 'PO version is stale.', 1;
                SELECT @role=Role FROM AppUsers WITH (HOLDLOCK) WHERE Id=@ActorId;
                IF @role IS NULL OR @At IS NULL OR @HistoryId IS NULL OR @revision=2147483647
                    THROW 51011, 'A valid actor, event and next revision are required.', 1;
                IF @Action IN (1,2)
                BEGIN
                    IF @role NOT IN (0,2) OR @creator=@ActorId OR @state<>1
                        THROW 51011, 'Only authorized non-creators may decide Pending POs.', 1;
                END
                ELSE IF @Action IN (0,3)
                BEGIN
                    IF @role NOT IN (0,4) OR (@role=4 AND @creator<>@ActorId)
                        THROW 51011, 'PO ownership or role is invalid.', 1;
                    IF @Action=0 AND @state<>0 THROW 51011, 'Only Draft POs may be submitted.', 1;
                    IF @Action=3 AND (@state NOT IN (0,1,2) OR @receiving IS NOT NULL OR (@state=2 AND @role<>0))
                        THROW 51011, 'PO cancellation is not allowed.', 1;
                END
                ELSE THROW 51011, 'Unknown PO action.', 1;
                IF @Action IN (2,3) AND LEN(LTRIM(RTRIM(ISNULL(@Comments,N''))))=0
                    THROW 51011, 'A rejection or cancellation reason is required.', 1;
                IF @Action IN (0,1)
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM Warehouses WITH (UPDLOCK,HOLDLOCK) WHERE Id=@warehouse AND IsActive=1)
                        OR NOT EXISTS(SELECT 1 FROM Suppliers WITH (UPDLOCK,HOLDLOCK) WHERE Id=@supplier AND IsActive=1)
                        THROW 51011, 'PO supplier and warehouse must be active.', 1;
                END;
                SET @target=CASE @Action WHEN 0 THEN 1 WHEN 1 THEN 2 WHEN 2 THEN 3 ELSE 4 END;
                BEGIN TRY
                    EXEC sys.sp_set_session_context @key=N'ERPFlow.POTransitionEvent', @value=@HistoryId;
                    INSERT INTO PurchaseOrderHistories(Id,PurchaseOrderId,ActorId,Action,FromStatus,ToStatus,Comments,Revision,CreatedAt)
                        VALUES(@HistoryId,@Id,@ActorId,@Action,@state,@target,@Comments,@revision+1,@At);
                    UPDATE PurchaseOrders SET Status=@target, Revision=@revision+1,
                        SubmittedAt=CASE WHEN @Action=0 THEN @At ELSE SubmittedAt END,
                        DecidedAt=CASE WHEN @Action IN (1,2) THEN @At ELSE DecidedAt END
                        WHERE Id=@Id AND RowVersion=@ExpectedVersion;
                    IF @@ROWCOUNT<>1 THROW 51013, 'PO transition version changed.', 1;
                    EXEC sys.sp_set_session_context @key=N'ERPFlow.POTransitionEvent', @value=NULL;
                END TRY
                BEGIN CATCH
                    EXEC sys.sp_set_session_context @key=N'ERPFlow.POTransitionEvent', @value=NULL;
                    THROW;
                END CATCH;
            END;
            """);
    }
}
