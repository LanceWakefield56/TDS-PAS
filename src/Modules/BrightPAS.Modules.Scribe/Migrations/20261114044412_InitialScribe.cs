using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Scribe.Migrations
{
    /// <inheritdoc />
    public partial class InitialScribe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scribe");

            migrationBuilder.CreateTable(
                name: "Consents",
                schema: "scribe",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Given = table.Column<bool>(type: "bit", nullable: false),
                    Standing = table.Column<bool>(type: "bit", nullable: false),
                    Product = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WithdrawnBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WithdrawnAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DictionaryTerms",
                schema: "scribe",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Term = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SoundsLike = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DictionaryTerms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "scribe",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Speech = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SpeechUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SpeechModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SpeechKey = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Draft = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DraftUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DraftModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DraftKey = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CloudPrivacyReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CloudApprovedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CloudApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MedowPiaReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MedowSupplierAssessment = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MedowAgreementReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MedowLaunchUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    InboundTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MedowApprovedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MedowApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AudioRetentionDays = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                schema: "scribe",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Context = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Sections = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                schema: "scribe",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Context = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersion = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Clinician = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ConsentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AudioFolder = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AudioType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    AudioChunks = table.Column<int>(type: "int", nullable: false),
                    AudioBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<decimal>(type: "decimal(9,1)", precision: 9, scale: 1, nullable: true),
                    AudioDeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SpeechEngine = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DraftEngine = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeIdentified = table.Column<bool>(type: "bit", nullable: false),
                    Draft = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignedText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EditPercent = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    NoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DiscardReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Consents_ConsentId",
                        column: x => x.ConsentId,
                        principalSchema: "scribe",
                        principalTable: "Consents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sessions_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "scribe",
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DraftSentences",
                schema: "scribe",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Sources = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftSentences", x => new { x.SessionId, x.Seq });
                    table.ForeignKey(
                        name: "FK_DraftSentences_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "scribe",
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TranscriptSegments",
                schema: "scribe",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Speaker = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StartSeconds = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    EndSeconds = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(4,3)", precision: 4, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranscriptSegments", x => new { x.SessionId, x.Seq });
                    table.ForeignKey(
                        name: "FK_TranscriptSegments_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "scribe",
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Consents_PatientId_RecordedAt",
                schema: "scribe",
                table: "Consents",
                columns: new[] { "PatientId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DictionaryTerms_Category",
                schema: "scribe",
                table: "DictionaryTerms",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_DictionaryTerms_Term",
                schema: "scribe",
                table: "DictionaryTerms",
                column: "Term",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_BookingId",
                schema: "scribe",
                table: "Sessions",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Clinician_Status",
                schema: "scribe",
                table: "Sessions",
                columns: new[] { "Clinician", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ConsentId",
                schema: "scribe",
                table: "Sessions",
                column: "ConsentId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_DocumentId",
                schema: "scribe",
                table: "Sessions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_NoteId",
                schema: "scribe",
                table: "Sessions",
                column: "NoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_PatientId",
                schema: "scribe",
                table: "Sessions",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_TemplateId",
                schema: "scribe",
                table: "Sessions",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_Code",
                schema: "scribe",
                table: "Templates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Templates_Context_Status",
                schema: "scribe",
                table: "Templates",
                columns: new[] { "Context", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DictionaryTerms",
                schema: "scribe");

            migrationBuilder.DropTable(
                name: "DraftSentences",
                schema: "scribe");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "scribe");

            migrationBuilder.DropTable(
                name: "TranscriptSegments",
                schema: "scribe");

            migrationBuilder.DropTable(
                name: "Sessions",
                schema: "scribe");

            migrationBuilder.DropTable(
                name: "Consents",
                schema: "scribe");

            migrationBuilder.DropTable(
                name: "Templates",
                schema: "scribe");
        }
    }
}
