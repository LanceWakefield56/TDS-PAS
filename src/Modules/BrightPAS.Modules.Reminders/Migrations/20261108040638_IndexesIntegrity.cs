using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Reminders.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Replies_BookingId",
                schema: "reminders",
                table: "Replies",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Replies_MatchedMessageId",
                schema: "reminders",
                table: "Replies",
                column: "MatchedMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Replies_PatientId",
                schema: "reminders",
                table: "Replies",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_BookingId",
                schema: "reminders",
                table: "Messages",
                column: "BookingId");

            migrationBuilder.AddForeignKey(
                name: "FK_Replies_Messages_MatchedMessageId",
                schema: "reminders",
                table: "Replies",
                column: "MatchedMessageId",
                principalSchema: "reminders",
                principalTable: "Messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Replies_Messages_MatchedMessageId",
                schema: "reminders",
                table: "Replies");

            migrationBuilder.DropIndex(
                name: "IX_Replies_BookingId",
                schema: "reminders",
                table: "Replies");

            migrationBuilder.DropIndex(
                name: "IX_Replies_MatchedMessageId",
                schema: "reminders",
                table: "Replies");

            migrationBuilder.DropIndex(
                name: "IX_Replies_PatientId",
                schema: "reminders",
                table: "Replies");

            migrationBuilder.DropIndex(
                name: "IX_Messages_BookingId",
                schema: "reminders",
                table: "Messages");
        }
    }
}
