using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Research;

/// <summary>De-identified and anonymised reporting (spec 7.21). Licensed with Coding &amp; statutory reporting ("reporting").</summary>
public sealed class ResearchModule : IPasModule
{
    public const string Id = "research";
    public const string LicenceId = "reporting";
    public ModuleManifest Manifest { get; } = new(Id, "De-identified reporting", typeof(ResearchModule).Assembly.GetName().Version ?? new Version(0, 0),
        ResearchDbContext.Schema, ["patients"], "Pseudonymised, de-identified and aggregate extracts with small-number suppression, re-identification check and Privacy Officer approval.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<ResearchDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, ResearchDbContext.Schema));
        services.AddScoped<ExtractService>();
    }
}

/// <summary>Spec 7.21.1 levels (Identified is normal reporting, not offered here).</summary>
public enum DeidLevel { Pseudonymised = 1, DeIdentified = 2, Aggregate = 3 }

public enum ExtractStatus { Draft = 1, Submitted = 2, Approved = 3, Declined = 4, Released = 5 }

public enum ExtractPurpose { QualityImprovement = 1, Board = 2, Benchmarking = 3, Teaching = 4, Research = 5, Audit = 6 }

/// <summary>An extract request: what, why, for whom, at which level; approved by the Privacy Officer when patient-level.</summary>
public sealed class Extract
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Dataset { get; set; } = "";
    public DeidLevel Level { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public ExtractPurpose Purpose { get; set; }
    public string Recipient { get; set; } = "";
    public string? EthicsReference { get; set; }
    public string? MaoriDataGovernance { get; set; }
    public int SmallCell { get; set; } = 5;
    public bool RemoveSmallGroups { get; set; }
    public ExtractStatus Status { get; set; } = ExtractStatus.Draft;
    public int? Rows { get; set; }
    public int? MinGroupSize { get; set; }
    public int? AtRiskRows { get; set; }
    public int? SuppressedCells { get; set; }
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public string? ReleasedBy { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public int Downloads { get; set; }
    public List<ExtractColumn> Columns { get; set; } = [];
}

public sealed class ExtractColumn
{
    public Guid ExtractId { get; set; }
    public string Column { get; set; } = "";
    public int Position { get; set; }
}

/// <summary>Pseudonym linking key (spec 7.21.1): study code ↔ patient, readable only by the Privacy Officer.</summary>
public sealed class StudyCode
{
    public Guid ExtractId { get; set; }
    public Guid PatientId { get; set; }
    public string Code { get; set; } = "";
}

public sealed class ResearchSettings
{
    public int Id { get; set; } = 1;
    public int NextNumber { get; set; } = 1;
}

public sealed class ResearchDbContext(DbContextOptions<ResearchDbContext> options) : DbContext(options)
{
    public const string Schema = "research";
    public DbSet<Extract> Extracts => Set<Extract>();
    public DbSet<ExtractColumn> Columns => Set<ExtractColumn>();
    public DbSet<StudyCode> StudyCodes => Set<StudyCode>();
    public DbSet<ResearchSettings> Settings => Set<ResearchSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<Extract>(e =>
        {
            e.ToTable("Extracts");
            e.Property(x => x.Number).HasMaxLength(20); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Title).HasMaxLength(200); e.Property(x => x.Dataset).HasMaxLength(30); e.Property(x => x.Recipient).HasMaxLength(200);
            e.Property(x => x.Level).HasConversion<string>().HasMaxLength(16); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EthicsReference).HasMaxLength(100); e.Property(x => x.MaoriDataGovernance).HasMaxLength(1000);
            e.Property(x => x.RequestedBy).HasMaxLength(100); e.Property(x => x.DecidedBy).HasMaxLength(100); e.Property(x => x.DecisionNote).HasMaxLength(2000); e.Property(x => x.ReleasedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.Status, x.RequestedAt });
            e.HasMany(x => x.Columns).WithOne().HasForeignKey(x => x.ExtractId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ExtractColumn>(e => { e.ToTable("ExtractColumns"); e.HasKey(x => new { x.ExtractId, x.Column }); e.Property(x => x.Column).HasMaxLength(40); });
        b.Entity<StudyCode>(e =>
        {
            e.ToTable("StudyCodes"); e.HasKey(x => new { x.ExtractId, x.PatientId }); e.Property(x => x.Code).HasMaxLength(12);
            e.HasIndex(x => new { x.ExtractId, x.Code }).IsUnique(); e.HasIndex(x => x.PatientId);
            e.HasOne<Extract>().WithMany().HasForeignKey(x => x.ExtractId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ResearchSettings>(e =>
        {
            e.ToTable("Settings", t => t.HasCheckConstraint("CK_ResearchSettings_SingleRow", "[Id] = 1"));
            e.Property(x => x.Id).ValueGeneratedNever(); e.HasData(new ResearchSettings());
        });
    }
}

public sealed class ResearchDbContextFactory : IDesignTimeDbContextFactory<ResearchDbContext>
{
    public ResearchDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ResearchDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", ResearchDbContext.Schema)).Options);
}
