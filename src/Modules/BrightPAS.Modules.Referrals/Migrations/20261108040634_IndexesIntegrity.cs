using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Referrals.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Waitlist_ReferralId",
                schema: "referrals",
                table: "Waitlist",
                column: "ReferralId");

            migrationBuilder.AddForeignKey(
                name: "FK_Waitlist_Referrals_ReferralId",
                schema: "referrals",
                table: "Waitlist",
                column: "ReferralId",
                principalSchema: "referrals",
                principalTable: "Referrals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Waitlist_Referrals_ReferralId",
                schema: "referrals",
                table: "Waitlist");

            migrationBuilder.DropIndex(
                name: "IX_Waitlist_ReferralId",
                schema: "referrals",
                table: "Waitlist");
        }
    }
}
