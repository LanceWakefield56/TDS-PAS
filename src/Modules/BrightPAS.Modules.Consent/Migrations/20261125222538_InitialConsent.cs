using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Consent.Migrations
{
    /// <inheritdoc />
    public partial class InitialConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "consent");

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "consent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false),
                    PublicUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LinkDays = table.Column<int>(type: "int", nullable: false),
                    RequireBeforeConfirm = table.Column<bool>(type: "bit", nullable: false),
                    PrepareDaysAhead = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sheets",
                schema: "consent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ProcedureWords = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    About = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Benefits = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Alternatives = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    NoTreatment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Anaesthetic = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Preparation = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Recovery = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovalNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RetiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sheets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Consents",
                schema: "consent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Procedure = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Laterality = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClinicianId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClinicianName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Snapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExtraRisks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NoteToPatient = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LinkSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LinkExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SectionsRead = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Signer = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SignerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SignerRelationship = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Interpreter = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SignatureImage = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SignedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConfirmNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    WithdrawnAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WithdrawnBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WithdrawnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Consents_Sheets_SheetId",
                        column: x => x.SheetId,
                        principalSchema: "consent",
                        principalTable: "Sheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Links",
                schema: "consent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Links_Sheets_SheetId",
                        column: x => x.SheetId,
                        principalSchema: "consent",
                        principalTable: "Sheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Risks",
                schema: "consent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Serious = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Risks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Risks_Sheets_SheetId",
                        column: x => x.SheetId,
                        principalSchema: "consent",
                        principalTable: "Sheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Questions",
                schema: "consent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AskedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AnsweredBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Questions_Consents_ConsentId",
                        column: x => x.ConsentId,
                        principalSchema: "consent",
                        principalTable: "Consents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "consent",
                table: "Settings",
                columns: new[] { "Id", "LinkDays", "NextNumber", "PrepareDaysAhead", "PublicUrl", "RequireBeforeConfirm", "UpdatedAt", "UpdatedBy" },
                values: new object[] { 1, 14, 1, 28, null, false, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Consents_BookingId",
                schema: "consent",
                table: "Consents",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Consents_ClinicianId",
                schema: "consent",
                table: "Consents",
                column: "ClinicianId");

            migrationBuilder.CreateIndex(
                name: "IX_Consents_DocumentId",
                schema: "consent",
                table: "Consents",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Consents_Number",
                schema: "consent",
                table: "Consents",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Consents_PatientId",
                schema: "consent",
                table: "Consents",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Consents_SheetId",
                schema: "consent",
                table: "Consents",
                column: "SheetId");

            migrationBuilder.CreateIndex(
                name: "IX_Consents_Status",
                schema: "consent",
                table: "Consents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Consents_TokenHash",
                schema: "consent",
                table: "Consents",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_Links_SheetId_Order",
                schema: "consent",
                table: "Links",
                columns: new[] { "SheetId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_ConsentId_AskedAt",
                schema: "consent",
                table: "Questions",
                columns: new[] { "ConsentId", "AskedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Risks_SheetId_Order",
                schema: "consent",
                table: "Risks",
                columns: new[] { "SheetId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_Sheets_Code_Version",
                schema: "consent",
                table: "Sheets",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sheets_Status",
                schema: "consent",
                table: "Sheets",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Links",
                schema: "consent");

            migrationBuilder.DropTable(
                name: "Questions",
                schema: "consent");

            migrationBuilder.DropTable(
                name: "Risks",
                schema: "consent");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "consent");

            migrationBuilder.DropTable(
                name: "Consents",
                schema: "consent");

            migrationBuilder.DropTable(
                name: "Sheets",
                schema: "consent");
        }
    }
}
