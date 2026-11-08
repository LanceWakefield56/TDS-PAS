using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Credentialing.Migrations
{
    /// <inheritdoc />
    public partial class IndexesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiredTypes",
                schema: "cred",
                table: "Settings");

            migrationBuilder.CreateTable(
                name: "RequiredTypes",
                schema: "cred",
                columns: table => new
                {
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequiredTypes", x => x.Type);
                });

            migrationBuilder.InsertData(
                schema: "cred",
                table: "RequiredTypes",
                column: "Type",
                values: new object[]
                {
                    "Apc",
                    "CredentialingApproval",
                    "Indemnity"
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequiredTypes",
                schema: "cred");

            migrationBuilder.AddColumn<string>(
                name: "RequiredTypes",
                schema: "cred",
                table: "Settings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
