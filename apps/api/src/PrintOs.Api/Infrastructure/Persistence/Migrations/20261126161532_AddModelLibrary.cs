using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintOs.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrintModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    ThumbnailKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ThumbnailContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintModels_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrintModelVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LayerHeightMillimetres = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: true),
                    InfillPercent = table.Column<int>(type: "integer", nullable: true),
                    SupportsRequired = table.Column<bool>(type: "boolean", nullable: true),
                    MaterialType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PrintProfileNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintModelVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintModelVersions_PrintModels_ModelId",
                        column: x => x.ModelId,
                        principalTable: "PrintModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrintModelVersions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintModels_WorkspaceId_CreatedAt",
                table: "PrintModels",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PrintModelVersions_ModelId_VersionNumber",
                table: "PrintModelVersions",
                columns: new[] { "ModelId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintModelVersions_StorageKey",
                table: "PrintModelVersions",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintModelVersions_WorkspaceId",
                table: "PrintModelVersions",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrintModelVersions");

            migrationBuilder.DropTable(
                name: "PrintModels");
        }
    }
}
