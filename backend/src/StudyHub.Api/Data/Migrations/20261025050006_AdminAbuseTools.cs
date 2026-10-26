using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudyHub.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdminAbuseTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FlagReason",
                table: "Users",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FlaggedAt",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFlagged",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastIp",
                table: "Users",
                type: "TEXT",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegisterIp",
                table: "Users",
                type: "TEXT",
                maxLength: 45,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IpBans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ip = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    BannedBy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpBans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IpBans_Ip",
                table: "IpBans",
                column: "Ip",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IpBans");

            migrationBuilder.DropColumn(
                name: "FlagReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FlaggedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsFlagged",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastIp",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RegisterIp",
                table: "Users");
        }
    }
}
