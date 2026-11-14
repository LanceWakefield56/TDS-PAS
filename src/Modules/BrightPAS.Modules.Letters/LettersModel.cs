using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Letters;

public sealed class LettersModule : IPasModule
{
    public const string Id = "letters";
    public ModuleManifest Manifest { get; } = new(Id, "Letters & documents", typeof(LettersModule).Assembly.GetName().Version ?? new Version(0, 0),
        LettersDbContext.Schema, ["patients"], "Letter templates, letters to patients, GPs and referrers, printing and GP messaging via HealthLink.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<LettersDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, LettersDbContext.Schema));
        services.AddScoped<LetterService>();
        services.AddScoped<HealthLinkService>();
    }
}

public enum LetterKind { Appointment = 1, Admission = 2, GpNotification = 3, ReferralAcknowledgement = 4, ReferralDeclined = 5, Custom = 6 }
public enum RecipientType { Patient = 1, Gp = 2, Referrer = 3, Other = 4 }
public enum LetterStatus { Draft = 1, Final = 2, Sent = 3, Acknowledged = 4, Failed = 5, Void = 6 }
public enum LetterChannel { None = 0, Print = 1, HealthLink = 2 }
public enum DeliveryChannel { HealthLink = 1, Post = 2, Email = 3 }
public enum HealthLinkMode { Off = 1, TestOutbox = 2, FolderDrop = 3 }

/// <summary>A GP practice (or other provider) that receives letters. Matched to patients by HPI facility id.</summary>
public sealed class GpPractice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? HpiFacilityId { get; set; }
    /// <summary>HealthLink EDI account of the practice (e.g. "stokemed").</summary>
    public string? HealthLinkEdi { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Postcode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DeliveryChannel PreferredChannel { get; set; } = DeliveryChannel.HealthLink;
    public bool IsActive { get; set; } = true;
}

public sealed class LetterTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public LetterKind Kind { get; set; }
    public RecipientType Recipient { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>A letter. The text is filled in from the template when the draft is made, can be edited while Draft, and is locked once Final.</summary>
public sealed class Letter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid PatientId { get; set; }
    public string TemplateCode { get; set; } = "";
    public int TemplateVersion { get; set; }
    public LetterKind Kind { get; set; }
    public RecipientType Recipient { get; set; }
    public string RecipientName { get; set; } = "";
    public string? RecipientAddress { get; set; }
    public Guid? GpPracticeId { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public Guid? BookingId { get; set; }
    public Guid? ReferralId { get; set; }
    public LetterStatus Status { get; set; } = LetterStatus.Draft;
    public LetterChannel Channel { get; set; }
    public int PrintCount { get; set; }
    public string? MessageControlId { get; set; }
    public string? AckCode { get; set; }
    public string? AckText { get; set; }
    public DateTimeOffset? AckAt { get; set; }
    public string? VoidReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public string? Author { get; set; }
    public DateTimeOffset? FinalisedAt { get; set; }
    public string? FinalisedBy { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? SentBy { get; set; }
}

/// <summary>A PDF sent with a letter (0.15.1: endoscopy report to the GP by HealthLink, embedded as OBX ED).</summary>
public sealed class LetterAttachment
{
    public Guid LetterId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/pdf";
    public byte[] Content { get; set; } = [];
}

/// <summary>HealthLink connection (single row). FolderDrop writes HL7 files for the HealthLink messaging client to send and reads acknowledgements back.</summary>
public sealed class HealthLinkSettings
{
    public int Id { get; set; } = 1;
    public HealthLinkMode Mode { get; set; } = HealthLinkMode.TestOutbox;
    /// <summary>TDS's own HealthLink EDI account.</summary>
    public string? SenderEdi { get; set; }
    public string? OutboxFolder { get; set; }
    public string? InboxFolder { get; set; }
    public int NextControlNumber { get; set; } = 1;
    public int NextLetterNumber { get; set; } = 1;
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class LettersDbContext(DbContextOptions<LettersDbContext> options) : DbContext(options)
{
    public const string Schema = "letters";
    public DbSet<GpPractice> Practices => Set<GpPractice>();
    public DbSet<LetterTemplate> Templates => Set<LetterTemplate>();
    public DbSet<Letter> Letters => Set<Letter>();
    public DbSet<LetterAttachment> Attachments => Set<LetterAttachment>();
    public DbSet<HealthLinkSettings> Settings => Set<HealthLinkSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        // 0.10.1: indexes on lookup columns and referential integrity within the schema.
        b.Entity<LetterAttachment>(e =>
        {
            e.ToTable("LetterAttachments"); e.HasKey(x => x.LetterId);
            e.Property(x => x.FileName).HasMaxLength(200); e.Property(x => x.ContentType).HasMaxLength(60);
            e.HasOne<Letter>().WithOne().HasForeignKey<LetterAttachment>(x => x.LetterId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Letter>().HasIndex(x => x.BookingId);
        b.Entity<Letter>().HasIndex(x => x.ReferralId);
        b.Entity<Letter>().HasOne<GpPractice>().WithMany().HasForeignKey(x => x.GpPracticeId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<GpPractice>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.HpiFacilityId).HasMaxLength(10);
            e.HasIndex(x => x.HpiFacilityId).IsUnique().HasFilter("[HpiFacilityId] IS NOT NULL");
            e.Property(x => x.HealthLinkEdi).HasMaxLength(20);
            e.Property(x => x.AddressLine1).HasMaxLength(150);
            e.Property(x => x.AddressLine2).HasMaxLength(150);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Postcode).HasMaxLength(10);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.PreferredChannel).HasConversion<string>().HasMaxLength(20);
        });
        b.Entity<LetterTemplate>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Recipient).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
        b.Entity<Letter>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.TemplateCode).HasMaxLength(30);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Recipient).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RecipientName).HasMaxLength(200);
            e.Property(x => x.RecipientAddress).HasMaxLength(500);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MessageControlId).HasMaxLength(30);
            e.HasIndex(x => x.MessageControlId);
            e.Property(x => x.AckCode).HasMaxLength(5);
            e.Property(x => x.AckText).HasMaxLength(500);
            e.Property(x => x.VoidReason).HasMaxLength(300);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.Author).HasMaxLength(150);
            e.Property(x => x.FinalisedBy).HasMaxLength(100);
            e.Property(x => x.SentBy).HasMaxLength(100);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });
        b.Entity<HealthLinkSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SenderEdi).HasMaxLength(20);
            e.Property(x => x.OutboxFolder).HasMaxLength(260);
            e.Property(x => x.InboxFolder).HasMaxLength(260);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
    }
}

public sealed class LettersDbContextFactory : IDesignTimeDbContextFactory<LettersDbContext>
{
    public LettersDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<LettersDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", LettersDbContext.Schema)).Options);
}
