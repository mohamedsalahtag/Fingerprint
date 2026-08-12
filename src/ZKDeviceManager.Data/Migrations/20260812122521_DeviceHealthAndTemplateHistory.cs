using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKDeviceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeviceHealthAndTemplateHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DevFaceCapacity",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DevFaceCount",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DevFpCapacity",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DevFpCount",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DevLogCount",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DevUserCapacity",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DevUserCount",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastInfoUtc",
                table: "Devices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TemplateChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    Pin = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    FingerIndex = table.Column<int>(type: "int", nullable: false),
                    OldData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldLength = table.Column<int>(type: "int", nullable: false),
                    ChangedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateChanges_ChangedUtc",
                table: "TemplateChanges",
                column: "ChangedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateChanges_DeviceId_Pin",
                table: "TemplateChanges",
                columns: new[] { "DeviceId", "Pin" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TemplateChanges");

            migrationBuilder.DropColumn(
                name: "DevFaceCapacity",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DevFaceCount",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DevFpCapacity",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DevFpCount",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DevLogCount",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DevUserCapacity",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DevUserCount",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastInfoUtc",
                table: "Devices");
        }
    }
}
