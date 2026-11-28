using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Imaging.Migrations
{
    /// <inheritdoc />
    public partial class InitialImaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "imaging");

            migrationBuilder.CreateTable(
                name: "Requests",
                schema: "imaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccessionNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StudyInstanceUid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Modality = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Examination = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BodyPart = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ClinicalNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    WantedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StudyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Studies",
                schema: "imaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyInstanceUid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MatchedBy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AccessionNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    StudyDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StudyTime = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Modalities = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    BodyPart = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReferringPhysician = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Institution = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FilePatientId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FilePatientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FileBirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FileSex = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceivedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FiledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FiledBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ImageCount = table.Column<int>(type: "int", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    Report = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ReportAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReportBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeletedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Studies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Series",
                schema: "imaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesInstanceUid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Modality = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SeriesNumber = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BodyPart = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ImageCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Series_Studies_StudyId",
                        column: x => x.StudyId,
                        principalSchema: "imaging",
                        principalTable: "Studies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Images",
                schema: "imaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SopInstanceUid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SopClassUid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InstanceNumber = table.Column<int>(type: "int", nullable: true),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    Columns = table.Column<int>(type: "int", nullable: false),
                    Frames = table.Column<int>(type: "int", nullable: false),
                    Photometric = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TransferSyntax = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Viewable = table.Column<bool>(type: "bit", nullable: false),
                    WindowCentre = table.Column<double>(type: "float", nullable: true),
                    WindowWidth = table.Column<double>(type: "float", nullable: true),
                    PixelSpacing = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Images_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalSchema: "imaging",
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Images_SeriesId",
                schema: "imaging",
                table: "Images",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Images_SopInstanceUid",
                schema: "imaging",
                table: "Images",
                column: "SopInstanceUid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Images_StudyId",
                schema: "imaging",
                table: "Images",
                column: "StudyId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_AccessionNumber",
                schema: "imaging",
                table: "Requests",
                column: "AccessionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_BookingId",
                schema: "imaging",
                table: "Requests",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_PatientId",
                schema: "imaging",
                table: "Requests",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_StudyInstanceUid",
                schema: "imaging",
                table: "Requests",
                column: "StudyInstanceUid");

            migrationBuilder.CreateIndex(
                name: "IX_Series_SeriesInstanceUid",
                schema: "imaging",
                table: "Series",
                column: "SeriesInstanceUid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Series_StudyId",
                schema: "imaging",
                table: "Series",
                column: "StudyId");

            migrationBuilder.CreateIndex(
                name: "IX_Studies_AccessionNumber",
                schema: "imaging",
                table: "Studies",
                column: "AccessionNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Studies_BookingId",
                schema: "imaging",
                table: "Studies",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Studies_PatientId_Status",
                schema: "imaging",
                table: "Studies",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Studies_Status",
                schema: "imaging",
                table: "Studies",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Studies_StudyInstanceUid",
                schema: "imaging",
                table: "Studies",
                column: "StudyInstanceUid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Images",
                schema: "imaging");

            migrationBuilder.DropTable(
                name: "Requests",
                schema: "imaging");

            migrationBuilder.DropTable(
                name: "Series",
                schema: "imaging");

            migrationBuilder.DropTable(
                name: "Studies",
                schema: "imaging");
        }
    }
}
