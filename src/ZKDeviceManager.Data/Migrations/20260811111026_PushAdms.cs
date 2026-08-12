using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKDeviceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class PushAdms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PushCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SerialNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CommandText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveredUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushCommands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SerialNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    LastIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    Info = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsersReceived = table.Column<int>(type: "int", nullable: false),
                    FacesReceived = table.Column<int>(type: "int", nullable: false),
                    FingersReceived = table.Column<int>(type: "int", nullable: false),
                    LogsReceived = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDevices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushCommands_SerialNumber_CompletedUtc",
                table: "PushCommands",
                columns: new[] { "SerialNumber", "CompletedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_SerialNumber",
                table: "PushDevices",
                column: "SerialNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushCommands");

            migrationBuilder.DropTable(
                name: "PushDevices");
        }
    }
}
