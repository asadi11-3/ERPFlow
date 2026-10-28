using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfessionalDemoSeedOperationsPhase6B2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfessionalDemoSeedOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OperationType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfessionalDemoSeedOperations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfessionalDemoSeedOperations_ScenarioKey_OperationKey",
                table: "ProfessionalDemoSeedOperations",
                columns: new[] { "ScenarioKey", "OperationKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfessionalDemoSeedOperations");
        }
    }
}
