using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AtomicInventoryCutoverRehearsal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_Kind",
                table: "StockMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryReservations_Status",
                table: "InventoryReservations");

            migrationBuilder.AddColumn<bool>(
                name: "IsLegacyImport",
                table: "InventoryReservations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceCreatedAt",
                table: "InventoryReservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryAuthorities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    MinimumWriterVersion = table.Column<int>(type: "int", nullable: false),
                    PreparationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DefaultWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PhysicalAttributionApproval = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SourceFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    CommandHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImportedStocks = table.Column<int>(type: "int", nullable: false),
                    ImportedReservations = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAuthorities", x => x.Id);
                    table.CheckConstraint("CK_InventoryAuthorities_Singleton", "[Id] = 1");
                    table.CheckConstraint("CK_InventoryAuthorities_State", "([Mode] = 0 AND [MinimumWriterVersion] = 1 AND [PreparationId] IS NULL AND [CompletedAt] IS NULL) OR ([Mode] = 1 AND [MinimumWriterVersion] = 2 AND [PreparationId] IS NOT NULL AND [CompletedAt] IS NOT NULL AND [ApprovedByUserId] IS NOT NULL AND [DefaultWarehouseId] IS NOT NULL AND [PhysicalAttributionApproval] IS NOT NULL AND [SourceFingerprint] IS NOT NULL AND [CommandHash] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_InventoryAuthorities_AppUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAuthorities_LegacyInventoryImportBatches_PreparationId",
                        column: x => x.PreparationId,
                        principalTable: "LegacyInventoryImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAuthorities_Warehouses_DefaultWarehouseId",
                        column: x => x.DefaultWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "InventoryAuthorities",
                columns: new[] { "Id", "ApprovedByUserId", "CommandHash", "CompletedAt", "DefaultWarehouseId", "ImportedReservations", "ImportedStocks", "MinimumWriterVersion", "Mode", "PhysicalAttributionApproval", "PreparationId", "SourceFingerprint" },
                values: new object[] { 1, null, null, null, null, 0, 0, 1, 0, null, null, null });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_Kind",
                table: "StockMovements",
                sql: "([Kind] = 0 AND [Sequence] = 1 AND [BeforeOnHand] = 0 AND [BeforeReserved] = 0 AND [PhysicalDelta] >= 0 AND [ReservedDelta] >= 0) OR ([Kind] = 1 AND [PhysicalDelta] = 0 AND [ReservedDelta] > 0) OR ([Kind] = 2 AND [PhysicalDelta] = 0 AND [ReservedDelta] < 0) OR ([Kind] = 3 AND [PhysicalDelta] = [ReservedDelta] AND [PhysicalDelta] < 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryReservations_Status",
                table: "InventoryReservations",
                sql: "([Status] = 0 AND [ClosedAt] IS NULL) OR ([Status] IN (1, 2) AND ([ClosedAt] IS NOT NULL OR [IsLegacyImport] = 1)) OR ([Status] = 3 AND [IsLegacyImport] = 1)");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuthorities_ApprovedByUserId",
                table: "InventoryAuthorities",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuthorities_DefaultWarehouseId",
                table: "InventoryAuthorities",
                column: "DefaultWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuthorities_PreparationId",
                table: "InventoryAuthorities",
                column: "PreparationId");
            ERPFlow.Infrastructure.Persistence.Inventory.InventorySqlGuards.Create(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Atomic inventory authority cannot be destructively downgraded. Use a compatible build or an approved recovery under a writer freeze.");
        }
    }
}
