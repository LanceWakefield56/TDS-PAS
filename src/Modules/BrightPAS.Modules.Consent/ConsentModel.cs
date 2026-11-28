using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Consent;

/// <summary>
/// E-consent with patient education (0.29.0, ADR 0042). TDS keeps procedure information sheets (what the operation is, benefits, risks with how
/// often they happen, alternatives, what happens without treatment, getting ready, recovery, videos and web pages), versioned and approved by a
/// clinician. For each booking the surgeon's team prepares a consent from the sheet (adding risks that apply to this patient) and sends the patient
/// a secret link (date of birth as a second factor). The patient reads each section, can ask the surgeon a question, and signs (themselves, or a
/// parent / guardian / welfare guardian / EPOA attorney), with an interpreter recorded if one helped. A signed PDF is filed in the patient record.
/// On the day a clinician confirms the consent with the patient, which ticks the theatre "consent" check. Paper consent can be scanned in instead.
/// The patient can withdraw at any time before surgery. BrightPAS records consent; the surgeon remains responsible for the consent discussion.
/// </summary>
public sealed class ConsentModule : IPasModule
{
    public const string Id = "consent";
    public ModuleManifest Manifest { get; } = new(Id, "E-consent and patient education", typeof(ConsentModule).Assembly.GetName().Version ?? new Version(0, 0),
        ConsentDbContext.Schema, ["theatre", "reminders"], "Procedure information sheets, consent sent to the patient by a secure link, signed online or on paper, confirmed on the day.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<ConsentDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, ConsentDbContext.Schema));
        services.AddScoped<ConsentSheetService>();
        services.AddScoped<ConsentService>();
    }
}

/// <summary>Draft: being written. Published: the one in use (one per code). Retired: replaced or withdrawn; kept because signed consents refer to it.</summary>
public enum SheetStatus { Draft = 1, Published = 2, Retired = 3 }

/// <summary>How often a risk happens, in the words the Royal Colleges use (with the numbers shown to the patient).</summary>
public enum RiskFrequency { Common = 1, Uncommon = 2, Rare = 3, VeryRare = 4 }

/// <summary>A procedure information sheet (one version). The text the patient reads before they sign.</summary>
public sealed class ConsentSheet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Groups the versions of one sheet, e.g. LAPCHOLE.</summary>
    public string Code { get; set; } = "";
    public int Version { get; set; } = 1;
    public SheetStatus Status { get; set; } = SheetStatus.Draft;
    public string Name { get; set; } = "";
    /// <summary>Words that pick this sheet for a booking's procedure (comma separated, any one matches), e.g. "cholecystectomy, gallbladder".</summary>
    public string? ProcedureWords { get; set; }
    public string About { get; set; } = "";
    public string Benefits { get; set; } = "";
    public string Alternatives { get; set; } = "";
    public string NoTreatment { get; set; } = "";
    public string? Anaesthetic { get; set; }
    public string? Preparation { get; set; }
    public string? Recovery { get; set; }
    public List<SheetRisk> Risks { get; set; } = [];
    public List<SheetLink> Links { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public string? ApprovalNote { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
}

public sealed class SheetRisk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SheetId { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = "";
    public RiskFrequency Frequency { get; set; }
    /// <summary>A serious risk (death, disability, further surgery) – always shown, even when rare.</summary>
    public bool Serious { get; set; }
}

/// <summary>A video or web page the patient can watch or read (opens outside BrightPAS).</summary>
public sealed class SheetLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SheetId { get; set; }
    public int Order { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>
/// Draft: prepared, not sent. Sent: the patient has the link. Signed: the patient (or their representative) signed – online or on paper.
/// Confirmed: a clinician went through it with the patient on the day. Withdrawn: the patient withdrew consent, or staff cancelled it.
/// </summary>
public enum ConsentStatus { Draft = 1, Sent = 2, Signed = 3, Confirmed = 4, Withdrawn = 5 }

public enum ConsentMethod { Online = 1, Paper = 2 }

/// <summary>Who signed. Under-16s and people who cannot consent are signed for by a representative (NZ Code of Rights, right 7).</summary>
public enum SignerKind { Patient = 1, Parent = 2, WelfareGuardian = 3, Epoa = 4, Other = 5 }

/// <summary>Consent for one booking.</summary>
public sealed class ConsentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid PatientId { get; set; }
    /// <summary>Theatre booking (theatre.Bookings).</summary>
    public Guid BookingId { get; set; }
    public Guid SheetId { get; set; }
    public ConsentStatus Status { get; set; } = ConsentStatus.Draft;
    public ConsentMethod Method { get; set; } = ConsentMethod.Online;
    public string Procedure { get; set; } = "";
    public Laterality Laterality { get; set; }
    /// <summary>Surgeon or endoscopist doing the procedure (core.Clinicians).</summary>
    public Guid? ClinicianId { get; set; }
    public string? ClinicianName { get; set; }
    /// <summary>The sheet as the patient saw it (JSON): later edits to the sheet never change a consent.</summary>
    public string Snapshot { get; set; } = "";
    /// <summary>Risks that apply to this patient in particular (one per line), added by the surgeon's team.</summary>
    public string? ExtraRisks { get; set; }
    public string? NoteToPatient { get; set; }

    // Secure link.
    public string? TokenHash { get; set; }
    public DateTimeOffset? LinkSentAt { get; set; }
    public DateTimeOffset? LinkExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    /// <summary>Sections the patient marked as read (comma separated keys).</summary>
    public string? SectionsRead { get; set; }

    // Signature.
    public SignerKind? Signer { get; set; }
    public string? SignerName { get; set; }
    public string? SignerRelationship { get; set; }
    public string? Interpreter { get; set; }
    /// <summary>Drawn signature (PNG), encrypted like patient documents.</summary>
    public byte[]? SignatureImage { get; set; }
    public DateTimeOffset? SignedAt { get; set; }
    public string? SignedBy { get; set; }
    /// <summary>The signed PDF filed in the patient record (patients.Documents).</summary>
    public Guid? DocumentId { get; set; }

    // On the day.
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public string? ConfirmNote { get; set; }

    public DateTimeOffset? WithdrawnAt { get; set; }
    public string? WithdrawnBy { get; set; }
    public string? WithdrawnReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<ConsentQuestion> Questions { get; set; } = [];
}

/// <summary>A question from the patient and the answer from the surgeon's team (shown to the patient when they open the link again).</summary>
public sealed class ConsentQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConsentId { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset AskedAt { get; set; }
    public string? Answer { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public string? AnsweredBy { get; set; }
}

public sealed class ConsentSettings
{
    public int Id { get; set; } = 1;
    public int NextNumber { get; set; } = 1;
    /// <summary>The address patients use to open links from outside, e.g. https://portal.tasmandaysurgery.co.nz.</summary>
    public string? PublicUrl { get; set; }
    public int LinkDays { get; set; } = 14;
    /// <summary>Off by default: a theatre booking cannot be confirmed until the consent is signed.</summary>
    public bool RequireBeforeConfirm { get; set; }
    /// <summary>Bookings this many days ahead appear on the "To prepare" list.</summary>
    public int PrepareDaysAhead { get; set; } = 28;
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class ConsentDbContext(DbContextOptions<ConsentDbContext> options) : DbContext(options)
{
    public const string Schema = "consent";
    public DbSet<ConsentSheet> Sheets => Set<ConsentSheet>();
    public DbSet<SheetRisk> Risks => Set<SheetRisk>();
    public DbSet<SheetLink> Links => Set<SheetLink>();
    public DbSet<ConsentRecord> Consents => Set<ConsentRecord>();
    public DbSet<ConsentQuestion> Questions => Set<ConsentQuestion>();
    public DbSet<ConsentSettings> Settings => Set<ConsentSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<ConsentSheet>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30);
            e.HasIndex(x => new { x.Code, x.Version }).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.ProcedureWords).HasMaxLength(300);
            e.Property(x => x.About).HasMaxLength(4000);
            e.Property(x => x.Benefits).HasMaxLength(4000);
            e.Property(x => x.Alternatives).HasMaxLength(4000);
            e.Property(x => x.NoTreatment).HasMaxLength(4000);
            e.Property(x => x.Anaesthetic).HasMaxLength(4000);
            e.Property(x => x.Preparation).HasMaxLength(4000);
            e.Property(x => x.Recovery).HasMaxLength(4000);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.ApprovedBy).HasMaxLength(100);
            e.Property(x => x.ApprovalNote).HasMaxLength(500);
            e.HasMany(x => x.Risks).WithOne().HasForeignKey(r => r.SheetId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Links).WithOne().HasForeignKey(r => r.SheetId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SheetRisk>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(500);
            e.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.SheetId, x.Order });
        });
        b.Entity<SheetLink>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(150);
            e.Property(x => x.Url).HasMaxLength(500);
            e.HasIndex(x => new { x.SheetId, x.Order });
        });
        b.Entity<ConsentRecord>(e =>
        {
            e.ToTable("Consents");
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.BookingId);
            e.HasIndex(x => x.SheetId);
            e.HasIndex(x => x.ClinicianId);
            e.HasIndex(x => x.DocumentId);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Procedure).HasMaxLength(300);
            e.Property(x => x.Laterality).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ClinicianName).HasMaxLength(150);
            e.Property(x => x.ExtraRisks).HasMaxLength(2000);
            e.Property(x => x.NoteToPatient).HasMaxLength(1000);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash);
            e.Property(x => x.SectionsRead).HasMaxLength(200);
            e.Property(x => x.Signer).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SignerName).HasMaxLength(150);
            e.Property(x => x.SignerRelationship).HasMaxLength(100);
            e.Property(x => x.Interpreter).HasMaxLength(150);
            e.Property(x => x.SignedBy).HasMaxLength(100);
            e.Property(x => x.ConfirmedBy).HasMaxLength(100);
            e.Property(x => x.ConfirmNote).HasMaxLength(1000);
            e.Property(x => x.WithdrawnBy).HasMaxLength(100);
            e.Property(x => x.WithdrawnReason).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasOne<ConsentSheet>().WithMany().HasForeignKey(x => x.SheetId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(q => q.ConsentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ConsentQuestion>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(1000);
            e.Property(x => x.Answer).HasMaxLength(2000);
            e.Property(x => x.AnsweredBy).HasMaxLength(100);
            e.HasIndex(x => new { x.ConsentId, x.AskedAt });
        });
        b.Entity<ConsentSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.PublicUrl).HasMaxLength(300);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasData(new ConsentSettings { Id = 1 });
        });
    }
}

public sealed class ConsentDbContextFactory : IDesignTimeDbContextFactory<ConsentDbContext>
{
    public ConsentDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ConsentDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", ConsentDbContext.Schema)).Options);
}
