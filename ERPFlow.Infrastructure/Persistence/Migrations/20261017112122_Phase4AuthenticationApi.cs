using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4AuthenticationApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "AppUsers",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "!PASSWORD-RESET-REQUIRED!");

            migrationBuilder.Sql(
                """
                UPDATE salesOrder
                SET CreatedByUserId = firstHistory.UserId
                FROM SalesOrders AS salesOrder
                CROSS APPLY (
                    SELECT TOP (1) history.UserId
                    FROM ApprovalHistories AS history
                    WHERE history.SalesOrderId = salesOrder.Id
                    ORDER BY history.CreatedAt, history.Id
                ) AS firstHistory;

                IF EXISTS (SELECT 1 FROM SalesOrders WHERE CreatedByUserId IS NULL)
                BEGIN
                    DECLARE @legacyOwnerId uniqueidentifier = NEWID();
                    INSERT INTO AppUsers (Id, Name, Email, Role, PasswordHash, CreatedAt)
                    VALUES (
                        @legacyOwnerId,
                        N'Legacy Order Owner',
                        CONCAT(N'legacy-', CONVERT(nvarchar(36), @legacyOwnerId), N'@erpflow.invalid'),
                        0,
                        N'!PASSWORD-RESET-REQUIRED!',
                        SYSUTCDATETIME());

                    UPDATE SalesOrders
                    SET CreatedByUserId = @legacyOwnerId
                    WHERE CreatedByUserId IS NULL;
                END
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "AppUsers",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(512)",
                oldMaxLength: 512,
                oldDefaultValue: "!PASSWORD-RESET-REQUIRED!");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CreatedByUserId_CreatedAt",
                table: "SalesOrders",
                columns: new[] { "CreatedByUserId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_AppUsers_CreatedByUserId",
                table: "SalesOrders",
                column: "CreatedByUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_AppUsers_CreatedByUserId",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_CreatedByUserId_CreatedAt",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "AppUsers");
        }
    }
}
