using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductImagesPhase6A : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductImageObjects",
                columns: table => new
                {
                    StorageKey = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImageObjects", x => x.StorageKey);
                    table.CheckConstraint("CK_ProductImageObjects_State", "[State] IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_ProductImageObjects_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductImageStores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Identity = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImageStores", x => x.Id);
                    table.CheckConstraint("CK_ProductImageStores_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "ProductImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.Id);
                    table.CheckConstraint("CK_ProductImages_ContentType", "[ContentType] IN ('image/jpeg', 'image/png', 'image/webp')");
                    table.CheckConstraint("CK_ProductImages_Dimensions", "[Width] > 0 AND [Height] > 0 AND [Width] <= 8192 AND [Height] <= 8192 AND CAST([Width] AS bigint) * [Height] <= 16000000");
                    table.CheckConstraint("CK_ProductImages_FileSize", "[FileSize] > 0 AND [FileSize] <= 5242880");
                    table.ForeignKey(
                        name: "FK_ProductImages_ProductImageObjects_StorageKey",
                        column: x => x.StorageKey,
                        principalTable: "ProductImageObjects",
                        principalColumn: "StorageKey",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductImages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImageObjects_ProductId",
                table: "ProductImageObjects",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImageObjects_State_ExpiresAt",
                table: "ProductImageObjects",
                columns: new[] { "State", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId",
                table: "ProductImages",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_StorageKey",
                table: "ProductImages",
                column: "StorageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Product image storage history cannot be destructively downgraded.");
        }
    }
}
