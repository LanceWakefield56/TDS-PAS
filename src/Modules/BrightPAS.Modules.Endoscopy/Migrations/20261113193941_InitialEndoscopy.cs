using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Endoscopy.Migrations
{
    /// <inheritdoc />
    public partial class InitialEndoscopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "endo");

            migrationBuilder.CreateTable(
                name: "AdviceTexts",
                schema: "endo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdviceTexts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Procedures",
                schema: "endo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EndoscopistId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EndoscopistName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Assistants = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpperScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LowerScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TowerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccessionNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StudyInstanceUid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NbspScreening = table.Column<bool>(type: "bit", nullable: false),
                    NbspNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    OtherIndication = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ConsentConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    Sedation = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SedationNotes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Co2 = table.Column<bool>(type: "bit", nullable: false),
                    WaterAssisted = table.Column<bool>(type: "bit", nullable: false),
                    ScopeGuide = table.Column<bool>(type: "bit", nullable: false),
                    PositionChange = table.Column<bool>(type: "bit", nullable: false),
                    AbdominalPressure = table.Column<bool>(type: "bit", nullable: false),
                    UpperStartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpperEndAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpperExtent = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Retroflexion = table.Column<bool>(type: "bit", nullable: true),
                    UpperPhotoDocumentation = table.Column<bool>(type: "bit", nullable: true),
                    InsertedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CaecumAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WithdrawalStartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LowerExtent = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AppendicealOrifice = table.Column<bool>(type: "bit", nullable: true),
                    IleocaecalValve = table.Column<bool>(type: "bit", nullable: true),
                    TerminalIleum = table.Column<bool>(type: "bit", nullable: true),
                    CaecumPhotographed = table.Column<bool>(type: "bit", nullable: true),
                    BbpsRight = table.Column<int>(type: "int", nullable: true),
                    BbpsTransverse = table.Column<int>(type: "int", nullable: true),
                    BbpsLeft = table.Column<int>(type: "int", nullable: true),
                    RectalRetroflexion = table.Column<bool>(type: "bit", nullable: true),
                    IncompleteReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Comfort = table.Column<int>(type: "int", nullable: true),
                    Complication = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ComplicationDetails = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Diagnosis = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Recommendations = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FollowUp = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SurveillanceMonths = table.Column<int>(type: "int", nullable: true),
                    PatientAdvice = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    MppsUid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SignedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Procedures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Findings",
                schema: "endo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Site = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    SizeMm = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Findings_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalSchema: "endo",
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Interventions",
                schema: "endo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Site = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Count = table.Column<int>(type: "int", nullable: true),
                    Pot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Result = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interventions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Interventions_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalSchema: "endo",
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Polyps",
                schema: "endo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Site = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DistanceCm = table.Column<int>(type: "int", nullable: true),
                    SizeMm = table.Column<int>(type: "int", nullable: false),
                    Paris = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Nice = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Jnet = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Technique = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Lifted = table.Column<bool>(type: "bit", nullable: true),
                    Resection = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Retrieved = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Pot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Complete = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Clips = table.Column<int>(type: "int", nullable: false),
                    Tattooed = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Histology = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Dysplasia = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    HistologyNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HistologyBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HistologyAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Polyps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Polyps_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalSchema: "endo",
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcedureIndications",
                schema: "endo",
                columns: table => new
                {
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcedureIndications", x => new { x.ProcedureId, x.Code });
                    table.ForeignKey(
                        name: "FK_ProcedureIndications_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalSchema: "endo",
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tattoos",
                schema: "endo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Site = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DistanceCm = table.Column<int>(type: "int", nullable: true),
                    Position = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Ink = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    VolumeMl = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    Injections = table.Column<int>(type: "int", nullable: true),
                    PolypId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tattoos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tattoos_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalSchema: "endo",
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Media",
                schema: "endo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Chunks = table.Column<int>(type: "int", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CapturedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DurationSeconds = table.Column<decimal>(type: "decimal(9,1)", precision: 9, scale: 1, nullable: true),
                    Site = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Caption = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    InReport = table.Column<bool>(type: "bit", nullable: false),
                    InPatientReport = table.Column<bool>(type: "bit", nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    FindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolypId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SopInstanceUid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DicomPatientId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DicomPatientName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DicomAccession = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Media", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Media_Findings_FindingId",
                        column: x => x.FindingId,
                        principalSchema: "endo",
                        principalTable: "Findings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Media_Polyps_PolypId",
                        column: x => x.PolypId,
                        principalSchema: "endo",
                        principalTable: "Polyps",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Media_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalSchema: "endo",
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdviceTexts_Key",
                schema: "endo",
                table: "AdviceTexts",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Findings_ProcedureId_Number",
                schema: "endo",
                table: "Findings",
                columns: new[] { "ProcedureId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Interventions_ProcedureId",
                schema: "endo",
                table: "Interventions",
                column: "ProcedureId");

            migrationBuilder.CreateIndex(
                name: "IX_Media_FindingId",
                schema: "endo",
                table: "Media",
                column: "FindingId");

            migrationBuilder.CreateIndex(
                name: "IX_Media_PatientId",
                schema: "endo",
                table: "Media",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Media_PolypId",
                schema: "endo",
                table: "Media",
                column: "PolypId");

            migrationBuilder.CreateIndex(
                name: "IX_Media_ProcedureId_Sort",
                schema: "endo",
                table: "Media",
                columns: new[] { "ProcedureId", "Sort" });

            migrationBuilder.CreateIndex(
                name: "IX_Media_SopInstanceUid",
                schema: "endo",
                table: "Media",
                column: "SopInstanceUid",
                unique: true,
                filter: "[SopInstanceUid] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Media_Status",
                schema: "endo",
                table: "Media",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Polyps_ProcedureId_Number",
                schema: "endo",
                table: "Polyps",
                columns: new[] { "ProcedureId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_AccessionNumber",
                schema: "endo",
                table: "Procedures",
                column: "AccessionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_BookingId",
                schema: "endo",
                table: "Procedures",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_EndoscopistId",
                schema: "endo",
                table: "Procedures",
                column: "EndoscopistId");

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_LowerScopeId",
                schema: "endo",
                table: "Procedures",
                column: "LowerScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_PatientId",
                schema: "endo",
                table: "Procedures",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_Status_Kind",
                schema: "endo",
                table: "Procedures",
                columns: new[] { "Status", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_TowerId",
                schema: "endo",
                table: "Procedures",
                column: "TowerId");

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_UpperScopeId",
                schema: "endo",
                table: "Procedures",
                column: "UpperScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_Tattoos_PolypId",
                schema: "endo",
                table: "Tattoos",
                column: "PolypId");

            migrationBuilder.CreateIndex(
                name: "IX_Tattoos_ProcedureId",
                schema: "endo",
                table: "Tattoos",
                column: "ProcedureId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdviceTexts",
                schema: "endo");

            migrationBuilder.DropTable(
                name: "Interventions",
                schema: "endo");

            migrationBuilder.DropTable(
                name: "Media",
                schema: "endo");

            migrationBuilder.DropTable(
                name: "ProcedureIndications",
                schema: "endo");

            migrationBuilder.DropTable(
                name: "Tattoos",
                schema: "endo");

            migrationBuilder.DropTable(
                name: "Findings",
                schema: "endo");

            migrationBuilder.DropTable(
                name: "Polyps",
                schema: "endo");

            migrationBuilder.DropTable(
                name: "Procedures",
                schema: "endo");
        }
    }
}
