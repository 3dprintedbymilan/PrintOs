using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintOs.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantReadyForSale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReadyForSale",
                table: "ProductVariants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SaleRiskAcknowledgedAt",
                table: "ProductVariants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReadyForSale",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "SaleRiskAcknowledgedAt",
                table: "ProductVariants");
        }
    }
}
