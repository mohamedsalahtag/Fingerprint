using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKDeviceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    CommKey = table.Column<int>(type: "int", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    FirmwareVersion = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Platform = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnrolledUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceUserId = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Password = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Privilege = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    CardNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SourceDeviceId = table.Column<int>(type: "int", nullable: false),
                    LastDownloadedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrolledUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnrolledUsers_Devices_SourceDeviceId",
                        column: x => x.SourceDeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    TargetDeviceId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Progress = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncJobs_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncJobs_Devices_TargetDeviceId",
                        column: x => x.TargetDeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BiometricTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EnrolledUserId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    FingerIndex = table.Column<int>(type: "int", nullable: false),
                    Flag = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Length = table.Column<int>(type: "int", nullable: false),
                    DownloadedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BiometricTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BiometricTemplates_EnrolledUsers_EnrolledUserId",
                        column: x => x.EnrolledUserId,
                        principalTable: "EnrolledUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BiometricTemplates_EnrolledUserId_Type_FingerIndex",
                table: "BiometricTemplates",
                columns: new[] { "EnrolledUserId", "Type", "FingerIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_IpAddress_Port",
                table: "Devices",
                columns: new[] { "IpAddress", "Port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrolledUsers_SourceDeviceId_DeviceUserId",
                table: "EnrolledUsers",
                columns: new[] { "SourceDeviceId", "DeviceUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_DeviceId",
                table: "SyncJobs",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_TargetDeviceId",
                table: "SyncJobs",
                column: "TargetDeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BiometricTemplates");

            migrationBuilder.DropTable(
                name: "SyncJobs");

            migrationBuilder.DropTable(
                name: "EnrolledUsers");

            migrationBuilder.DropTable(
                name: "Devices");
        }
    }
}
