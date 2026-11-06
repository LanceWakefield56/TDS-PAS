using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Labs;

public sealed class LabsModule : IPasModule
{
    public const string Id = "labs";
    public ModuleManifest Manifest { get; } = new(Id, "Lab results", typeof(LabsModule).Assembly.GetName().Version ?? new Version(0, 0),
        LabsDbContext.Schema, ["patients"], "HL7 lab and histology results, clinician inbox and acknowledgement.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<LabsDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, LabsDbContext.Schema));
        services.AddScoped<LabService>();
    }
}

/// <summary>Every HL7 message received, kept as received (for troubleshooting and medico-legal record).</summary>
public sealed class LabMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset ReceivedAt { get; set; }
    public string Source { get; set; } = "";
    public string? ControlId { get; set; }
    public string? SendingFacility { get; set; }
    public string? MessageType { get; set; }
    public string Raw { get; set; } = "";
    public LabMessageOutcome Outcome { get; set; }
    public string? Error { get; set; }
    public string ReceivedBy { get; set; } = "";
}

public enum LabMessageOutcome { Accepted = 1, Duplicate = 2, Rejected = 3 }

/// <summary>One report (OBR) – a test or panel – with its observations.</summary>
public sealed class LabReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public Guid? PatientId { get; set; }
    public LabMatchStatus MatchStatus { get; set; }
    public string? MatchNote { get; set; }
    public string? MatchedBy { get; set; }
    // Patient as described by the lab (kept for checking and for unmatched results)
    public string? PatientNhi { get; set; }
    public string? PatientMrn { get; set; }
    public string PatientFamilyName { get; set; } = "";
    public string PatientGivenNames { get; set; } = "";
    public DateOnly? PatientDateOfBirth { get; set; }
    // Report
    public string Laboratory { get; set; } = "";
    public string? PlacerOrderNumber { get; set; }
    public string? AccessionNumber { get; set; }
    public string TestCode { get; set; } = "";
    public string TestName { get; set; } = "";
    public string? RequestedBy { get; set; }
    public string? RequestedByCode { get; set; }
    public DateTimeOffset? CollectedAt { get; set; }
    public DateTimeOffset? ReportedAt { get; set; }
    public LabResultStatus ResultStatus { get; set; }
    public bool IsAbnormal { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public Guid? SupersededById { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public string? AcknowledgementComment { get; set; }
    public List<LabObservation> Observations { get; set; } = [];
}

public enum LabMatchStatus { Matched = 1, Unmatched = 2, ManuallyMatched = 3 }
public enum LabResultStatus { Preliminary = 1, Final = 2, Corrected = 3, Cancelled = 4, Partial = 5 }

public sealed class LabObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReportId { get; set; }
    public int Sequence { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ValueType { get; set; }
    public string Value { get; set; } = "";
    public string? Units { get; set; }
    public string? ReferenceRange { get; set; }
    /// <summary>HL7 abnormal flag: H, L, HH, LL, A, AA, N or empty.</summary>
    public string? Flag { get; set; }
    public string? Comment { get; set; }
    public bool IsAbnormal => Flag is { Length: > 0 } f && f != "N";
}

public sealed class LabsDbContext(DbContextOptions<LabsDbContext> options) : DbContext(options)
{
    public const string Schema = "labs";
    public DbSet<LabMessage> Messages => Set<LabMessage>();
    public DbSet<LabReport> Reports => Set<LabReport>();
    public DbSet<LabObservation> Observations => Set<LabObservation>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<LabMessage>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(20);
            e.Property(x => x.ControlId).HasMaxLength(50);
            e.Property(x => x.SendingFacility).HasMaxLength(100);
            e.Property(x => x.MessageType).HasMaxLength(20);
            e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.Property(x => x.ReceivedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.SendingFacility, x.ControlId });
            e.HasIndex(x => x.ReceivedAt);
        });
        b.Entity<LabReport>(e =>
        {
            e.Property(x => x.MatchStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ResultStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MatchNote).HasMaxLength(300);
            e.Property(x => x.MatchedBy).HasMaxLength(100);
            e.Property(x => x.PatientNhi).HasMaxLength(7);
            e.Property(x => x.PatientMrn).HasMaxLength(20);
            e.Property(x => x.PatientFamilyName).HasMaxLength(100);
            e.Property(x => x.PatientGivenNames).HasMaxLength(150);
            e.Property(x => x.Laboratory).HasMaxLength(100);
            e.Property(x => x.PlacerOrderNumber).HasMaxLength(50);
            e.Property(x => x.AccessionNumber).HasMaxLength(50);
            e.Property(x => x.TestCode).HasMaxLength(50);
            e.Property(x => x.TestName).HasMaxLength(200);
            e.Property(x => x.RequestedBy).HasMaxLength(150);
            e.Property(x => x.RequestedByCode).HasMaxLength(30);
            e.Property(x => x.Comment).HasMaxLength(4000);
            e.Property(x => x.AcknowledgedBy).HasMaxLength(100);
            e.Property(x => x.AcknowledgementComment).HasMaxLength(1000);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => new { x.AcknowledgedAt, x.ReceivedAt });
            e.HasIndex(x => new { x.Laboratory, x.AccessionNumber, x.TestCode });
            e.HasMany(x => x.Observations).WithOne().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<LabObservation>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ValueType).HasMaxLength(5);
            e.Property(x => x.Value).HasMaxLength(4000);
            e.Property(x => x.Units).HasMaxLength(50);
            e.Property(x => x.ReferenceRange).HasMaxLength(60);
            e.Property(x => x.Flag).HasMaxLength(5);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.Ignore(x => x.IsAbnormal);
        });
    }
}

public sealed class LabsDbContextFactory : IDesignTimeDbContextFactory<LabsDbContext>
{
    public LabsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<LabsDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", LabsDbContext.Schema)).Options);
}
