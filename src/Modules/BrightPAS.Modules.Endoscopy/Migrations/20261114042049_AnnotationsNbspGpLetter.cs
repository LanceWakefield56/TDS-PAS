using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Endoscopy.Migrations
{
    /// <inheritdoc />
    public partial class AnnotationsNbspGpLetter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GpLetterId",
                schema: "endo",
                table: "Procedures",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NbspExportedAt",
                schema: "endo",
                table: "Procedures",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AnnotatedFromId",
                schema: "endo",
                table: "Media",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalContentType",
                schema: "endo",
                table: "Media",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                schema: "endo",
                table: "Media",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_GpLetterId",
                schema: "endo",
                table: "Procedures",
                column: "GpLetterId");

            migrationBuilder.CreateIndex(
                name: "IX_Media_AnnotatedFromId",
                schema: "endo",
                table: "Media",
                column: "AnnotatedFromId");

            migrationBuilder.AddForeignKey(
                name: "FK_Media_Media_AnnotatedFromId",
                schema: "endo",
                table: "Media",
                column: "AnnotatedFromId",
                principalSchema: "endo",
                principalTable: "Media",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Media_Media_AnnotatedFromId",
                schema: "endo",
                table: "Media");

            migrationBuilder.DropIndex(
                name: "IX_Procedures_GpLetterId",
                schema: "endo",
                table: "Procedures");

            migrationBuilder.DropIndex(
                name: "IX_Media_AnnotatedFromId",
                schema: "endo",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "GpLetterId",
                schema: "endo",
                table: "Procedures");

            migrationBuilder.DropColumn(
                name: "NbspExportedAt",
                schema: "endo",
                table: "Procedures");

            migrationBuilder.DropColumn(
                name: "AnnotatedFromId",
                schema: "endo",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "OriginalContentType",
                schema: "endo",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                schema: "endo",
                table: "Media");
        }
    }
}
