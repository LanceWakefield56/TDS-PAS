using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Theatre.Migrations
{
    /// <inheritdoc />
    public partial class SessionTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Templates",
                schema: "theatre",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Surgeon = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Anaesthetist = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    EveryWeeks = table.Column<int>(type: "int", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Templates",
                schema: "theatre");
        }
    }
}
