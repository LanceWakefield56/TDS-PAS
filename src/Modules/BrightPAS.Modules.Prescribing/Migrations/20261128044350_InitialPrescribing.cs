using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Prescribing.Migrations
{
    /// <inheritdoc />
    public partial class InitialPrescribing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "rx");

            migrationBuilder.CreateTable(
                name: "Orders",
                schema: "rx",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MedicineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Medicine = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Dose = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Route = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Indication = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PrescribedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PrescribedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StoppedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StoppedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StopReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TimesGiven = table.Column<int>(type: "int", nullable: false),
                    LastGivenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Prescribers",
                schema: "rx",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    HpiCpn = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Registration = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prescribers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scans",
                schema: "rx",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MedicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Medicine = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    User = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PatientScanned = table.Column<bool>(type: "bit", nullable: false),
                    PatientMatched = table.Column<bool>(type: "bit", nullable: true),
                    PackScanned = table.Column<bool>(type: "bit", nullable: false),
                    Gtin = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
                    Batch = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    Serial = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PackMatched = table.Column<bool>(type: "bit", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OverrideReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Findings = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "rx",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    BcmaMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WarnWithoutOrder = table.Column<bool>(type: "bit", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NzepsUrl = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NzepsClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NzepsKeyProtected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FacilityHpi = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ExportFolder = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    NextNumber = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Prescriptions",
                schema: "rx",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NzepsId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PrescriberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SendReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SendError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TimesPrinted = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prescriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prescriptions_Prescribers_PrescriberId",
                        column: x => x.PrescriberId,
                        principalSchema: "rx",
                        principalTable: "Prescribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                schema: "rx",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MedicineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Medicine = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NzmtId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Directions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Repeats = table.Column<int>(type: "int", nullable: false),
                    Controlled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Items_Prescriptions_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalSchema: "rx",
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Items_PrescriptionId",
                schema: "rx",
                table: "Items",
                column: "PrescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_EpisodeId",
                schema: "rx",
                table: "Orders",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PatientId",
                schema: "rx",
                table: "Orders",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Prescribers_UserName",
                schema: "rx",
                table: "Prescribers",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_Number",
                schema: "rx",
                table: "Prescriptions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_NzepsId",
                schema: "rx",
                table: "Prescriptions",
                column: "NzepsId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_PatientId",
                schema: "rx",
                table: "Prescriptions",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_PrescriberId",
                schema: "rx",
                table: "Prescriptions",
                column: "PrescriberId");

            migrationBuilder.CreateIndex(
                name: "IX_Scans_At",
                schema: "rx",
                table: "Scans",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_Scans_EpisodeId",
                schema: "rx",
                table: "Scans",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Scans_MedicationId",
                schema: "rx",
                table: "Scans",
                column: "MedicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Scans_PatientId",
                schema: "rx",
                table: "Scans",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Items",
                schema: "rx");

            migrationBuilder.DropTable(
                name: "Orders",
                schema: "rx");

            migrationBuilder.DropTable(
                name: "Scans",
                schema: "rx");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "rx");

            migrationBuilder.DropTable(
                name: "Prescriptions",
                schema: "rx");

            migrationBuilder.DropTable(
                name: "Prescribers",
                schema: "rx");
        }
    }
}
