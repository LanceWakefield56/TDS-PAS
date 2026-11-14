using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Scribe;

/// <summary>
/// BrightPAS Scribe (spec 7.22, ADR 0028): AI dictation in free-text fields and an ambient scribe that records a consultation (with consent),
/// transcribes it and drafts a note from a TDS-approved template. Drafts only – a clinician reviews and signs. The speech engine and drafting
/// engine are pluggable (on site or cloud, with de-identification for cloud). Medow Health can be chosen instead (spec 7.17).
/// </summary>
public sealed class ScribeModule : IPasModule
{
    public const string Id = "scribe";
    public ModuleManifest Manifest { get; } = new(Id, "BrightPAS Scribe", typeof(ScribeModule).Assembly.GetName().Version ?? new Version(0, 0),
        ScribeDbContext.Schema, [], "AI dictation and ambient clinical documentation (drafts for clinician sign-off).");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<ScribeDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, ScribeDbContext.Schema));
        services.AddScoped<ScribeService>();
        services.AddScoped<AudioStore>();
    }
}

public enum ScribeProvider { Off = 0, BrightPas = 1, Medow = 2 }
/// <summary>Speech to text: Off, an OpenAI-compatible transcription server on site (e.g. Whisper), or a cloud service (audio leaves TDS – needs approval).</summary>
public enum SpeechMode { Off = 0, OnSite = 1, Cloud = 2 }
/// <summary>Drafting: template only (no AI), an on-site language model (OpenAI-compatible chat), or the Claude API with a de-identified transcript.</summary>
public enum DraftMode { TemplateOnly = 0, OnSite = 1, CloudClaude = 2 }
public enum ScribeContext { Consultation = 1, PreAnaesthetic = 2, PreAdmission = 3, TriageCall = 4, PostOpReview = 5, Discharge = 6, FollowUpCall = 7 }
public enum SessionStatus { Recording = 1, Recorded = 2, Transcribed = 3, Drafted = 4, Signed = 5, Discarded = 6 }
public enum TemplateStatus { Draft = 1, Approved = 2, Retired = 3 }

public sealed class ScribeSettings
{
    public int Id { get; set; } = 1;
    public ScribeProvider Provider { get; set; } = ScribeProvider.BrightPas;
    public SpeechMode Speech { get; set; }
    public string? SpeechUrl { get; set; }
    public string? SpeechModel { get; set; }
    public byte[]? SpeechKey { get; set; }
    public DraftMode Draft { get; set; }
    public string? DraftUrl { get; set; }
    public string? DraftModel { get; set; }
    public byte[]? DraftKey { get; set; }
    /// <summary>Privacy impact assessment addendum / supplier assessment reference for any cloud processing (speech or drafting).</summary>
    public string? CloudPrivacyReference { get; set; }
    public string? CloudApprovedBy { get; set; }
    public DateTimeOffset? CloudApprovedAt { get; set; }
    public string? MedowPiaReference { get; set; }
    public string? MedowSupplierAssessment { get; set; }
    public string? MedowAgreementReference { get; set; }
    public string? MedowLaunchUrl { get; set; }
    /// <summary>SHA-256 of the bearer token Medow (or another external scribe) uses to return drafts.</summary>
    public string? InboundTokenHash { get; set; }
    public string? MedowApprovedBy { get; set; }
    public DateTimeOffset? MedowApprovedAt { get; set; }
    /// <summary>0 = delete audio as soon as the note is signed or discarded; otherwise keep for this many days (e.g. for accuracy audits).</summary>
    public int AudioRetentionDays { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class ScribeTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public ScribeContext Context { get; set; }
    /// <summary>One section per line: "Heading: what to put here".</summary>
    public string Sections { get; set; } = "";
    public int Version { get; set; } = 1;
    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>TDS custom dictionary: names, medicines, procedures, places and te reo Māori kupu, with what the speech engine tends to hear instead.</summary>
public sealed class DictionaryTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Term { get; set; } = "";
    public string? SoundsLike { get; set; }
    public string Category { get; set; } = "Other";
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Patient consent to AI recording: per session or a standing preference (withdrawable). Names the product.</summary>
public sealed class ScribeConsent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public bool Given { get; set; }
    public bool Standing { get; set; }
    public ScribeProvider Product { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
    public string? WithdrawnBy { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
}

public sealed class ScribeSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? BookingId { get; set; }
    public ScribeContext Context { get; set; }
    public Guid TemplateId { get; set; }
    public int TemplateVersion { get; set; }
    public ScribeProvider Provider { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Recording;
    public string Clinician { get; set; } = "";
    public Guid? ConsentId { get; set; }
    public string? AudioFolder { get; set; }
    public string? AudioType { get; set; }
    public int AudioChunks { get; set; }
    public long AudioBytes { get; set; }
    public decimal? DurationSeconds { get; set; }
    public DateTimeOffset? AudioDeletedAt { get; set; }
    public string? SpeechEngine { get; set; }
    public string? DraftEngine { get; set; }
    public bool DeIdentified { get; set; }
    public string? Draft { get; set; }
    public string? SignedText { get; set; }
    public string? SignedBy { get; set; }
    public DateTimeOffset? SignedAt { get; set; }
    /// <summary>How much the clinician changed the AI draft before signing (0–100 %, word level) – accuracy audit.</summary>
    public decimal? EditPercent { get; set; }
    public Guid? NoteId { get; set; }
    public Guid? DocumentId { get; set; }
    public string? DiscardReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class TranscriptSegment
{
    public Guid SessionId { get; set; }
    public int Seq { get; set; }
    public string Speaker { get; set; } = "Unknown";
    public decimal StartSeconds { get; set; }
    public decimal EndSeconds { get; set; }
    public string Text { get; set; } = "";
    public decimal? Confidence { get; set; }
}

/// <summary>A drafted sentence and the transcript segments it came from (review screen links each sentence to its source).</summary>
public sealed class DraftSentence
{
    public Guid SessionId { get; set; }
    public int Seq { get; set; }
    public string Section { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Sources { get; set; }
}

public sealed class ScribeDbContext(DbContextOptions<ScribeDbContext> options) : DbContext(options)
{
    public const string Schema = "scribe";
    public DbSet<ScribeSettings> Settings => Set<ScribeSettings>();
    public DbSet<ScribeTemplate> Templates => Set<ScribeTemplate>();
    public DbSet<DictionaryTerm> Dictionary => Set<DictionaryTerm>();
    public DbSet<ScribeConsent> Consents => Set<ScribeConsent>();
    public DbSet<ScribeSession> Sessions => Set<ScribeSession>();
    public DbSet<TranscriptSegment> Segments => Set<TranscriptSegment>();
    public DbSet<DraftSentence> Sentences => Set<DraftSentence>();

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
        b.Entity<ScribeSettings>(e =>
        {
            e.ToTable("Settings"); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Speech).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Draft).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SpeechUrl).HasMaxLength(300); e.Property(x => x.SpeechModel).HasMaxLength(100); e.Property(x => x.DraftUrl).HasMaxLength(300); e.Property(x => x.DraftModel).HasMaxLength(100);
            e.Property(x => x.CloudPrivacyReference).HasMaxLength(200); e.Property(x => x.CloudApprovedBy).HasMaxLength(100);
            e.Property(x => x.MedowPiaReference).HasMaxLength(200); e.Property(x => x.MedowSupplierAssessment).HasMaxLength(200); e.Property(x => x.MedowAgreementReference).HasMaxLength(200);
            e.Property(x => x.MedowLaunchUrl).HasMaxLength(300); e.Property(x => x.InboundTokenHash).HasMaxLength(64); e.Property(x => x.MedowApprovedBy).HasMaxLength(100); e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
        b.Entity<ScribeTemplate>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30); e.Property(x => x.Name).HasMaxLength(100); e.Property(x => x.Context).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Sections).HasMaxLength(4000); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.ApprovedBy).HasMaxLength(100); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => new { x.Context, x.Status });
        });
        b.Entity<DictionaryTerm>(e =>
        {
            e.ToTable("DictionaryTerms");
            e.Property(x => x.Term).HasMaxLength(100); e.Property(x => x.SoundsLike).HasMaxLength(300); e.Property(x => x.Category).HasMaxLength(30); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Term).IsUnique(); e.HasIndex(x => x.Category);
        });
        b.Entity<ScribeConsent>(e =>
        {
            e.ToTable("Consents");
            e.Property(x => x.Product).HasConversion<string>().HasMaxLength(20); e.Property(x => x.RecordedBy).HasMaxLength(100); e.Property(x => x.WithdrawnBy).HasMaxLength(100);
            e.HasIndex(x => new { x.PatientId, x.RecordedAt });
        });
        b.Entity<ScribeSession>(e =>
        {
            e.ToTable("Sessions");
            e.Property(x => x.Context).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Clinician).HasMaxLength(100); e.Property(x => x.AudioFolder).HasMaxLength(100); e.Property(x => x.AudioType).HasMaxLength(60);
            e.Property(x => x.DurationSeconds).HasPrecision(9, 1); e.Property(x => x.EditPercent).HasPrecision(5, 1);
            e.Property(x => x.SpeechEngine).HasMaxLength(100); e.Property(x => x.DraftEngine).HasMaxLength(100); e.Property(x => x.SignedBy).HasMaxLength(100); e.Property(x => x.DiscardReason).HasMaxLength(300);
            e.HasIndex(x => x.PatientId); e.HasIndex(x => x.BookingId); e.HasIndex(x => new { x.Clinician, x.Status }); e.HasIndex(x => x.TemplateId); e.HasIndex(x => x.ConsentId);
            e.HasIndex(x => x.NoteId); e.HasIndex(x => x.DocumentId);
            e.HasOne<ScribeTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ScribeConsent>().WithMany().HasForeignKey(x => x.ConsentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TranscriptSegment>(e =>
        {
            e.ToTable("TranscriptSegments"); e.HasKey(x => new { x.SessionId, x.Seq });
            e.Property(x => x.Speaker).HasMaxLength(30); e.Property(x => x.Text).HasMaxLength(4000);
            e.Property(x => x.StartSeconds).HasPrecision(9, 2); e.Property(x => x.EndSeconds).HasPrecision(9, 2); e.Property(x => x.Confidence).HasPrecision(4, 3);
            e.HasOne<ScribeSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DraftSentence>(e =>
        {
            e.ToTable("DraftSentences"); e.HasKey(x => new { x.SessionId, x.Seq });
            e.Property(x => x.Section).HasMaxLength(100); e.Property(x => x.Text).HasMaxLength(2000); e.Property(x => x.Sources).HasMaxLength(200);
            e.HasOne<ScribeSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class ScribeDbContextFactory : IDesignTimeDbContextFactory<ScribeDbContext>
{
    public ScribeDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ScribeDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", ScribeDbContext.Schema)).Options);
}
