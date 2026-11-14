using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Devices.Migrations
{
    /// <inheritdoc />
    public partial class InitialDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "devices");

            migrationBuilder.CreateTable(
                name: "AdtSends",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Event = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    By = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdtSends", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnaestheticRecords",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CaseNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ControlId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsCorrection = table.Column<bool>(type: "bit", nullable: false),
                    SupersededById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Nhi = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Anaesthetist = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Asa = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Technique = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Airway = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Comments = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnaestheticRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnaestheticRecords_AnaestheticRecords_SupersededById",
                        column: x => x.SupersededById,
                        principalSchema: "devices",
                        principalTable: "AnaestheticRecords",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Monitors",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastNhi = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Monitors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    TheatreChartMinutes = table.Column<int>(type: "int", nullable: false),
                    RecoveryChartMinutes = table.Column<int>(type: "int", nullable: false),
                    WardChartMinutes = table.Column<int>(type: "int", nullable: false),
                    StoreEverySeconds = table.Column<int>(type: "int", nullable: false),
                    OfflineAfterSeconds = table.Column<int>(type: "int", nullable: false),
                    UnassociatedKeepHours = table.Column<int>(type: "int", nullable: false),
                    KeepMonitorMessagesHours = table.Column<int>(type: "int", nullable: false),
                    AutoConnectByNhi = table.Column<bool>(type: "bit", nullable: false),
                    SaferSleepEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SaferSleepOutCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SaferSleepApplication = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SaferSleepFacility = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SendOn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileDrugsToEmr = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnaestheticDrugs",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Dose = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Route = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Rate = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnaestheticDrugs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnaestheticDrugs_AnaestheticRecords_RecordId",
                        column: x => x.RecordId,
                        principalSchema: "devices",
                        principalTable: "AnaestheticRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnaestheticEvents",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnaestheticEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnaestheticEvents_AnaestheticRecords_RecordId",
                        column: x => x.RecordId,
                        principalSchema: "devices",
                        principalTable: "AnaestheticRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnaestheticVitals",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnaestheticVitals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnaestheticVitals_AnaestheticRecords_RecordId",
                        column: x => x.RecordId,
                        principalSchema: "devices",
                        principalTable: "AnaestheticRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Associations",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonitorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Associations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Associations_Monitors_MonitorId",
                        column: x => x.MonitorId,
                        principalSchema: "devices",
                        principalTable: "Monitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Captures",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssociationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonitorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HeartRate = table.Column<int>(type: "int", nullable: true),
                    SpO2 = table.Column<int>(type: "int", nullable: true),
                    SystolicBp = table.Column<int>(type: "int", nullable: true),
                    DiastolicBp = table.Column<int>(type: "int", nullable: true),
                    MeanBp = table.Column<int>(type: "int", nullable: true),
                    RespiratoryRate = table.Column<int>(type: "int", nullable: true),
                    TemperatureC = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: true),
                    EtCo2 = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ObservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Captures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Captures_Associations_AssociationId",
                        column: x => x.AssociationId,
                        principalSchema: "devices",
                        principalTable: "Associations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Captures_Monitors_MonitorId",
                        column: x => x.MonitorId,
                        principalSchema: "devices",
                        principalTable: "Monitors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Readings",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MonitorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssociationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Readings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Readings_Associations_AssociationId",
                        column: x => x.AssociationId,
                        principalSchema: "devices",
                        principalTable: "Associations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Readings_Monitors_MonitorId",
                        column: x => x.MonitorId,
                        principalSchema: "devices",
                        principalTable: "Monitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdtSends_EpisodeId_Event",
                schema: "devices",
                table: "AdtSends",
                columns: new[] { "EpisodeId", "Event" });

            migrationBuilder.CreateIndex(
                name: "IX_AdtSends_MessageId",
                schema: "devices",
                table: "AdtSends",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_AnaestheticDrugs_RecordId_At",
                schema: "devices",
                table: "AnaestheticDrugs",
                columns: new[] { "RecordId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AnaestheticEvents_RecordId_At",
                schema: "devices",
                table: "AnaestheticEvents",
                columns: new[] { "RecordId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AnaestheticRecords_CaseNumber",
                schema: "devices",
                table: "AnaestheticRecords",
                column: "CaseNumber");

            migrationBuilder.CreateIndex(
                name: "IX_AnaestheticRecords_ControlId",
                schema: "devices",
                table: "AnaestheticRecords",
                column: "ControlId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnaestheticRecords_DocumentId",
                schema: "devices",
                table: "AnaestheticRecords",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AnaestheticRecords_EpisodeId",
                schema: "devices",
                table: "AnaestheticRecords",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_AnaestheticRecords_MessageId",
                schema: "devices",
                table: "AnaestheticRecords",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_AnaestheticRecords_NoteId",
                schema: "devices",
                table: "AnaestheticRecords",
                column: "NoteId");

            migrationBuilder.CreateIndex(
                name: "IX_AnaestheticRecords_PatientId",
                schema: "devices",
                table: "AnaestheticRecords",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_AnaestheticRecords_SupersededById",
                schema: "devices",
                table: "AnaestheticRecords",
                column: "SupersededById");

            migrationBuilder.CreateIndex(
                name: "IX_AnaestheticVitals_RecordId_At",
                schema: "devices",
                table: "AnaestheticVitals",
                columns: new[] { "RecordId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Associations_EpisodeId_StartedAt",
                schema: "devices",
                table: "Associations",
                columns: new[] { "EpisodeId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Associations_MonitorId_StartedAt",
                schema: "devices",
                table: "Associations",
                columns: new[] { "MonitorId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Associations_PatientId",
                schema: "devices",
                table: "Associations",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "UX_Associations_OpenEpisode",
                schema: "devices",
                table: "Associations",
                column: "EpisodeId",
                unique: true,
                filter: "[EndedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Associations_OpenMonitor",
                schema: "devices",
                table: "Associations",
                column: "MonitorId",
                unique: true,
                filter: "[EndedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Captures_AssociationId_At",
                schema: "devices",
                table: "Captures",
                columns: new[] { "AssociationId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Captures_EpisodeId_At",
                schema: "devices",
                table: "Captures",
                columns: new[] { "EpisodeId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Captures_MonitorId",
                schema: "devices",
                table: "Captures",
                column: "MonitorId");

            migrationBuilder.CreateIndex(
                name: "IX_Captures_ObservationId",
                schema: "devices",
                table: "Captures",
                column: "ObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Monitors_Code",
                schema: "devices",
                table: "Monitors",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Monitors_EquipmentId",
                schema: "devices",
                table: "Monitors",
                column: "EquipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Readings_AssociationId_At",
                schema: "devices",
                table: "Readings",
                columns: new[] { "AssociationId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Readings_MonitorId_At",
                schema: "devices",
                table: "Readings",
                columns: new[] { "MonitorId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdtSends",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "AnaestheticDrugs",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "AnaestheticEvents",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "AnaestheticVitals",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "Captures",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "Readings",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "AnaestheticRecords",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "Associations",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "Monitors",
                schema: "devices");
        }
    }
}
