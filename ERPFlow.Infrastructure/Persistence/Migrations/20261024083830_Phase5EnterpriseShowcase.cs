using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5EnterpriseShowcase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Carrier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TrackingNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DispatchedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryNotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryNotes_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MachineAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InstalledAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    WarrantyExpiresAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MeterReading = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineAssets_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineAssets_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineAssets_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Jobcards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TechnicianName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ReportedFault = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    TechnicianNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FinalMeterReading = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobcards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Jobcards_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Jobcards_MachineAssets_MachineAssetId",
                        column: x => x.MachineAssetId,
                        principalTable: "MachineAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "JobcardItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobcardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobcardItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobcardItems_Jobcards_JobcardId",
                        column: x => x.JobcardId,
                        principalTable: "Jobcards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobcardItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNotes_CustomerId",
                table: "DeliveryNotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNotes_DeliveryNumber",
                table: "DeliveryNotes",
                column: "DeliveryNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNotes_SalesOrderId",
                table: "DeliveryNotes",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_JobcardItems_JobcardId_ProductId",
                table: "JobcardItems",
                columns: new[] { "JobcardId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobcardItems_ProductId",
                table: "JobcardItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobcards_CustomerId",
                table: "Jobcards",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobcards_MachineAssetId",
                table: "Jobcards",
                column: "MachineAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobcards_Status",
                table: "Jobcards",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Jobcards_TicketNumber",
                table: "Jobcards",
                column: "TicketNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineAssets_CustomerId",
                table: "MachineAssets",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineAssets_ProductId",
                table: "MachineAssets",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineAssets_SalesOrderId",
                table: "MachineAssets",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineAssets_SerialNumber",
                table: "MachineAssets",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineAssets_TagNumber",
                table: "MachineAssets",
                column: "TagNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryNotes");

            migrationBuilder.DropTable(
                name: "JobcardItems");

            migrationBuilder.DropTable(
                name: "Jobcards");

            migrationBuilder.DropTable(
                name: "MachineAssets");
        }
    }
}
