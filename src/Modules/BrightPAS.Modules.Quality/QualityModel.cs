using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Quality;

/// <summary>
/// Quality management (0.19.0, ADR 0031; spec 7.12 and 7.14): incidents with HQSC SAC rating, investigation, open disclosure and external
/// notifications; complaints to Right 10 of the Code of Rights timeframes; compliments; risk and hazard registers; corrective actions with
/// closure sign-off; a policy register with read-and-acknowledge; and a configurable quality indicator library with SPC charts and an equity view.
/// </summary>
public sealed class QualityModule : IPasModule
{
    public const string Id = "quality";
    public ModuleManifest Manifest { get; } = new(Id, "Quality management", typeof(QualityModule).Assembly.GetName().Version ?? new Version(0, 0),
        QualityDbContext.Schema, [], "Incidents, complaints, risks, corrective actions, policies and quality indicators.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<QualityDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, QualityDbContext.Schema));
        services.AddScoped<QualityService>();
        services.AddScoped<IndicatorService>();
    }
}

// ------------------------------------------------------------------ incidents (7.14.5)
public enum IncidentCategory { Fall = 1, Medication = 2, ClinicalCare = 3, SurgicalProcedural = 4, Anaesthetic = 5, Infection = 6, EquipmentDevice = 7, InformationPrivacy = 8, StaffInjury = 9, Security = 10, PressureInjury = 11, Other = 12 }
public enum Harm { None = 0, Minor = 1, Moderate = 2, Severe = 3, Death = 4 }
public enum IncidentStatus { Reported = 1, Triaged = 2, Investigating = 3, Actions = 4, Closed = 5 }
public enum Agency { Hqsc = 1, AccTreatmentInjury = 2, Medsafe = 3, Hdc = 4, HealthNz = 5, WorkSafe = 6, PrivacyCommissioner = 7, Coroner = 8 }

public sealed class Incident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>INC-yyyy-nnnn.</summary>
    public string Number { get; set; } = "";
    public DateTimeOffset ReportedAt { get; set; }
    /// <summary>Null when reported anonymously – the reporter is not stored anywhere (the audit records "anonymous").</summary>
    public string? ReportedBy { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Location { get; set; } = "";
    public IncidentCategory Category { get; set; }
    public string Description { get; set; } = "";
    public string? ImmediateAction { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? EquipmentId { get; set; }
    public string? StaffInvolved { get; set; }
    /// <summary>Harm as the reporter saw it; confirmed at triage.</summary>
    public Harm Harm { get; set; }
    /// <summary>HQSC Severity Assessment Code 1 (most severe) – 4, set at triage.</summary>
    public int? Sac { get; set; }
    /// <summary>On the HQSC "Always Report and Review" list.</summary>
    public bool AlwaysReport { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.Reported;
    public string? Investigator { get; set; }
    public string? TriagedBy { get; set; }
    public DateTimeOffset? TriagedAt { get; set; }
    /// <summary>Set when SAC 1–2 or Always Report and Review – the GM and Clinical Governance were alerted.</summary>
    public DateTimeOffset? EscalatedAt { get; set; }
    public string? Findings { get; set; }
    public string? ContributingFactors { get; set; }
    /// <summary>Shared with staff – no names or identifying details.</summary>
    public string? LearningSummary { get; set; }
    public string? ClosedBy { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public List<Disclosure> Disclosures { get; set; } = [];
    public List<Notification> Notifications { get; set; } = [];
}

/// <summary>Open disclosure: who was told, when, by whom, how, and the follow-up.</summary>
public sealed class Disclosure
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public DateTimeOffset At { get; set; }
    public string By { get; set; } = "";
    public string PersonTold { get; set; } = "";
    public string Method { get; set; } = "";
    public string? Notes { get; set; }
}

/// <summary>An external notification the incident needs (or has had).</summary>
public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public Agency Agency { get; set; }
    public DateTimeOffset? NotifiedAt { get; set; }
    public string? NotifiedBy { get; set; }
    public string? Reference { get; set; }
}

// ------------------------------------------------------------------ complaints and compliments (7.14.6)
public enum ComplaintStatus { Received = 1, Acknowledged = 2, Investigating = 3, Responded = 4, Closed = 5 }
public enum ComplaintOutcome { Upheld = 1, PartlyUpheld = 2, NotUpheld = 3, Withdrawn = 4 }

public sealed class Complaint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public DateOnly ReceivedOn { get; set; }
    public string ReceivedVia { get; set; } = "";
    public string Complainant { get; set; } = "";
    public string? Contact { get; set; }
    public Guid? PatientId { get; set; }
    public string Summary { get; set; } = "";
    public string Category { get; set; } = "";
    public ComplaintStatus Status { get; set; } = ComplaintStatus.Received;
    /// <summary>Right 10(6): written acknowledgement within 5 working days.</summary>
    public DateOnly AcknowledgeBy { get; set; }
    public DateOnly? AcknowledgedOn { get; set; }
    /// <summary>Right 10(7): decide within 10 working days of acknowledging (or tell the complainant more time is needed, and why).</summary>
    public DateOnly? DecideBy { get; set; }
    public DateOnly? ExtensionNotifiedOn { get; set; }
    public string? ExtensionReason { get; set; }
    public DateOnly? RespondedOn { get; set; }
    public ComplaintOutcome? Outcome { get; set; }
    public string? Response { get; set; }
    public bool HdcReferral { get; set; }
    public bool AdvocacyInvolved { get; set; }
    public Guid? IncidentId { get; set; }
    public string Owner { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed class Compliment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly ReceivedOn { get; set; }
    public string From { get; set; } = "";
    public string? StaffNamed { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset? SharedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

// ------------------------------------------------------------------ risks and hazards (7.14.7)
public enum RiskCategory { Clinical = 1, Operational = 2, Financial = 3, InformationTechnology = 4, People = 5, Compliance = 6, HealthAndSafety = 7 }
public enum RiskStatus { Open = 1, Monitoring = 2, Closed = 3 }

public sealed class Risk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    /// <summary>Hazard register entry (Health and Safety at Work Act 2015) rather than an organisational risk.</summary>
    public bool IsHazard { get; set; }
    public string Title { get; set; } = "";
    public RiskCategory Category { get; set; }
    public string Description { get; set; } = "";
    public string Owner { get; set; } = "";
    /// <summary>1 rare – 5 almost certain.</summary>
    public int Likelihood { get; set; }
    /// <summary>1 minimal – 5 catastrophic.</summary>
    public int Consequence { get; set; }
    public string? Controls { get; set; }
    public int? ResidualLikelihood { get; set; }
    public int? ResidualConsequence { get; set; }
    public string? Location { get; set; }
    public Guid? EquipmentId { get; set; }
    public DateOnly ReviewBy { get; set; }
    public RiskStatus Status { get; set; } = RiskStatus.Open;
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

// ------------------------------------------------------------------ corrective actions (7.12)
public enum ActionSource { Incident = 1, Complaint = 2, InternalAudit = 3, DaaAudit = 4, Risk = 5, Feedback = 6, Other = 7 }
public enum ActionStatus { Open = 1, Done = 2, Verified = 3 }

public sealed class CorrectiveAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public ActionSource Source { get; set; }
    /// <summary>The incident, complaint or risk it came from (when from one of those).</summary>
    public Guid? SourceId { get; set; }
    public string? SourceRef { get; set; }
    public string Title { get; set; } = "";
    public string? Detail { get; set; }
    public string Owner { get; set; } = "";
    public DateOnly DueOn { get; set; }
    public ActionStatus Status { get; set; } = ActionStatus.Open;
    public string? Evidence { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public string? DoneBy { get; set; }
    /// <summary>Closure sign-off by a second person (not the one who did it).</summary>
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

// ------------------------------------------------------------------ policy register (7.14.7)
public sealed class PolicyDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Version { get; set; } = "";
    public string Owner { get; set; } = "";
    /// <summary>Where the document lives (e.g. TDS SharePoint) – BrightPAS keeps the register, not the file.</summary>
    public string? Link { get; set; }
    public DateOnly ReviewBy { get; set; }
    /// <summary>Staff must read and acknowledge the current version (training evidence).</summary>
    public bool RequiresAcknowledgement { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

public sealed class PolicyAcknowledgement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string Version { get; set; } = "";
    public string UserName { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

// ------------------------------------------------------------------ indicators (7.14.1)
public enum IndicatorDirection { LowerIsBetter = 1, HigherIsBetter = 2 }

/// <summary>A quality indicator: definition, target and where its numbers come from (a built-in calculation or monthly manual entry).</summary>
public sealed class Indicator
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Definition { get; set; } = "";
    public string Numerator { get; set; } = "";
    public string Denominator { get; set; } = "";
    /// <summary>Key of the built-in calculation (see IndicatorCalculators), or null for monthly manual entry.</summary>
    public string? Calculator { get; set; }
    /// <summary>Rate shown per this many (100 = %, 1000 = per 1,000 episodes; 1 = a count).</summary>
    public int Scale { get; set; } = 100;
    public IndicatorDirection Direction { get; set; } = IndicatorDirection.LowerIsBetter;
    public decimal? Target { get; set; }
    /// <summary>Amber band: between the target and this value; beyond it is red.</summary>
    public decimal? AlertAt { get; set; }
    public string Owner { get; set; } = "";
    public string? Source { get; set; }
    public bool IsActive { get; set; } = true;
    public int Sort { get; set; }
}

/// <summary>Monthly figures for a manually entered indicator (e.g. patient satisfaction from an external survey).</summary>
public sealed class IndicatorValue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IndicatorId { get; set; }
    /// <summary>First day of the month.</summary>
    public DateOnly Month { get; set; }
    public decimal NumeratorValue { get; set; }
    public decimal? DenominatorValue { get; set; }
    public string EnteredBy { get; set; } = "";
    public DateTimeOffset EnteredAt { get; set; }
}

public sealed class QualitySettings
{
    public int Id { get; set; } = 1;
    /// <summary>Who is alerted about SAC 1–2 and Always Report and Review events (names or roles; shown on the incident).</summary>
    public string EscalateTo { get; set; } = "General Manager; Clinical Governance chair";
    /// <summary>Extra non-working days for complaint timeframes (e.g. Nelson Anniversary, TDS closure days), ISO dates separated by commas.</summary>
    public string? ExtraNonWorkingDays { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

/// <summary>Running numbers per register and year (INC-2026-0001 …).</summary>
public sealed class Sequence
{
    public string Key { get; set; } = "";
    public int Next { get; set; }
}

public sealed class QualityDbContext(DbContextOptions<QualityDbContext> options) : DbContext(options)
{
    public const string Schema = "quality";
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<Disclosure> Disclosures => Set<Disclosure>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<Compliment> Compliments => Set<Compliment>();
    public DbSet<Risk> Risks => Set<Risk>();
    public DbSet<CorrectiveAction> Actions => Set<CorrectiveAction>();
    public DbSet<PolicyDocument> Policies => Set<PolicyDocument>();
    public DbSet<PolicyAcknowledgement> Acknowledgements => Set<PolicyAcknowledgement>();
    public DbSet<Indicator> Indicators => Set<Indicator>();
    public DbSet<IndicatorValue> IndicatorValues => Set<IndicatorValue>();
    public DbSet<QualitySettings> Settings => Set<QualitySettings>();
    public DbSet<Sequence> Sequences => Set<Sequence>();

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
        b.Entity<Incident>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20); e.Property(x => x.ReportedBy).HasMaxLength(100); e.Property(x => x.Location).HasMaxLength(100);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(30); e.Property(x => x.Harm).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.Description).HasMaxLength(4000); e.Property(x => x.ImmediateAction).HasMaxLength(2000); e.Property(x => x.StaffInvolved).HasMaxLength(300);
            e.Property(x => x.Investigator).HasMaxLength(100); e.Property(x => x.TriagedBy).HasMaxLength(100); e.Property(x => x.ClosedBy).HasMaxLength(100);
            e.Property(x => x.Findings).HasMaxLength(8000); e.Property(x => x.ContributingFactors).HasMaxLength(4000); e.Property(x => x.LearningSummary).HasMaxLength(4000);
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => new { x.Status, x.ReportedAt }); e.HasIndex(x => x.OccurredAt); e.HasIndex(x => x.PatientId); e.HasIndex(x => x.BookingId); e.HasIndex(x => x.EquipmentId);
            e.HasMany(x => x.Disclosures).WithOne().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Notifications).WithOne().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Disclosure>(e =>
        {
            e.Property(x => x.By).HasMaxLength(100); e.Property(x => x.PersonTold).HasMaxLength(150); e.Property(x => x.Method).HasMaxLength(60); e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => new { x.IncidentId, x.At });
        });
        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Agency).HasConversion<string>().HasMaxLength(25); e.Property(x => x.NotifiedBy).HasMaxLength(100); e.Property(x => x.Reference).HasMaxLength(100);
            e.HasIndex(x => new { x.IncidentId, x.Agency }).IsUnique();
        });
        b.Entity<Complaint>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20); e.Property(x => x.ReceivedVia).HasMaxLength(40); e.Property(x => x.Complainant).HasMaxLength(150); e.Property(x => x.Contact).HasMaxLength(200);
            e.Property(x => x.Summary).HasMaxLength(4000); e.Property(x => x.Category).HasMaxLength(60); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.ExtensionReason).HasMaxLength(500); e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(15); e.Property(x => x.Response).HasMaxLength(4000);
            e.Property(x => x.Owner).HasMaxLength(100); e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => new { x.Status, x.AcknowledgeBy }); e.HasIndex(x => x.ReceivedOn); e.HasIndex(x => x.PatientId); e.HasIndex(x => x.IncidentId);
            e.HasOne<Incident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<Compliment>(e =>
        {
            e.Property(x => x.From).HasMaxLength(150); e.Property(x => x.StaffNamed).HasMaxLength(300); e.Property(x => x.Text).HasMaxLength(2000); e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.ReceivedOn);
        });
        b.Entity<Risk>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20); e.Property(x => x.Title).HasMaxLength(200); e.Property(x => x.Category).HasConversion<string>().HasMaxLength(25);
            e.Property(x => x.Description).HasMaxLength(4000); e.Property(x => x.Owner).HasMaxLength(100); e.Property(x => x.Controls).HasMaxLength(4000); e.Property(x => x.Location).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => new { x.IsHazard, x.Status }); e.HasIndex(x => x.ReviewBy); e.HasIndex(x => x.EquipmentId);
        });
        b.Entity<CorrectiveAction>(e =>
        {
            e.ToTable("CorrectiveActions");
            e.Property(x => x.Number).HasMaxLength(20); e.Property(x => x.Source).HasConversion<string>().HasMaxLength(15); e.Property(x => x.SourceRef).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(200); e.Property(x => x.Detail).HasMaxLength(4000); e.Property(x => x.Owner).HasMaxLength(100); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Evidence).HasMaxLength(4000); e.Property(x => x.DoneBy).HasMaxLength(100); e.Property(x => x.VerifiedBy).HasMaxLength(100); e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => new { x.Status, x.DueOn }); e.HasIndex(x => new { x.Source, x.SourceId });
        });
        b.Entity<PolicyDocument>(e =>
        {
            e.ToTable("Policies");
            e.Property(x => x.Code).HasMaxLength(30); e.Property(x => x.Title).HasMaxLength(200); e.Property(x => x.Version).HasMaxLength(20); e.Property(x => x.Owner).HasMaxLength(100);
            e.Property(x => x.Link).HasMaxLength(500); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => x.ReviewBy);
        });
        b.Entity<PolicyAcknowledgement>(e =>
        {
            e.ToTable("PolicyAcknowledgements");
            e.Property(x => x.Version).HasMaxLength(20); e.Property(x => x.UserName).HasMaxLength(100);
            e.HasIndex(x => new { x.DocumentId, x.Version, x.UserName }).IsUnique();
            e.HasOne<PolicyDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Indicator>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20); e.Property(x => x.Name).HasMaxLength(150); e.Property(x => x.Definition).HasMaxLength(2000);
            e.Property(x => x.Numerator).HasMaxLength(500); e.Property(x => x.Denominator).HasMaxLength(500); e.Property(x => x.Calculator).HasMaxLength(30);
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(15); e.Property(x => x.Target).HasPrecision(9, 2); e.Property(x => x.AlertAt).HasPrecision(9, 2);
            e.Property(x => x.Owner).HasMaxLength(100); e.Property(x => x.Source).HasMaxLength(200);
            e.HasIndex(x => x.Code).IsUnique();
        });
        b.Entity<IndicatorValue>(e =>
        {
            e.Property(x => x.NumeratorValue).HasPrecision(12, 2); e.Property(x => x.DenominatorValue).HasPrecision(12, 2); e.Property(x => x.EnteredBy).HasMaxLength(100);
            e.HasIndex(x => new { x.IndicatorId, x.Month }).IsUnique();
            e.HasOne<Indicator>().WithMany().HasForeignKey(x => x.IndicatorId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<QualitySettings>(e =>
        {
            e.ToTable("Settings"); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.EscalateTo).HasMaxLength(300); e.Property(x => x.ExtraNonWorkingDays).HasMaxLength(1000); e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
        b.Entity<Sequence>(e => { e.HasKey(x => x.Key); e.Property(x => x.Key).HasMaxLength(20); });
    }
}

public sealed class QualityDbContextFactory : IDesignTimeDbContextFactory<QualityDbContext>
{
    public QualityDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<QualityDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", QualityDbContext.Schema)).Options);
}
