using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintOs.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BambuPrintEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "BambuTaskImports",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "sync");

            migrationBuilder.AddColumn<string>(
                name: "ActiveJobName",
                table: "BambuDevices",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ActiveObservedFromStart",
                table: "BambuDevices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActiveStartedAt",
                table: "BambuDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveTaskId",
                table: "BambuDevices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveTraysJson",
                table: "BambuDevices",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastCompletedTaskId",
                table: "BambuDevices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "BambuTaskImports");

            migrationBuilder.DropColumn(
                name: "ActiveJobName",
                table: "BambuDevices");

            migrationBuilder.DropColumn(
                name: "ActiveObservedFromStart",
                table: "BambuDevices");

            migrationBuilder.DropColumn(
                name: "ActiveStartedAt",
                table: "BambuDevices");

            migrationBuilder.DropColumn(
                name: "ActiveTaskId",
                table: "BambuDevices");

            migrationBuilder.DropColumn(
                name: "ActiveTraysJson",
                table: "BambuDevices");

            migrationBuilder.DropColumn(
                name: "LastCompletedTaskId",
                table: "BambuDevices");
        }
    }
}
