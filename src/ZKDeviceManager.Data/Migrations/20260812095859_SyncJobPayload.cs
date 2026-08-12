using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZKDeviceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncJobPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Payload",
                table: "SyncJobs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Payload",
                table: "SyncJobs");
        }
    }
}
