using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintOs.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpoolEmptyWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmptySpoolGrams",
                table: "FilamentSpools",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmptySpoolGrams",
                table: "FilamentSpools");
        }
    }
}
