using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintOs.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BambuStateHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HmsCodesJson",
                table: "BambuDevices",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PrintErrorCode",
                table: "BambuDevices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BambuStateChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    JobName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BambuStateChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BambuStateChanges_BambuDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "BambuDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BambuStateChanges_DeviceId",
                table: "BambuStateChanges",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_BambuStateChanges_WorkspaceId_DeviceId_ChangedAt",
                table: "BambuStateChanges",
                columns: new[] { "WorkspaceId", "DeviceId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BambuStateChanges");

            migrationBuilder.DropColumn(
                name: "HmsCodesJson",
                table: "BambuDevices");

            migrationBuilder.DropColumn(
                name: "PrintErrorCode",
                table: "BambuDevices");
        }
    }
}
