using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Prescribing;

/// <summary>
/// Prescribing and barcode medicine checks (0.31.0, ADR 0044). Prescribers write medicine orders on the episode's medicine chart (once, regular or
/// as needed); nurses give against an order. Barcode medicine administration (BCMA): before a medicine is charted the nurse scans the patient's
/// wristband and the medicine pack (GS1 barcode: GTIN, expiry, batch); BrightPAS checks right patient, right medicine, in date, and that there is a
/// current order – the check runs inside the clinical record's medicine checks (IMedicineSafetyCheck). Discharge prescriptions: printed with the
/// NZePS barcode and, when connected, sent to the NZ ePrescription Service; a FHIR copy can be written to a folder as the fallback.
/// </summary>
public sealed class PrescribingModule : IPasModule
{
    public const string Id = "prescribing";
    public ModuleManifest Manifest { get; } = new(Id, "Prescribing and barcode medicine checks", typeof(PrescribingModule).Assembly.GetName().Version ?? new Version(0, 0),
        RxDbContext.Schema, ["emr", "medicines"], "Medicine orders, barcode checks when giving medicines, and NZ ePrescriptions.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<RxDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, RxDbContext.Schema));
        services.AddScoped<OrderService>();
        services.AddScoped<BcmaService>();
        services.AddScoped<PrescriptionService>();
    }
}

/// <summary>Off: no barcode check. Advise: findings shown, never blocked. Enforce: a Stop needs a reason (wrong patient and expired always stop).</summary>
public enum BcmaMode { Off = 0, Advise = 1, Enforce = 2 }

/// <summary>Off: print only. Simulator: training (nothing leaves BrightPAS). Nzeps: the NZ ePrescription Service (Health NZ), once connected.</summary>
public enum NzepsProvider { Off = 1, Simulator = 2, Nzeps = 3 }

public enum OrderKind { Once = 1, Regular = 2, AsNeeded = 3 }

public enum OrderStatus { Active = 1, Given = 2, Stopped = 3 }

public enum BcmaResult { Clear = 1, Warnings = 2, Overridden = 3, Stopped = 4 }

public enum RxStatus { Draft = 1, Signed = 2, Sent = 3, Failed = 4, Cancelled = 5 }

public sealed class RxSettings
{
    public int Id { get; set; } = 1;
    public BcmaMode BcmaMode { get; set; } = BcmaMode.Off;
    /// <summary>Warn when a medicine is given without a current order on the chart (anaesthetists' theatre drugs are usually not ordered first).</summary>
    public bool WarnWithoutOrder { get; set; }
    public NzepsProvider Provider { get; set; } = NzepsProvider.Off;
    public string? NzepsUrl { get; set; }
    public string? NzepsClientId { get; set; }
    public string? NzepsKeyProtected { get; set; }
    /// <summary>HPI facility id of TDS (Fxxxxx-C), printed and sent with every prescription.</summary>
    public string? FacilityHpi { get; set; }
    /// <summary>Optional folder for a FHIR copy of each signed prescription (the fallback when NZePS is not connected).</summary>
    public string? ExportFolder { get; set; }
    public int NextNumber { get; set; } = 1;
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Someone who may prescribe: linked to a BrightPAS user, with their HPI person id (CPN) and council registration.</summary>
public sealed class Prescriber
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>HPI common person number, e.g. 12ABCD.</summary>
    public string HpiCpn { get; set; } = "";
    /// <summary>Medical Council / Nursing Council registration number.</summary>
    public string? Registration { get; set; }
    /// <summary>Medical practitioner, Nurse practitioner, Dentist, Designated prescriber.</summary>
    public string Scope { get; set; } = "Medical practitioner";
    public bool IsActive { get; set; } = true;
}

/// <summary>A medicine order on the episode's medicine chart.</summary>
public sealed class MedOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid EpisodeId { get; set; }
    public Guid? MedicineId { get; set; }
    public string Medicine { get; set; } = "";
    public string Dose { get; set; } = "";
    public string Route { get; set; } = "";
    public OrderKind Kind { get; set; }
    /// <summary>For regular and as-needed orders, e.g. "6-hourly", "every 4 h, max 4 doses in 24 h".</summary>
    public string? Frequency { get; set; }
    public string? Indication { get; set; }
    public string? Notes { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Active;
    public DateTimeOffset PrescribedAt { get; set; }
    public string PrescribedBy { get; set; } = "";
    public DateTimeOffset? StoppedAt { get; set; }
    public string? StoppedBy { get; set; }
    public string? StopReason { get; set; }
    public int TimesGiven { get; set; }
    public DateTimeOffset? LastGivenAt { get; set; }
}

/// <summary>One barcode check when a medicine was about to be charted, kept with what was scanned and what was found.</summary>
public sealed class BcmaScan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid EpisodeId { get; set; }
    public Guid? OrderId { get; set; }
    /// <summary>The emr.Medications row, once saved.</summary>
    public Guid? MedicationId { get; set; }
    public string Medicine { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string User { get; set; } = "";
    public BcmaMode Mode { get; set; }
    public bool PatientScanned { get; set; }
    public bool? PatientMatched { get; set; }
    public bool PackScanned { get; set; }
    public string? Gtin { get; set; }
    public string? Batch { get; set; }
    public DateOnly? Expiry { get; set; }
    public string? Serial { get; set; }
    public bool? PackMatched { get; set; }
    public BcmaResult Result { get; set; }
    public string? OverrideReason { get; set; }
    /// <summary>The findings, one per line ("Stop: …").</summary>
    public string Findings { get; set; } = "";
}

/// <summary>A prescription to take away (discharge medicines), printed with its NZePS barcode and optionally sent to NZePS.</summary>
public sealed class Prescription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>RX000001.</summary>
    public string Number { get; set; } = "";
    /// <summary>The 18-character NZePS prescription id (17 characters and an ISO 7064 Mod 37,2 check character), printed as the barcode.</summary>
    public string NzepsId { get; set; } = "";
    public Guid PatientId { get; set; }
    public Guid? EpisodeId { get; set; }
    public Guid PrescriberId { get; set; }
    public RxStatus Status { get; set; } = RxStatus.Draft;
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? SignedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? SendReference { get; set; }
    public string? SendError { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancelReason { get; set; }
    public int TimesPrinted { get; set; }
    public List<PrescriptionItem> Items { get; set; } = [];
}

public sealed class PrescriptionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PrescriptionId { get; set; }
    public Guid? MedicineId { get; set; }
    public string Medicine { get; set; } = "";
    public string? NzmtId { get; set; }
    /// <summary>Directions as the patient reads them ("Take 1 or 2 tablets every 4 to 6 hours when needed for pain. Max 8 in 24 hours.").</summary>
    public string Directions { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "tablet";
    public int Repeats { get; set; }
    /// <summary>A controlled drug – needs the prescriber's own signature on the printed copy.</summary>
    public bool Controlled { get; set; }
}

public sealed class RxDbContext(DbContextOptions<RxDbContext> options) : DbContext(options)
{
    public const string Schema = "rx";
    public DbSet<RxSettings> Settings => Set<RxSettings>();
    public DbSet<Prescriber> Prescribers => Set<Prescriber>();
    public DbSet<MedOrder> Orders => Set<MedOrder>();
    public DbSet<BcmaScan> Scans => Set<BcmaScan>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionItem> Items => Set<PrescriptionItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<RxSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.BcmaMode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.NzepsUrl).HasMaxLength(200);
            e.Property(x => x.NzepsClientId).HasMaxLength(100);
            e.Property(x => x.NzepsKeyProtected).HasMaxLength(1000);
            e.Property(x => x.FacilityHpi).HasMaxLength(20);
            e.Property(x => x.ExportFolder).HasMaxLength(260);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
        b.Entity<Prescriber>(e =>
        {
            e.Property(x => x.UserName).HasMaxLength(100);
            e.HasIndex(x => x.UserName).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.HpiCpn).HasMaxLength(10);
            e.Property(x => x.Registration).HasMaxLength(20);
            e.Property(x => x.Scope).HasMaxLength(40);
        });
        b.Entity<MedOrder>(e =>
        {
            e.Property(x => x.Medicine).HasMaxLength(150);
            e.Property(x => x.Dose).HasMaxLength(60);
            e.Property(x => x.Route).HasMaxLength(30);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Frequency).HasMaxLength(100);
            e.Property(x => x.Indication).HasMaxLength(150);
            e.Property(x => x.Notes).HasMaxLength(300);
            e.Property(x => x.PrescribedBy).HasMaxLength(100);
            e.Property(x => x.StoppedBy).HasMaxLength(100);
            e.Property(x => x.StopReason).HasMaxLength(300);
            e.HasIndex(x => x.EpisodeId);
            e.HasIndex(x => x.PatientId);
        });
        b.Entity<BcmaScan>(e =>
        {
            e.Property(x => x.Medicine).HasMaxLength(150);
            e.Property(x => x.User).HasMaxLength(100);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Gtin).HasMaxLength(14);
            e.Property(x => x.Batch).HasMaxLength(20);
            e.Property(x => x.Serial).HasMaxLength(20);
            e.Property(x => x.Result).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.OverrideReason).HasMaxLength(500);
            e.Property(x => x.Findings).HasMaxLength(2000);
            e.HasIndex(x => x.At);
            e.HasIndex(x => x.EpisodeId);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.MedicationId);
        });
        b.Entity<Prescription>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.NzepsId).HasMaxLength(18);
            e.HasIndex(x => x.NzepsId).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.SendReference).HasMaxLength(100);
            e.Property(x => x.SendError).HasMaxLength(500);
            e.Property(x => x.CancelledBy).HasMaxLength(100);
            e.Property(x => x.CancelReason).HasMaxLength(300);
            e.HasIndex(x => x.PatientId);
            e.HasOne<Prescriber>().WithMany().HasForeignKey(x => x.PrescriberId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.PrescriptionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PrescriptionItem>(e =>
        {
            e.Property(x => x.Medicine).HasMaxLength(150);
            e.Property(x => x.NzmtId).HasMaxLength(20);
            e.Property(x => x.Directions).HasMaxLength(500);
            e.Property(x => x.Quantity).HasPrecision(9, 2);
            e.Property(x => x.Unit).HasMaxLength(30);
        });
    }
}

public sealed class RxDbContextFactory : IDesignTimeDbContextFactory<RxDbContext>
{
    public RxDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<RxDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", RxDbContext.Schema)).Options);
}
