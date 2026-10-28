using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfessionalDemoSeederPhase6B : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfessionalDemoSeedManifests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CountsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ValidationJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfessionalDemoSeedManifests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfessionalDemoSeedManifests_ScenarioKey_Version",
                table: "ProfessionalDemoSeedManifests",
                columns: new[] { "ScenarioKey", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfessionalDemoSeedManifests");
        }
    }
}
