using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Quality.Migrations
{
    /// <inheritdoc />
    public partial class InitialQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "quality");

            migrationBuilder.CreateTable(
                name: "Compliments",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    From = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StaffNamed = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SharedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Compliments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CorrectiveActions",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceRef = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Owner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DoneBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerifiedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectiveActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Incidents",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReportedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ImmediateAction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StaffInvolved = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Harm = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Sac = table.Column<int>(type: "int", nullable: true),
                    AlwaysReport = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Investigator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TriagedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TriagedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Findings = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ContributingFactors = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LearningSummary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ClosedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Indicators",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Definition = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Numerator = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Denominator = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Calculator = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Scale = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Target = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    AlertAt = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    Owner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Indicators", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Policies",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Link = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReviewBy = table.Column<DateOnly>(type: "date", nullable: false),
                    RequiresAcknowledgement = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Risks",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsHazard = table.Column<bool>(type: "bit", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Likelihood = table.Column<int>(type: "int", nullable: false),
                    Consequence = table.Column<int>(type: "int", nullable: false),
                    Controls = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ResidualLikelihood = table.Column<int>(type: "int", nullable: true),
                    ResidualConsequence = table.Column<int>(type: "int", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewBy = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Risks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sequences",
                schema: "quality",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Next = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sequences", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    EscalateTo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ExtraNonWorkingDays = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Complaints",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedVia = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Complainant = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Contact = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    AcknowledgeBy = table.Column<DateOnly>(type: "date", nullable: false),
                    AcknowledgedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DecideBy = table.Column<DateOnly>(type: "date", nullable: true),
                    ExtensionNotifiedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ExtensionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RespondedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    Response = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    HdcReferral = table.Column<bool>(type: "bit", nullable: false),
                    AdvocacyInvolved = table.Column<bool>(type: "bit", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Owner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Complaints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Complaints_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalSchema: "quality",
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Disclosures",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    By = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PersonTold = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disclosures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Disclosures_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalSchema: "quality",
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Agency = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    NotifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NotifiedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalSchema: "quality",
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndicatorValues",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IndicatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    NumeratorValue = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DenominatorValue = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    EnteredBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EnteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndicatorValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndicatorValues_Indicators_IndicatorId",
                        column: x => x.IndicatorId,
                        principalSchema: "quality",
                        principalTable: "Indicators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PolicyAcknowledgements",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyAcknowledgements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicyAcknowledgements_Policies_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "quality",
                        principalTable: "Policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_IncidentId",
                schema: "quality",
                table: "Complaints",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_Number",
                schema: "quality",
                table: "Complaints",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_PatientId",
                schema: "quality",
                table: "Complaints",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_ReceivedOn",
                schema: "quality",
                table: "Complaints",
                column: "ReceivedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_Status_AcknowledgeBy",
                schema: "quality",
                table: "Complaints",
                columns: new[] { "Status", "AcknowledgeBy" });

            migrationBuilder.CreateIndex(
                name: "IX_Compliments_ReceivedOn",
                schema: "quality",
                table: "Compliments",
                column: "ReceivedOn");

            migrationBuilder.CreateIndex(
                name: "IX_CorrectiveActions_Number",
                schema: "quality",
                table: "CorrectiveActions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrectiveActions_Source_SourceId",
                schema: "quality",
                table: "CorrectiveActions",
                columns: new[] { "Source", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectiveActions_Status_DueOn",
                schema: "quality",
                table: "CorrectiveActions",
                columns: new[] { "Status", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Disclosures_IncidentId_At",
                schema: "quality",
                table: "Disclosures",
                columns: new[] { "IncidentId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_BookingId",
                schema: "quality",
                table: "Incidents",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_EquipmentId",
                schema: "quality",
                table: "Incidents",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Number",
                schema: "quality",
                table: "Incidents",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_OccurredAt",
                schema: "quality",
                table: "Incidents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_PatientId",
                schema: "quality",
                table: "Incidents",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Status_ReportedAt",
                schema: "quality",
                table: "Incidents",
                columns: new[] { "Status", "ReportedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Indicators_Code",
                schema: "quality",
                table: "Indicators",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndicatorValues_IndicatorId_Month",
                schema: "quality",
                table: "IndicatorValues",
                columns: new[] { "IndicatorId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_IncidentId_Agency",
                schema: "quality",
                table: "Notifications",
                columns: new[] { "IncidentId", "Agency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Policies_Code",
                schema: "quality",
                table: "Policies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Policies_ReviewBy",
                schema: "quality",
                table: "Policies",
                column: "ReviewBy");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyAcknowledgements_DocumentId_Version_UserName",
                schema: "quality",
                table: "PolicyAcknowledgements",
                columns: new[] { "DocumentId", "Version", "UserName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Risks_EquipmentId",
                schema: "quality",
                table: "Risks",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Risks_IsHazard_Status",
                schema: "quality",
                table: "Risks",
                columns: new[] { "IsHazard", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Risks_Number",
                schema: "quality",
                table: "Risks",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Risks_ReviewBy",
                schema: "quality",
                table: "Risks",
                column: "ReviewBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Complaints",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "Compliments",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "CorrectiveActions",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "Disclosures",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "IndicatorValues",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "PolicyAcknowledgements",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "Risks",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "Sequences",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "Indicators",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "Incidents",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "Policies",
                schema: "quality");
        }
    }
}
