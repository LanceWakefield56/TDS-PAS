using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Identity.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BreakGlassGrants_PatientId",
                schema: "identity",
                table: "BreakGlassGrants",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BreakGlassGrants_PatientId",
                schema: "identity",
                table: "BreakGlassGrants");
        }
    }
}
