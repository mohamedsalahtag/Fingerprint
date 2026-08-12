using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKDeviceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceLogsAndCopySelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludeCards",
                table: "SyncJobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeFaces",
                table: "SyncJobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeFingerprints",
                table: "SyncJobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AttendanceLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    DeviceUserId = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VerifyMode = table.Column<int>(type: "int", nullable: false),
                    InOutMode = table.Column<int>(type: "int", nullable: false),
                    WorkCode = table.Column<int>(type: "int", nullable: false),
                    DownloadedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceLogs_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLogs_DeviceId_DeviceUserId_Timestamp",
                table: "AttendanceLogs",
                columns: new[] { "DeviceId", "DeviceUserId", "Timestamp" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceLogs");

            migrationBuilder.DropColumn(
                name: "IncludeCards",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "IncludeFaces",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "IncludeFingerprints",
                table: "SyncJobs");
        }
    }
}
