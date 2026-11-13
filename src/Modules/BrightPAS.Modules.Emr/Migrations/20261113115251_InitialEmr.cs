using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Emr.Migrations
{
    /// <inheritdoc />
    public partial class InitialEmr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "emr");

            migrationBuilder.CreateTable(
                name: "ControlledDrugs",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Strength = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Form = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Balance = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlledDrugs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Episodes",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AdmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AdmittedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SedationOrAnaesthesia = table.Column<bool>(type: "bit", nullable: false),
                    EscortName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EscortPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    OvernightCarer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EscortConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    EscortOverrideReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DischargedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DischargedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DischargeDestination = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FollowUpCallOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Assessments",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Risk = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Actions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assessments_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "emr",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BloodUnits",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Product = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ColdChainOk = table.Column<bool>(type: "bit", nullable: false),
                    CheckedBy1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CheckedBy2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StoppedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reaction = table.Column<bool>(type: "bit", nullable: false),
                    ReactionDetails = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Returned = table.Column<bool>(type: "bit", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BloodUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BloodUnits_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "emr",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ControlledDrugRegister",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ControlledDrugId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Movement = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    BalanceAfter = table.Column<int>(type: "int", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    By = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WitnessedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlledDrugRegister", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ControlledDrugRegister_ControlledDrugs_ControlledDrugId",
                        column: x => x.ControlledDrugId,
                        principalSchema: "emr",
                        principalTable: "ControlledDrugs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ControlledDrugRegister_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "emr",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Counts",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Item = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Expected = table.Column<int>(type: "int", nullable: false),
                    Counted = table.Column<int>(type: "int", nullable: false),
                    CountedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CheckedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Resolved = table.Column<bool>(type: "bit", nullable: false),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Counts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Counts_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "emr",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FluidBalance",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsInput = table.Column<bool>(type: "bit", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Millilitres = table.Column<int>(type: "int", nullable: false),
                    By = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FluidBalance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FluidBalance_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "emr",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntraopRecords",
                schema: "emr",
                columns: table => new
                {
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InTheatreAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AnaestheticStartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    KnifeToSkinAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosureAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OutOfTheatreAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Position = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PositioningAids = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SkinPrep = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DiathermyUnit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DiathermyPadSite = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PadSiteSkinIntactAfter = table.Column<bool>(type: "bit", nullable: true),
                    TourniquetSite = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TourniquetPressure = table.Column<int>(type: "int", nullable: true),
                    TourniquetOnAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TourniquetOffAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SmokeEvacuation = table.Column<bool>(type: "bit", nullable: false),
                    WoundClass = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Drains = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Dressings = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntraopRecords", x => x.EpisodeId);
                    table.ForeignKey(
                        name: "FK_IntraopRecords_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "emr",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MedicationsGiven",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Medicine = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Dose = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Route = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GivenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GivenBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WitnessedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Barcode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ControlledDrugId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationsGiven", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicationsGiven_ControlledDrugs_ControlledDrugId",
                        column: x => x.ControlledDrugId,
                        principalSchema: "emr",
                        principalTable: "ControlledDrugs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicationsGiven_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "emr",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notes",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Signed = table.Column<bool>(type: "bit", nullable: false),
                    AmendsNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notes_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "emr",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notes_Notes_AmendsNoteId",
                        column: x => x.AmendsNoteId,
                        principalSchema: "emr",
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Observations",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RespiratoryRate = table.Column<int>(type: "int", nullable: true),
                    SpO2 = table.Column<int>(type: "int", nullable: true),
                    OnOxygen = table.Column<bool>(type: "bit", nullable: false),
                    OxygenLitres = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: true),
                    SystolicBp = table.Column<int>(type: "int", nullable: true),
                    DiastolicBp = table.Column<int>(type: "int", nullable: true),
                    HeartRate = table.Column<int>(type: "int", nullable: true),
                    TemperatureC = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: true),
                    Consciousness = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    Pain = table.Column<int>(type: "int", nullable: true),
                    Sedation = table.Column<int>(type: "int", nullable: true),
                    Ews = table.Column<int>(type: "int", nullable: true),
                    Escalation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Observations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Observations_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "emr",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SafetyChecks",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Phase = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafetyChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SafetyChecks_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "emr",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Specimens",
                schema: "emr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Site = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Test = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Container = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CollectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CollectedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Courier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Specimens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Specimens_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "emr",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentAnswers",
                schema: "emr",
                columns: table => new
                {
                    AssessmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentAnswers", x => new { x.AssessmentId, x.Key });
                    table.ForeignKey(
                        name: "FK_AssessmentAnswers_Assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalSchema: "emr",
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SafetyCheckItems",
                schema: "emr",
                columns: table => new
                {
                    SafetyCheckId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Confirmed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafetyCheckItems", x => new { x.SafetyCheckId, x.Key });
                    table.ForeignKey(
                        name: "FK_SafetyCheckItems_SafetyChecks_SafetyCheckId",
                        column: x => x.SafetyCheckId,
                        principalSchema: "emr",
                        principalTable: "SafetyChecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SafetyCheckTeam",
                schema: "emr",
                columns: table => new
                {
                    SafetyCheckId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafetyCheckTeam", x => new { x.SafetyCheckId, x.Role });
                    table.ForeignKey(
                        name: "FK_SafetyCheckTeam_SafetyChecks_SafetyCheckId",
                        column: x => x.SafetyCheckId,
                        principalSchema: "emr",
                        principalTable: "SafetyChecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_EpisodeId_Kind",
                schema: "emr",
                table: "Assessments",
                columns: new[] { "EpisodeId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_BloodUnits_EpisodeId",
                schema: "emr",
                table: "BloodUnits",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_BloodUnits_UnitNumber",
                schema: "emr",
                table: "BloodUnits",
                column: "UnitNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ControlledDrugRegister_ControlledDrugId_At",
                schema: "emr",
                table: "ControlledDrugRegister",
                columns: new[] { "ControlledDrugId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ControlledDrugRegister_EpisodeId",
                schema: "emr",
                table: "ControlledDrugRegister",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlledDrugRegister_PatientId",
                schema: "emr",
                table: "ControlledDrugRegister",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlledDrugs_Name_Strength_Form",
                schema: "emr",
                table: "ControlledDrugs",
                columns: new[] { "Name", "Strength", "Form" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Counts_EpisodeId_Stage",
                schema: "emr",
                table: "Counts",
                columns: new[] { "EpisodeId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_BookingId",
                schema: "emr",
                table: "Episodes",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_PatientId",
                schema: "emr",
                table: "Episodes",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_Stage",
                schema: "emr",
                table: "Episodes",
                column: "Stage");

            migrationBuilder.CreateIndex(
                name: "IX_FluidBalance_EpisodeId_At",
                schema: "emr",
                table: "FluidBalance",
                columns: new[] { "EpisodeId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicationsGiven_ControlledDrugId",
                schema: "emr",
                table: "MedicationsGiven",
                column: "ControlledDrugId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicationsGiven_EpisodeId_GivenAt",
                schema: "emr",
                table: "MedicationsGiven",
                columns: new[] { "EpisodeId", "GivenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_AmendsNoteId",
                schema: "emr",
                table: "Notes",
                column: "AmendsNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_EpisodeId_At",
                schema: "emr",
                table: "Notes",
                columns: new[] { "EpisodeId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Observations_DeviceId",
                schema: "emr",
                table: "Observations",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_EpisodeId_At",
                schema: "emr",
                table: "Observations",
                columns: new[] { "EpisodeId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_SafetyChecks_EpisodeId_Phase",
                schema: "emr",
                table: "SafetyChecks",
                columns: new[] { "EpisodeId", "Phase" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Specimens_EpisodeId_Number",
                schema: "emr",
                table: "Specimens",
                columns: new[] { "EpisodeId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Specimens_Status",
                schema: "emr",
                table: "Specimens",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_Name",
                schema: "emr",
                table: "Templates",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssessmentAnswers",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "BloodUnits",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "ControlledDrugRegister",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "Counts",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "FluidBalance",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "IntraopRecords",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "MedicationsGiven",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "Notes",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "Observations",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "SafetyCheckItems",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "SafetyCheckTeam",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "Specimens",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "Templates",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "Assessments",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "ControlledDrugs",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "SafetyChecks",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "Episodes",
                schema: "emr");
        }
    }
}
