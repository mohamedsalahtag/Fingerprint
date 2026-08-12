using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKDeviceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class UsersUnificationAndPerUserCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserFilter",
                table: "SyncJobs",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Pin = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_Pin",
                table: "UserProfiles",
                column: "Pin",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "UserFilter",
                table: "SyncJobs");
        }
    }
}
