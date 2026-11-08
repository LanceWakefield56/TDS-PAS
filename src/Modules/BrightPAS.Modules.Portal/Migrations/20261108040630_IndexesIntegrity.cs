using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Portal.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Requests_AccessId",
                schema: "portal",
                table: "Requests",
                column: "AccessId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_BookingId",
                schema: "portal",
                table: "Requests",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Delegates_ClinicianId",
                schema: "portal",
                table: "Delegates",
                column: "ClinicianId");

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Access_AccessId",
                schema: "portal",
                table: "Requests",
                column: "AccessId",
                principalSchema: "portal",
                principalTable: "Access",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Access_AccessId",
                schema: "portal",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_AccessId",
                schema: "portal",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_BookingId",
                schema: "portal",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Delegates_ClinicianId",
                schema: "portal",
                table: "Delegates");
        }
    }
}
