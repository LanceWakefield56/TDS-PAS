using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Triage.Migrations
{
    /// <inheritdoc />
    public partial class InitialTriage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "triage");

            migrationBuilder.CreateTable(
                name: "RuleSets",
                schema: "triage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsExample = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ApprovedRole = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RetiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "triage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    HoldBookingUntilSignedOff = table.Column<bool>(type: "bit", nullable: false),
                    HoldProcedures = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RetriageAfterDays = table.Column<int>(type: "int", nullable: false),
                    SignOffDaysBefore = table.Column<int>(type: "int", nullable: false),
                    LastScanAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                    table.CheckConstraint("CK_TriageSettings_SingleRow", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Assessments",
                schema: "triage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FormRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviousId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Trigger = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TriggerDetail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Procedure = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Anaesthetic = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProcedureDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RuleSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RuleSetVersion = table.Column<int>(type: "int", nullable: true),
                    RulesApproved = table.Column<bool>(type: "bit", nullable: false),
                    FactsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    StaffInputsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Bmi = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    StopBang = table.Column<int>(type: "int", nullable: true),
                    StopBangKnown = table.Column<int>(type: "int", nullable: true),
                    Frailty = table.Column<int>(type: "int", nullable: true),
                    Vte = table.Column<int>(type: "int", nullable: true),
                    Rcri = table.Column<int>(type: "int", nullable: true),
                    SuggestedAsa = table.Column<int>(type: "int", nullable: true),
                    ConfirmedAsa = table.Column<int>(type: "int", nullable: true),
                    SuggestedOutcome = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    FinalOutcome = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    Precautions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Missing = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangeSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Worse = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OverrideReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SignedOffBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SignedOffRole = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SignedOffAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assessments_Assessments_PreviousId",
                        column: x => x.PreviousId,
                        principalSchema: "triage",
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assessments_RuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalSchema: "triage",
                        principalTable: "RuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MedicineRules",
                schema: "triage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Keywords = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Instruction = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicineRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicineRules_RuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalSchema: "triage",
                        principalTable: "RuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SuitabilityRules",
                schema: "triage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Fact = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Operator = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Threshold = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: true),
                    TextValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProcedureContains = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Anaesthetic = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Precaution = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuitabilityRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuitabilityRules_RuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalSchema: "triage",
                        principalTable: "RuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Findings",
                schema: "triage",
                columns: table => new
                {
                    AssessmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Precaution = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Findings", x => new { x.AssessmentId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_Findings_Assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalSchema: "triage",
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "triage",
                table: "Settings",
                columns: new[] { "Id", "HoldBookingUntilSignedOff", "HoldProcedures", "LastScanAt", "RetriageAfterDays", "SignOffDaysBefore" },
                values: new object[] { 1, false, null, null, 90, 7 });

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_BookingId",
                schema: "triage",
                table: "Assessments",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_FormRequestId",
                schema: "triage",
                table: "Assessments",
                column: "FormRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_PatientId_Status",
                schema: "triage",
                table: "Assessments",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_PreviousId",
                schema: "triage",
                table: "Assessments",
                column: "PreviousId");

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_RuleSetId",
                schema: "triage",
                table: "Assessments",
                column: "RuleSetId");

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_Status_ProcedureDate",
                schema: "triage",
                table: "Assessments",
                columns: new[] { "Status", "ProcedureDate" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicineRules_RuleSetId",
                schema: "triage",
                table: "MedicineRules",
                column: "RuleSetId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleSets_Version",
                schema: "triage",
                table: "RuleSets",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RuleSets_OneApproved",
                schema: "triage",
                table: "RuleSets",
                column: "Status",
                unique: true,
                filter: "[Status] = 'Approved'");

            migrationBuilder.CreateIndex(
                name: "IX_SuitabilityRules_RuleSetId_Code",
                schema: "triage",
                table: "SuitabilityRules",
                columns: new[] { "RuleSetId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Findings",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "MedicineRules",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "SuitabilityRules",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "Assessments",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "RuleSets",
                schema: "triage");
        }
    }
}
