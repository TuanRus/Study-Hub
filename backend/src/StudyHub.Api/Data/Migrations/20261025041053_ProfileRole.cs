using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudyHub.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProfileRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Profiles",
                type: "TEXT",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "Profiles");
        }
    }
}
