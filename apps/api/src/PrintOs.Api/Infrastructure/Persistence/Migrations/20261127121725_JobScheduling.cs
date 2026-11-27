using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintOs.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PartXMillimetres",
                table: "PrintJobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartYMillimetres",
                table: "PrintJobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartZMillimetres",
                table: "PrintJobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PinnedStartAt",
                table: "PrintJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PrinterPinned",
                table: "PrintJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "PrintJobs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("UPDATE \"PrintJobs\" SET \"PrinterPinned\" = TRUE WHERE \"Status\" = 'Planned'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PartXMillimetres",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "PartYMillimetres",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "PartZMillimetres",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "PinnedStartAt",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "PrinterPinned",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "PrintJobs");
        }
    }
}
