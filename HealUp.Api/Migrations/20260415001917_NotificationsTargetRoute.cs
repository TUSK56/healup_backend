using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealUp.Api.Migrations
{
    /// <inheritdoc />
    public partial class NotificationsTargetRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetRoute",
                table: "notifications",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetRoute",
                table: "notifications");
        }
    }
}
