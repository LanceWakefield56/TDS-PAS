using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Letters.Migrations
{
    /// <inheritdoc />
    public partial class SendSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrectionForId",
                schema: "letters",
                table: "Letters",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WrongRecipientAt",
                schema: "letters",
                table: "Letters",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WrongRecipientReason",
                schema: "letters",
                table: "Letters",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Letters_CorrectionForId",
                schema: "letters",
                table: "Letters",
                column: "CorrectionForId");

            migrationBuilder.AddForeignKey(
                name: "FK_Letters_Letters_CorrectionForId",
                schema: "letters",
                table: "Letters",
                column: "CorrectionForId",
                principalSchema: "letters",
                principalTable: "Letters",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Letters_Letters_CorrectionForId",
                schema: "letters",
                table: "Letters");

            migrationBuilder.DropIndex(
                name: "IX_Letters_CorrectionForId",
                schema: "letters",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "CorrectionForId",
                schema: "letters",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "WrongRecipientAt",
                schema: "letters",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "WrongRecipientReason",
                schema: "letters",
                table: "Letters");
        }
    }
}
