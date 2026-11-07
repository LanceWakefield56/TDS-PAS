using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ModuleSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModuleSetup",
                schema: "core",
                columns: table => new
                {
                    ModuleId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SetUpAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SetUpBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SkippedChecks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModuleSetup", x => x.ModuleId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModuleSetup",
                schema: "core");
        }
    }
}
