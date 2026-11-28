using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Triage;

/// <summary>
/// Patient triage and day-surgery suitability (0.22.0, spec 7.25, ADR 0035). Builds a risk profile from the pre-admission questionnaire,
/// staff findings, the patient record and lab results: scores (BMI, STOP-BANG, clinical frailty, VTE, cardiac risk, suggested ASA), medicine
/// prompts and versioned, clinician-approved suitability rules, giving a suggested outcome that a nurse or anaesthetist signs off.
/// Re-triage runs when new information arrives or the booking changes. BrightPAS suggests; a clinician always decides.
/// </summary>
public sealed class TriageModule : IPasModule
{
    public const string Id = "triage";
    public ModuleManifest Manifest { get; } = new(Id, "Patient triage and suitability", typeof(TriageModule).Assembly.GetName().Version ?? new Version(0, 0),
        TriageDbContext.Schema, ["patients", "forms"], "Risk scores, clinician-approved day-surgery suitability rules, recommendation and sign-off, re-triage after booking, early warning score on the day, anaesthetist video/phone reviews.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<TriageDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, TriageDbContext.Schema));
        services.AddScoped<TriageService>();
        services.AddScoped<TriageRulesService>();
        services.AddScoped<ReviewService>();
    }
}

/// <summary>Ordered from least to most serious – the suggested outcome is the most serious one any rule gives.</summary>
public enum TriageOutcome { Suitable = 1, SuitableWithPrecautions = 2, MoreInformation = 3, AnaesthetistReview = 4, NotSuitable = 5 }

public enum AssessmentStatus { Open = 1, SignedOff = 2, Superseded = 3, Cancelled = 4 }

public enum TriageTrigger { Initial = 1, NewAnswers = 2, NewResult = 3, BookingChanged = 4, Stale = 5, Manual = 6, Observations = 7 }

public enum ReviewMode { Video = 1, Phone = 2, InPerson = 3 }

public enum ReviewStatus { Booked = 1, Done = 2, DidNotAttend = 3, Cancelled = 4 }

public enum RuleSetStatus { Draft = 1, Approved = 2, Retired = 3 }

/// <summary>How a rule compares a fact: a number limit, a yes answer, or missing information.</summary>
public enum RuleOperator { AtLeast = 1, AtMost = 2, Above = 3, Below = 4, IsYes = 5, Equals = 6, Missing = 7 }

public static class Outcomes
{
    public static readonly IReadOnlyDictionary<TriageOutcome, string> Labels = new Dictionary<TriageOutcome, string>
    {
        [TriageOutcome.Suitable] = "Suitable",
        [TriageOutcome.SuitableWithPrecautions] = "Suitable with precautions",
        [TriageOutcome.MoreInformation] = "More information needed",
        [TriageOutcome.AnaesthetistReview] = "Anaesthetist review",
        [TriageOutcome.NotSuitable] = "Not suitable for day surgery at TDS",
    };
}

/// <summary>A versioned set of suitability rules and medicine prompts. Only an approved set is used for real patients.</summary>
public sealed class RuleSet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public RuleSetStatus Status { get; set; } = RuleSetStatus.Draft;
    /// <summary>Shipped examples – must be reviewed by TDS's clinicians before approval.</summary>
    public bool IsExample { get; set; }
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public string? ApprovedRole { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovalNote { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public List<SuitabilityRule> Rules { get; set; } = [];
    public List<MedicineRule> Medicines { get; set; } = [];
}

public sealed class SuitabilityRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleSetId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Fact compared (see <see cref="TriageFacts"/>): bmi, age, asa, stopbang, cfs, vte, rcri, home_oxygen …</summary>
    public string Fact { get; set; } = "";
    public RuleOperator Operator { get; set; }
    public decimal? Threshold { get; set; }
    public string? TextValue { get; set; }
    /// <summary>Applies only when the procedure contains one of these words (comma separated); blank = all procedures.</summary>
    public string? ProcedureContains { get; set; }
    /// <summary>Applies only to this anaesthetic (General, Sedation, Regional, Local); blank = all.</summary>
    public string? Anaesthetic { get; set; }
    public TriageOutcome Outcome { get; set; }
    /// <summary>Printed on the booking and theatre list when the rule fires (e.g. "First on the list").</summary>
    public string? Precaution { get; set; }
    public string? Explanation { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A medicine prompt: when the patient's medicines mention one of the words, show the hold/stop instruction.</summary>
public sealed class MedicineRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleSetId { get; set; }
    public string Name { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string Instruction { get; set; } = "";
    public TriageOutcome Outcome { get; set; } = TriageOutcome.SuitableWithPrecautions;
    public bool IsActive { get; set; } = true;
}

/// <summary>One triage of a patient for a planned procedure. Each re-triage is a new row; the earlier one is superseded.</summary>
public sealed class Assessment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? FormRequestId { get; set; }
    public Guid? PreviousId { get; set; }
    public TriageTrigger Trigger { get; set; }
    public string? TriggerDetail { get; set; }
    public AssessmentStatus Status { get; set; } = AssessmentStatus.Open;
    public string Procedure { get; set; } = "";
    public string? Anaesthetic { get; set; }
    public DateOnly? ProcedureDate { get; set; }
    public Guid? RuleSetId { get; set; }
    public int? RuleSetVersion { get; set; }
    public bool RulesApproved { get; set; }
    /// <summary>JSON of the facts used (patient answers, staff findings, results, age), so the assessment can be shown exactly as it was.</summary>
    public string FactsJson { get; set; } = "{}";
    /// <summary>JSON of staff-entered findings and corrections (fact → value, with reason for corrections).</summary>
    public string? StaffInputsJson { get; set; }
    public decimal? Bmi { get; set; }
    public int? StopBang { get; set; }
    public int? StopBangKnown { get; set; }
    public int? Frailty { get; set; }
    public int? Vte { get; set; }
    public int? Rcri { get; set; }
    public int? SuggestedAsa { get; set; }
    public int? ConfirmedAsa { get; set; }
    public TriageOutcome SuggestedOutcome { get; set; }
    public TriageOutcome? FinalOutcome { get; set; }
    public string? Precautions { get; set; }
    public string? Missing { get; set; }
    public string? ChangeSummary { get; set; }
    public bool Worse { get; set; }
    public string? Notes { get; set; }
    public string? OverrideReason { get; set; }
    public string? SignedOffBy { get; set; }
    public string? SignedOffRole { get; set; }
    public DateTimeOffset? SignedOffAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<Finding> Findings { get; set; } = [];
}

public sealed class Finding
{
    public Guid AssessmentId { get; set; }
    public int Sequence { get; set; }
    /// <summary>Rule code, MED:name or SCORE:name.</summary>
    public string Source { get; set; } = "";
    public TriageOutcome Outcome { get; set; }
    public string Text { get; set; } = "";
    public string? Precaution { get; set; }
}

/// <summary>Module settings (single row).</summary>
public sealed class TriageSettings
{
    public int Id { get; set; } = 1;
    /// <summary>A booking can't be confirmed until triage is signed off as suitable (with or without precautions).</summary>
    public bool HoldBookingUntilSignedOff { get; set; }
    /// <summary>Only for procedures containing these words (comma separated); blank = all bookings.</summary>
    public string? HoldProcedures { get; set; }
    /// <summary>Re-triage when the last sign-off is older than this many days on the day of surgery.</summary>
    public int RetriageAfterDays { get; set; } = 90;
    /// <summary>How many days before surgery triage must be signed off (shown as overdue after that).</summary>
    public int SignOffDaysBefore { get; set; } = 7;
    public DateTimeOffset? LastScanAt { get; set; }
    /// <summary>0.36.0: use the early warning score from observations taken on the day of surgery (off until switched on).</summary>
    public bool EwsOnTheDay { get; set; }
    /// <summary>EWS at or above this (or any single parameter scoring 3) suggests an anaesthetist review before surgery.</summary>
    public int EwsReviewAt { get; set; } = 5;
    /// <summary>Video review link with {code} for the meeting code, e.g. https://meet.example.nz/tds-{code}.</summary>
    public string? VideoLinkTemplate { get; set; }
    public int ReviewMinutes { get; set; } = 15;
}

/// <summary>An anaesthetist review (video, phone or in person) booked from a triage assessment (0.36.0).</summary>
public sealed class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public Guid PatientId { get; set; }
    public Guid? BookingId { get; set; }
    /// <summary>core.Clinicians (an anaesthetist).</summary>
    public Guid AnaesthetistId { get; set; }
    public string AnaesthetistName { get; set; } = "";
    public ReviewMode Mode { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int Minutes { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Booked;
    public string? JoinLink { get; set; }
    public string? Phone { get; set; }
    public string? Reason { get; set; }
    public string? Outcome { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? DoneAt { get; set; }
    public string? DoneBy { get; set; }
}

public sealed class TriageDbContext(DbContextOptions<TriageDbContext> options) : DbContext(options)
{
    public const string Schema = "triage";
    public DbSet<RuleSet> RuleSets => Set<RuleSet>();
    public DbSet<SuitabilityRule> Rules => Set<SuitabilityRule>();
    public DbSet<MedicineRule> Medicines => Set<MedicineRule>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<TriageSettings> Settings => Set<TriageSettings>();
    public DbSet<Review> Reviews => Set<Review>();

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
        b.Entity<RuleSet>(e =>
        {
            e.ToTable("RuleSets");
            e.HasIndex(x => x.Version).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150); e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.CreatedBy).HasMaxLength(100); e.Property(x => x.ApprovedBy).HasMaxLength(150); e.Property(x => x.ApprovedRole).HasMaxLength(100);
            e.Property(x => x.ApprovalNote).HasMaxLength(1000);
            e.HasMany(x => x.Rules).WithOne().HasForeignKey(x => x.RuleSetId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Medicines).WithOne().HasForeignKey(x => x.RuleSetId).OnDelete(DeleteBehavior.Cascade);
            // Only one approved rule set at a time.
            e.HasIndex(x => x.Status).IsUnique().HasFilter("[Status] = 'Approved'").HasDatabaseName("UX_RuleSets_OneApproved");
        });
        b.Entity<SuitabilityRule>(e =>
        {
            e.ToTable("SuitabilityRules");
            e.Property(x => x.Code).HasMaxLength(20); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Fact).HasMaxLength(40);
            e.Property(x => x.Operator).HasConversion<string>().HasMaxLength(10); e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Threshold).HasPrecision(8, 2); e.Property(x => x.TextValue).HasMaxLength(100);
            e.Property(x => x.ProcedureContains).HasMaxLength(300); e.Property(x => x.Anaesthetic).HasMaxLength(20);
            e.Property(x => x.Precaution).HasMaxLength(300); e.Property(x => x.Explanation).HasMaxLength(1000);
            e.HasIndex(x => new { x.RuleSetId, x.Code }).IsUnique();
        });
        b.Entity<MedicineRule>(e =>
        {
            e.ToTable("MedicineRules");
            e.Property(x => x.Name).HasMaxLength(100); e.Property(x => x.Keywords).HasMaxLength(500); e.Property(x => x.Instruction).HasMaxLength(1000);
            e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(24);
            e.HasIndex(x => x.RuleSetId);
        });
        b.Entity<Assessment>(e =>
        {
            e.ToTable("Assessments");
            e.Property(x => x.Trigger).HasConversion<string>().HasMaxLength(16); e.Property(x => x.TriggerDetail).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Procedure).HasMaxLength(200); e.Property(x => x.Anaesthetic).HasMaxLength(20);
            e.Property(x => x.FactsJson).HasMaxLength(8000); e.Property(x => x.StaffInputsJson).HasMaxLength(4000);
            e.Property(x => x.Bmi).HasPrecision(5, 1);
            e.Property(x => x.SuggestedOutcome).HasConversion<string>().HasMaxLength(24); e.Property(x => x.FinalOutcome).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Precautions).HasMaxLength(1000); e.Property(x => x.Missing).HasMaxLength(1000); e.Property(x => x.ChangeSummary).HasMaxLength(2000);
            e.Property(x => x.Notes).HasMaxLength(4000); e.Property(x => x.OverrideReason).HasMaxLength(1000);
            e.Property(x => x.SignedOffBy).HasMaxLength(150); e.Property(x => x.SignedOffRole).HasMaxLength(100); e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasOne<RuleSet>().WithMany().HasForeignKey(x => x.RuleSetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Assessment>().WithMany().HasForeignKey(x => x.PreviousId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Findings).WithOne().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.PatientId, x.Status }); e.HasIndex(x => x.BookingId); e.HasIndex(x => x.FormRequestId);
            e.HasIndex(x => x.RuleSetId); e.HasIndex(x => x.PreviousId); e.HasIndex(x => new { x.Status, x.ProcedureDate });
        });
        b.Entity<Finding>(e =>
        {
            e.ToTable("Findings");
            e.HasKey(x => new { x.AssessmentId, x.Sequence });
            e.Property(x => x.Source).HasMaxLength(40); e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Text).HasMaxLength(500); e.Property(x => x.Precaution).HasMaxLength(300);
        });
        b.Entity<Review>(e =>
        {
            e.ToTable("Reviews");
            e.Property(x => x.AnaesthetistName).HasMaxLength(200);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(10); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.JoinLink).HasMaxLength(400); e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Reason).HasMaxLength(500); e.Property(x => x.Outcome).HasMaxLength(2000);
            e.Property(x => x.CreatedBy).HasMaxLength(100); e.Property(x => x.DoneBy).HasMaxLength(100);
            e.HasIndex(x => x.AssessmentId); e.HasIndex(x => x.PatientId); e.HasIndex(x => x.BookingId);
            e.HasIndex(x => new { x.AnaesthetistId, x.StartsAt });
            e.HasOne<Assessment>().WithMany().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TriageSettings>(e =>
        {
            e.ToTable("Settings", t => t.HasCheckConstraint("CK_TriageSettings_SingleRow", "[Id] = 1"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.HoldProcedures).HasMaxLength(500); e.Property(x => x.VideoLinkTemplate).HasMaxLength(300);
            e.HasData(new TriageSettings());
        });
    }
}

public sealed class TriageDbContextFactory : IDesignTimeDbContextFactory<TriageDbContext>
{
    public TriageDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<TriageDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", TriageDbContext.Schema)).Options);
}
