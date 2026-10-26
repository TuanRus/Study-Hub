using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudyHub.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlaylistSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsShared",
                table: "Playlists",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ShareCode",
                table: "Playlists",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_ShareCode",
                table: "Playlists",
                column: "ShareCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Playlists_ShareCode",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "IsShared",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "ShareCode",
                table: "Playlists");
        }
    }
}
