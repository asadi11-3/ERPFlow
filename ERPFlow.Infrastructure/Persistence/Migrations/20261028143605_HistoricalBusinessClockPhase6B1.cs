using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistoricalBusinessClockPhase6B1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.AdmitGoodsReceipt
                    @ReceiptId uniqueidentifier, @OrderVersion binary(8), @ActorId uniqueidentifier, @At datetime2(7) AS
                BEGIN
                    SET NOCOUNT ON;
                    IF @@TRANCOUNT=0 OR @ReceiptId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN'))
                        OR TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN')) IS NULL
                        THROW 51020, 'Receipt admission requires the workflow transaction.', 1;
                    DECLARE @order uniqueidentifier, @state int, @revision int, @started datetime2(7), @event uniqueidentifier=NEWID();
                    SELECT @order=PurchaseOrderId FROM GoodsReceipts WHERE Id=@ReceiptId AND Status=0 AND CreatedByUserId=@ActorId
                        AND CreatedAt=@At;
                    IF @order IS NULL OR @At>DATEADD(minute,5,SYSUTCDATETIME())
                        OR NOT EXISTS(SELECT 1 FROM AppUsers WHERE Id=@ActorId AND Role IN (0,5))
                        THROW 51020, 'Receipt admission actor or business time is invalid.', 1;
                    SELECT @state=Status,@revision=Revision,@started=ReceivingStartedAt FROM PurchaseOrders WITH(UPDLOCK,HOLDLOCK)
                        WHERE Id=@order AND RowVersion=@OrderVersion AND CreatedAt<=@At;
                    IF @state IS NULL THROW 51013, 'PO version changed before receipt admission.', 1;
                    IF @state NOT IN (2,5) THROW 51020, 'Only approved open POs accept receipts.', 1;
                    IF @started IS NOT NULL RETURN;
                    BEGIN TRY
                        EXEC sys.sp_set_session_context @key=N'ERPFlow.POTransitionEvent', @value=@event;
                        INSERT INTO PurchaseOrderHistories(Id,PurchaseOrderId,ActorId,Action,FromStatus,ToStatus,Revision,CreatedAt,GoodsReceiptId)
                            VALUES(@event,@order,@ActorId,4,@state,@state,@revision+1,@At,@ReceiptId);
                        UPDATE PurchaseOrders SET ReceivingStartedAt=@At, Revision=@revision+1 WHERE Id=@order AND RowVersion=@OrderVersion;
                        IF @@ROWCOUNT<>1 THROW 51013, 'PO receipt admission conflicted.', 1;
                        EXEC sys.sp_set_session_context @key=N'ERPFlow.POTransitionEvent', @value=NULL;
                    END TRY
                    BEGIN CATCH
                        EXEC sys.sp_set_session_context @key=N'ERPFlow.POTransitionEvent', @value=NULL;
                        THROW;
                    END CATCH;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.PostGoodsReceipt
                    @ReceiptId uniqueidentifier, @ReceiptVersion binary(8), @ActorId uniqueidentifier, @At datetime2(7) AS
                BEGIN
                    SET NOCOUNT ON;
                    IF @@TRANCOUNT=0 OR TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN')) IS NULL
                        OR @ReceiptId<>TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'ERPFlow.GRN'))
                        OR NOT EXISTS(SELECT 1 FROM InventoryAuthorities WHERE Id=1 AND Mode=1)
                        THROW 51020, 'Posting requires warehouse authority and receipt workflow scope.', 1;
                    IF NOT EXISTS(SELECT 1 FROM AppUsers WITH(HOLDLOCK) WHERE Id=@ActorId AND Role IN (0,5))
                        THROW 51020, 'Receipt posting actor is invalid.', 1;
                    DECLARE @order uniqueidentifier, @state int, @revision int, @target int, @event uniqueidentifier=NEWID();
                    SELECT @order=PurchaseOrderId FROM GoodsReceipts WHERE Id=@ReceiptId AND CreatedAt<=@At;
                    IF @order IS NULL OR @At>DATEADD(minute,5,SYSUTCDATETIME())
                        THROW 51020, 'Receipt posting business time is invalid.', 1;
                    SELECT @state=Status,@revision=Revision FROM PurchaseOrders WITH(UPDLOCK,HOLDLOCK) WHERE Id=@order;
                    IF @state NOT IN (2,5) OR @state IS NULL THROW 51020, 'The PO is not open for receiving.', 1;
                    IF NOT EXISTS(SELECT 1 FROM GoodsReceipts WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReceiptId AND Status=0 AND RowVersion=@ReceiptVersion)
                        THROW 51013, 'GRN version/state changed.', 1;
                    IF NOT EXISTS(SELECT 1 FROM GoodsReceiptItems WHERE GoodsReceiptId=@ReceiptId)
                        OR EXISTS(SELECT 1 FROM GoodsReceiptItems l JOIN PurchaseOrderItems p ON p.Id=l.PurchaseOrderItemId
                            WHERE l.GoodsReceiptId=@ReceiptId AND CONVERT(bigint,p.ReceivedQuantity)+l.Quantity>p.Quantity)
                        THROW 51020, 'Receipt would exceed the original ordered quantity.', 1;
                    IF EXISTS(SELECT 1 FROM PurchaseOrderItems p WHERE p.PurchaseOrderId=@order AND p.ReceivedQuantity<>
                        ISNULL((SELECT SUM(CONVERT(bigint,l.Quantity)) FROM GoodsReceiptItems l JOIN GoodsReceipts g ON g.Id=l.GoodsReceiptId
                            WHERE l.PurchaseOrderItemId=p.Id AND g.Status=1),0))
                        THROW 51020, 'PO received quantities do not reconcile; no repair is permitted.', 1;
                    IF NOT EXISTS(SELECT 1 FROM GoodsReceipts g JOIN Warehouses w WITH(UPDLOCK,HOLDLOCK) ON w.Id=g.WarehouseId
                        JOIN Suppliers s WITH(UPDLOCK,HOLDLOCK) ON s.Id=g.SupplierId WHERE g.Id=@ReceiptId AND w.IsActive=1 AND s.IsActive=1)
                        THROW 51020, 'Receipt supplier or warehouse is inactive.', 1;
                    BEGIN TRY
                        UPDATE GoodsReceipts SET Status=1, PostedAt=@At, PostedByUserId=@ActorId WHERE Id=@ReceiptId AND RowVersion=@ReceiptVersion;
                        IF @@ROWCOUNT<>1 THROW 51013, 'GRN posting conflicted.', 1;
                        UPDATE p SET ReceivedQuantity=p.ReceivedQuantity+l.Quantity FROM PurchaseOrderItems p
                            JOIN GoodsReceiptItems l ON l.PurchaseOrderItemId=p.Id WHERE l.GoodsReceiptId=@ReceiptId;
                        SET @target=CASE WHEN EXISTS(SELECT 1 FROM PurchaseOrderItems WHERE PurchaseOrderId=@order AND ReceivedQuantity<Quantity) THEN 5 ELSE 6 END;
                        EXEC sys.sp_set_session_context @key=N'ERPFlow.POTransitionEvent', @value=@event;
                        INSERT INTO PurchaseOrderHistories(Id,PurchaseOrderId,ActorId,Action,FromStatus,ToStatus,Revision,CreatedAt,GoodsReceiptId)
                            VALUES(@event,@order,@ActorId,5,@state,@target,@revision+1,@At,@ReceiptId);
                        UPDATE PurchaseOrders SET Status=@target,Revision=@revision+1 WHERE Id=@order;
                        EXEC sys.sp_set_session_context @key=N'ERPFlow.POTransitionEvent', @value=NULL;
                    END TRY
                    BEGIN CATCH
                        EXEC sys.sp_set_session_context @key=N'ERPFlow.POTransitionEvent', @value=NULL;
                        THROW;
                    END CATCH;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE dbo.AdmitGoodsReceipt; DROP PROCEDURE dbo.PostGoodsReceipt;");
            ReceivingSqlGuards.InstallProcedures(migrationBuilder);
        }
    }
}
