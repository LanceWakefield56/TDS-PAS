using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Referrals.Migrations
{
    /// <inheritdoc />
    public partial class InitialReferrals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "referrals");

            migrationBuilder.CreateTable(
                name: "Referrals",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReferrerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ReferrerOrganisation = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ReferrerHpi = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Specialist = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Service = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Funder = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FunderReference = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TriagedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TriagedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TriageNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DeclineReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    UrgentDays = table.Column<int>(type: "int", nullable: false),
                    SemiUrgentDays = table.Column<int>(type: "int", nullable: false),
                    RoutineDays = table.Column<int>(type: "int", nullable: false),
                    NextReferralNumber = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Waitlist",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Procedure = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Laterality = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Anaesthetic = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Service = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Specialist = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Funder = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AddedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HoldReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    HoldUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BookedFor = table.Column<DateOnly>(type: "date", nullable: true),
                    RemovedReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StatusAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Waitlist", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_Number",
                schema: "referrals",
                table: "Referrals",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_PatientId",
                schema: "referrals",
                table: "Referrals",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_Status_ReceivedOn",
                schema: "referrals",
                table: "Referrals",
                columns: new[] { "Status", "ReceivedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Waitlist_BookingId",
                schema: "referrals",
                table: "Waitlist",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Waitlist_PatientId",
                schema: "referrals",
                table: "Waitlist",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Waitlist_Status_TargetDate",
                schema: "referrals",
                table: "Waitlist",
                columns: new[] { "Status", "TargetDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Referrals",
                schema: "referrals");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "referrals");

            migrationBuilder.DropTable(
                name: "Waitlist",
                schema: "referrals");
        }
    }
}
