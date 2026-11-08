using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Billing.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Documents_BookingId",
                schema: "billing",
                table: "Documents",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_SourceEstimateId",
                schema: "billing",
                table: "Documents",
                column: "SourceEstimateId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Documents_SourceEstimateId",
                schema: "billing",
                table: "Documents",
                column: "SourceEstimateId",
                principalSchema: "billing",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Documents_SourceEstimateId",
                schema: "billing",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_BookingId",
                schema: "billing",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_SourceEstimateId",
                schema: "billing",
                table: "Documents");
        }
    }
}
