using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Endoscopy.Migrations
{
    /// <inheritdoc />
    public partial class VideoArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                schema: "endo",
                table: "Media",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedTo",
                schema: "endo",
                table: "Media",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                schema: "endo",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "ArchivedTo",
                schema: "endo",
                table: "Media");
        }
    }
}
