using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WarehouseSourcePreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockReservations_SalesOrderId_ProductId",
                table: "StockReservations");

            migrationBuilder.AddColumn<int>(
                name: "ClosureKind",
                table: "StockReservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHistorical",
                table: "StockReservations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "StockReservations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "SalesOrderItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "JobcardItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LegacyInventoryImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefaultWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    FormatVersion = table.Column<int>(type: "int", nullable: false),
                    PreparedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyInventoryImportBatches", x => x.Id);
                    table.CheckConstraint("CK_LegacyInventoryImportBatches_Format", "[FormatVersion] = 1");
                    table.ForeignKey(
                        name: "FK_LegacyInventoryImportBatches_AppUsers_PreparedByUserId",
                        column: x => x.PreparedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegacyInventoryImportBatches_Warehouses_DefaultWarehouseId",
                        column: x => x.DefaultWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegacyInventoryOpenings",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetWarehouseStockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockOnHand = table.Column<int>(type: "int", nullable: false),
                    ReservedQuantity = table.Column<int>(type: "int", nullable: false),
                    SourceProductRowVersion = table.Column<byte[]>(type: "binary(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyInventoryOpenings", x => new { x.BatchId, x.ProductId });
                    table.CheckConstraint("CK_LegacyInventoryOpenings_Balances", "[StockOnHand] >= [ReservedQuantity] AND [ReservedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_LegacyInventoryOpenings_LegacyInventoryImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "LegacyInventoryImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegacyInventoryOpenings_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegacyInventoryOpenings_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegacyInventoryReservationImports",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobcardItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Disposition = table.Column<int>(type: "int", nullable: false),
                    WarehouseInferred = table.Column<bool>(type: "bit", nullable: false),
                    IsHistorical = table.Column<bool>(type: "bit", nullable: false),
                    SourceCreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SourceClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyInventoryReservationImports", x => new { x.BatchId, x.SourceKind, x.SourceId });
                    table.CheckConstraint("CK_LegacyInventoryReservationImports_Disposition", "[Disposition] IN (0,1,2,3) AND ([IsHistorical] = 0 OR [Disposition] <> 0)");
                    table.CheckConstraint("CK_LegacyInventoryReservationImports_Owner", "([SourceKind] = 0 AND [SalesOrderId] IS NOT NULL AND [JobcardItemId] IS NULL AND [TargetReservationId] = [SourceId]) OR ([SourceKind] = 1 AND [SalesOrderId] IS NULL AND [JobcardItemId] IS NOT NULL AND [JobcardItemId] = [SourceId])");
                    table.CheckConstraint("CK_LegacyInventoryReservationImports_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_LegacyInventoryReservationImports_LegacyInventoryImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "LegacyInventoryImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegacyInventoryReservationImports_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegacyInventoryReservationImports_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Warehouses",
                columns: new[] { "Id", "Code", "CreatedAt", "IsActive", "Name" },
                values: new object[] { new Guid("0f96d350-19ce-4d10-92d9-b1f06be839f1"), "LEGACY-DEFAULT", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, "Legacy inventory compatibility warehouse" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_SalesOrderId_ProductId",
                table: "StockReservations",
                columns: new[] { "SalesOrderId", "ProductId" },
                unique: true,
                filter: "[IsHistorical] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_WarehouseId",
                table: "StockReservations",
                column: "WarehouseId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_ClosureKind",
                table: "StockReservations",
                sql: "[ClosureKind] IS NULL OR ([IsReleased] = 1 AND [ClosureKind] IN (1, 2))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_HistoricalClosed",
                table: "StockReservations",
                sql: "[IsHistorical] = 0 OR [IsReleased] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderItems_WarehouseId",
                table: "SalesOrderItems",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_JobcardItems_WarehouseId",
                table: "JobcardItems",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyInventoryImportBatches_DefaultWarehouseId",
                table: "LegacyInventoryImportBatches",
                column: "DefaultWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyInventoryImportBatches_PreparedByUserId",
                table: "LegacyInventoryImportBatches",
                column: "PreparedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyInventoryOpenings_BatchId_TargetWarehouseStockId",
                table: "LegacyInventoryOpenings",
                columns: new[] { "BatchId", "TargetWarehouseStockId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyInventoryOpenings_ProductId",
                table: "LegacyInventoryOpenings",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyInventoryOpenings_WarehouseId",
                table: "LegacyInventoryOpenings",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyInventoryReservationImports_BatchId_ProductId_Disposition",
                table: "LegacyInventoryReservationImports",
                columns: new[] { "BatchId", "ProductId", "Disposition" });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyInventoryReservationImports_BatchId_TargetReservationId",
                table: "LegacyInventoryReservationImports",
                columns: new[] { "BatchId", "TargetReservationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyInventoryReservationImports_ProductId",
                table: "LegacyInventoryReservationImports",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyInventoryReservationImports_WarehouseId",
                table: "LegacyInventoryReservationImports",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_JobcardItems_Warehouses_WarehouseId",
                table: "JobcardItems",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderItems_Warehouses_WarehouseId",
                table: "SalesOrderItems",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockReservations_Warehouses_WarehouseId",
                table: "StockReservations",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Warehouse source/history preparation cannot be destructively downgraded. Use a reviewed forward migration or an approved restore under a write freeze.");
        }
    }
}
