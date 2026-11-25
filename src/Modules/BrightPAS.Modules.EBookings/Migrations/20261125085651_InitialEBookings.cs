using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.EBookings.Migrations
{
    /// <inheritdoc />
    public partial class InitialEBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ebookings");

            migrationBuilder.CreateTable(
                name: "Requests",
                schema: "ebookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExternalRef = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    SpecialistId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomsName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Nhi = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    FamilyName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GivenNames = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    Sex = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MobilePhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AddressLine1 = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Suburb = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Postcode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    InterpreterLanguage = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Service = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProcedureItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Procedure = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Laterality = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Anaesthetic = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Funder = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FunderReference = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    InsurerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EarliestDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LatestDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Availability = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClinicalNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    WeightKg = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    HeightCm = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    Asa = table.Column<int>(type: "int", nullable: true),
                    Diabetic = table.Column<bool>(type: "bit", nullable: false),
                    SleepApnoea = table.Column<bool>(type: "bit", nullable: false),
                    Anticoagulated = table.Column<bool>(type: "bit", nullable: false),
                    AnticoagulantDetails = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Allergies = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Comorbidities = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResponsibleAdultArranged = table.Column<bool>(type: "bit", nullable: false),
                    ConsentSigned = table.Column<bool>(type: "bit", nullable: false),
                    ConsentSignedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StatusAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeclineReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WaitlistEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "ebookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false),
                    WatchFolder = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    EmailRooms = table.Column<bool>(type: "bit", nullable: false),
                    MaxFileMegabytes = table.Column<int>(type: "int", nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastProblem = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Files",
                schema: "ebookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AddedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PatientDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Files_Requests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "ebookings",
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                schema: "ebookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromTds = table.Column<bool>(type: "bit", nullable: false),
                    Author = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Requests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "ebookings",
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "ebookings",
                table: "Settings",
                columns: new[] { "Id", "EmailRooms", "Instructions", "LastCheckedAt", "LastProblem", "MaxFileMegabytes", "NextNumber", "UpdatedAt", "UpdatedBy", "WatchFolder" },
                values: new object[] { 1, false, null, null, null, 10, 1, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Files_PatientDocumentId",
                schema: "ebookings",
                table: "Files",
                column: "PatientDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Files_RequestId",
                schema: "ebookings",
                table: "Files",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_RequestId_At",
                schema: "ebookings",
                table: "Messages",
                columns: new[] { "RequestId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_ExternalRef",
                schema: "ebookings",
                table: "Requests",
                column: "ExternalRef");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Nhi",
                schema: "ebookings",
                table: "Requests",
                column: "Nhi");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Number",
                schema: "ebookings",
                table: "Requests",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_PatientId",
                schema: "ebookings",
                table: "Requests",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_ProcedureItemId",
                schema: "ebookings",
                table: "Requests",
                column: "ProcedureItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_ReferralId",
                schema: "ebookings",
                table: "Requests",
                column: "ReferralId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_SpecialistId",
                schema: "ebookings",
                table: "Requests",
                column: "SpecialistId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Status_SubmittedAt",
                schema: "ebookings",
                table: "Requests",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_WaitlistEntryId",
                schema: "ebookings",
                table: "Requests",
                column: "WaitlistEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Files",
                schema: "ebookings");

            migrationBuilder.DropTable(
                name: "Messages",
                schema: "ebookings");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "ebookings");

            migrationBuilder.DropTable(
                name: "Requests",
                schema: "ebookings");
        }
    }
}
