using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintOs.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LowStockGrams",
                table: "Materials",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReorderGrams",
                table: "Materials",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LowStockGrams",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "ReorderGrams",
                table: "Materials");
        }
    }
}
