using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockTransfersPhase5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_Kind",
                table: "StockMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryPostings_Kind",
                table: "InventoryPostings");

            migrationBuilder.AddColumn<Guid>(
                name: "StockTransferItemId",
                table: "InventoryPostings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    SourceWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfers", x => x.Id);
                    table.CheckConstraint("CK_StockTransfers_Status", "([Status]=0 AND [PostedByUserId] IS NULL AND [PostedAt] IS NULL AND [CancelledByUserId] IS NULL AND [CancelledAt] IS NULL AND [CancellationReason] IS NULL) OR ([Status]=1 AND [PostedByUserId] IS NOT NULL AND [PostedAt] IS NOT NULL AND [CancelledByUserId] IS NULL AND [CancelledAt] IS NULL AND [CancellationReason] IS NULL) OR ([Status]=2 AND [PostedByUserId] IS NULL AND [PostedAt] IS NULL AND [CancelledByUserId] IS NOT NULL AND [CancelledAt] IS NOT NULL AND LEN(LTRIM(RTRIM([CancellationReason])))>0)");
                    table.CheckConstraint("CK_StockTransfers_Warehouses", "[SourceWarehouseId]<>[DestinationWarehouseId]");
                    table.ForeignKey(
                        name: "FK_StockTransfers_AppUsers_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_AppUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_AppUsers_PostedByUserId",
                        column: x => x.PostedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Warehouses_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Warehouses_SourceWarehouseId",
                        column: x => x.SourceWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Operation = table.Column<int>(type: "int", nullable: false),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferCommands", x => x.Id);
                    table.CheckConstraint("CK_StockTransferCommands_Operation", "[Operation] IN (0,1)");
                    table.CheckConstraint("CK_StockTransferCommands_Response", "ISJSON([ResponseJson])=1");
                    table.ForeignKey(
                        name: "FK_StockTransferCommands_AppUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferCommands_StockTransfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferItems", x => x.Id);
                    table.CheckConstraint("CK_StockTransferItems_Quantity", "[Quantity]>0");
                    table.ForeignKey(
                        name: "FK_StockTransferItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferItems_StockTransfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_Kind",
                table: "StockMovements",
                sql: "([Kind] = 0 AND [Sequence] = 1 AND [BeforeOnHand] = 0 AND [BeforeReserved] = 0 AND [PhysicalDelta] >= 0 AND [ReservedDelta] >= 0) OR ([Kind] = 1 AND [PhysicalDelta] = 0 AND [ReservedDelta] > 0) OR ([Kind] = 2 AND [PhysicalDelta] = 0 AND [ReservedDelta] < 0) OR ([Kind] = 3 AND [PhysicalDelta] = [ReservedDelta] AND [PhysicalDelta] < 0) OR ([Kind] IN (4,6) AND [PhysicalDelta]>0 AND [ReservedDelta]=0) OR ([Kind]=5 AND [PhysicalDelta]<0 AND [ReservedDelta]=0)");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPostings_StockTransferItemId_Kind",
                table: "InventoryPostings",
                columns: new[] { "StockTransferItemId", "Kind" },
                unique: true,
                filter: "[StockTransferItemId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryPostings_Kind",
                table: "InventoryPostings",
                sql: "([Kind]=0 AND [ReservationId] IS NULL AND [GoodsReceiptItemId] IS NULL AND [StockTransferItemId] IS NULL) OR ([Kind] IN (1,2,3) AND [ReservationId] IS NOT NULL AND [GoodsReceiptItemId] IS NULL AND [StockTransferItemId] IS NULL) OR ([Kind]=4 AND [ReservationId] IS NULL AND [GoodsReceiptItemId] IS NOT NULL AND [StockTransferItemId] IS NULL) OR ([Kind] IN (5,6) AND [ReservationId] IS NULL AND [GoodsReceiptItemId] IS NULL AND [StockTransferItemId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferCommands_ActorId_IdempotencyKey",
                table: "StockTransferCommands",
                columns: new[] { "ActorId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferCommands_StockTransferId",
                table: "StockTransferCommands",
                column: "StockTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_ProductId",
                table: "StockTransferItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_StockTransferId_ProductId",
                table: "StockTransferItems",
                columns: new[] { "StockTransferId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_CancelledByUserId",
                table: "StockTransfers",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_CreatedAt_Id",
                table: "StockTransfers",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_CreatedByUserId",
                table: "StockTransfers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_DestinationWarehouseId_Status",
                table: "StockTransfers",
                columns: new[] { "DestinationWarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_Number",
                table: "StockTransfers",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_PostedByUserId",
                table: "StockTransfers",
                column: "PostedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_SourceWarehouseId_Status",
                table: "StockTransfers",
                columns: new[] { "SourceWarehouseId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryPostings_StockTransferItems_StockTransferItemId",
                table: "InventoryPostings",
                column: "StockTransferItemId",
                principalTable: "StockTransferItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            TransferSqlGuards.Create(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Posted transfer inventory and paired audit history cannot be destructively downgraded. Use a compatible forward migration.");
        }
    }
}
