using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Coding.Migrations
{
    /// <inheritdoc />
    public partial class InitialCoding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "coding");

            migrationBuilder.CreateTable(
                name: "Advice",
                schema: "coding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Edition = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    AppliesToNz = table.Column<bool>(type: "bit", nullable: false),
                    ReviewedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CodePrefixes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Link = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Advice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Codes",
                schema: "coding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    System = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Sex = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    AgeFromYears = table.Column<int>(type: "int", nullable: true),
                    AgeToYears = table.Column<int>(type: "int", nullable: true),
                    NeverAssign = table.Column<bool>(type: "bit", nullable: false),
                    NotPrincipal = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Codes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Episodes",
                schema: "coding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdmissionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DischargeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PurchaserCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    AdmissionSource = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    EventEndType = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    NotReportableReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CodedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CodedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CoderNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HealthNzError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NbrsBatches",
                schema: "coding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Records = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Accepted = table.Column<int>(type: "int", nullable: false),
                    Rejected = table.Column<int>(type: "int", nullable: false),
                    Warnings = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NbrsBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NbrsEntries",
                schema: "coding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalId = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WaitlistEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FacilityCode = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    BookingSource = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    ReferralDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FirstAssessmentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CpacDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CpacScore = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    CpacSystem = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PurchaserCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    HealthSpecialty = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ClinicalCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    ClinicalCodeType = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    DomicileCode = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ProfessionalGroup = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    ResponsibilityCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AssessorCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AssessorGroup = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    StagedFlag = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NbrsEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcedureMaps",
                schema: "coding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Term = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ProcedureCodes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DiagnosisCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcedureMaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Registrations",
                schema: "coding",
                columns: table => new
                {
                    ClinicianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfessionalGroup = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Registrations", x => x.ClinicianId);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "coding",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CodingSystem = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    EditionName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    HealthAgencyCode = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    FacilityCode = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    FileAcronym = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    PurchaserSelfFunded = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    PurchaserInsurer = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    PurchaserAcc = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    PurchaserHealthNz = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    PurchaserNoCharge = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    TdsReportsContractedEvents = table.Column<bool>(type: "bit", nullable: false),
                    ReportWithinDays = table.Column<int>(type: "int", nullable: false),
                    NbrsAgencyCode = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    NbrsEnvironment = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    NextNbrsBatchCmpl = table.Column<int>(type: "int", nullable: false),
                    NextNbrsBatchProd = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Submissions",
                schema: "coding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    Records = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    IsResubmission = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Submissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                schema: "coding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodedEpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    DiagnosisType = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Items_Episodes_CodedEpisodeId",
                        column: x => x.CodedEpisodeId,
                        principalSchema: "coding",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NbrsEvents",
                schema: "coding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    StatusCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    StatusDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CertaintyDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BookingMadeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BookedForDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DeferredBy = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    ExitDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExitCategory = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    ContractAgency = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    TreatmentFacility = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ClientSystemId = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
                    EventLocalId = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LineNumber = table.Column<int>(type: "int", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Messages = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NbrsEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NbrsEvents_NbrsBatches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "coding",
                        principalTable: "NbrsBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NbrsEvents_NbrsEntries_EntryId",
                        column: x => x.EntryId,
                        principalSchema: "coding",
                        principalTable: "NbrsEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionEvents",
                schema: "coding",
                columns: table => new
                {
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodedEpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionEvents", x => new { x.SubmissionId, x.CodedEpisodeId });
                    table.ForeignKey(
                        name: "FK_SubmissionEvents_Episodes_CodedEpisodeId",
                        column: x => x.CodedEpisodeId,
                        principalSchema: "coding",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubmissionEvents_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalSchema: "coding",
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Advice_Reference",
                schema: "coding",
                table: "Advice",
                column: "Reference");

            migrationBuilder.CreateIndex(
                name: "IX_Codes_Description",
                schema: "coding",
                table: "Codes",
                column: "Description");

            migrationBuilder.CreateIndex(
                name: "IX_Codes_System_Code_Type",
                schema: "coding",
                table: "Codes",
                columns: new[] { "System", "Code", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_EpisodeId",
                schema: "coding",
                table: "Episodes",
                column: "EpisodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_PatientId",
                schema: "coding",
                table: "Episodes",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_Status_DischargeDate",
                schema: "coding",
                table: "Episodes",
                columns: new[] { "Status", "DischargeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SubmissionId",
                schema: "coding",
                table: "Episodes",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_CodedEpisodeId_Sequence",
                schema: "coding",
                table: "Items",
                columns: new[] { "CodedEpisodeId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NbrsBatches_Environment_Number",
                schema: "coding",
                table: "NbrsBatches",
                columns: new[] { "Environment", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NbrsEntries_FacilityCode_LocalId",
                schema: "coding",
                table: "NbrsEntries",
                columns: new[] { "FacilityCode", "LocalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NbrsEntries_PatientId",
                schema: "coding",
                table: "NbrsEntries",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_NbrsEntries_WaitlistEntryId",
                schema: "coding",
                table: "NbrsEntries",
                column: "WaitlistEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_NbrsEvents_BatchId",
                schema: "coding",
                table: "NbrsEvents",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_NbrsEvents_EntryId",
                schema: "coding",
                table: "NbrsEvents",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_NbrsEvents_Result_CreatedAt",
                schema: "coding",
                table: "NbrsEvents",
                columns: new[] { "Result", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureMaps_Term",
                schema: "coding",
                table: "ProcedureMaps",
                column: "Term",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionEvents_CodedEpisodeId",
                schema: "coding",
                table: "SubmissionEvents",
                column: "CodedEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_CreatedAt",
                schema: "coding",
                table: "Submissions",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Advice",
                schema: "coding");

            migrationBuilder.DropTable(
                name: "Codes",
                schema: "coding");

            migrationBuilder.DropTable(
                name: "Items",
                schema: "coding");

            migrationBuilder.DropTable(
                name: "NbrsEvents",
                schema: "coding");

            migrationBuilder.DropTable(
                name: "ProcedureMaps",
                schema: "coding");

            migrationBuilder.DropTable(
                name: "Registrations",
                schema: "coding");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "coding");

            migrationBuilder.DropTable(
                name: "SubmissionEvents",
                schema: "coding");

            migrationBuilder.DropTable(
                name: "NbrsBatches",
                schema: "coding");

            migrationBuilder.DropTable(
                name: "NbrsEntries",
                schema: "coding");

            migrationBuilder.DropTable(
                name: "Episodes",
                schema: "coding");

            migrationBuilder.DropTable(
                name: "Submissions",
                schema: "coding");
        }
    }
}
