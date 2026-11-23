using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Reminders.Migrations
{
    /// <inheritdoc />
    public partial class SendSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BulkHoldSeconds",
                schema: "reminders",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HoldSeconds",
                schema: "reminders",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewKinds",
                schema: "reminders",
                table: "Settings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CorrectionForId",
                schema: "reminders",
                table: "Messages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecallReason",
                schema: "reminders",
                table: "Messages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecalledAt",
                schema: "reminders",
                table: "Messages",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecalledBy",
                schema: "reminders",
                table: "Messages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReleasedAt",
                schema: "reminders",
                table: "Messages",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleasedBy",
                schema: "reminders",
                table: "Messages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresRelease",
                schema: "reminders",
                table: "Messages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WrongRecipientAt",
                schema: "reminders",
                table: "Messages",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_CorrectionForId",
                schema: "reminders",
                table: "Messages",
                column: "CorrectionForId");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Messages_CorrectionForId",
                schema: "reminders",
                table: "Messages",
                column: "CorrectionForId",
                principalSchema: "reminders",
                principalTable: "Messages",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Messages_CorrectionForId",
                schema: "reminders",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_CorrectionForId",
                schema: "reminders",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "BulkHoldSeconds",
                schema: "reminders",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "HoldSeconds",
                schema: "reminders",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReviewKinds",
                schema: "reminders",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CorrectionForId",
                schema: "reminders",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "RecallReason",
                schema: "reminders",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "RecalledAt",
                schema: "reminders",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "RecalledBy",
                schema: "reminders",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ReleasedAt",
                schema: "reminders",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ReleasedBy",
                schema: "reminders",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "RequiresRelease",
                schema: "reminders",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "WrongRecipientAt",
                schema: "reminders",
                table: "Messages");
        }
    }
}
