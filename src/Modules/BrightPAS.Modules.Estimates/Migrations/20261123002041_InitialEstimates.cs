using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Estimates.Migrations
{
    /// <inheritdoc />
    public partial class InitialEstimates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "est");

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    GstRate = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    Loading = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    Markup = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ExtendedTimePer15 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ValidityMonths = table.Column<int>(type: "int", nullable: false),
                    DepositPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    DepositDaysBefore = table.Column<int>(type: "int", nullable: false),
                    BalanceDaysAfter = table.Column<int>(type: "int", nullable: false),
                    SurgeonFeeLabel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    BankAccount = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Terms = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SelfFundingText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    InsuranceText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AcceptanceText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SendMode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    HoldMinutes = table.Column<int>(type: "int", nullable: false),
                    RequireIssued = table.Column<bool>(type: "bit", nullable: false),
                    PatientPassword = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    InsurerSameAsPatient = table.Column<bool>(type: "bit", nullable: false),
                    EmailSubject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmailBody = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    EmailSubjectInsurer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmailBodyInsurer = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    HintDobShort = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    HintDobLong = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    HintNhi = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    HintTyped = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BccAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    InsurerAddresses = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IncisiveFolder = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    IncisiveFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ArchiveIssued = table.Column<bool>(type: "bit", nullable: false),
                    StaffCostPerMinute = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    OverheadPerCase = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    TemplatesSeeded = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                    table.CheckConstraint("CK_EstSettings_SingleRow", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Specialties",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DefaultNotes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Specialties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockItems",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Supplier = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PackPrice = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    UnitsPerPack = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    InStockQty = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    NeededInHouse = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    WhereUsed = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Issues = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Surgeons",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ClinicianId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Surgeons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Procedures",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SpecialtyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Group = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PrintTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    DefaultAnaesthetic = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Facility = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    FacilitySpecial = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    Anaesthetist = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    AnaesthetistSpecial = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    ConsumablesLine = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    IncludedAddOnCodes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Procedures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Procedures_Specialties_SpecialtyId",
                        column: x => x.SpecialtyId,
                        principalSchema: "est",
                        principalTable: "Specialties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AddOns",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SpecialtyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Group = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SurgeonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddOns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddOns_Specialties_SpecialtyId",
                        column: x => x.SpecialtyId,
                        principalSchema: "est",
                        principalTable: "Specialties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AddOns_Surgeons_SurgeonId",
                        column: x => x.SurgeonId,
                        principalSchema: "est",
                        principalTable: "Surgeons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SurgeonSpecialties",
                schema: "est",
                columns: table => new
                {
                    SurgeonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpecialtyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurgeonSpecialties", x => new { x.SurgeonId, x.SpecialtyId });
                    table.ForeignKey(
                        name: "FK_SurgeonSpecialties_Specialties_SpecialtyId",
                        column: x => x.SpecialtyId,
                        principalSchema: "est",
                        principalTable: "Specialties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SurgeonSpecialties_Surgeons_SurgeonId",
                        column: x => x.SurgeonId,
                        principalSchema: "est",
                        principalTable: "Surgeons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Estimates",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ref = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EstimateDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Nhi = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Town = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Postcode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Funding = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Insurer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PolicyNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PriorApproval = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProcedureCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SpecialtyName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProcedureName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SurgeonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SurgeonName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ProcedureDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Anaesthetic = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    PlannedMinutes = table.Column<int>(type: "int", nullable: true),
                    UseSpecial = table.Column<bool>(type: "bit", nullable: false),
                    Multiplier = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    FacilityOverride = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    SurgeonOverride = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    AnaesthetistOverride = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    InputsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Deposit = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EmailedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentToIncisiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Estimates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Estimates_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalSchema: "est",
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Estimates_Surgeons_SurgeonId",
                        column: x => x.SurgeonId,
                        principalSchema: "est",
                        principalTable: "Surgeons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PendingPrices",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NewValue = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingPrices_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalSchema: "est",
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcedureConsumables",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcedureConsumables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcedureConsumables_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalSchema: "est",
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcedureConsumables_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalSchema: "est",
                        principalTable: "StockItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SurgeonFees",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SurgeonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Fee = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    SpecialFee = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurgeonFees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurgeonFees_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalSchema: "est",
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SurgeonFees_Surgeons_SurgeonId",
                        column: x => x.SurgeonId,
                        principalSchema: "est",
                        principalTable: "Surgeons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SurgeonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Anaesthetic = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    PlannedMinutes = table.Column<int>(type: "int", nullable: true),
                    UseSpecial = table.Column<bool>(type: "bit", nullable: false),
                    FacilityOverride = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    SurgeonOverride = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    AnaesthetistOverride = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    InputsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Visible = table.Column<bool>(type: "bit", nullable: false),
                    Favourite = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UsedCount = table.Column<int>(type: "int", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Templates_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalSchema: "est",
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Templates_Surgeons_SurgeonId",
                        column: x => x.SurgeonId,
                        principalSchema: "est",
                        principalTable: "Surgeons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ArchivedPdfs",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PdfCipher = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchivedPdfs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchivedPdfs_Estimates_EstimateId",
                        column: x => x.EstimateId,
                        principalSchema: "est",
                        principalTable: "Estimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EstimateLines",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    DisplayText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstimateLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EstimateLines_Estimates_EstimateId",
                        column: x => x.EstimateId,
                        principalSchema: "est",
                        principalTable: "Estimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "est",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateRef = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PatientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ToAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    AttachmentName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AttachmentCipher = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    PasswordProtected = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SendAfter = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StoppedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StoppedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Outbox_Estimates_EstimateId",
                        column: x => x.EstimateId,
                        principalSchema: "est",
                        principalTable: "Estimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemplateSurgeons",
                schema: "est",
                columns: table => new
                {
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SurgeonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateSurgeons", x => new { x.TemplateId, x.SurgeonId });
                    table.ForeignKey(
                        name: "FK_TemplateSurgeons_Surgeons_SurgeonId",
                        column: x => x.SurgeonId,
                        principalSchema: "est",
                        principalTable: "Surgeons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TemplateSurgeons_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "est",
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "est",
                table: "Settings",
                columns: new[] { "Id", "AcceptanceText", "Address", "ArchiveIssued", "BalanceDaysAfter", "BankAccount", "BccAddress", "DepositDaysBefore", "DepositPercent", "Email", "EmailBody", "EmailBodyInsurer", "EmailSubject", "EmailSubjectInsurer", "ExtendedTimePer15", "FromName", "GstRate", "HintDobLong", "HintDobShort", "HintNhi", "HintTyped", "HoldMinutes", "IncisiveFileName", "IncisiveFolder", "InsuranceText", "InsurerAddresses", "InsurerSameAsPatient", "Loading", "Markup", "OverheadPerCase", "PatientPassword", "Phone", "RequireIssued", "SelfFundingText", "SendMode", "StaffCostPerMinute", "SurgeonFeeLabel", "TemplatesSeeded", "Terms", "ValidityMonths" },
                values: new object[] { 1, "Email acceptance of this estimate is required prior to confirmation of your booking.", "341 Lower Queen Street, Richmond, Tasman 7020", true, 14, "03-0703-0046726-000", null, 14, 50m, "enquiries@tasmandaysurgery.co.nz", "Dear {first},\n\nPlease find attached your estimate for {procedure}.\n\nIf you are self-funding, please reply to this email to accept the estimate. If you are insured, please forward it to your insurer and send us their approval.\n\nKind regards\n{preparedby}\nTasman Day Surgery\n{phone}", "Hello,\n\nPlease find attached our estimate for {first} {last} (policy {policy}) for {procedure}, for prior approval.\n\nKind regards\n{preparedby}\nTasman Day Surgery\n{phone}", "Your estimate from Tasman Day Surgery – {ref}", "Estimate for {first} {last} – {ref} – {insurer} {policy}", 90m, "Tasman Day Surgery", 0.15m, "This estimate is password protected. The password is your date of birth as 8 digits, DDMMYYYY (for example 14 March 1965 = 14031965).", "This estimate is password protected. The password is your date of birth as 6 digits, DDMMYY (for example 14 March 1965 = 140365).", "This estimate is password protected. The password is your NHI number in capital letters (it is on your hospital letters and prescriptions).", "This estimate is password protected. We will give you the password separately by phone or text message.", 10, "{NHI}_{LAST}_{FIRST}_Estimate_{DATE}", null, "If you are funding via insurance, please forward this estimate to your provider, and email back their confirmed acceptance of the estimate. If your policy includes a shortfall or excess, the outstanding amount must be paid in full prior to the procedure.", "Southern Cross = \nnib = \nAIA = \nPartners Life = \nAccuro = \nUniMed = ", false, 0.15m, 0.25m, 350m, "DobShort", "0800 827 329", true, "If you are self-funding, please indicate that you accept the estimate by return email. A payment of {deposit}% of the estimate is required at least two weeks prior to the procedure and the balance must be paid within {balancedays} days of the surgery.", "OutlookDraft", 4.45m, "Consultant Fee", false, "All estimates are subject to change without prior notice. This estimate is valid for a total of {validity} months (until {validuntil}).\nWhile it is likely to be the total cost, actual fees may vary depending on the length of procedure and/or length of stay, whether special equipment or materials are used, pre-existing conditions or in certain unusual circumstances. This may include an anaesthetic safety pre-assessment if required.\nThese fees do not include the initial diagnostic consultation or visits more than three months after surgery. Estimates for the facility and anaesthetist are approximate only and if there are any concerns, they can be clarified with Tasman Day Surgery {phone}.\nOccasionally unforeseen circumstances may result in unexpected costs not outlined in this estimate. Examples of this include but are not limited to costs related to emergencies such as cardiac arrest, an unexpected allergic reaction during your stay or an unexpected finding during your procedure.", 3 });

            migrationBuilder.CreateIndex(
                name: "IX_AddOns_Code",
                schema: "est",
                table: "AddOns",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AddOns_SpecialtyId",
                schema: "est",
                table: "AddOns",
                column: "SpecialtyId");

            migrationBuilder.CreateIndex(
                name: "IX_AddOns_SurgeonId",
                schema: "est",
                table: "AddOns",
                column: "SurgeonId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedPdfs_EstimateId",
                schema: "est",
                table: "ArchivedPdfs",
                column: "EstimateId");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateLines_EstimateId_Sequence",
                schema: "est",
                table: "EstimateLines",
                columns: new[] { "EstimateId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Estimates_Email",
                schema: "est",
                table: "Estimates",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Estimates_EstimateDate",
                schema: "est",
                table: "Estimates",
                column: "EstimateDate");

            migrationBuilder.CreateIndex(
                name: "IX_Estimates_LastName_FirstName",
                schema: "est",
                table: "Estimates",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_Estimates_Nhi",
                schema: "est",
                table: "Estimates",
                column: "Nhi");

            migrationBuilder.CreateIndex(
                name: "IX_Estimates_PatientId",
                schema: "est",
                table: "Estimates",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Estimates_ProcedureId",
                schema: "est",
                table: "Estimates",
                column: "ProcedureId");

            migrationBuilder.CreateIndex(
                name: "IX_Estimates_Ref",
                schema: "est",
                table: "Estimates",
                column: "Ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Estimates_SurgeonId",
                schema: "est",
                table: "Estimates",
                column: "SurgeonId");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_EstimateId",
                schema: "est",
                table: "Outbox",
                column: "EstimateId");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Status_SendAfter",
                schema: "est",
                table: "Outbox",
                columns: new[] { "Status", "SendAfter" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingPrices_AppliedAt_EffectiveFrom",
                schema: "est",
                table: "PendingPrices",
                columns: new[] { "AppliedAt", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingPrices_ProcedureId",
                schema: "est",
                table: "PendingPrices",
                column: "ProcedureId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureConsumables_ProcedureId",
                schema: "est",
                table: "ProcedureConsumables",
                column: "ProcedureId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureConsumables_StockItemId",
                schema: "est",
                table: "ProcedureConsumables",
                column: "StockItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_Code",
                schema: "est",
                table: "Procedures",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_SpecialtyId_Group",
                schema: "est",
                table: "Procedures",
                columns: new[] { "SpecialtyId", "Group" });

            migrationBuilder.CreateIndex(
                name: "IX_Specialties_Name",
                schema: "est",
                table: "Specialties",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_Supplier_Name",
                schema: "est",
                table: "StockItems",
                columns: new[] { "Supplier", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_SurgeonFees_ProcedureId_SurgeonId",
                schema: "est",
                table: "SurgeonFees",
                columns: new[] { "ProcedureId", "SurgeonId" },
                unique: true,
                filter: "[SurgeonId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SurgeonFees_SurgeonId",
                schema: "est",
                table: "SurgeonFees",
                column: "SurgeonId");

            migrationBuilder.CreateIndex(
                name: "UX_SurgeonFees_AllSurgeons",
                schema: "est",
                table: "SurgeonFees",
                column: "ProcedureId",
                unique: true,
                filter: "[SurgeonId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Surgeons_ClinicianId",
                schema: "est",
                table: "Surgeons",
                column: "ClinicianId");

            migrationBuilder.CreateIndex(
                name: "IX_Surgeons_Name",
                schema: "est",
                table: "Surgeons",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SurgeonSpecialties_SpecialtyId",
                schema: "est",
                table: "SurgeonSpecialties",
                column: "SpecialtyId");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_ProcedureId",
                schema: "est",
                table: "Templates",
                column: "ProcedureId");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_SurgeonId",
                schema: "est",
                table: "Templates",
                column: "SurgeonId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateSurgeons_SurgeonId",
                schema: "est",
                table: "TemplateSurgeons",
                column: "SurgeonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AddOns",
                schema: "est");

            migrationBuilder.DropTable(
                name: "ArchivedPdfs",
                schema: "est");

            migrationBuilder.DropTable(
                name: "EstimateLines",
                schema: "est");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "est");

            migrationBuilder.DropTable(
                name: "PendingPrices",
                schema: "est");

            migrationBuilder.DropTable(
                name: "ProcedureConsumables",
                schema: "est");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "est");

            migrationBuilder.DropTable(
                name: "SurgeonFees",
                schema: "est");

            migrationBuilder.DropTable(
                name: "SurgeonSpecialties",
                schema: "est");

            migrationBuilder.DropTable(
                name: "TemplateSurgeons",
                schema: "est");

            migrationBuilder.DropTable(
                name: "Estimates",
                schema: "est");

            migrationBuilder.DropTable(
                name: "StockItems",
                schema: "est");

            migrationBuilder.DropTable(
                name: "Templates",
                schema: "est");

            migrationBuilder.DropTable(
                name: "Procedures",
                schema: "est");

            migrationBuilder.DropTable(
                name: "Surgeons",
                schema: "est");

            migrationBuilder.DropTable(
                name: "Specialties",
                schema: "est");
        }
    }
}
