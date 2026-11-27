using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintOs.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBambuConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BambuConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Region = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EncryptedAccessToken = table.Column<string>(type: "text", nullable: false),
                    AccountUid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StatusChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BambuConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BambuConnections_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BambuTaskImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PrintJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumedStock = table.Column<bool>(type: "boolean", nullable: false),
                    Flag = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BambuTaskImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BambuTaskImports_PrintJobs_PrintJobId",
                        column: x => x.PrintJobId,
                        principalTable: "PrintJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BambuDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Serial = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Online = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    JobName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: true),
                    RemainingMinutes = table.Column<int>(type: "integer", nullable: true),
                    Layer = table.Column<int>(type: "integer", nullable: true),
                    TotalLayers = table.Column<int>(type: "integer", nullable: true),
                    NozzleTemperature = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    BedTemperature = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    FilamentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FilamentColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TrayNow = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    TraysJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BambuDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BambuDevices_BambuConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "BambuConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BambuDevices_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BambuConnections_WorkspaceId",
                table: "BambuConnections",
                column: "WorkspaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BambuDevices_ConnectionId",
                table: "BambuDevices",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_BambuDevices_PrinterId",
                table: "BambuDevices",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_BambuDevices_WorkspaceId_PrinterId",
                table: "BambuDevices",
                columns: new[] { "WorkspaceId", "PrinterId" },
                unique: true,
                filter: "\"PrinterId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BambuDevices_WorkspaceId_Serial",
                table: "BambuDevices",
                columns: new[] { "WorkspaceId", "Serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BambuTaskImports_PrintJobId",
                table: "BambuTaskImports",
                column: "PrintJobId");

            migrationBuilder.CreateIndex(
                name: "IX_BambuTaskImports_WorkspaceId_TaskId",
                table: "BambuTaskImports",
                columns: new[] { "WorkspaceId", "TaskId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BambuDevices");

            migrationBuilder.DropTable(
                name: "BambuTaskImports");

            migrationBuilder.DropTable(
                name: "BambuConnections");
        }
    }
}
