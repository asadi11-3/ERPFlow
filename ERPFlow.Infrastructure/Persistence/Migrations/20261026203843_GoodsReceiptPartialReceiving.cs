using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GoodsReceiptPartialReceiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_Kind",
                table: "StockMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderHistories_Action",
                table: "PurchaseOrderHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryPostings_Kind",
                table: "InventoryPostings");

            migrationBuilder.AddColumn<int>(
                name: "ReceivedQuantity",
                table: "PurchaseOrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "GoodsReceiptId",
                table: "PurchaseOrderHistories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GoodsReceiptItemId",
                table: "InventoryPostings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PurchaseOrderItems_Id_PurchaseOrderId_ProductId",
                table: "PurchaseOrderItems",
                columns: new[] { "Id", "PurchaseOrderId", "ProductId" });

            migrationBuilder.CreateTable(
                name: "GoodsReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupplierDeliveryReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceipts", x => x.Id);
                    table.UniqueConstraint("AK_GoodsReceipts_Id_PurchaseOrderId", x => new { x.Id, x.PurchaseOrderId });
                    table.CheckConstraint("CK_GoodsReceipts_Status", "([Status]=0 AND [PostedByUserId] IS NULL AND [PostedAt] IS NULL AND [CancelledByUserId] IS NULL AND [CancelledAt] IS NULL AND [CancellationReason] IS NULL) OR ([Status]=1 AND [PostedByUserId] IS NOT NULL AND [PostedAt] IS NOT NULL AND [CancelledByUserId] IS NULL AND [CancelledAt] IS NULL AND [CancellationReason] IS NULL) OR ([Status]=2 AND [PostedByUserId] IS NULL AND [PostedAt] IS NULL AND [CancelledByUserId] IS NOT NULL AND [CancelledAt] IS NOT NULL AND LEN(LTRIM(RTRIM([CancellationReason])))>0)");
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_AppUsers_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_AppUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_AppUsers_PostedByUserId",
                        column: x => x.PostedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceiptCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Operation = table.Column<int>(type: "int", nullable: false),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    GoodsReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptCommands", x => x.Id);
                    table.CheckConstraint("CK_GoodsReceiptCommands_Operation", "[Operation] IN (0,1)");
                    table.CheckConstraint("CK_GoodsReceiptCommands_Response", "ISJSON([ResponseJson])=1");
                    table.ForeignKey(
                        name: "FK_GoodsReceiptCommands_AppUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptCommands_GoodsReceipts_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "GoodsReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceiptItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoodsReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptItems", x => x.Id);
                    table.CheckConstraint("CK_GoodsReceiptItems_Quantity", "[Quantity]>0");
                    table.ForeignKey(
                        name: "FK_GoodsReceiptItems_GoodsReceipts_GoodsReceiptId_PurchaseOrderId",
                        columns: x => new { x.GoodsReceiptId, x.PurchaseOrderId },
                        principalTable: "GoodsReceipts",
                        principalColumns: new[] { "Id", "PurchaseOrderId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptItems_PurchaseOrderItems_PurchaseOrderItemId_PurchaseOrderId_ProductId",
                        columns: x => new { x.PurchaseOrderItemId, x.PurchaseOrderId, x.ProductId },
                        principalTable: "PurchaseOrderItems",
                        principalColumns: new[] { "Id", "PurchaseOrderId", "ProductId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_Kind",
                table: "StockMovements",
                sql: "([Kind] = 0 AND [Sequence] = 1 AND [BeforeOnHand] = 0 AND [BeforeReserved] = 0 AND [PhysicalDelta] >= 0 AND [ReservedDelta] >= 0) OR ([Kind] = 1 AND [PhysicalDelta] = 0 AND [ReservedDelta] > 0) OR ([Kind] = 2 AND [PhysicalDelta] = 0 AND [ReservedDelta] < 0) OR ([Kind] = 3 AND [PhysicalDelta] = [ReservedDelta] AND [PhysicalDelta] < 0) OR ([Kind]=4 AND [PhysicalDelta]>0 AND [ReservedDelta]=0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderItems_Received",
                table: "PurchaseOrderItems",
                sql: "[ReceivedQuantity]>=0 AND [ReceivedQuantity]<=[Quantity]");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderHistories_GoodsReceiptId",
                table: "PurchaseOrderHistories",
                column: "GoodsReceiptId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderHistories_Action",
                table: "PurchaseOrderHistories",
                sql: "([Action]=0 AND [FromStatus]=0 AND [ToStatus]=1 AND [GoodsReceiptId] IS NULL) OR ([Action]=1 AND [FromStatus]=1 AND [ToStatus]=2 AND [GoodsReceiptId] IS NULL) OR ([Action]=2 AND [FromStatus]=1 AND [ToStatus]=3 AND [GoodsReceiptId] IS NULL) OR ([Action]=3 AND [FromStatus] IN (0,1,2) AND [ToStatus]=4 AND [GoodsReceiptId] IS NULL) OR ([Action]=4 AND [FromStatus]=2 AND [ToStatus]=2 AND [GoodsReceiptId] IS NOT NULL) OR ([Action]=5 AND [FromStatus] IN (2,5) AND [ToStatus] IN (5,6) AND [GoodsReceiptId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPostings_GoodsReceiptItemId",
                table: "InventoryPostings",
                column: "GoodsReceiptItemId",
                unique: true,
                filter: "[GoodsReceiptItemId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryPostings_Kind",
                table: "InventoryPostings",
                sql: "([Kind]=0 AND [ReservationId] IS NULL AND [GoodsReceiptItemId] IS NULL) OR ([Kind] IN (1,2,3) AND [ReservationId] IS NOT NULL AND [GoodsReceiptItemId] IS NULL) OR ([Kind]=4 AND [ReservationId] IS NULL AND [GoodsReceiptItemId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptCommands_ActorId_IdempotencyKey",
                table: "GoodsReceiptCommands",
                columns: new[] { "ActorId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptCommands_GoodsReceiptId",
                table: "GoodsReceiptCommands",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptItems_GoodsReceiptId_ProductId",
                table: "GoodsReceiptItems",
                columns: new[] { "GoodsReceiptId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptItems_GoodsReceiptId_PurchaseOrderId",
                table: "GoodsReceiptItems",
                columns: new[] { "GoodsReceiptId", "PurchaseOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptItems_GoodsReceiptId_PurchaseOrderItemId",
                table: "GoodsReceiptItems",
                columns: new[] { "GoodsReceiptId", "PurchaseOrderItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptItems_PurchaseOrderItemId_PurchaseOrderId_ProductId",
                table: "GoodsReceiptItems",
                columns: new[] { "PurchaseOrderItemId", "PurchaseOrderId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_CancelledByUserId",
                table: "GoodsReceipts",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_CreatedByUserId",
                table: "GoodsReceipts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_Number",
                table: "GoodsReceipts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_PostedByUserId",
                table: "GoodsReceipts",
                column: "PostedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_PurchaseOrderId_Status",
                table: "GoodsReceipts",
                columns: new[] { "PurchaseOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_SupplierId_SupplierDeliveryReference",
                table: "GoodsReceipts",
                columns: new[] { "SupplierId", "SupplierDeliveryReference" },
                unique: true,
                filter: "[SupplierDeliveryReference] IS NOT NULL AND [Status] <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_WarehouseId_Status",
                table: "GoodsReceipts",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryPostings_GoodsReceiptItems_GoodsReceiptItemId",
                table: "InventoryPostings",
                column: "GoodsReceiptItemId",
                principalTable: "GoodsReceiptItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderHistories_GoodsReceipts_GoodsReceiptId",
                table: "PurchaseOrderHistories",
                column: "GoodsReceiptId",
                principalTable: "GoodsReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            ReceivingSqlGuards.Create(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Posted receipt inventory and audit history cannot be destructively downgraded. Use an approved forward migration.");
        }
    }
}
