using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClinicianColour : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Colour",
                schema: "core",
                table: "Clinicians",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Colour",
                schema: "core",
                table: "Clinicians");
        }
    }
}
