using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Referrals.Migrations
{
    /// <inheritdoc />
    public partial class NbrsCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NbrsAgencyCode",
                schema: "referrals",
                table: "Settings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NbrsFacilityCode",
                schema: "referrals",
                table: "Settings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NbrsSpecialtyEndoscopy",
                schema: "referrals",
                table: "Settings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NbrsSpecialtySurgical",
                schema: "referrals",
                table: "Settings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NbrsAgencyCode",
                schema: "referrals",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NbrsFacilityCode",
                schema: "referrals",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NbrsSpecialtyEndoscopy",
                schema: "referrals",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NbrsSpecialtySurgical",
                schema: "referrals",
                table: "Settings");
        }
    }
}
