using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKDeviceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppAccessUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppAccessUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Login = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GrantedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAccessUsers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAccessUsers_Login",
                table: "AppAccessUsers",
                column: "Login",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAccessUsers");
        }
    }
}
