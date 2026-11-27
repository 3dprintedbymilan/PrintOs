using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintOs.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LicenseId",
                table: "PrintModels",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Licenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AttributionRequired = table.Column<bool>(type: "boolean", nullable: false),
                    AttributionText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UnitsLimit = table.Column<int>(type: "integer", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ProofUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProofFileKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ProofFileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProofContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Licenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Licenses_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintModels_LicenseId",
                table: "PrintModels",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_WorkspaceId_Name",
                table: "Licenses",
                columns: new[] { "WorkspaceId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_PrintModels_Licenses_LicenseId",
                table: "PrintModels",
                column: "LicenseId",
                principalTable: "Licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrintModels_Licenses_LicenseId",
                table: "PrintModels");

            migrationBuilder.DropTable(
                name: "Licenses");

            migrationBuilder.DropIndex(
                name: "IX_PrintModels_LicenseId",
                table: "PrintModels");

            migrationBuilder.DropColumn(
                name: "LicenseId",
                table: "PrintModels");
        }
    }
}
