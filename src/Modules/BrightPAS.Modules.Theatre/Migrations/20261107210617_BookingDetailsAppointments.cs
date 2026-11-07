using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Theatre.Migrations
{
    /// <inheritdoc />
    public partial class BookingDetailsAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BilledExternally",
                schema: "theatre",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Checklist",
                schema: "theatre",
                table: "Bookings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepositNote",
                schema: "theatre",
                table: "Bookings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTentative",
                schema: "theatre",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LoanItems",
                schema: "theatre",
                table: "Bookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Payment",
                schema: "theatre",
                table: "Bookings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScopeId",
                schema: "theatre",
                table: "Bookings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "TheatreIn",
                schema: "theatre",
                table: "Bookings",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "TheatreOut",
                schema: "theatre",
                table: "Bookings",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraysNeeded",
                schema: "theatre",
                table: "Bookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraysUsed",
                schema: "theatre",
                table: "Bookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Appointments",
                schema: "theatre",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Clinician = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NoCharge = table.Column<bool>(type: "bit", nullable: false),
                    IsTentative = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_Date",
                schema: "theatre",
                table: "Appointments",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PatientId",
                schema: "theatre",
                table: "Appointments",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointments",
                schema: "theatre");

            migrationBuilder.DropColumn(
                name: "BilledExternally",
                schema: "theatre",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Checklist",
                schema: "theatre",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DepositNote",
                schema: "theatre",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "IsTentative",
                schema: "theatre",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "LoanItems",
                schema: "theatre",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Payment",
                schema: "theatre",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ScopeId",
                schema: "theatre",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TheatreIn",
                schema: "theatre",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TheatreOut",
                schema: "theatre",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TraysNeeded",
                schema: "theatre",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TraysUsed",
                schema: "theatre",
                table: "Bookings");
        }
    }
}
