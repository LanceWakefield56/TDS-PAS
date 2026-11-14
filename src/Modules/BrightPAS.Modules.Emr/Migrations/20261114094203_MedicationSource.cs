using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Emr.Migrations
{
    /// <inheritdoc />
    public partial class MedicationSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalRef",
                schema: "emr",
                table: "MedicationsGiven",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "emr",
                table: "MedicationsGiven",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.CreateIndex(
                name: "IX_MedicationsGiven_ExternalRef",
                schema: "emr",
                table: "MedicationsGiven",
                column: "ExternalRef");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MedicationsGiven_ExternalRef",
                schema: "emr",
                table: "MedicationsGiven");

            migrationBuilder.DropColumn(
                name: "ExternalRef",
                schema: "emr",
                table: "MedicationsGiven");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "emr",
                table: "MedicationsGiven");
        }
    }
}
