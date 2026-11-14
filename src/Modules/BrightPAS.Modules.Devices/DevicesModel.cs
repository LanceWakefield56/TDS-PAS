using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Devices;

/// <summary>
/// Devices &amp; SaferSleep (0.17.0, ADR 0029). Bedside monitors (GE CARESCAPE B450 / B1x5 through the CARESCAPE Gateway / Unity network)
/// send HL7 ORU vital signs through the messaging engine; a nurse connects the patient to the monitor and validates readings into the
/// observation chart (nothing reaches the chart unvalidated). SaferSleep (anaesthesia) is sent the patient and booking (HL7 ADT) and returns
/// the anaesthetic record (HL7 ORU with PDF), which is filed to the episode: drugs, events, vital signs, a note and the PDF.
/// </summary>
public sealed class DevicesModule : IPasModule
{
    public const string Id = "devices";
    public ModuleManifest Manifest { get; } = new(Id, "Devices & SaferSleep", typeof(DevicesModule).Assembly.GetName().Version ?? new Version(0, 0),
        DevicesDbContext.Schema, [], "Bedside monitor feeds with nurse validation, and the SaferSleep anaesthetic record interface.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<DevicesDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, DevicesDbContext.Schema));
        services.AddScoped<DeviceService>();
        services.AddScoped<AnaesthesiaService>();
    }
}

public enum CaptureStatus { Pending = 1, Validated = 2, Rejected = 3 }
public enum SendAdtOn { Admission = 1, InTheatre = 2 }

public sealed class DeviceSettings
{
    public int Id { get; set; } = 1;
    /// <summary>How often a monitor reading is offered to the chart, by where the patient is.</summary>
    public int TheatreChartMinutes { get; set; } = 5;
    public int RecoveryChartMinutes { get; set; } = 5;
    public int WardChartMinutes { get; set; } = 15;
    /// <summary>Raw readings are kept at most this often per measurement (the gateway may send every few seconds).</summary>
    public int StoreEverySeconds { get; set; } = 30;
    /// <summary>A monitor that has sent nothing for this long shows as offline.</summary>
    public int OfflineAfterSeconds { get; set; } = 120;
    /// <summary>Readings from a monitor with no patient connected are deleted after this many hours.</summary>
    public int UnassociatedKeepHours { get; set; } = 24;
    /// <summary>Monitor HL7 messages in the messaging store are deleted after this many hours (the readings are kept; failed messages are kept 7 days).</summary>
    public int KeepMonitorMessagesHours { get; set; } = 48;
    /// <summary>Connect the patient automatically when the monitor sends their NHI (admitted on the monitor / gateway).</summary>
    public bool AutoConnectByNhi { get; set; } = true;
    public bool SaferSleepEnabled { get; set; }
    /// <summary>Messaging engine outbound interface code for ADT to SaferSleep.</summary>
    public string SaferSleepOutCode { get; set; } = "safersleep-out";
    public string SaferSleepApplication { get; set; } = "SAFERSLEEP";
    public string SaferSleepFacility { get; set; } = "TDS";
    public SendAdtOn SendOn { get; set; } = SendAdtOn.Admission;
    /// <summary>Medicines in the anaesthetic record are filed into the eMR medicines list (Source = SaferSleep).</summary>
    public bool FileDrugsToEmr { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

/// <summary>A bedside monitor connected through the gateway. The equipment register (assets) holds make, model, location and maintenance.</summary>
public sealed class Monitor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquipmentId { get; set; }
    /// <summary>What the gateway calls this monitor: the bed label (PV1-3, e.g. "PACU/BAY1") or the equipment id it sends (OBX-18, e.g. "MON-02").</summary>
    public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastSeenAt { get; set; }
    /// <summary>The NHI the monitor last sent (if the patient was admitted on the monitor).</summary>
    public string? LastNhi { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

/// <summary>A patient connected to a monitor for an episode. Only one open connection per monitor.</summary>
public sealed class Association
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MonitorId { get; set; }
    public Guid EpisodeId { get; set; }
    public Guid PatientId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string StartedBy { get; set; } = "";
    /// <summary>"Manual" (nurse confirmed the monitor and patient) or "Monitor NHI".</summary>
    public string Method { get; set; } = "Manual";
    public DateTimeOffset? EndedAt { get; set; }
    public string? EndedBy { get; set; }
}

/// <summary>One measurement from a monitor (downsampled). Code is BrightPAS's short name: HR, PR, SpO2, NIBP-S/D/M, ART-S/D/M, RR, Temp, EtCO2.</summary>
public sealed class Reading
{
    public long Id { get; set; }
    public Guid MonitorId { get; set; }
    public Guid? AssociationId { get; set; }
    public DateTimeOffset At { get; set; }
    public string Code { get; set; } = "";
    public decimal Value { get; set; }
    public string? Unit { get; set; }
}

/// <summary>A set of monitor values offered to the observation chart. A nurse validates (it becomes an eMR observation) or rejects it (artefact).</summary>
public sealed class Capture
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssociationId { get; set; }
    public Guid EpisodeId { get; set; }
    public Guid MonitorId { get; set; }
    public DateTimeOffset At { get; set; }
    public int? HeartRate { get; set; }
    public int? SpO2 { get; set; }
    public int? SystolicBp { get; set; }
    public int? DiastolicBp { get; set; }
    public int? MeanBp { get; set; }
    public int? RespiratoryRate { get; set; }
    public decimal? TemperatureC { get; set; }
    /// <summary>End-tidal CO2 in mmHg (kPa converted).</summary>
    public decimal? EtCo2 { get; set; }
    public CaptureStatus Status { get; set; } = CaptureStatus.Pending;
    public Guid? ObservationId { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? RejectReason { get; set; }
}

/// <summary>An anaesthetic record received from SaferSleep. A correction supersedes the earlier version.</summary>
public sealed class AnaestheticRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? EpisodeId { get; set; }
    public Guid? PatientId { get; set; }
    /// <summary>SaferSleep's case number (OBR-3) – the same across corrections.</summary>
    public string CaseNumber { get; set; } = "";
    public string ControlId { get; set; } = "";
    public bool IsCorrection { get; set; }
    public Guid? SupersededById { get; set; }
    public string? Nhi { get; set; }
    public string? Anaesthetist { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public string? Asa { get; set; }
    public string? Technique { get; set; }
    public string? Airway { get; set; }
    public string? Comments { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? NoteId { get; set; }
    public Guid? MessageId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public List<AnaestheticDrug> Drugs { get; set; } = [];
    public List<AnaestheticEvent> Events { get; set; } = [];
    public List<AnaestheticVital> Vitals { get; set; } = [];
}

public sealed class AnaestheticDrug
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecordId { get; set; }
    public DateTimeOffset At { get; set; }
    public string Name { get; set; } = "";
    public decimal? Dose { get; set; }
    public string? Unit { get; set; }
    public string? Route { get; set; }
    /// <summary>Infusion rate text (e.g. "6 mg/kg/h") when it is an infusion.</summary>
    public string? Rate { get; set; }
    /// <summary>Drug barcode / code SaferSleep scanned (e.g. NZMT or its own label code).</summary>
    public string? Code { get; set; }
}

public sealed class AnaestheticEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecordId { get; set; }
    public DateTimeOffset At { get; set; }
    public string Text { get; set; } = "";
}

public sealed class AnaestheticVital
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecordId { get; set; }
    public DateTimeOffset At { get; set; }
    public string Code { get; set; } = "";
    public decimal Value { get; set; }
    public string? Unit { get; set; }
}

/// <summary>An ADT message sent to SaferSleep for an episode (A01 admit, A08 update, A03 discharge).</summary>
public sealed class AdtSend
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public string Event { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string By { get; set; } = "";
    public Guid? MessageId { get; set; }
    public string? Error { get; set; }
}

public sealed class DevicesDbContext(DbContextOptions<DevicesDbContext> options) : DbContext(options)
{
    public const string Schema = "devices";
    public DbSet<DeviceSettings> Settings => Set<DeviceSettings>();
    public DbSet<Monitor> Monitors => Set<Monitor>();
    public DbSet<Association> Associations => Set<Association>();
    public DbSet<Reading> Readings => Set<Reading>();
    public DbSet<Capture> Captures => Set<Capture>();
    public DbSet<AnaestheticRecord> Records => Set<AnaestheticRecord>();
    public DbSet<AnaestheticDrug> Drugs => Set<AnaestheticDrug>();
    public DbSet<AnaestheticEvent> Events => Set<AnaestheticEvent>();
    public DbSet<AnaestheticVital> Vitals => Set<AnaestheticVital>();
    public DbSet<AdtSend> AdtSends => Set<AdtSend>();

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
        b.Entity<DeviceSettings>(e =>
        {
            e.ToTable("Settings"); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SaferSleepOutCode).HasMaxLength(40); e.Property(x => x.SaferSleepApplication).HasMaxLength(40); e.Property(x => x.SaferSleepFacility).HasMaxLength(40);
            e.Property(x => x.SendOn).HasConversion<string>().HasMaxLength(20); e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
        b.Entity<Monitor>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(60); e.Property(x => x.LastNhi).HasMaxLength(10); e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => x.EquipmentId).IsUnique();
        });
        b.Entity<Association>(e =>
        {
            e.Property(x => x.StartedBy).HasMaxLength(100); e.Property(x => x.EndedBy).HasMaxLength(100); e.Property(x => x.Method).HasMaxLength(20);
            // The database itself guarantees one patient per monitor and one monitor per patient at a time (two messages or two nurses at once cannot both win).
            e.HasIndex(x => x.MonitorId).IsUnique().HasFilter("[EndedAt] IS NULL").HasDatabaseName("UX_Associations_OpenMonitor");
            e.HasIndex(x => x.EpisodeId).IsUnique().HasFilter("[EndedAt] IS NULL").HasDatabaseName("UX_Associations_OpenEpisode");
            e.HasIndex(x => new { x.MonitorId, x.StartedAt }); e.HasIndex(x => new { x.EpisodeId, x.StartedAt }); e.HasIndex(x => x.PatientId);
            e.HasOne<Monitor>().WithMany().HasForeignKey(x => x.MonitorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Reading>(e =>
        {
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Code).HasMaxLength(12); e.Property(x => x.Unit).HasMaxLength(20); e.Property(x => x.Value).HasPrecision(9, 2);
            e.HasIndex(x => new { x.MonitorId, x.At }); e.HasIndex(x => new { x.AssociationId, x.At });
            e.HasOne<Monitor>().WithMany().HasForeignKey(x => x.MonitorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Association>().WithMany().HasForeignKey(x => x.AssociationId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<Capture>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.TemperatureC).HasPrecision(4, 1); e.Property(x => x.EtCo2).HasPrecision(5, 1);
            e.Property(x => x.DecidedBy).HasMaxLength(100); e.Property(x => x.RejectReason).HasMaxLength(200);
            e.HasIndex(x => new { x.EpisodeId, x.At }); e.HasIndex(x => new { x.AssociationId, x.At }); e.HasIndex(x => x.MonitorId); e.HasIndex(x => x.ObservationId);
            e.HasOne<Association>().WithMany().HasForeignKey(x => x.AssociationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Monitor>().WithMany().HasForeignKey(x => x.MonitorId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<AnaestheticRecord>(e =>
        {
            e.ToTable("AnaestheticRecords");
            e.Property(x => x.CaseNumber).HasMaxLength(64); e.Property(x => x.ControlId).HasMaxLength(64); e.Property(x => x.Nhi).HasMaxLength(10);
            e.Property(x => x.Anaesthetist).HasMaxLength(100); e.Property(x => x.Asa).HasMaxLength(10); e.Property(x => x.Technique).HasMaxLength(100); e.Property(x => x.Airway).HasMaxLength(100);
            e.Property(x => x.Comments).HasMaxLength(2000);
            e.HasIndex(x => x.EpisodeId); e.HasIndex(x => x.PatientId); e.HasIndex(x => x.CaseNumber); e.HasIndex(x => x.ControlId).IsUnique();
            e.HasIndex(x => x.SupersededById); e.HasIndex(x => x.DocumentId); e.HasIndex(x => x.NoteId); e.HasIndex(x => x.MessageId);
            e.HasOne<AnaestheticRecord>().WithMany().HasForeignKey(x => x.SupersededById).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Drugs).WithOne().HasForeignKey(x => x.RecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.RecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Vitals).WithOne().HasForeignKey(x => x.RecordId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AnaestheticDrug>(e =>
        {
            e.ToTable("AnaestheticDrugs");
            e.Property(x => x.Name).HasMaxLength(150); e.Property(x => x.Dose).HasPrecision(10, 3); e.Property(x => x.Unit).HasMaxLength(20); e.Property(x => x.Route).HasMaxLength(30);
            e.Property(x => x.Rate).HasMaxLength(60); e.Property(x => x.Code).HasMaxLength(60);
            e.HasIndex(x => new { x.RecordId, x.At });
        });
        b.Entity<AnaestheticEvent>(e =>
        {
            e.ToTable("AnaestheticEvents"); e.Property(x => x.Text).HasMaxLength(300); e.HasIndex(x => new { x.RecordId, x.At });
        });
        b.Entity<AnaestheticVital>(e =>
        {
            e.ToTable("AnaestheticVitals"); e.Property(x => x.Code).HasMaxLength(12); e.Property(x => x.Unit).HasMaxLength(20); e.Property(x => x.Value).HasPrecision(9, 2);
            e.HasIndex(x => new { x.RecordId, x.At });
        });
        b.Entity<AdtSend>(e =>
        {
            e.Property(x => x.Event).HasMaxLength(3); e.Property(x => x.By).HasMaxLength(100); e.Property(x => x.Error).HasMaxLength(300);
            e.HasIndex(x => new { x.EpisodeId, x.Event }); e.HasIndex(x => x.MessageId);
        });
    }
}

public sealed class DevicesDbContextFactory : IDesignTimeDbContextFactory<DevicesDbContext>
{
    public DevicesDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<DevicesDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", DevicesDbContext.Schema)).Options);
}
