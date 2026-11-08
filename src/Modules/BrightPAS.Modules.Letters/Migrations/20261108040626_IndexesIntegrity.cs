using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Letters.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Letters_BookingId",
                schema: "letters",
                table: "Letters",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_GpPracticeId",
                schema: "letters",
                table: "Letters",
                column: "GpPracticeId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_ReferralId",
                schema: "letters",
                table: "Letters",
                column: "ReferralId");

            migrationBuilder.AddForeignKey(
                name: "FK_Letters_Practices_GpPracticeId",
                schema: "letters",
                table: "Letters",
                column: "GpPracticeId",
                principalSchema: "letters",
                principalTable: "Practices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Letters_Practices_GpPracticeId",
                schema: "letters",
                table: "Letters");

            migrationBuilder.DropIndex(
                name: "IX_Letters_BookingId",
                schema: "letters",
                table: "Letters");

            migrationBuilder.DropIndex(
                name: "IX_Letters_GpPracticeId",
                schema: "letters",
                table: "Letters");

            migrationBuilder.DropIndex(
                name: "IX_Letters_ReferralId",
                schema: "letters",
                table: "Letters");
        }
    }
}
