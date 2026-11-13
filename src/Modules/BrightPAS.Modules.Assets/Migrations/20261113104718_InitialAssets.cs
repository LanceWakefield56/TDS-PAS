using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrightPAS.Modules.Assets.Migrations
{
    /// <inheritdoc />
    public partial class InitialAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assets");

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ScopeStorageHours = table.Column<int>(type: "int", nullable: false),
                    MaintenanceWarningDays = table.Column<int>(type: "int", nullable: false),
                    ReleaseNeedsSecondPerson = table.Column<bool>(type: "bit", nullable: false),
                    NextLoad = table.Column<int>(type: "int", nullable: false),
                    NextNc = table.Column<int>(type: "int", nullable: false),
                    NextPo = table.Column<int>(type: "int", nullable: false),
                    NextLoan = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ContactName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    XeroContactName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TraySets",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Specialty = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ShelfLifeDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TraySets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Equipment",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Make = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Owner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchasedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Value = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    WarrantyUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Risk = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OutOfServiceReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LockedOutBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LockedOutAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NetworkAddress = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Equipment_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "assets",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "assets",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockItems",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsImplant = table.Column<bool>(type: "bit", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ReorderLevel = table.Column<int>(type: "int", nullable: false),
                    ReorderQuantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    AccountCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockItems_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "assets",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrayItems",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TraySetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrayItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrayItems_TraySets_TraySetId",
                        column: x => x.TraySetId,
                        principalSchema: "assets",
                        principalTable: "TraySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Cycles",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoadNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MachineCycleNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Program = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MaxTemperatureC = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    HoldSeconds = table.Column<int>(type: "int", nullable: true),
                    PressureKpa = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: true),
                    A0 = table.Column<int>(type: "int", nullable: true),
                    MachinePassed = table.Column<bool>(type: "bit", nullable: false),
                    ChemicalIndicator = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BiologicalIndicator = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BowieDickTest = table.Column<bool>(type: "bit", nullable: false),
                    Release = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ReleasedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleaseNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Operator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RawLog = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cycles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cycles_Equipment_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "assets",
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenancePlans",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IntervalMonths = table.Column<int>(type: "int", nullable: false),
                    NextDue = table.Column<DateOnly>(type: "date", nullable: false),
                    DoneBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenancePlans_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "assets",
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRecords",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DoneOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DoneBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    TagNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RecordedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceRecords_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "assets",
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierInvoices",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AmountExGst = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Matched = table.Column<bool>(type: "bit", nullable: false),
                    MatchNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExportedToXeroAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecordedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierInvoices_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "assets",
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    QuantityReceived = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "assets",
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalSchema: "assets",
                        principalTable: "StockItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockLots",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lot = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    OnHand = table.Column<int>(type: "int", nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockLots_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalSchema: "assets",
                        principalTable: "StockItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoanSets",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CheckedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CheckNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReturnedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanSets_Cycles_CycleId",
                        column: x => x.CycleId,
                        principalSchema: "assets",
                        principalTable: "Cycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoanSets_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "assets",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScopeEvents",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    By = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: true),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScopeEvents_Cycles_CycleId",
                        column: x => x.CycleId,
                        principalSchema: "assets",
                        principalTable: "Cycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScopeEvents_Equipment_ScopeId",
                        column: x => x.ScopeId,
                        principalSchema: "assets",
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Trays",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TraySetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SterileUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    PackedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PackedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trays_Cycles_LastCycleId",
                        column: x => x.LastCycleId,
                        principalSchema: "assets",
                        principalTable: "Cycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trays_TraySets_TraySetId",
                        column: x => x.TraySetId,
                        principalSchema: "assets",
                        principalTable: "TraySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Implants",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StockItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Lot = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Site = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ImplantedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Implants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Implants_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalSchema: "assets",
                        principalTable: "StockItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Implants_StockLots_LotId",
                        column: x => x.LotId,
                        principalSchema: "assets",
                        principalTable: "StockLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    By = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "assets",
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalSchema: "assets",
                        principalTable: "StockItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_StockLots_LotId",
                        column: x => x.LotId,
                        principalSchema: "assets",
                        principalTable: "StockLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CycleItems",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrayInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CycleItems_Cycles_CycleId",
                        column: x => x.CycleId,
                        principalSchema: "assets",
                        principalTable: "Cycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CycleItems_Trays_TrayInstanceId",
                        column: x => x.TrayInstanceId,
                        principalSchema: "assets",
                        principalTable: "Trays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Nonconformances",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RaisedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RaisedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TrayInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ImmediateAction = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RootCause = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrectiveAction = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PreventiveAction = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClosedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nonconformances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Nonconformances_Cycles_CycleId",
                        column: x => x.CycleId,
                        principalSchema: "assets",
                        principalTable: "Cycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Nonconformances_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "assets",
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Nonconformances_Trays_TrayInstanceId",
                        column: x => x.TrayInstanceId,
                        principalSchema: "assets",
                        principalTable: "Trays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrayIssues",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrayInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IssuedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReturnedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrayIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrayIssues_Cycles_CycleId",
                        column: x => x.CycleId,
                        principalSchema: "assets",
                        principalTable: "Cycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrayIssues_Trays_TrayInstanceId",
                        column: x => x.TrayInstanceId,
                        principalSchema: "assets",
                        principalTable: "Trays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CycleItems_CycleId",
                schema: "assets",
                table: "CycleItems",
                column: "CycleId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleItems_TrayInstanceId",
                schema: "assets",
                table: "CycleItems",
                column: "TrayInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Cycles_LoadNumber",
                schema: "assets",
                table: "Cycles",
                column: "LoadNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cycles_MachineId_StartedAt",
                schema: "assets",
                table: "Cycles",
                columns: new[] { "MachineId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Cycles_Release",
                schema: "assets",
                table: "Cycles",
                column: "Release");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_AssetNumber",
                schema: "assets",
                table: "Equipment",
                column: "AssetNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_Category",
                schema: "assets",
                table: "Equipment",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_LocationId",
                schema: "assets",
                table: "Equipment",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_SerialNumber",
                schema: "assets",
                table: "Equipment",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_SupplierId",
                schema: "assets",
                table: "Equipment",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Implants_BookingId",
                schema: "assets",
                table: "Implants",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Implants_LotId",
                schema: "assets",
                table: "Implants",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_Implants_PatientId",
                schema: "assets",
                table: "Implants",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Implants_StockItemId_Lot",
                schema: "assets",
                table: "Implants",
                columns: new[] { "StockItemId", "Lot" });

            migrationBuilder.CreateIndex(
                name: "IX_LoanSets_BookingId",
                schema: "assets",
                table: "LoanSets",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanSets_CycleId",
                schema: "assets",
                table: "LoanSets",
                column: "CycleId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanSets_Number",
                schema: "assets",
                table: "LoanSets",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoanSets_Status",
                schema: "assets",
                table: "LoanSets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_LoanSets_SupplierId",
                schema: "assets",
                table: "LoanSets",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_EquipmentId_Kind",
                schema: "assets",
                table: "MaintenancePlans",
                columns: new[] { "EquipmentId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_NextDue",
                schema: "assets",
                table: "MaintenancePlans",
                column: "NextDue");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecords_EquipmentId_DoneOn",
                schema: "assets",
                table: "MaintenanceRecords",
                columns: new[] { "EquipmentId", "DoneOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Nonconformances_CycleId",
                schema: "assets",
                table: "Nonconformances",
                column: "CycleId");

            migrationBuilder.CreateIndex(
                name: "IX_Nonconformances_EquipmentId",
                schema: "assets",
                table: "Nonconformances",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Nonconformances_Number",
                schema: "assets",
                table: "Nonconformances",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Nonconformances_Status",
                schema: "assets",
                table: "Nonconformances",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Nonconformances_TrayInstanceId",
                schema: "assets",
                table: "Nonconformances",
                column: "TrayInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId",
                schema: "assets",
                table: "PurchaseOrderLines",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_StockItemId",
                schema: "assets",
                table: "PurchaseOrderLines",
                column: "StockItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_Number",
                schema: "assets",
                table: "PurchaseOrders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_Status",
                schema: "assets",
                table: "PurchaseOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SupplierId",
                schema: "assets",
                table: "PurchaseOrders",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeEvents_BookingId",
                schema: "assets",
                table: "ScopeEvents",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeEvents_CycleId",
                schema: "assets",
                table: "ScopeEvents",
                column: "CycleId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeEvents_PatientId",
                schema: "assets",
                table: "ScopeEvents",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeEvents_ScopeId_At",
                schema: "assets",
                table: "ScopeEvents",
                columns: new[] { "ScopeId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_Code",
                schema: "assets",
                table: "StockItems",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_SupplierId",
                schema: "assets",
                table: "StockItems",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLots_Expiry",
                schema: "assets",
                table: "StockLots",
                column: "Expiry");

            migrationBuilder.CreateIndex(
                name: "IX_StockLots_StockItemId_Lot",
                schema: "assets",
                table: "StockLots",
                columns: new[] { "StockItemId", "Lot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_BookingId",
                schema: "assets",
                table: "StockMovements",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_LotId",
                schema: "assets",
                table: "StockMovements",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_PatientId",
                schema: "assets",
                table: "StockMovements",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_PurchaseOrderId",
                schema: "assets",
                table: "StockMovements",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_StockItemId_At",
                schema: "assets",
                table: "StockMovements",
                columns: new[] { "StockItemId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_ExportedToXeroAt",
                schema: "assets",
                table: "SupplierInvoices",
                column: "ExportedToXeroAt");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_PurchaseOrderId",
                schema: "assets",
                table: "SupplierInvoices",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Name",
                schema: "assets",
                table: "Suppliers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrayIssues_BookingId",
                schema: "assets",
                table: "TrayIssues",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_TrayIssues_CycleId",
                schema: "assets",
                table: "TrayIssues",
                column: "CycleId");

            migrationBuilder.CreateIndex(
                name: "IX_TrayIssues_PatientId",
                schema: "assets",
                table: "TrayIssues",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_TrayIssues_TrayInstanceId",
                schema: "assets",
                table: "TrayIssues",
                column: "TrayInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_TrayItems_TraySetId_Sort",
                schema: "assets",
                table: "TrayItems",
                columns: new[] { "TraySetId", "Sort" });

            migrationBuilder.CreateIndex(
                name: "IX_Trays_Barcode",
                schema: "assets",
                table: "Trays",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trays_LastCycleId",
                schema: "assets",
                table: "Trays",
                column: "LastCycleId");

            migrationBuilder.CreateIndex(
                name: "IX_Trays_Status",
                schema: "assets",
                table: "Trays",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Trays_TraySetId",
                schema: "assets",
                table: "Trays",
                column: "TraySetId");

            migrationBuilder.CreateIndex(
                name: "IX_TraySets_Code",
                schema: "assets",
                table: "TraySets",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CycleItems",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "Implants",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "LoanSets",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "MaintenancePlans",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "MaintenanceRecords",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "Nonconformances",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "ScopeEvents",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "StockMovements",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "SupplierInvoices",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "TrayIssues",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "TrayItems",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "StockLots",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "PurchaseOrders",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "Trays",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "StockItems",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "Cycles",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "TraySets",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "Equipment",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "Suppliers",
                schema: "assets");
        }
    }
}
