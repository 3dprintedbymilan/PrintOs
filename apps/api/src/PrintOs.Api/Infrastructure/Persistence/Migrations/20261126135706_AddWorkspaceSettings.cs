using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintOs.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Workspaces",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "EUR");

            migrationBuilder.AddColumn<decimal>(
                name: "ElectricityPricePerKwh",
                table: "Workspaces",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0.30m);

            migrationBuilder.AddColumn<decimal>(
                name: "LabourRatePerHour",
                table: "Workspaces",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 20m);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Workspaces",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "nl");

            migrationBuilder.AddColumn<decimal>(
                name: "MachineRatePerHour",
                table: "Workspaces",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "WastePercent",
                table: "Workspaces",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 5m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "ElectricityPricePerKwh",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "LabourRatePerHour",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "MachineRatePerHour",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "WastePercent",
                table: "Workspaces");
        }
    }
}
