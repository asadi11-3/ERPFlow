using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                    table.CheckConstraint("CK_Warehouses_Code", "LEN(LTRIM(RTRIM([Code]))) > 0");
                    table.CheckConstraint("CK_Warehouses_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "WarehouseStocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockOnHand = table.Column<int>(type: "int", nullable: false),
                    ReservedQuantity = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseStocks", x => x.Id);
                    table.UniqueConstraint("AK_WarehouseStocks_Id_ProductId", x => new { x.Id, x.ProductId });
                    table.CheckConstraint("CK_WarehouseStocks_Balances", "[StockOnHand] >= 0 AND [ReservedQuantity] >= 0 AND [ReservedQuantity] <= [StockOnHand]");
                    table.CheckConstraint("CK_WarehouseStocks_Sequence", "[Sequence] >= 0 AND ([Sequence] > 0 OR ([StockOnHand] = 0 AND [ReservedQuantity] = 0))");
                    table.ForeignKey(
                        name: "FK_WarehouseStocks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseStocks_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseStockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobcardItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReservations", x => x.Id);
                    table.CheckConstraint("CK_InventoryReservations_Owner", "([SalesOrderId] IS NOT NULL AND [JobcardItemId] IS NULL) OR ([SalesOrderId] IS NULL AND [JobcardItemId] IS NOT NULL)");
                    table.CheckConstraint("CK_InventoryReservations_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_InventoryReservations_Status", "([Status] = 0 AND [ClosedAt] IS NULL) OR ([Status] IN (1, 2) AND [ClosedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_InventoryReservations_JobcardItems_JobcardItemId",
                        column: x => x.JobcardItemId,
                        principalTable: "JobcardItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservations_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservations_WarehouseStocks_WarehouseStockId_ProductId",
                        columns: x => new { x.WarehouseStockId, x.ProductId },
                        principalTable: "WarehouseStocks",
                        principalColumns: new[] { "Id", "ProductId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryPostings", x => x.Id);
                    table.UniqueConstraint("AK_InventoryPostings_Id_Kind", x => new { x.Id, x.Kind });
                    table.CheckConstraint("CK_InventoryPostings_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0");
                    table.CheckConstraint("CK_InventoryPostings_Kind", "([Kind] = 0 AND [ReservationId] IS NULL) OR ([Kind] IN (1, 2, 3) AND [ReservationId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_InventoryPostings_AppUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryPostings_InventoryReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "InventoryReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseStockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    BeforeOnHand = table.Column<int>(type: "int", nullable: false),
                    BeforeReserved = table.Column<int>(type: "int", nullable: false),
                    AfterOnHand = table.Column<int>(type: "int", nullable: false),
                    AfterReserved = table.Column<int>(type: "int", nullable: false),
                    PhysicalDelta = table.Column<int>(type: "int", nullable: false),
                    ReservedDelta = table.Column<int>(type: "int", nullable: false),
                    ResultRowVersion = table.Column<byte[]>(type: "binary(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.CheckConstraint("CK_StockMovements_Balances", "[BeforeOnHand] >= [BeforeReserved] AND [BeforeReserved] >= 0 AND [AfterOnHand] >= [AfterReserved] AND [AfterReserved] >= 0");
                    table.CheckConstraint("CK_StockMovements_Deltas", "[AfterOnHand] - [BeforeOnHand] = [PhysicalDelta] AND [AfterReserved] - [BeforeReserved] = [ReservedDelta]");
                    table.CheckConstraint("CK_StockMovements_Kind", "([Kind] = 0 AND [Sequence] = 1 AND [BeforeOnHand] = 0 AND [BeforeReserved] = 0 AND [PhysicalDelta] >= 0 AND [ReservedDelta] = 0) OR ([Kind] = 1 AND [PhysicalDelta] = 0 AND [ReservedDelta] > 0) OR ([Kind] = 2 AND [PhysicalDelta] = 0 AND [ReservedDelta] < 0) OR ([Kind] = 3 AND [PhysicalDelta] = [ReservedDelta] AND [PhysicalDelta] < 0)");
                    table.CheckConstraint("CK_StockMovements_Sequence", "[Sequence] > 0");
                    table.ForeignKey(
                        name: "FK_StockMovements_InventoryPostings_PostingId_Kind",
                        columns: x => new { x.PostingId, x.Kind },
                        principalTable: "InventoryPostings",
                        principalColumns: new[] { "Id", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_WarehouseStocks_WarehouseStockId",
                        column: x => x.WarehouseStockId,
                        principalTable: "WarehouseStocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPostings_ActorId_IdempotencyKey",
                table: "InventoryPostings",
                columns: new[] { "ActorId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPostings_ReservationId",
                table: "InventoryPostings",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_JobcardItemId",
                table: "InventoryReservations",
                column: "JobcardItemId",
                unique: true,
                filter: "[JobcardItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_SalesOrderId_ProductId",
                table: "InventoryReservations",
                columns: new[] { "SalesOrderId", "ProductId" },
                unique: true,
                filter: "[SalesOrderId] IS NOT NULL AND [Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_WarehouseStockId_ProductId",
                table: "InventoryReservations",
                columns: new[] { "WarehouseStockId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_WarehouseStockId_Status",
                table: "InventoryReservations",
                columns: new[] { "WarehouseStockId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_Opening",
                table: "StockMovements",
                column: "WarehouseStockId",
                unique: true,
                filter: "[Kind] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_PostingId_Kind",
                table: "StockMovements",
                columns: new[] { "PostingId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_PostingId_WarehouseStockId",
                table: "StockMovements",
                columns: new[] { "PostingId", "WarehouseStockId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_WarehouseStockId_Sequence",
                table: "StockMovements",
                columns: new[] { "WarehouseStockId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStocks_ProductId",
                table: "WarehouseStocks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStocks_WarehouseId_ProductId",
                table: "WarehouseStocks",
                columns: new[] { "WarehouseId", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "InventoryPostings");

            migrationBuilder.DropTable(
                name: "InventoryReservations");

            migrationBuilder.DropTable(
                name: "WarehouseStocks");

            migrationBuilder.DropTable(
                name: "Warehouses");
        }
    }
}
