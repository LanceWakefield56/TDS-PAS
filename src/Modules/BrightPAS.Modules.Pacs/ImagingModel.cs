using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Pacs;

/// <summary>
/// Imaging (PACS) (0.37.0, ADR 0050). Stores X-rays and scans (DICOM) linked to patients and episodes (theatre bookings) and shows them in the
/// browser. Images come in by manual upload (files, a ZIP or a whole CD folder) or from a watched network folder; each is matched to a patient by
/// accession number (an imaging request made in BrightPAS) or by NHI/MRN plus date of birth, otherwise it waits in "To match" for a person.
/// Designed so a vendor modality/PACS link (likely GE: DICOM C-STORE and Modality Worklist) can be added later: every route ends in
/// <see cref="ImagingService.StoreAsync"/> and requests already carry accession numbers and study UIDs for a worklist.
/// </summary>
public sealed class ImagingModule : IPasModule
{
    public const string Id = "imaging";
    public ModuleManifest Manifest { get; } = new(Id, "Imaging (PACS)", typeof(ImagingModule).Assembly.GetName().Version ?? new Version(0, 0),
        ImagingDbContext.Schema, [], "X-rays and scans (DICOM) linked to patients and episodes: upload, watched folder, matching and a browser viewer.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<ImagingDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, ImagingDbContext.Schema));
        services.AddScoped<ImagingStore>();
        services.AddScoped<ImagingService>();
    }
}

public enum StudyStatus { Unmatched = 1, Filed = 2, Deleted = 3 }

/// <summary>How the study was put with its patient.</summary>
public enum MatchMethod { Accession = 1, PatientIdAndBirthDate = 2, Person = 3 }

/// <summary>Where the pictures came from. Network is for the later vendor link (DICOM C-STORE).</summary>
public enum ImageSource { Upload = 1, Folder = 2, Network = 3, Demo = 4 }

public enum RequestStatus { Requested = 1, Done = 2, Cancelled = 3 }

/// <summary>
/// An imaging request (order) made in BrightPAS. It gives an accession number (X + yyMMdd + 4 digits) and a study UID: written on the request
/// form or entered at the modality, they file the pictures to the right patient and episode automatically. A later Modality Worklist serves these.
/// </summary>
public sealed class ImagingRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    /// <summary>The episode (theatre booking) the imaging is for, if any.</summary>
    public Guid? BookingId { get; set; }
    public string AccessionNumber { get; set; } = "";
    public string StudyInstanceUid { get; set; } = "";
    /// <summary>DICOM modality code: CR, DX, CT, MR, US, XA, RF, MG, OT.</summary>
    public string Modality { get; set; } = "DX";
    public string Examination { get; set; } = "";
    public string? BodyPart { get; set; }
    public string? ClinicalNotes { get; set; }
    public DateOnly? WantedOn { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Requested;
    public Guid? StudyId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
}

/// <summary>One imaging study (DICOM Study Instance UID): a set of series and images taken together.</summary>
public sealed class Study
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StudyInstanceUid { get; set; } = "";
    public StudyStatus Status { get; set; } = StudyStatus.Unmatched;
    public Guid? PatientId { get; set; }
    /// <summary>The episode (theatre booking) the study belongs to, if any.</summary>
    public Guid? BookingId { get; set; }
    public Guid? RequestId { get; set; }
    public MatchMethod? MatchedBy { get; set; }
    public string? AccessionNumber { get; set; }
    public DateOnly? StudyDate { get; set; }
    public string? StudyTime { get; set; }
    public string? Description { get; set; }
    /// <summary>Modalities in the study, comma separated (e.g. "CR" or "CT,SR").</summary>
    public string Modalities { get; set; } = "";
    public string? BodyPart { get; set; }
    public string? ReferringPhysician { get; set; }
    public string? Institution { get; set; }
    // What the pictures themselves say about the patient (kept so a person can check the match).
    public string? FilePatientId { get; set; }
    public string? FilePatientName { get; set; }
    public DateOnly? FileBirthDate { get; set; }
    public string? FileSex { get; set; }
    public ImageSource Source { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string ReceivedBy { get; set; } = "";
    public DateTimeOffset? FiledAt { get; set; }
    public string? FiledBy { get; set; }
    public int ImageCount { get; set; }
    public long Bytes { get; set; }
    /// <summary>Radiology report or clinician's note on the images (free text).</summary>
    public string? Report { get; set; }
    public DateTimeOffset? ReportAt { get; set; }
    public string? ReportBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public string? DeletedReason { get; set; }
    public List<Series> Series { get; set; } = [];
}

public sealed class Series
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudyId { get; set; }
    public string SeriesInstanceUid { get; set; } = "";
    public string Modality { get; set; } = "";
    public int? SeriesNumber { get; set; }
    public string? Description { get; set; }
    public string? BodyPart { get; set; }
    public int ImageCount { get; set; }
    public List<Image> Images { get; set; } = [];
}

/// <summary>One DICOM object (SOP instance). The original file is kept exactly as received; the viewer renders it on request.</summary>
public sealed class Image
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudyId { get; set; }
    public Guid SeriesId { get; set; }
    public string SopInstanceUid { get; set; } = "";
    public string SopClassUid { get; set; } = "";
    public int? InstanceNumber { get; set; }
    public int Rows { get; set; }
    public int Columns { get; set; }
    public int Frames { get; set; } = 1;
    public string? Photometric { get; set; }
    public string TransferSyntax { get; set; } = "";
    /// <summary>False when BrightPAS cannot draw it (e.g. a compression it has no decoder for, or a report object); it can still be downloaded.</summary>
    public bool Viewable { get; set; }
    public double? WindowCentre { get; set; }
    public double? WindowWidth { get; set; }
    /// <summary>Millimetres per pixel (row, column) when the file says, for future measuring.</summary>
    public string? PixelSpacing { get; set; }
    public string FileName { get; set; } = "";
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class ImagingDbContext(DbContextOptions<ImagingDbContext> options) : DbContext(options)
{
    public const string Schema = "imaging";
    public DbSet<ImagingRequest> Requests => Set<ImagingRequest>();
    public DbSet<Study> Studies => Set<Study>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<Image> Images => Set<Image>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<ImagingRequest>(e =>
        {
            e.Property(x => x.AccessionNumber).HasMaxLength(16);
            e.HasIndex(x => x.AccessionNumber).IsUnique();
            e.Property(x => x.StudyInstanceUid).HasMaxLength(64);
            e.HasIndex(x => x.StudyInstanceUid);
            e.Property(x => x.Modality).HasMaxLength(4);
            e.Property(x => x.Examination).HasMaxLength(200);
            e.Property(x => x.BodyPart).HasMaxLength(64);
            e.Property(x => x.ClinicalNotes).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RequestedBy).HasMaxLength(100);
            e.Property(x => x.CancelledBy).HasMaxLength(100);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.BookingId);
        });
        b.Entity<Study>(e =>
        {
            e.Property(x => x.StudyInstanceUid).HasMaxLength(64);
            e.HasIndex(x => x.StudyInstanceUid).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MatchedBy).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AccessionNumber).HasMaxLength(16);
            e.HasIndex(x => x.AccessionNumber);
            e.Property(x => x.StudyTime).HasMaxLength(16);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.Modalities).HasMaxLength(60);
            e.Property(x => x.BodyPart).HasMaxLength(64);
            e.Property(x => x.ReferringPhysician).HasMaxLength(200);
            e.Property(x => x.Institution).HasMaxLength(200);
            e.Property(x => x.FilePatientId).HasMaxLength(64);
            e.Property(x => x.FilePatientName).HasMaxLength(200);
            e.Property(x => x.FileSex).HasMaxLength(16);
            e.Property(x => x.ReceivedBy).HasMaxLength(200);
            e.Property(x => x.FiledBy).HasMaxLength(100);
            e.Property(x => x.Report).HasMaxLength(8000);
            e.Property(x => x.ReportBy).HasMaxLength(100);
            e.Property(x => x.DeletedBy).HasMaxLength(100);
            e.Property(x => x.DeletedReason).HasMaxLength(500);
            e.HasIndex(x => new { x.PatientId, x.Status });
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.BookingId);
            e.HasMany(x => x.Series).WithOne().HasForeignKey(x => x.StudyId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Series>(e =>
        {
            e.Property(x => x.SeriesInstanceUid).HasMaxLength(64);
            e.HasIndex(x => x.SeriesInstanceUid).IsUnique();
            e.Property(x => x.Modality).HasMaxLength(16);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.BodyPart).HasMaxLength(64);
            e.HasMany(x => x.Images).WithOne().HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Image>(e =>
        {
            e.Property(x => x.SopInstanceUid).HasMaxLength(64);
            e.HasIndex(x => x.SopInstanceUid).IsUnique();
            e.Property(x => x.SopClassUid).HasMaxLength(64);
            e.Property(x => x.Photometric).HasMaxLength(20);
            e.Property(x => x.TransferSyntax).HasMaxLength(64);
            e.Property(x => x.PixelSpacing).HasMaxLength(40);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.HasIndex(x => x.StudyId);
        });
    }
}

public sealed class ImagingDbContextFactory : IDesignTimeDbContextFactory<ImagingDbContext>
{
    public ImagingDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ImagingDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", ImagingDbContext.Schema)).Options);
}
