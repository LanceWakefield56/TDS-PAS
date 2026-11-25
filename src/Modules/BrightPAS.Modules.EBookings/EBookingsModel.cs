using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.EBookings;

/// <summary>
/// Bookings from specialists' rooms (0.28.0, ADR 0041). The specialist or their practice assistant sends a booking request through the
/// specialist portal (patient, procedure, funding, preferred dates, pre-operative details, referral letter and signed consent). Practices whose
/// own system can export a booking send it as a file instead – BrightPAS JSON or HL7 v2 REF^I12 – by upload or into a watched folder.
/// The TDS booking office checks the request, matches or registers the patient, asks questions, and accepts it onto the waitlist
/// (a referral and waitlist entry are created, files are filed in the patient record) or declines it. The rooms see the status and booked date.
/// </summary>
public sealed class EBookingsModule : IPasModule
{
    public const string Id = "ebookings";
    public ModuleManifest Manifest { get; } = new(Id, "Bookings from specialists' rooms", typeof(EBookingsModule).Assembly.GetName().Version ?? new Version(0, 0),
        EBookingsDbContext.Schema, ["referrals", "portal"], "Booking requests sent by specialists' rooms through the portal or as files (JSON or HL7 REF^I12); booking office review onto the waitlist.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<EBookingsDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, EBookingsDbContext.Schema));
        services.AddScoped<EBookingService>();
        services.AddScoped<EBookingImporter>();
    }
}

/// <summary>Submitted: waiting for the booking office. Query: TDS asked the rooms a question. Accepted: on the waitlist. Declined / Withdrawn: closed.</summary>
public enum EBookingStatus { Submitted = 1, Query = 2, Accepted = 3, Declined = 4, Withdrawn = 5 }

public enum EBookingSource { Portal = 1, File = 2, Hl7 = 3 }

/// <summary>A booking request from a specialist's rooms. Patient details are kept as sent until the booking office matches or registers the patient.</summary>
public sealed class BookingRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public EBookingStatus Status { get; set; } = EBookingStatus.Submitted;
    public EBookingSource Source { get; set; }
    /// <summary>The rooms' own reference (file imports use it to skip a request already received).</summary>
    public string? ExternalRef { get; set; }

    /// <summary>Specialist (core.Clinicians) the patient is booked with.</summary>
    public Guid SpecialistId { get; set; }
    public string? RoomsName { get; set; }
    public string ContactName { get; set; } = "";
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    // Patient as sent by the rooms.
    public string? Nhi { get; set; }
    public string FamilyName { get; set; } = "";
    public string GivenNames { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public Patients.BirthSex Sex { get; set; }
    public string? MobilePhone { get; set; }
    public string? Email { get; set; }
    public string? AddressLine1 { get; set; }
    public string? Suburb { get; set; }
    public string? City { get; set; }
    public string? Postcode { get; set; }
    public string? InterpreterLanguage { get; set; }
    /// <summary>Patient (patients.Patients) once matched or registered by the booking office.</summary>
    public Guid? PatientId { get; set; }

    // What is booked.
    public ServiceType Service { get; set; } = ServiceType.Surgical;
    /// <summary>Procedure list item (core.Procedures), when picked.</summary>
    public Guid? ProcedureItemId { get; set; }
    public string Procedure { get; set; } = "";
    public Laterality Laterality { get; set; }
    public AnaestheticType Anaesthetic { get; set; } = AnaestheticType.General;
    public int DurationMinutes { get; set; } = 30;
    public Priority Priority { get; set; } = Priority.Routine;
    public Funder Funder { get; set; }
    /// <summary>Insurer approval number, ACC claim number or Health NZ referral id.</summary>
    public string? FunderReference { get; set; }
    public string? InsurerName { get; set; }
    public DateOnly? EarliestDate { get; set; }
    public DateOnly? LatestDate { get; set; }
    public string? Availability { get; set; }
    public string? ClinicalNotes { get; set; }

    // Pre-operative details.
    public decimal? WeightKg { get; set; }
    public decimal? HeightCm { get; set; }
    public int? Asa { get; set; }
    public bool Diabetic { get; set; }
    public bool SleepApnoea { get; set; }
    public bool Anticoagulated { get; set; }
    public string? AnticoagulantDetails { get; set; }
    public string? Allergies { get; set; }
    public string? Comorbidities { get; set; }
    public bool ResponsibleAdultArranged { get; set; }
    public bool ConsentSigned { get; set; }
    public DateOnly? ConsentSignedOn { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
    public string SubmittedBy { get; set; } = "";
    public DateTimeOffset StatusAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? DeclineReason { get; set; }
    /// <summary>Referral (referrals.Referrals) and waitlist entry (referrals.Waitlist) created on acceptance.</summary>
    public Guid? ReferralId { get; set; }
    public Guid? WaitlistEntryId { get; set; }
    public List<BookingRequestFile> Files { get; set; } = [];
    public List<BookingRequestMessage> Messages { get; set; } = [];
}

/// <summary>A file sent with the request (referral letter, signed consent, results). Encrypted at rest like patient documents; filed in the patient record on acceptance.</summary>
public sealed class BookingRequestFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Patients.DocumentCategory Category { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public byte[] Content { get; set; } = [];
    public DateTimeOffset AddedAt { get; set; }
    public string AddedBy { get; set; } = "";
    /// <summary>Patient document (patients.Documents) once filed.</summary>
    public Guid? PatientDocumentId { get; set; }
}

/// <summary>A question or answer between the booking office and the rooms.</summary>
public sealed class BookingRequestMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public bool FromTds { get; set; }
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class EBookingSettings
{
    public int Id { get; set; } = 1;
    public int NextNumber { get; set; } = 1;
    /// <summary>Folder (share or USB) where practice systems drop booking files (JSON or HL7); imported files move to an "imported" subfolder.</summary>
    public string? WatchFolder { get; set; }
    /// <summary>Email the rooms contact when the booking office asks a question, accepts or declines (no patient details in the email; needs Reminders email set up).</summary>
    public bool EmailRooms { get; set; }
    public int MaxFileMegabytes { get; set; } = 10;
    public string? Instructions { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string? LastProblem { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class EBookingsDbContext(DbContextOptions<EBookingsDbContext> options) : DbContext(options)
{
    public const string Schema = "ebookings";
    public DbSet<BookingRequest> Requests => Set<BookingRequest>();
    public DbSet<BookingRequestFile> Files => Set<BookingRequestFile>();
    public DbSet<BookingRequestMessage> Messages => Set<BookingRequestMessage>();
    public DbSet<EBookingSettings> Settings => Set<EBookingSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<BookingRequest>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ExternalRef).HasMaxLength(60);
            e.HasIndex(x => x.ExternalRef);
            e.HasIndex(x => x.SpecialistId);
            e.Property(x => x.RoomsName).HasMaxLength(150);
            e.Property(x => x.ContactName).HasMaxLength(150);
            e.Property(x => x.ContactPhone).HasMaxLength(30);
            e.Property(x => x.ContactEmail).HasMaxLength(200);
            e.Property(x => x.Nhi).HasMaxLength(7);
            e.HasIndex(x => x.Nhi);
            e.Property(x => x.FamilyName).HasMaxLength(100);
            e.Property(x => x.GivenNames).HasMaxLength(100);
            e.Property(x => x.Sex).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MobilePhone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.AddressLine1).HasMaxLength(150);
            e.Property(x => x.Suburb).HasMaxLength(100);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Postcode).HasMaxLength(10);
            e.Property(x => x.InterpreterLanguage).HasMaxLength(60);
            e.HasIndex(x => x.PatientId);
            e.Property(x => x.Service).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.ProcedureItemId);
            e.Property(x => x.Procedure).HasMaxLength(300);
            e.Property(x => x.Laterality).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Anaesthetic).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Funder).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FunderReference).HasMaxLength(60);
            e.Property(x => x.InsurerName).HasMaxLength(100);
            e.Property(x => x.Availability).HasMaxLength(500);
            e.Property(x => x.ClinicalNotes).HasMaxLength(2000);
            e.Property(x => x.WeightKg).HasPrecision(5, 1);
            e.Property(x => x.HeightCm).HasPrecision(5, 1);
            e.Property(x => x.AnticoagulantDetails).HasMaxLength(300);
            e.Property(x => x.Allergies).HasMaxLength(500);
            e.Property(x => x.Comorbidities).HasMaxLength(1000);
            e.Property(x => x.SubmittedBy).HasMaxLength(100);
            e.Property(x => x.DecidedBy).HasMaxLength(100);
            e.Property(x => x.DeclineReason).HasMaxLength(500);
            e.HasIndex(x => x.ReferralId);
            e.HasIndex(x => x.WaitlistEntryId);
            e.HasIndex(x => new { x.Status, x.SubmittedAt });
            e.HasMany(x => x.Files).WithOne().HasForeignKey(f => f.RequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(m => m.RequestId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<BookingRequestFile>(e =>
        {
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FileName).HasMaxLength(150);
            e.Property(x => x.ContentType).HasMaxLength(50);
            e.Property(x => x.AddedBy).HasMaxLength(100);
            e.HasIndex(x => x.RequestId);
            e.HasIndex(x => x.PatientDocumentId);
        });
        b.Entity<BookingRequestMessage>(e =>
        {
            e.Property(x => x.Author).HasMaxLength(100);
            e.Property(x => x.Text).HasMaxLength(2000);
            e.HasIndex(x => new { x.RequestId, x.At });
        });
        b.Entity<EBookingSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.WatchFolder).HasMaxLength(300);
            e.Property(x => x.Instructions).HasMaxLength(1000);
            e.Property(x => x.LastProblem).HasMaxLength(500);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasData(new EBookingSettings { Id = 1 });
        });
    }
}

public sealed class EBookingsDbContextFactory : IDesignTimeDbContextFactory<EBookingsDbContext>
{
    public EBookingsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<EBookingsDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", EBookingsDbContext.Schema)).Options);
}
