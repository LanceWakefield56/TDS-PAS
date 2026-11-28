using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Triage.Migrations
{
    /// <inheritdoc />
    public partial class EwsAndReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EwsOnTheDay",
                schema: "triage",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EwsReviewAt",
                schema: "triage",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReviewMinutes",
                schema: "triage",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VideoLinkTemplate",
                schema: "triage",
                table: "Settings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Reviews",
                schema: "triage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AnaesthetistId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnaesthetistName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    JoinLink = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DoneBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reviews_Assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalSchema: "triage",
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                schema: "triage",
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "EwsOnTheDay", "EwsReviewAt", "ReviewMinutes", "VideoLinkTemplate" },
                values: new object[] { false, 5, 15, null });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_AnaesthetistId_StartsAt",
                schema: "triage",
                table: "Reviews",
                columns: new[] { "AnaesthetistId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_AssessmentId",
                schema: "triage",
                table: "Reviews",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_BookingId",
                schema: "triage",
                table: "Reviews",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_PatientId",
                schema: "triage",
                table: "Reviews",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reviews",
                schema: "triage");

            migrationBuilder.DropColumn(
                name: "EwsOnTheDay",
                schema: "triage",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "EwsReviewAt",
                schema: "triage",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReviewMinutes",
                schema: "triage",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "VideoLinkTemplate",
                schema: "triage",
                table: "Settings");
        }
    }
}
