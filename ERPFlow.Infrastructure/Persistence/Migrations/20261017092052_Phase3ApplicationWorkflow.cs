using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERPFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3ApplicationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Customers",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Customers");
        }
    }
}
