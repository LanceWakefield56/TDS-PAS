using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Medicines.Migrations
{
    /// <inheritdoc />
    public partial class InitialMedicines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "meds");

            migrationBuilder.CreateTable(
                name: "AllergyLinks",
                schema: "meds",
                columns: table => new
                {
                    AlertId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Term = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    LinkedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllergyLinks", x => x.AlertId);
                });

            migrationBuilder.CreateTable(
                name: "ImportProfiles",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    NameColumn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NzmtColumn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FormColumn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RouteColumn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IngredientColumn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AmountColumn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitColumn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PerMlColumn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GtinColumn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeactivateMissing = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportRuns",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    Added = table.Column<int>(type: "int", nullable: false),
                    Updated = table.Column<int>(type: "int", nullable: false),
                    Deactivated = table.Column<int>(type: "int", nullable: false),
                    Skipped = table.Column<int>(type: "int", nullable: false),
                    Problems = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ImportedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ingredients",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NzmtId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ingredients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Medicines",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    NzmtId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Form = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    DefaultRoute = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    OnFormulary = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Medicines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NzfReleases",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Edition = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    ReleaseNumber = table.Column<int>(type: "int", nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Monographs = table.Column<int>(type: "int", nullable: false),
                    Doses = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    Problems = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ImportedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NzfReleases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NzfSettings",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    NzfUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NzfcUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AutoDownload = table.Column<bool>(type: "bit", nullable: false),
                    WatchFolder = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastProblem = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NzfSettings", x => x.Id);
                    table.CheckConstraint("CK_NzfSettings_SingleRow", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "RuleSets",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsExample = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ApprovedRole = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    CheckRegularMedicines = table.Column<bool>(type: "bit", nullable: false),
                    DuplicateWithinMinutes = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                    table.CheckConstraint("CK_MedSettings_SingleRow", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "IngredientAliases",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Alias = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientAliases_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalSchema: "meds",
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Checks",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MedicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MedicineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MedicineText = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Dose = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Route = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    OverrideReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RuleSetVersion = table.Column<int>(type: "int", nullable: true),
                    Mode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Checks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Checks_Medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalSchema: "meds",
                        principalTable: "Medicines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MedicineGtins",
                schema: "meds",
                columns: table => new
                {
                    Gtin = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false),
                    MedicineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicineGtins", x => x.Gtin);
                    table.ForeignKey(
                        name: "FK_MedicineGtins_Medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalSchema: "meds",
                        principalTable: "Medicines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MedicineIngredients",
                schema: "meds",
                columns: table => new
                {
                    MedicineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    PerMl = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicineIngredients", x => new { x.MedicineId, x.IngredientId });
                    table.ForeignKey(
                        name: "FK_MedicineIngredients_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalSchema: "meds",
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicineIngredients_Medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalSchema: "meds",
                        principalTable: "Medicines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NzfMonographs",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BaseName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Synonyms = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Chapter = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Indications = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContraIndications = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Cautions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HepaticImpairment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RenalImpairment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Pregnancy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BreastFeeding = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdverseEffects = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PatientAdvice = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OtherSections = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NzfMonographs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NzfMonographs_NzfReleases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalSchema: "meds",
                        principalTable: "NzfReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrossReactions",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromGroup = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ToGroup = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrossReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrossReactions_RuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalSchema: "meds",
                        principalTable: "RuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DoseRules",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Ingredient = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Route = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    MinAgeYears = table.Column<int>(type: "int", nullable: true),
                    MaxAgeYears = table.Column<int>(type: "int", nullable: true),
                    MinWeightKg = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    MaxWeightKg = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    MaxSingle = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    MaxDaily = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    MaxSinglePerKg = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    MaxDailyPerKg = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    MinIntervalHours = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoseRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoseRules_RuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalSchema: "meds",
                        principalTable: "RuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Groups",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AllergyClass = table.Column<bool>(type: "bit", nullable: false),
                    SameGroupSeverity = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Groups_RuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalSchema: "meds",
                        principalTable: "RuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Interactions",
                schema: "meds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TermA = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    TermB = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    Effect = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Advice = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Interactions_RuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalSchema: "meds",
                        principalTable: "RuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckFindings",
                schema: "meds",
                columns: table => new
                {
                    CheckId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RuleRef = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckFindings", x => new { x.CheckId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_CheckFindings_Checks_CheckId",
                        column: x => x.CheckId,
                        principalSchema: "meds",
                        principalTable: "Checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NzfDoses",
                schema: "meds",
                columns: table => new
                {
                    MonographId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Indication = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Route = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RouteCodes = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Population = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Dose = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MinAgeYears = table.Column<int>(type: "int", nullable: true),
                    MaxAgeYears = table.Column<int>(type: "int", nullable: true),
                    MinWeightKg = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    MaxWeightKg = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Neonate = table.Column<bool>(type: "bit", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    MaxSingle = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    MaxSinglePerKg = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    MaxDaily = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    MaxDailyPerKg = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    IntervalHours = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Titration = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NzfDoses", x => new { x.MonographId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_NzfDoses_NzfMonographs_MonographId",
                        column: x => x.MonographId,
                        principalSchema: "meds",
                        principalTable: "NzfMonographs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NzfPreparations",
                schema: "meds",
                columns: table => new
                {
                    MonographId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Form = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Strength = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Subsidy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NzfPreparations", x => new { x.MonographId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_NzfPreparations_NzfMonographs_MonographId",
                        column: x => x.MonographId,
                        principalSchema: "meds",
                        principalTable: "NzfMonographs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupMembers",
                schema: "meds",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Term = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsKeyword = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMembers", x => new { x.GroupId, x.Term });
                    table.ForeignKey(
                        name: "FK_GroupMembers_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "meds",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "meds",
                table: "NzfSettings",
                columns: new[] { "Id", "AutoDownload", "LastCheckedAt", "LastProblem", "NzfUrl", "NzfcUrl", "UpdatedAt", "UpdatedBy", "WatchFolder" },
                values: new object[] { 1, false, null, null, "https://www.nzf.org.nz/home/downloadpdf", null, null, null, null });

            migrationBuilder.InsertData(
                schema: "meds",
                table: "Settings",
                columns: new[] { "Id", "CheckRegularMedicines", "DuplicateWithinMinutes", "Mode", "UpdatedAt", "UpdatedBy" },
                values: new object[] { 1, true, 60, "Advise", null, null });

            migrationBuilder.CreateIndex(
                name: "IX_AllergyLinks_PatientId",
                schema: "meds",
                table: "AllergyLinks",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Checks_EpisodeId",
                schema: "meds",
                table: "Checks",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Checks_MedicationId",
                schema: "meds",
                table: "Checks",
                column: "MedicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Checks_MedicineId",
                schema: "meds",
                table: "Checks",
                column: "MedicineId");

            migrationBuilder.CreateIndex(
                name: "IX_Checks_PatientId_CheckedAt",
                schema: "meds",
                table: "Checks",
                columns: new[] { "PatientId", "CheckedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Checks_Result_CheckedAt",
                schema: "meds",
                table: "Checks",
                columns: new[] { "Result", "CheckedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CrossReactions_RuleSetId_FromGroup_ToGroup",
                schema: "meds",
                table: "CrossReactions",
                columns: new[] { "RuleSetId", "FromGroup", "ToGroup" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DoseRules_RuleSetId_Code",
                schema: "meds",
                table: "DoseRules",
                columns: new[] { "RuleSetId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_RuleSetId_Code",
                schema: "meds",
                table: "Groups",
                columns: new[] { "RuleSetId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportProfiles_Name",
                schema: "meds",
                table: "ImportProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_ImportedAt",
                schema: "meds",
                table: "ImportRuns",
                column: "ImportedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientAliases_Alias",
                schema: "meds",
                table: "IngredientAliases",
                column: "Alias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngredientAliases_IngredientId",
                schema: "meds",
                table: "IngredientAliases",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_Name",
                schema: "meds",
                table: "Ingredients",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_NzmtId",
                schema: "meds",
                table: "Ingredients",
                column: "NzmtId",
                filter: "[NzmtId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Interactions_RuleSetId_Code",
                schema: "meds",
                table: "Interactions",
                columns: new[] { "RuleSetId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicineGtins_MedicineId",
                schema: "meds",
                table: "MedicineGtins",
                column: "MedicineId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicineIngredients_IngredientId",
                schema: "meds",
                table: "MedicineIngredients",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_Medicines_Name",
                schema: "meds",
                table: "Medicines",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Medicines_NzmtId",
                schema: "meds",
                table: "Medicines",
                column: "NzmtId",
                unique: true,
                filter: "[NzmtId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Medicines_OnFormulary_IsActive",
                schema: "meds",
                table: "Medicines",
                columns: new[] { "OnFormulary", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_NzfMonographs_ReleaseId_BaseName",
                schema: "meds",
                table: "NzfMonographs",
                columns: new[] { "ReleaseId", "BaseName" });

            migrationBuilder.CreateIndex(
                name: "IX_NzfMonographs_ReleaseId_Name",
                schema: "meds",
                table: "NzfMonographs",
                columns: new[] { "ReleaseId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_NzfReleases_ImportedAt",
                schema: "meds",
                table: "NzfReleases",
                column: "ImportedAt");

            migrationBuilder.CreateIndex(
                name: "UX_NzfReleases_OneCurrent",
                schema: "meds",
                table: "NzfReleases",
                column: "Edition",
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_RuleSets_Version",
                schema: "meds",
                table: "RuleSets",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_MedRuleSets_OneApproved",
                schema: "meds",
                table: "RuleSets",
                column: "Status",
                unique: true,
                filter: "[Status] = 'Approved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllergyLinks",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "CheckFindings",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "CrossReactions",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "DoseRules",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "GroupMembers",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "ImportProfiles",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "ImportRuns",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "IngredientAliases",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "Interactions",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "MedicineGtins",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "MedicineIngredients",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "NzfDoses",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "NzfPreparations",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "NzfSettings",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "Checks",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "Groups",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "Ingredients",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "NzfMonographs",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "Medicines",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "RuleSets",
                schema: "meds");

            migrationBuilder.DropTable(
                name: "NzfReleases",
                schema: "meds");
        }
    }
}
