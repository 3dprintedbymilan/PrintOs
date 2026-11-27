using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintOs.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinishedStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProductVariantId",
                table: "PrintJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinishedStockLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnHand = table.Column<int>(type: "integer", nullable: false),
                    Reserved = table.Column<int>(type: "integer", nullable: false),
                    AverageUnitCost = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    LowStockThreshold = table.Column<int>(type: "integer", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinishedStockLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinishedStockLevels_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinishedStockLevels_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinishedStockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Delta = table.Column<int>(type: "integer", nullable: false),
                    ReservedDelta = table.Column<int>(type: "integer", nullable: false),
                    BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                    ReservedAfter = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    PrintJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarketSaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinishedStockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinishedStockMovements_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinishedStockMovements_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    FulfilledQuantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockReservations_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_ProductVariantId",
                table: "PrintJobs",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_FinishedStockLevels_VariantId",
                table: "FinishedStockLevels",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_FinishedStockLevels_WorkspaceId_VariantId",
                table: "FinishedStockLevels",
                columns: new[] { "WorkspaceId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinishedStockMovements_OrderId",
                table: "FinishedStockMovements",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FinishedStockMovements_PrintJobId",
                table: "FinishedStockMovements",
                column: "PrintJobId");

            migrationBuilder.CreateIndex(
                name: "IX_FinishedStockMovements_VariantId_CreatedAt",
                table: "FinishedStockMovements",
                columns: new[] { "VariantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinishedStockMovements_WorkspaceId",
                table: "FinishedStockMovements",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderId",
                table: "StockReservations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderLineId",
                table: "StockReservations",
                column: "OrderLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_VariantId",
                table: "StockReservations",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_WorkspaceId",
                table: "StockReservations",
                column: "WorkspaceId");

            migrationBuilder.AddForeignKey(
                name: "FK_PrintJobs_ProductVariants_ProductVariantId",
                table: "PrintJobs",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrintJobs_ProductVariants_ProductVariantId",
                table: "PrintJobs");

            migrationBuilder.DropTable(
                name: "FinishedStockLevels");

            migrationBuilder.DropTable(
                name: "FinishedStockMovements");

            migrationBuilder.DropTable(
                name: "StockReservations");

            migrationBuilder.DropIndex(
                name: "IX_PrintJobs_ProductVariantId",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "ProductVariantId",
                table: "PrintJobs");
        }
    }
}
