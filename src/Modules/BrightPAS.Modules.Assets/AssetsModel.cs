using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Assets;

/// <summary>
/// Assets &amp; CSSD (spec 7.9, ADR 0025): equipment register with planned maintenance, test-and-tag and lockout; CSSD traceability to
/// AS/NZS 5369:2023 (tray catalogue, washer/steriliser cycles with Belimed log import, parametric release, issue to case, recall by load,
/// nonconformance/CAPA); endoscope traceability; loan sets; stock, implants and purchasing (draft orders, goods receipt, invoice matching, Xero bills).
/// </summary>
public sealed class AssetsModule : IPasModule
{
    public const string Id = "assets";
    public ModuleManifest Manifest { get; } = new(Id, "Assets & CSSD", typeof(AssetsModule).Assembly.GetName().Version ?? new Version(0, 0),
        AssetsDbContext.Schema, [], "Equipment register and maintenance, sterile instrument and endoscope traceability, loan sets, stock, implants and purchasing.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<AssetsDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, AssetsDbContext.Schema));
        services.AddScoped<EquipmentService>();
        services.AddScoped<CssdService>();
        services.AddScoped<StockService>();
    }
}

// ------------------------------------------------------------------ equipment
public enum EquipmentCategory { Electromedical = 1, Washer = 2, Steriliser = 3, Aer = 4, DryingCabinet = 5, Endoscope = 6, Instrument = 7, Furniture = 8, It = 9, Other = 10, Monitor = 11, EndoscopyTower = 12 }
public enum RiskClass { Low = 1, Medium = 2, High = 3 }
public enum EquipmentStatus { InService = 1, OutOfService = 2, Retired = 3 }
public enum MaintenanceKind { Ppm = 1, Calibration = 2, TestAndTag = 3, BiomedicalSafety = 4, Validation = 5, Repair = 6 }

public sealed class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>The contact name used in Xero (bills are exported against it).</summary>
    public string? XeroContactName { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>An item of equipment: electromedical devices, washers, sterilisers, endoscopes, monitors, towers, furniture.</summary>
public sealed class Equipment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AssetNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public EquipmentCategory Category { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    /// <summary>Room it lives in (core.Locations), when it has a fixed place.</summary>
    public Guid? LocationId { get; set; }
    public string? Owner { get; set; }
    public Guid? SupplierId { get; set; }
    public DateOnly? PurchasedOn { get; set; }
    public decimal? Value { get; set; }
    public DateOnly? WarrantyUntil { get; set; }
    public RiskClass Risk { get; set; } = RiskClass.Medium;
    public EquipmentStatus Status { get; set; } = EquipmentStatus.InService;
    public string? OutOfServiceReason { get; set; }
    public string? LockedOutBy { get; set; }
    public DateTimeOffset? LockedOutAt { get; set; }
    /// <summary>Network address of a device that talks to BrightPAS (monitor, endoscopy tower, washer) – used by the device integration.</summary>
    public string? NetworkAddress { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A recurring check: PPM, calibration, test-and-tag (AS/NZS 3760), biomedical safety, validation.</summary>
public sealed class MaintenancePlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquipmentId { get; set; }
    public MaintenanceKind Kind { get; set; }
    public int IntervalMonths { get; set; } = 12;
    public DateOnly NextDue { get; set; }
    public string? DoneBy { get; set; }
}

public sealed class MaintenanceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquipmentId { get; set; }
    public MaintenanceKind Kind { get; set; }
    public DateOnly DoneOn { get; set; }
    public string DoneBy { get; set; } = "";
    public bool Passed { get; set; }
    public string? TagNumber { get; set; }
    public string? Notes { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}

// ------------------------------------------------------------------ CSSD
public enum TrayStatus { Dirty = 1, Washed = 2, Packed = 3, Sterile = 4, Issued = 5, Quarantine = 6, Retired = 7 }
public enum CycleKind { Wash = 1, Steam = 2, LowTemperature = 3, Aer = 4 }
public enum CycleRelease { Pending = 1, Released = 2, Failed = 3 }
public enum IndicatorResult { NotUsed = 1, Pending = 2, Pass = 3, Fail = 4 }

/// <summary>A tray or set in the catalogue, with its contents list (count sheet).</summary>
public sealed class TraySet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Specialty { get; set; }
    /// <summary>Days a sterile tray may be stored before it must be reprocessed (event-related sterility can be set by choosing a long period).</summary>
    public int ShelfLifeDays { get; set; } = 90;
    public bool IsActive { get; set; } = true;
    public List<TrayItem> Items { get; set; } = [];
}

public sealed class TrayItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TraySetId { get; set; }
    public int Sort { get; set; }
    public string Description { get; set; } = "";
    public int Quantity { get; set; } = 1;
}

/// <summary>A physical tray/pack with its own barcode (several trays may share a catalogue entry).</summary>
public sealed class TrayInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Barcode { get; set; } = "";
    public Guid TraySetId { get; set; }
    public TrayStatus Status { get; set; } = TrayStatus.Dirty;
    /// <summary>The steriliser load it was last sterilised in.</summary>
    public Guid? LastCycleId { get; set; }
    public DateOnly? SterileUntil { get; set; }
    public string? PackedBy { get; set; }
    public DateTimeOffset? PackedAt { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One washer, steriliser or AER cycle (a load), entered by hand or imported from the machine's log.</summary>
public sealed class Cycle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LoadNumber { get; set; } = "";
    public Guid MachineId { get; set; }
    public CycleKind Kind { get; set; }
    public string? MachineCycleNumber { get; set; }
    public string? Program { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public decimal? MaxTemperatureC { get; set; }
    public int? HoldSeconds { get; set; }
    public decimal? PressureKpa { get; set; }
    /// <summary>Washer-disinfector A0 value (ISO 15883).</summary>
    public int? A0 { get; set; }
    /// <summary>The machine reported the cycle completed without fault.</summary>
    public bool MachinePassed { get; set; }
    public IndicatorResult ChemicalIndicator { get; set; } = IndicatorResult.NotUsed;
    public IndicatorResult BiologicalIndicator { get; set; } = IndicatorResult.NotUsed;
    public bool BowieDickTest { get; set; }
    public CycleRelease Release { get; set; } = CycleRelease.Pending;
    public string? ReleasedBy { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public string? ReleaseNote { get; set; }
    public string Operator { get; set; } = "";
    /// <summary>"Manual" or the file name of the imported machine log.</summary>
    public string Source { get; set; } = "Manual";
    /// <summary>The machine log as received (kept for audit).</summary>
    public string? RawLog { get; set; }
    public List<CycleItem> Items { get; set; } = [];
}

/// <summary>What was in the load: a tray (by barcode) or a described item (steri packs, misc devices).</summary>
public sealed class CycleItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CycleId { get; set; }
    public Guid? TrayInstanceId { get; set; }
    public string? Description { get; set; }
    public int Quantity { get; set; } = 1;
}

/// <summary>A sterile tray issued to a theatre case: the set-to-patient link used for recalls.</summary>
public sealed class TrayIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrayInstanceId { get; set; }
    public Guid? CycleId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? PatientId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public string IssuedBy { get; set; } = "";
    public DateTimeOffset? ReturnedAt { get; set; }
}

public enum ScopeEventKind { Use = 1, LeakTest = 2, ManualClean = 3, AerCycle = 4, DryingIn = 5, DryingOut = 6, MicroSample = 7, Repair = 8, Quarantine = 9 }

/// <summary>Endoscope history: use on a patient, leak test, manual clean, AER cycle, drying cabinet, microbiology, repair.</summary>
public sealed class ScopeEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScopeId { get; set; }
    public ScopeEventKind Kind { get; set; }
    public DateTimeOffset At { get; set; }
    public string By { get; set; } = "";
    public bool? Passed { get; set; }
    public Guid? CycleId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? PatientId { get; set; }
    public string? Notes { get; set; }
}

public enum LoanStatus { Expected = 1, Received = 2, Checked = 3, Reprocessed = 4, Used = 5, Returned = 6 }

public sealed class LoanSet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid? SupplierId { get; set; }
    public string Description { get; set; } = "";
    public int ItemCount { get; set; }
    public Guid? BookingId { get; set; }
    public LoanStatus Status { get; set; } = LoanStatus.Expected;
    public DateTimeOffset? ReceivedAt { get; set; }
    public string? CheckedBy { get; set; }
    public string? CheckNotes { get; set; }
    public Guid? CycleId { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum NcStatus { Open = 1, ActionsAgreed = 2, Closed = 3 }

/// <summary>Nonconformance with corrective and preventive action (CAPA).</summary>
public sealed class Nonconformance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public DateOnly RaisedOn { get; set; }
    public string RaisedBy { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid? CycleId { get; set; }
    public Guid? TrayInstanceId { get; set; }
    public Guid? EquipmentId { get; set; }
    public string? ImmediateAction { get; set; }
    public string? RootCause { get; set; }
    public string? CorrectiveAction { get; set; }
    public string? PreventiveAction { get; set; }
    public NcStatus Status { get; set; } = NcStatus.Open;
    public string? ClosedBy { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

// ------------------------------------------------------------------ stock & purchasing
public sealed class StockItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "each";
    public bool IsImplant { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierCode { get; set; }
    public int ReorderLevel { get; set; }
    public int ReorderQuantity { get; set; }
    public decimal UnitCost { get; set; }
    /// <summary>Xero account code for bills (e.g. 310 Medical supplies).</summary>
    public string? AccountCode { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class StockLot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StockItemId { get; set; }
    public string Lot { get; set; } = "";
    public DateOnly? Expiry { get; set; }
    public int OnHand { get; set; }
    public DateOnly ReceivedOn { get; set; }
}

public enum MovementKind { Receipt = 1, Use = 2, Adjust = 3, Expired = 4, Return = 5 }

public sealed class StockMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StockItemId { get; set; }
    public Guid? LotId { get; set; }
    public int Quantity { get; set; }
    public MovementKind Kind { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset At { get; set; }
    public string By { get; set; } = "";
}

/// <summary>Patient-level implant record (for manufacturer recalls and the implant registry).</summary>
public sealed class ImplantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid StockItemId { get; set; }
    public Guid? LotId { get; set; }
    public string Lot { get; set; } = "";
    public string? SerialNumber { get; set; }
    public string? Manufacturer { get; set; }
    public string? Site { get; set; }
    public DateOnly ImplantedOn { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}

public enum PoStatus { Draft = 1, Approved = 2, Sent = 3, PartReceived = 4, Received = 5, Invoiced = 6, Cancelled = 7 }

public sealed class PurchaseOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid SupplierId { get; set; }
    public PoStatus Status { get; set; } = PoStatus.Draft;
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = [];
}

public sealed class PurchaseOrderLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseOrderId { get; set; }
    public Guid StockItemId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public int QuantityReceived { get; set; }
}

/// <summary>The supplier's invoice, matched to the order and exported to Xero as a bill.</summary>
public sealed class SupplierInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseOrderId { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    /// <summary>Total excluding GST as on the invoice.</summary>
    public decimal AmountExGst { get; set; }
    public bool Matched { get; set; }
    public string? MatchNote { get; set; }
    public DateTimeOffset? ExportedToXeroAt { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class AssetsSettings
{
    public int Id { get; set; } = 1;
    /// <summary>Endoscopes stored longer than this in the drying cabinet must be reprocessed before use.</summary>
    public int ScopeStorageHours { get; set; } = 72;
    public int MaintenanceWarningDays { get; set; } = 30;
    /// <summary>Require a second person (not the operator) to release steriliser loads.</summary>
    public bool ReleaseNeedsSecondPerson { get; set; }
    public int NextLoad { get; set; } = 1;
    public int NextNc { get; set; } = 1;
    public int NextPo { get; set; } = 1;
    public int NextLoan { get; set; } = 1;
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class AssetsDbContext(DbContextOptions<AssetsDbContext> options) : DbContext(options)
{
    public const string Schema = "assets";
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<MaintenancePlan> Plans => Set<MaintenancePlan>();
    public DbSet<MaintenanceRecord> Maintenance => Set<MaintenanceRecord>();
    public DbSet<TraySet> TraySets => Set<TraySet>();
    public DbSet<TrayItem> TrayItems => Set<TrayItem>();
    public DbSet<TrayInstance> Trays => Set<TrayInstance>();
    public DbSet<Cycle> Cycles => Set<Cycle>();
    public DbSet<CycleItem> CycleItems => Set<CycleItem>();
    public DbSet<TrayIssue> Issues => Set<TrayIssue>();
    public DbSet<ScopeEvent> ScopeEvents => Set<ScopeEvent>();
    public DbSet<LoanSet> Loans => Set<LoanSet>();
    public DbSet<Nonconformance> Nonconformances => Set<Nonconformance>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockLot> Lots => Set<StockLot>();
    public DbSet<StockMovement> Movements => Set<StockMovement>();
    public DbSet<ImplantRecord> Implants => Set<ImplantRecord>();
    public DbSet<PurchaseOrder> Orders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> OrderLines => Set<PurchaseOrderLine>();
    public DbSet<SupplierInvoice> Invoices => Set<SupplierInvoice>();
    public DbSet<AssetsSettings> Settings => Set<AssetsSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
            c.Properties<decimal>().HaveConversion<double>();
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<Supplier>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150); e.Property(x => x.ContactName).HasMaxLength(100); e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(40); e.Property(x => x.XeroContactName).HasMaxLength(150);
            e.HasIndex(x => x.Name).IsUnique();
        });
        b.Entity<Equipment>(e =>
        {
            e.Property(x => x.AssetNumber).HasMaxLength(40); e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Risk).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Make).HasMaxLength(80); e.Property(x => x.Model).HasMaxLength(80); e.Property(x => x.SerialNumber).HasMaxLength(80);
            e.Property(x => x.Owner).HasMaxLength(100); e.Property(x => x.Value).HasPrecision(12, 2);
            e.Property(x => x.OutOfServiceReason).HasMaxLength(300); e.Property(x => x.LockedOutBy).HasMaxLength(100);
            e.Property(x => x.NetworkAddress).HasMaxLength(100); e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasIndex(x => x.AssetNumber).IsUnique();
            e.HasIndex(x => x.Category); e.HasIndex(x => x.LocationId); e.HasIndex(x => x.SerialNumber);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<MaintenancePlan>(e =>
        {
            e.ToTable("MaintenancePlans");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20); e.Property(x => x.DoneBy).HasMaxLength(100);
            e.HasIndex(x => new { x.EquipmentId, x.Kind }).IsUnique(); e.HasIndex(x => x.NextDue);
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<MaintenanceRecord>(e =>
        {
            e.ToTable("MaintenanceRecords");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20); e.Property(x => x.DoneBy).HasMaxLength(100);
            e.Property(x => x.TagNumber).HasMaxLength(40); e.Property(x => x.Notes).HasMaxLength(1000); e.Property(x => x.RecordedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.EquipmentId, x.DoneOn });
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TraySet>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30); e.Property(x => x.Name).HasMaxLength(150); e.Property(x => x.Specialty).HasMaxLength(60);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.TraySetId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TrayItem>(e => { e.Property(x => x.Description).HasMaxLength(200); e.HasIndex(x => new { x.TraySetId, x.Sort }); });
        b.Entity<TrayInstance>(e =>
        {
            e.ToTable("Trays");
            e.Property(x => x.Barcode).HasMaxLength(40); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PackedBy).HasMaxLength(100); e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => x.Barcode).IsUnique(); e.HasIndex(x => x.TraySetId); e.HasIndex(x => x.Status); e.HasIndex(x => x.LastCycleId);
            e.HasOne<TraySet>().WithMany().HasForeignKey(x => x.TraySetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Cycle>().WithMany().HasForeignKey(x => x.LastCycleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Cycle>(e =>
        {
            e.Property(x => x.LoadNumber).HasMaxLength(30); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MachineCycleNumber).HasMaxLength(40); e.Property(x => x.Program).HasMaxLength(80);
            e.Property(x => x.MaxTemperatureC).HasPrecision(6, 2); e.Property(x => x.PressureKpa).HasPrecision(8, 2);
            e.Property(x => x.ChemicalIndicator).HasConversion<string>().HasMaxLength(10); e.Property(x => x.BiologicalIndicator).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Release).HasConversion<string>().HasMaxLength(10); e.Property(x => x.ReleasedBy).HasMaxLength(100); e.Property(x => x.ReleaseNote).HasMaxLength(500);
            e.Property(x => x.Operator).HasMaxLength(100); e.Property(x => x.Source).HasMaxLength(200);
            e.HasIndex(x => x.LoadNumber).IsUnique(); e.HasIndex(x => new { x.MachineId, x.StartedAt }); e.HasIndex(x => x.Release);
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CycleItem>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(200);
            e.HasIndex(x => x.CycleId); e.HasIndex(x => x.TrayInstanceId);
            e.HasOne<TrayInstance>().WithMany().HasForeignKey(x => x.TrayInstanceId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TrayIssue>(e =>
        {
            e.ToTable("TrayIssues");
            e.Property(x => x.IssuedBy).HasMaxLength(100);
            e.HasIndex(x => x.TrayInstanceId); e.HasIndex(x => x.CycleId); e.HasIndex(x => x.BookingId); e.HasIndex(x => x.PatientId);
            e.HasOne<TrayInstance>().WithMany().HasForeignKey(x => x.TrayInstanceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ScopeEvent>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20); e.Property(x => x.By).HasMaxLength(100); e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => new { x.ScopeId, x.At }); e.HasIndex(x => x.CycleId); e.HasIndex(x => x.BookingId); e.HasIndex(x => x.PatientId);
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.ScopeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LoanSet>(e =>
        {
            e.ToTable("LoanSets");
            e.Property(x => x.Number).HasMaxLength(30); e.Property(x => x.Description).HasMaxLength(300); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CheckedBy).HasMaxLength(100); e.Property(x => x.CheckNotes).HasMaxLength(500);
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.SupplierId); e.HasIndex(x => x.BookingId); e.HasIndex(x => x.CycleId); e.HasIndex(x => x.Status);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Nonconformance>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20); e.Property(x => x.RaisedBy).HasMaxLength(100); e.Property(x => x.Category).HasMaxLength(60);
            e.Property(x => x.Description).HasMaxLength(2000); e.Property(x => x.ImmediateAction).HasMaxLength(1000); e.Property(x => x.RootCause).HasMaxLength(1000);
            e.Property(x => x.CorrectiveAction).HasMaxLength(1000); e.Property(x => x.PreventiveAction).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.ClosedBy).HasMaxLength(100);
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.Status); e.HasIndex(x => x.CycleId); e.HasIndex(x => x.TrayInstanceId); e.HasIndex(x => x.EquipmentId);
            e.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrayInstance>().WithMany().HasForeignKey(x => x.TrayInstanceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StockItem>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(40); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Unit).HasMaxLength(20);
            e.Property(x => x.SupplierCode).HasMaxLength(60); e.Property(x => x.UnitCost).HasPrecision(12, 2); e.Property(x => x.AccountCode).HasMaxLength(20);
            e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => x.SupplierId);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StockLot>(e =>
        {
            e.ToTable("StockLots");
            e.Property(x => x.Lot).HasMaxLength(60);
            e.HasIndex(x => new { x.StockItemId, x.Lot }).IsUnique(); e.HasIndex(x => x.Expiry);
            e.HasOne<StockItem>().WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StockMovement>(e =>
        {
            e.ToTable("StockMovements");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Reason).HasMaxLength(300); e.Property(x => x.By).HasMaxLength(100);
            e.HasIndex(x => new { x.StockItemId, x.At }); e.HasIndex(x => x.LotId); e.HasIndex(x => x.BookingId); e.HasIndex(x => x.PatientId); e.HasIndex(x => x.PurchaseOrderId);
            e.HasOne<StockItem>().WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StockLot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ImplantRecord>(e =>
        {
            e.ToTable("Implants");
            e.Property(x => x.Lot).HasMaxLength(60); e.Property(x => x.SerialNumber).HasMaxLength(60); e.Property(x => x.Manufacturer).HasMaxLength(100);
            e.Property(x => x.Site).HasMaxLength(100); e.Property(x => x.RecordedBy).HasMaxLength(100);
            e.HasIndex(x => x.PatientId); e.HasIndex(x => x.BookingId); e.HasIndex(x => new { x.StockItemId, x.Lot }); e.HasIndex(x => x.LotId);
            e.HasOne<StockItem>().WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StockLot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("PurchaseOrders");
            e.Property(x => x.Number).HasMaxLength(20); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CreatedBy).HasMaxLength(100); e.Property(x => x.ApprovedBy).HasMaxLength(100); e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.SupplierId); e.HasIndex(x => x.Status);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PurchaseOrderLine>(e =>
        {
            e.ToTable("PurchaseOrderLines");
            e.Property(x => x.UnitCost).HasPrecision(12, 2);
            e.HasIndex(x => x.PurchaseOrderId); e.HasIndex(x => x.StockItemId);
            e.HasOne<StockItem>().WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SupplierInvoice>(e =>
        {
            e.ToTable("SupplierInvoices");
            e.Property(x => x.InvoiceNumber).HasMaxLength(60); e.Property(x => x.AmountExGst).HasPrecision(12, 2); e.Property(x => x.MatchNote).HasMaxLength(500);
            e.Property(x => x.RecordedBy).HasMaxLength(100);
            e.HasIndex(x => x.PurchaseOrderId); e.HasIndex(x => x.ExportedToXeroAt);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AssetsSettings>(e => { e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.UpdatedBy).HasMaxLength(100); });
    }
}

public sealed class AssetsDbContextFactory : IDesignTimeDbContextFactory<AssetsDbContext>
{
    public AssetsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AssetsDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", AssetsDbContext.Schema)).Options);
}
