using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.FollowUp;

public sealed class FollowUpModule : IPasModule
{
    public const string Id = "followup";
    public ModuleManifest Manifest { get; } = new(Id, "Post-operative follow-up and SSI surveillance", typeof(FollowUpModule).Assembly.GetName().Version ?? new Version(0, 0),
        FollowUpDbContext.Schema, ["patients", "theatre"], "Follow-up plans and calendar, call scripts, patient check-ins with alerts, escalations, SSI surveillance (CDC/HQSC), GP letters, rates and export.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<FollowUpDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, FollowUpDbContext.Schema));
        services.AddScoped<FollowUpService>();
        services.AddScoped<SsiService>();
    }
}

/// <summary>What a contact is for. SSI checks run at day 30 (day 90 with an implant).</summary>
public enum ContactKind { Call = 1, WoundCheck = 2, Review = 3, SsiCheck = 4 }

/// <summary>How the contact is made. PatientForm sends the check-in link (SMS/email); the answers come back on their own.</summary>
public enum ContactMethod { Phone = 1, Clinic = 2, Surgeon = 3, PatientForm = 4 }

public enum ContactStatus { Due = 1, Done = 2, NoAnswer = 3, Unreachable = 4, Cancelled = 5 }

/// <summary>Escalation after a contact – tracked until closed.</summary>
public enum Escalation { None = 0, Advice = 1, GpReview = 2, ReturnToTds = 3, SurgeonContacted = 4, EmergencyDepartment = 5 }

public enum SsiSource { Patient = 1, Gp = 2, Surgeon = 3, Readmission = 4, Microbiology = 5, Staff = 6 }

/// <summary>CDC/HQSC classification. Suspected until the infection prevention lead confirms it.</summary>
public enum SsiClass { Suspected = 1, Superficial = 2, Deep = 3, OrganSpace = 4, NotSsi = 5 }

/// <summary>A follow-up plan for procedures matching any of the words (blank = default plan). Versioned by editing in place with an audit trail.</summary>
public sealed class Plan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? ProcedureContains { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int MaxAttempts { get; set; } = 3;
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public List<PlanStep> Steps { get; set; } = [];
}

public sealed class PlanStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public int Sequence { get; set; }
    public ContactKind Kind { get; set; }
    public ContactMethod Method { get; set; }
    /// <summary>Days after the operation. When <see cref="WorkingDays"/> is set, counted in NZ working days.</summary>
    public int Days { get; set; }
    /// <summary>Days when an implant was used (SSI check: 90). Null = same as <see cref="Days"/>.</summary>
    public int? DaysWithImplant { get; set; }
    public bool WorkingDays { get; set; }
    /// <summary>Added only when triage flagged a precaution or the patient is high risk.</summary>
    public bool OnlyIfHighRisk { get; set; }
    public string? AssignTo { get; set; }
    public string Label { get; set; } = "";
}

/// <summary>One planned contact with a patient after discharge.</summary>
public sealed class Contact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid BookingId { get; set; }
    public Guid? PlanId { get; set; }
    public string Procedure { get; set; } = "";
    public DateOnly OperationDate { get; set; }
    public ContactKind Kind { get; set; }
    public ContactMethod Method { get; set; }
    public string Label { get; set; } = "";
    public DateOnly DueOn { get; set; }
    public ContactStatus Status { get; set; } = ContactStatus.Due;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public string? AssignedTo { get; set; }
    public List<ContactAnswer> Answers { get; set; } = [];
    /// <summary>An answer met a warning rule – the duty nurse must call.</summary>
    public bool Concern { get; set; }
    public string? ConcernText { get; set; }
    public bool SuspectedSsi { get; set; }
    public Escalation Escalation { get; set; }
    public string? EscalationNote { get; set; }
    public DateTimeOffset? EscalationClosedAt { get; set; }
    public string? EscalationClosedBy { get; set; }
    public string? EscalationOutcome { get; set; }
    public Guid? IncidentId { get; set; }
    public string? DoneBy { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public string? DoneHow { get; set; }
    public string? Notes { get; set; }
    // Patient check-in link (only the hash is stored).
    public string? TokenHash { get; set; }
    public DateTimeOffset? LinkSentAt { get; set; }
    public DateTimeOffset? LinkExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

/// <summary>An SSI surveillance record, linked to the operation, with the risk factors at the time.</summary>
public sealed class SsiCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid PatientId { get; set; }
    public Guid BookingId { get; set; }
    public Guid? ContactId { get; set; }
    public string Procedure { get; set; } = "";
    public string? Surgeon { get; set; }
    public DateOnly OperationDate { get; set; }
    public DateOnly OnsetOn { get; set; }
    public string Signs { get; set; } = "";
    public SsiSource Source { get; set; }
    public bool Antibiotics { get; set; }
    public bool ReturnToTheatre { get; set; }
    public bool Readmitted { get; set; }
    public string? Microbiology { get; set; }
    public SsiClass Class { get; set; } = SsiClass.Suspected;
    public string? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmNote { get; set; }
    public int SurveillanceDays { get; set; } = 30;
    public bool Implant { get; set; }
    public string? WoundClass { get; set; }
    public int? Asa { get; set; }
    public List<SsiRiskFactor> Risk { get; set; } = [];
    public Guid? IncidentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

/// <summary>One answer to the follow-up script (one row per question – no lists in a column).</summary>
public sealed class ContactAnswer
{
    public Guid ContactId { get; set; }
    public string QuestionKey { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>A risk factor of the operation kept with the SSI record at the time it was opened.</summary>
public sealed class SsiRiskFactor
{
    public Guid SsiCaseId { get; set; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class FollowUpSettings
{
    public int Id { get; set; } = 1;
    /// <summary>Mobile of the duty nurse phone: an SMS goes there when a patient's answer needs a call.</summary>
    public string? DutyMobile { get; set; }
    /// <summary>The web address patients open (https://…). Check-in links are sent automatically only when this is set – never taken from a request.</summary>
    public string? PublicUrl { get; set; }
    public bool SendCheckInLinks { get; set; } = true;
    public int LinkDays { get; set; } = 5;
    /// <summary>Create a GP letter asking for wound infections to be reported within 30/90 days.</summary>
    public bool GpLetter { get; set; } = true;
    public DateOnly? StartFrom { get; set; }
    public DateTimeOffset? LastScanAt { get; set; }
    public int NextSsiNumber { get; set; } = 1;
}

public sealed class FollowUpDbContext(DbContextOptions<FollowUpDbContext> options) : DbContext(options)
{
    public const string Schema = "followup";
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanStep> Steps => Set<PlanStep>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<SsiCase> SsiCases => Set<SsiCase>();
    public DbSet<ContactAnswer> Answers => Set<ContactAnswer>();
    public DbSet<SsiRiskFactor> RiskFactors => Set<SsiRiskFactor>();
    public DbSet<FollowUpSettings> Settings => Set<FollowUpSettings>();

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
        b.Entity<Plan>(e =>
        {
            e.ToTable("Plans");
            e.Property(x => x.Name).HasMaxLength(150); e.Property(x => x.ProcedureContains).HasMaxLength(500); e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.IsDefault).IsUnique().HasFilter("[IsDefault] = 1").HasDatabaseName("UX_Plans_OneDefault");
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PlanStep>(e =>
        {
            e.ToTable("PlanSteps");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Method).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.AssignTo).HasMaxLength(100); e.Property(x => x.Label).HasMaxLength(100);
            e.HasIndex(x => new { x.PlanId, x.Sequence }).IsUnique();
        });
        b.Entity<Contact>(e =>
        {
            e.ToTable("Contacts");
            e.Property(x => x.Procedure).HasMaxLength(200); e.Property(x => x.Label).HasMaxLength(100);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Method).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Escalation).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AssignedTo).HasMaxLength(100);
            e.Property(x => x.ConcernText).HasMaxLength(1000); e.Property(x => x.EscalationNote).HasMaxLength(2000); e.Property(x => x.EscalationOutcome).HasMaxLength(2000);
            e.Property(x => x.EscalationClosedBy).HasMaxLength(100); e.Property(x => x.DoneBy).HasMaxLength(100); e.Property(x => x.DoneHow).HasMaxLength(40);
            e.Property(x => x.Notes).HasMaxLength(4000); e.Property(x => x.TokenHash).HasMaxLength(64); e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.TokenHash).IsUnique().HasFilter("[TokenHash] IS NOT NULL");
            e.HasIndex(x => new { x.Status, x.DueOn }); e.HasIndex(x => x.PatientId); e.HasIndex(x => x.BookingId); e.HasIndex(x => x.PlanId);
            e.HasIndex(x => new { x.Concern, x.Status });
            e.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Answers).WithOne().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ContactAnswer>(e =>
        {
            e.ToTable("ContactAnswers");
            e.HasKey(x => new { x.ContactId, x.QuestionKey });
            e.Property(x => x.QuestionKey).HasMaxLength(40); e.Property(x => x.Value).HasMaxLength(1000);
        });
        b.Entity<SsiRiskFactor>(e =>
        {
            e.ToTable("SsiRiskFactors");
            e.HasKey(x => new { x.SsiCaseId, x.Name });
            e.Property(x => x.Name).HasMaxLength(60); e.Property(x => x.Value).HasMaxLength(300);
        });
        b.Entity<SsiCase>(e =>
        {
            e.ToTable("SsiCases");
            e.Property(x => x.Number).HasMaxLength(20); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Procedure).HasMaxLength(200); e.Property(x => x.Surgeon).HasMaxLength(150); e.Property(x => x.Signs).HasMaxLength(1000);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(14); e.Property(x => x.Class).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Microbiology).HasMaxLength(500); e.Property(x => x.ConfirmedBy).HasMaxLength(150); e.Property(x => x.ConfirmNote).HasMaxLength(2000);
            e.Property(x => x.WoundClass).HasMaxLength(40); e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.PatientId); e.HasIndex(x => x.BookingId); e.HasIndex(x => x.ContactId); e.HasIndex(x => new { x.Class, x.OperationDate });
            e.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Risk).WithOne().HasForeignKey(x => x.SsiCaseId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<FollowUpSettings>(e =>
        {
            e.ToTable("Settings", t => t.HasCheckConstraint("CK_FollowUpSettings_SingleRow", "[Id] = 1"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.DutyMobile).HasMaxLength(30); e.Property(x => x.PublicUrl).HasMaxLength(200);
            e.HasData(new FollowUpSettings());
        });
    }
}

public sealed class FollowUpDbContextFactory : IDesignTimeDbContextFactory<FollowUpDbContext>
{
    public FollowUpDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<FollowUpDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", FollowUpDbContext.Schema)).Options);
}
