using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Rostering.Migrations
{
    /// <inheritdoc />
    public partial class InitialRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "roster");

            migrationBuilder.CreateTable(
                name: "Leave",
                schema: "roster",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leave", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                schema: "roster",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    AllDay = table.Column<bool>(type: "bit", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time", nullable: true),
                    End = table.Column<TimeOnly>(type: "time", nullable: true),
                    BreakMinutes = table.Column<int>(type: "int", nullable: false),
                    Area = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsTentative = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Staff",
                schema: "roster",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Colour = table.Column<int>(type: "int", nullable: false),
                    ContractedHours = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Staff", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                schema: "roster",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AllDay = table.Column<bool>(type: "bit", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time", nullable: true),
                    End = table.Column<TimeOnly>(type: "time", nullable: true),
                    BreakMinutes = table.Column<int>(type: "int", nullable: false),
                    Area = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EveryWeeks = table.Column<int>(type: "int", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leave_StaffId_From",
                schema: "roster",
                table: "Leave",
                columns: new[] { "StaffId", "From" });

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_Date_StaffId",
                schema: "roster",
                table: "Shifts",
                columns: new[] { "Date", "StaffId" });

            migrationBuilder.CreateIndex(
                name: "IX_Staff_Name",
                schema: "roster",
                table: "Staff",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Leave",
                schema: "roster");

            migrationBuilder.DropTable(
                name: "Shifts",
                schema: "roster");

            migrationBuilder.DropTable(
                name: "Staff",
                schema: "roster");

            migrationBuilder.DropTable(
                name: "Templates",
                schema: "roster");
        }
    }
}
