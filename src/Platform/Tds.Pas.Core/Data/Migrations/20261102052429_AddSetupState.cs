using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tds.Pas.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSetupState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SetupState",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    BootstrapCodeHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BootstrapCodeCreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BootstrapCodeUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InstalledVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InstalledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetupState", x => x.Id);
                    table.CheckConstraint("CK_SetupState_SingleRow", "[Id] = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SetupState",
                schema: "core");
        }
    }
}
