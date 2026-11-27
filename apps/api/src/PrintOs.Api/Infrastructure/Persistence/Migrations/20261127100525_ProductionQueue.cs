using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintOs.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrderLineId",
                table: "PrintJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlannedUnits",
                table: "PrintJobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QueuePosition",
                table: "PrintJobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequeuedFromId",
                table: "PrintJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_OrderLineId",
                table: "PrintJobs",
                column: "OrderLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_PrintJobs_OrderLines_OrderLineId",
                table: "PrintJobs",
                column: "OrderLineId",
                principalTable: "OrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrintJobs_OrderLines_OrderLineId",
                table: "PrintJobs");

            migrationBuilder.DropIndex(
                name: "IX_PrintJobs_OrderLineId",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "OrderLineId",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "PlannedUnits",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "QueuePosition",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "RequeuedFromId",
                table: "PrintJobs");
        }
    }
}
