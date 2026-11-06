using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Labs.Migrations
{
    /// <inheritdoc />
    public partial class InitialLabs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "labs");

            migrationBuilder.CreateTable(
                name: "Messages",
                schema: "labs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ControlId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SendingFacility = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MessageType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Raw = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReceivedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                schema: "labs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MatchStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MatchNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    MatchedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PatientNhi = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    PatientMrn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PatientFamilyName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PatientGivenNames = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PatientDateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Laboratory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PlacerOrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AccessionNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TestCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TestName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    RequestedByCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CollectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResultStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsAbnormal = table.Column<bool>(type: "bit", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SupersededById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AcknowledgementComment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Observations",
                schema: "labs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ValueType = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Units = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReferenceRange = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Flag = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Observations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Observations_Reports_ReportId",
                        column: x => x.ReportId,
                        principalSchema: "labs",
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ReceivedAt",
                schema: "labs",
                table: "Messages",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SendingFacility_ControlId",
                schema: "labs",
                table: "Messages",
                columns: new[] { "SendingFacility", "ControlId" });

            migrationBuilder.CreateIndex(
                name: "IX_Observations_ReportId",
                schema: "labs",
                table: "Observations",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_AcknowledgedAt_ReceivedAt",
                schema: "labs",
                table: "Reports",
                columns: new[] { "AcknowledgedAt", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Laboratory_AccessionNumber_TestCode",
                schema: "labs",
                table: "Reports",
                columns: new[] { "Laboratory", "AccessionNumber", "TestCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_PatientId",
                schema: "labs",
                table: "Reports",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Messages",
                schema: "labs");

            migrationBuilder.DropTable(
                name: "Observations",
                schema: "labs");

            migrationBuilder.DropTable(
                name: "Reports",
                schema: "labs");
        }
    }
}
