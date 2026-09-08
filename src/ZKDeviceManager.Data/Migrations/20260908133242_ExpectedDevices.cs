using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKDeviceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpectedDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpectedDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Site = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    VerifyIp = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpectedDevices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpectedDevices_Ip",
                table: "ExpectedDevices",
                column: "Ip");

            migrationBuilder.CreateIndex(
                name: "IX_ExpectedDevices_Site",
                table: "ExpectedDevices",
                column: "Site");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpectedDevices");
        }
    }
}
