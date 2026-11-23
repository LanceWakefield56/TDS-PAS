using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.FollowUp.Migrations
{
    /// <inheritdoc />
    public partial class InitialFollowUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "followup");

            migrationBuilder.CreateTable(
                name: "Plans",
                schema: "followup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ProcedureContains = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "followup",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DutyMobile = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PublicUrl = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SendCheckInLinks = table.Column<bool>(type: "bit", nullable: false),
                    LinkDays = table.Column<int>(type: "int", nullable: false),
                    GpLetter = table.Column<bool>(type: "bit", nullable: false),
                    StartFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    LastScanAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextSsiNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                    table.CheckConstraint("CK_FollowUpSettings_SingleRow", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Contacts",
                schema: "followup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Procedure = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OperationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    AssignedTo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Concern = table.Column<bool>(type: "bit", nullable: false),
                    ConcernText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SuspectedSsi = table.Column<bool>(type: "bit", nullable: false),
                    Escalation = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EscalationNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EscalationClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EscalationClosedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EscalationOutcome = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DoneBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DoneHow = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LinkSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LinkExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contacts_Plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "followup",
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlanSteps",
                schema: "followup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Days = table.Column<int>(type: "int", nullable: false),
                    DaysWithImplant = table.Column<int>(type: "int", nullable: true),
                    WorkingDays = table.Column<bool>(type: "bit", nullable: false),
                    OnlyIfHighRisk = table.Column<bool>(type: "bit", nullable: false),
                    AssignTo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanSteps_Plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "followup",
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContactAnswers",
                schema: "followup",
                columns: table => new
                {
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactAnswers", x => new { x.ContactId, x.QuestionKey });
                    table.ForeignKey(
                        name: "FK_ContactAnswers_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "followup",
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SsiCases",
                schema: "followup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Procedure = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Surgeon = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    OperationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OnsetOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Signs = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false),
                    Antibiotics = table.Column<bool>(type: "bit", nullable: false),
                    ReturnToTheatre = table.Column<bool>(type: "bit", nullable: false),
                    Readmitted = table.Column<bool>(type: "bit", nullable: false),
                    Microbiology = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Class = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SurveillanceDays = table.Column<int>(type: "int", nullable: false),
                    Implant = table.Column<bool>(type: "bit", nullable: false),
                    WoundClass = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Asa = table.Column<int>(type: "int", nullable: true),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SsiCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SsiCases_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "followup",
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SsiRiskFactors",
                schema: "followup",
                columns: table => new
                {
                    SsiCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SsiRiskFactors", x => new { x.SsiCaseId, x.Name });
                    table.ForeignKey(
                        name: "FK_SsiRiskFactors_SsiCases_SsiCaseId",
                        column: x => x.SsiCaseId,
                        principalSchema: "followup",
                        principalTable: "SsiCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "followup",
                table: "Settings",
                columns: new[] { "Id", "DutyMobile", "GpLetter", "LastScanAt", "LinkDays", "NextSsiNumber", "PublicUrl", "SendCheckInLinks", "StartFrom" },
                values: new object[] { 1, null, true, null, 5, 1, null, true, null });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_BookingId",
                schema: "followup",
                table: "Contacts",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_Concern_Status",
                schema: "followup",
                table: "Contacts",
                columns: new[] { "Concern", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_PatientId",
                schema: "followup",
                table: "Contacts",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_PlanId",
                schema: "followup",
                table: "Contacts",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_Status_DueOn",
                schema: "followup",
                table: "Contacts",
                columns: new[] { "Status", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_TokenHash",
                schema: "followup",
                table: "Contacts",
                column: "TokenHash",
                unique: true,
                filter: "[TokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Name",
                schema: "followup",
                table: "Plans",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Plans_OneDefault",
                schema: "followup",
                table: "Plans",
                column: "IsDefault",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PlanSteps_PlanId_Sequence",
                schema: "followup",
                table: "PlanSteps",
                columns: new[] { "PlanId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SsiCases_BookingId",
                schema: "followup",
                table: "SsiCases",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_SsiCases_Class_OperationDate",
                schema: "followup",
                table: "SsiCases",
                columns: new[] { "Class", "OperationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SsiCases_ContactId",
                schema: "followup",
                table: "SsiCases",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_SsiCases_Number",
                schema: "followup",
                table: "SsiCases",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SsiCases_PatientId",
                schema: "followup",
                table: "SsiCases",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactAnswers",
                schema: "followup");

            migrationBuilder.DropTable(
                name: "PlanSteps",
                schema: "followup");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "followup");

            migrationBuilder.DropTable(
                name: "SsiRiskFactors",
                schema: "followup");

            migrationBuilder.DropTable(
                name: "SsiCases",
                schema: "followup");

            migrationBuilder.DropTable(
                name: "Contacts",
                schema: "followup");

            migrationBuilder.DropTable(
                name: "Plans",
                schema: "followup");
        }
    }
}
