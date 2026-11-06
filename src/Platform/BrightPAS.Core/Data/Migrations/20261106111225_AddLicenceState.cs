using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenceState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LicenceState",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenceText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LicenceInstalledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LicenceInstalledBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActivationText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActivationCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActivationError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastKnownModules = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenceState", x => x.Id);
                    table.CheckConstraint("CK_LicenceState_SingleRow", "[Id] = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicenceState",
                schema: "core");
        }
    }
}
