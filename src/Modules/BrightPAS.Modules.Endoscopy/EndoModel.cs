using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Endoscopy;

/// <summary>
/// Endoscopy reporting and image/video documentation (spec 7.18, ADR 0027) – EndoBASE-style: one procedure per endoscopy booking
/// (gastroscopy, colonoscopy, flexible sigmoidoscopy, bidirectional) with structured terminology (extent, landmarks, Boston Bowel
/// Preparation Scale, withdrawal time, comfort, findings with LA/Prague/Forrest/Mayo grades, polyps with Paris/NICE, technique,
/// retrieval and histology, tattoos, biopsies and therapy, complications), pictures and video from the Olympus tower (DICOM worklist,
/// storage and MPPS, or a capture card in the browser), clinical and patient reports with pictures, and quality indicators (KPIs).
/// </summary>
public sealed class EndoscopyModule : IPasModule
{
    public const string Id = "endoscopy";
    public ModuleManifest Manifest { get; } = new(Id, "Endoscopy", typeof(EndoscopyModule).Assembly.GetName().Version ?? new Version(0, 0),
        EndoDbContext.Schema, [], "Endoscopy reporting, image and video capture from the tower, patient reports and quality indicators.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<EndoDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, EndoDbContext.Schema));
        services.AddScoped<EndoService>();
        services.AddScoped<MediaStore>();
    }
}

public enum ProcedureKind { Gastroscopy = 1, Colonoscopy = 2, FlexibleSigmoidoscopy = 3, Bidirectional = 4 }
public enum ProcedureStatus { Planned = 1, InProgress = 2, Completed = 3, Signed = 4 }
public enum SedationKind { None = 1, ThroatSpray = 2, Conscious = 3, Propofol = 4, GeneralAnaesthetic = 5 }
public enum UpperExtent { Oesophagus = 1, Stomach = 2, D1 = 3, D2 = 4, Failed = 5 }
public enum LowerExtent { Rectum = 1, Sigmoid = 2, Descending = 3, SplenicFlexure = 4, Transverse = 5, HepaticFlexure = 6, Ascending = 7, Caecum = 8, TerminalIleum = 9, Anastomosis = 10 }
public enum MediaKind { Image = 1, Video = 2 }
public enum MediaSource { CaptureStation = 1, Dicom = 2, Upload = 3 }
public enum MediaStatus { Recording = 1, Ready = 2, Deleted = 3 }

/// <summary>One endoscopy procedure (one theatre/procedure-room booking).</summary>
public sealed class Procedure
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Guid PatientId { get; set; }
    public ProcedureKind Kind { get; set; }
    public ProcedureStatus Status { get; set; } = ProcedureStatus.Planned;
    public Guid? EndoscopistId { get; set; }
    public string? EndoscopistName { get; set; }
    public string? Assistants { get; set; }
    public Guid? UpperScopeId { get; set; }
    public Guid? LowerScopeId { get; set; }
    public Guid? TowerId { get; set; }
    /// <summary>DICOM accession number given to the tower on the worklist (E + yyMMdd + 4 digits).</summary>
    public string AccessionNumber { get; set; } = "";
    public string StudyInstanceUid { get; set; } = "";
    public bool NbspScreening { get; set; }
    public string? NbspNumber { get; set; }
    public string? OtherIndication { get; set; }
    public bool ConsentConfirmed { get; set; }
    public SedationKind Sedation { get; set; } = SedationKind.Conscious;
    public string? SedationNotes { get; set; }
    public bool Co2 { get; set; } = true;
    public bool WaterAssisted { get; set; }
    public bool ScopeGuide { get; set; }
    public bool PositionChange { get; set; }
    public bool AbdominalPressure { get; set; }
    // Upper GI
    public DateTimeOffset? UpperStartAt { get; set; }
    public DateTimeOffset? UpperEndAt { get; set; }
    public UpperExtent? UpperExtent { get; set; }
    public bool? Retroflexion { get; set; }
    public bool? UpperPhotoDocumentation { get; set; }
    // Lower GI
    public DateTimeOffset? InsertedAt { get; set; }
    public DateTimeOffset? CaecumAt { get; set; }
    public DateTimeOffset? WithdrawalStartAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public LowerExtent? LowerExtent { get; set; }
    public bool? AppendicealOrifice { get; set; }
    public bool? IleocaecalValve { get; set; }
    public bool? TerminalIleum { get; set; }
    public bool? CaecumPhotographed { get; set; }
    public int? BbpsRight { get; set; }
    public int? BbpsTransverse { get; set; }
    public int? BbpsLeft { get; set; }
    public bool? RectalRetroflexion { get; set; }
    public string? IncompleteReason { get; set; }
    /// <summary>Modified Gloucester comfort score 1 (comfortable) – 5 (severe discomfort).</summary>
    public int? Comfort { get; set; }
    public string Complication { get; set; } = "None";
    public string? ComplicationDetails { get; set; }
    public string? Diagnosis { get; set; }
    public string? Recommendations { get; set; }
    public string? FollowUp { get; set; }
    public int? SurveillanceMonths { get; set; }
    public string? PatientAdvice { get; set; }
    public string? MppsUid { get; set; }
    /// <summary>Letter (Letters module) that sent the signed report to the GP.</summary>
    public Guid? GpLetterId { get; set; }
    public DateTimeOffset? NbspExportedAt { get; set; }
    public string? SignedBy { get; set; }
    public DateTimeOffset? SignedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<ProcedureIndication> Indications { get; set; } = [];
}

public sealed class ProcedureIndication { public Guid ProcedureId { get; set; } public string Code { get; set; } = ""; }

/// <summary>A structured finding (oesophagitis, Barrett's, ulcer, diverticulosis …) at a site.</summary>
public sealed class Finding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProcedureId { get; set; }
    public int Number { get; set; }
    public string Site { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Grade { get; set; }
    public int? SizeMm { get; set; }
    public string? Description { get; set; }
}

public sealed class Polyp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProcedureId { get; set; }
    public int Number { get; set; }
    public string Site { get; set; } = "";
    public int? DistanceCm { get; set; }
    public int SizeMm { get; set; }
    public string Paris { get; set; } = "";
    public string? Nice { get; set; }
    public string? Jnet { get; set; }
    public string Technique { get; set; } = "";
    public bool? Lifted { get; set; }
    public string Resection { get; set; } = "EnBloc";
    public string Retrieved { get; set; } = "Yes";
    public string? Pot { get; set; }
    public string Complete { get; set; } = "Yes";
    public int Clips { get; set; }
    public bool Tattooed { get; set; }
    public string? Notes { get; set; }
    public string? Histology { get; set; }
    public string? Dysplasia { get; set; }
    public string? HistologyNotes { get; set; }
    public string? HistologyBy { get; set; }
    public DateTimeOffset? HistologyAt { get; set; }
}

public sealed class Tattoo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProcedureId { get; set; }
    public string Site { get; set; } = "";
    public int? DistanceCm { get; set; }
    public string Position { get; set; } = "Distal";
    public string Ink { get; set; } = "";
    public decimal? VolumeMl { get; set; }
    public int? Injections { get; set; }
    public Guid? PolypId { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Biopsies and therapy: biopsy, clip, banding, dilatation, APC, injection, haemostasis, foreign body …</summary>
public sealed class Intervention
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProcedureId { get; set; }
    public string Kind { get; set; } = "";
    public string Site { get; set; } = "";
    public int? Count { get; set; }
    public string? Pot { get; set; }
    public string? Purpose { get; set; }
    public string? Details { get; set; }
    public string? Result { get; set; }
}

/// <summary>A picture or video. Files are kept in the endoscopy media folder; the row holds where, a checksum and how it is used.</summary>
public sealed class Media
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProcedureId { get; set; }
    public Guid? PatientId { get; set; }
    public MediaKind Kind { get; set; }
    public MediaSource Source { get; set; }
    public MediaStatus Status { get; set; } = MediaStatus.Ready;
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Bytes { get; set; }
    public string? Sha256 { get; set; }
    public int Chunks { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string CapturedBy { get; set; } = "";
    public decimal? DurationSeconds { get; set; }
    public string? Site { get; set; }
    public string? Caption { get; set; }
    public bool InReport { get; set; }
    public bool InPatientReport { get; set; }
    public int Sort { get; set; }
    public Guid? FindingId { get; set; }
    public Guid? PolypId { get; set; }
    public string? SopInstanceUid { get; set; }
    /// <summary>For an annotated copy: the original picture (kept unchanged).</summary>
    public Guid? AnnotatedFromId { get; set; }
    /// <summary>Original file when it was converted for display (e.g. TIFF kept beside the PNG shown).</summary>
    public string? OriginalFileName { get; set; }
    public string? OriginalContentType { get; set; }
    public string? DicomPatientId { get; set; }
    public string? DicomPatientName { get; set; }
    public string? DicomAccession { get; set; }
    public string? DeletedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeleteReason { get; set; }
}

/// <summary>Patient-friendly wording used in the patient report (editable by administrators).</summary>
public sealed class AdviceText
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class EndoDbContext(DbContextOptions<EndoDbContext> options) : DbContext(options)
{
    public const string Schema = "endo";
    public DbSet<Procedure> Procedures => Set<Procedure>();
    public DbSet<ProcedureIndication> Indications => Set<ProcedureIndication>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<Polyp> Polyps => Set<Polyp>();
    public DbSet<Tattoo> Tattoos => Set<Tattoo>();
    public DbSet<Intervention> Interventions => Set<Intervention>();
    public DbSet<Media> Media => Set<Media>();
    public DbSet<AdviceText> Advice => Set<AdviceText>();

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
        b.Entity<Procedure>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Sedation).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.UpperExtent).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LowerExtent).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EndoscopistName).HasMaxLength(100); e.Property(x => x.Assistants).HasMaxLength(200);
            e.Property(x => x.AccessionNumber).HasMaxLength(16); e.Property(x => x.StudyInstanceUid).HasMaxLength(64);
            e.Property(x => x.NbspNumber).HasMaxLength(30); e.Property(x => x.OtherIndication).HasMaxLength(300); e.Property(x => x.SedationNotes).HasMaxLength(200);
            e.Property(x => x.IncompleteReason).HasMaxLength(300); e.Property(x => x.Complication).HasMaxLength(30); e.Property(x => x.ComplicationDetails).HasMaxLength(1000);
            e.Property(x => x.Diagnosis).HasMaxLength(2000); e.Property(x => x.Recommendations).HasMaxLength(2000); e.Property(x => x.FollowUp).HasMaxLength(500);
            e.Property(x => x.PatientAdvice).HasMaxLength(2000); e.Property(x => x.MppsUid).HasMaxLength(64);
            e.Property(x => x.SignedBy).HasMaxLength(100); e.Property(x => x.CreatedBy).HasMaxLength(100); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasIndex(x => x.BookingId).IsUnique(); e.HasIndex(x => x.AccessionNumber).IsUnique(); e.HasIndex(x => x.PatientId);
            e.HasIndex(x => new { x.Status, x.Kind }); e.HasIndex(x => x.EndoscopistId);
            e.HasIndex(x => x.UpperScopeId); e.HasIndex(x => x.LowerScopeId); e.HasIndex(x => x.TowerId); e.HasIndex(x => x.GpLetterId);
            e.HasMany(x => x.Indications).WithOne().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ProcedureIndication>(e => { e.ToTable("ProcedureIndications"); e.HasKey(x => new { x.ProcedureId, x.Code }); e.Property(x => x.Code).HasMaxLength(40); });
        b.Entity<Finding>(e =>
        {
            e.Property(x => x.Site).HasMaxLength(40); e.Property(x => x.Kind).HasMaxLength(40); e.Property(x => x.Grade).HasMaxLength(60); e.Property(x => x.Description).HasMaxLength(1000);
            e.HasIndex(x => new { x.ProcedureId, x.Number }).IsUnique();
            e.HasOne<Procedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Polyp>(e =>
        {
            e.Property(x => x.Site).HasMaxLength(40); e.Property(x => x.Paris).HasMaxLength(10); e.Property(x => x.Nice).HasMaxLength(10); e.Property(x => x.Jnet).HasMaxLength(10);
            e.Property(x => x.Technique).HasMaxLength(30); e.Property(x => x.Resection).HasMaxLength(20); e.Property(x => x.Retrieved).HasMaxLength(10); e.Property(x => x.Pot).HasMaxLength(20);
            e.Property(x => x.Complete).HasMaxLength(10); e.Property(x => x.Notes).HasMaxLength(500); e.Property(x => x.Histology).HasMaxLength(40); e.Property(x => x.Dysplasia).HasMaxLength(20);
            e.Property(x => x.HistologyNotes).HasMaxLength(500); e.Property(x => x.HistologyBy).HasMaxLength(100);
            e.HasIndex(x => new { x.ProcedureId, x.Number }).IsUnique();
            e.HasOne<Procedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Tattoo>(e =>
        {
            e.Property(x => x.Site).HasMaxLength(40); e.Property(x => x.Position).HasMaxLength(20); e.Property(x => x.Ink).HasMaxLength(60); e.Property(x => x.Reason).HasMaxLength(300);
            e.Property(x => x.VolumeMl).HasPrecision(5, 1);
            e.HasIndex(x => x.ProcedureId); e.HasIndex(x => x.PolypId);
            e.HasOne<Procedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Intervention>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(30); e.Property(x => x.Site).HasMaxLength(40); e.Property(x => x.Pot).HasMaxLength(20); e.Property(x => x.Purpose).HasMaxLength(100);
            e.Property(x => x.Details).HasMaxLength(500); e.Property(x => x.Result).HasMaxLength(100);
            e.HasIndex(x => x.ProcedureId);
            e.HasOne<Procedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Media>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10); e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.FileName).HasMaxLength(260); e.Property(x => x.ContentType).HasMaxLength(60); e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.CapturedBy).HasMaxLength(100); e.Property(x => x.DurationSeconds).HasPrecision(9, 1);
            e.Property(x => x.Site).HasMaxLength(40); e.Property(x => x.Caption).HasMaxLength(200);
            e.Property(x => x.SopInstanceUid).HasMaxLength(64); e.Property(x => x.DicomPatientId).HasMaxLength(64); e.Property(x => x.DicomPatientName).HasMaxLength(100); e.Property(x => x.DicomAccession).HasMaxLength(16);
            e.Property(x => x.DeletedBy).HasMaxLength(100); e.Property(x => x.DeleteReason).HasMaxLength(300);
            e.Property(x => x.OriginalFileName).HasMaxLength(260); e.Property(x => x.OriginalContentType).HasMaxLength(60); e.HasIndex(x => x.AnnotatedFromId); e.HasOne<Media>().WithMany().HasForeignKey(x => x.AnnotatedFromId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.ProcedureId, x.Sort }); e.HasIndex(x => x.PatientId); e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SopInstanceUid).IsUnique().HasFilter("[SopInstanceUid] IS NOT NULL");
            e.HasOne<Procedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Finding>().WithMany().HasForeignKey(x => x.FindingId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Polyp>().WithMany().HasForeignKey(x => x.PolypId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<AdviceText>(e =>
        {
            e.ToTable("AdviceTexts");
            e.Property(x => x.Key).HasMaxLength(40); e.Property(x => x.Title).HasMaxLength(100); e.Property(x => x.Body).HasMaxLength(4000); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Key).IsUnique();
        });
    }
}

public sealed class EndoDbContextFactory : IDesignTimeDbContextFactory<EndoDbContext>
{
    public EndoDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<EndoDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", EndoDbContext.Schema)).Options);
}
