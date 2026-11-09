using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Referrals;

public sealed class ReferralsModule : IPasModule
{
    public const string Id = "referrals";
    public ModuleManifest Manifest { get; } = new(Id, "Referrals & waitlist", typeof(ReferralsModule).Assembly.GetName().Version ?? new Version(0, 0),
        ReferralsDbContext.Schema, ["patients", "theatre"], "Referral intake and triage, the surgical waitlist, and booking theatre from the waitlist.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<ReferralsDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, ReferralsDbContext.Schema));
        services.AddScoped<ReferralService>();
    }
}

/// <summary>Where a referral came from.</summary>
public enum ReferralSource { GeneralPractice = 1, Specialist = 2, SelfReferral = 3, HealthNz = 4, Acc = 5, Insurer = 6, Other = 7 }

/// <summary>Who is expected to pay. Publicly funded (Health NZ outsourced) work is reportable to NBRS.</summary>
public enum Funder { SelfPay = 1, Insurer = 2, Acc = 3, HealthNz = 4, Other = 5 }

public enum ServiceType { Surgical = 1, Endoscopy = 2 }

public enum ReferralStatus { Received = 1, Accepted = 2, Declined = 3, Withdrawn = 4 }

/// <summary>Clinical urgency. Target days come from <see cref="ReferralSettings"/>.</summary>
public enum Priority { Urgent = 1, SemiUrgent = 2, Routine = 3 }

public enum WaitlistStatus { Waiting = 1, OnHold = 2, Booked = 3, Completed = 4, Removed = 5 }

/// <summary>A referral letter received from a GP, specialist, funder or the patient.</summary>
public sealed class Referral
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid PatientId { get; set; }
    public DateOnly ReceivedOn { get; set; }
    public ReferralSource Source { get; set; }
    public string ReferrerName { get; set; } = "";
    public string? ReferrerOrganisation { get; set; }
    /// <summary>HPI person or facility identifier of the referrer, when known.</summary>
    public string? ReferrerHpi { get; set; }
    /// <summary>Specialist (core.Clinicians).</summary>
    public Guid SpecialistId { get; set; }
    public ServiceType Service { get; set; }
    public Funder Funder { get; set; }
    /// <summary>Insurer approval, ACC claim number or Health NZ referral id.</summary>
    public string? FunderReference { get; set; }
    public string Reason { get; set; } = "";
    public ReferralStatus Status { get; set; } = ReferralStatus.Received;
    public DateTimeOffset? TriagedAt { get; set; }
    public string? TriagedBy { get; set; }
    public string? TriageNotes { get; set; }
    public string? DeclineReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

/// <summary>A patient waiting for a procedure: created when a referral is accepted, or added directly by the specialist's team.</summary>
public sealed class WaitlistEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ReferralId { get; set; }
    public Guid PatientId { get; set; }
    public string Procedure { get; set; } = "";
    public Theatre.Laterality Laterality { get; set; }
    public Theatre.AnaestheticType Anaesthetic { get; set; } = Theatre.AnaestheticType.General;
    public int DurationMinutes { get; set; } = 30;
    public ServiceType Service { get; set; }
    /// <summary>Specialist (core.Clinicians).</summary>
    public Guid SpecialistId { get; set; }
    public Funder Funder { get; set; }
    public Priority Priority { get; set; }
    /// <summary>Date the decision to treat was made (start of the waiting time).</summary>
    public DateOnly AddedOn { get; set; }
    public DateOnly TargetDate { get; set; }
    public WaitlistStatus Status { get; set; } = WaitlistStatus.Waiting;
    public string? HoldReason { get; set; }
    /// <summary>Patient not ready (or unavailable) until this date; not counted as waiting time for publicly funded reporting.</summary>
    public DateOnly? HoldUntil { get; set; }
    public Guid? BookingId { get; set; }
    public DateOnly? BookedFor { get; set; }
    public string? RemovedReason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset StatusAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

/// <summary>Target times for each priority (days from the decision to treat). Health NZ's elective target is 4 months for routine work.</summary>
public sealed class ReferralSettings
{
    public int Id { get; set; } = 1;
    public int UrgentDays { get; set; } = 30;
    public int SemiUrgentDays { get; set; } = 90;
    public int RoutineDays { get; set; } = 120;
    public int NextReferralNumber { get; set; } = 1;
    // NBRS extract (publicly funded patients) – codes supplied by Health NZ; set by an administrator.
    public string? NbrsAgencyCode { get; set; }
    public string? NbrsFacilityCode { get; set; }
    public string? NbrsSpecialtySurgical { get; set; }
    public string? NbrsSpecialtyEndoscopy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class ReferralsDbContext(DbContextOptions<ReferralsDbContext> options) : DbContext(options)
{
    public const string Schema = "referrals";
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<WaitlistEntry> Waitlist => Set<WaitlistEntry>();
    public DbSet<ReferralSettings> Settings => Set<ReferralSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        // 0.10.1: indexes on lookup columns and referential integrity within the schema.
        b.Entity<WaitlistEntry>().HasOne<Referral>().WithMany().HasForeignKey(x => x.ReferralId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Referral>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ReferrerName).HasMaxLength(150);
            e.Property(x => x.ReferrerOrganisation).HasMaxLength(150);
            e.Property(x => x.ReferrerHpi).HasMaxLength(20);
            e.HasIndex(x => x.SpecialistId);
            e.Property(x => x.Service).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Funder).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FunderReference).HasMaxLength(60);
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TriagedBy).HasMaxLength(100);
            e.Property(x => x.TriageNotes).HasMaxLength(2000);
            e.Property(x => x.DeclineReason).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => new { x.Status, x.ReceivedOn });
        });
        b.Entity<WaitlistEntry>(e =>
        {
            e.Property(x => x.Procedure).HasMaxLength(300);
            e.Property(x => x.Laterality).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Anaesthetic).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Service).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.SpecialistId);
            e.Property(x => x.Funder).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.HoldReason).HasMaxLength(300);
            e.Property(x => x.RemovedReason).HasMaxLength(300);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => new { x.Status, x.TargetDate });
            e.HasIndex(x => x.BookingId);
        });
        b.Entity<ReferralSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.NbrsAgencyCode).HasMaxLength(10);
            e.Property(x => x.NbrsFacilityCode).HasMaxLength(10);
            e.Property(x => x.NbrsSpecialtySurgical).HasMaxLength(10);
            e.Property(x => x.NbrsSpecialtyEndoscopy).HasMaxLength(10);
        });
    }
}

public sealed class ReferralsDbContextFactory : IDesignTimeDbContextFactory<ReferralsDbContext>
{
    public ReferralsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ReferralsDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", ReferralsDbContext.Schema)).Options);
}
