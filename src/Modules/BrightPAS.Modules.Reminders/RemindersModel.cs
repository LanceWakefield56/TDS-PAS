using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Reminders;

public sealed class RemindersModule : IPasModule
{
    public const string Id = "reminders";
    public ModuleManifest Manifest { get; } = new(Id, "SMS & email reminders", typeof(RemindersModule).Assembly.GetName().Version ?? new Version(0, 0),
        RemindersDbContext.Schema, ["patients", "theatre"], "Booking confirmations, reminders, questionnaire links and two-way SMS replies.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<RemindersDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, RemindersDbContext.Schema));
        services.AddHttpClient(ModicaGateway.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient(GenericHttpGateway.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<ISmsGateway, ModicaGateway>();
        services.AddScoped<ISmsGateway, GenericHttpGateway>();
        services.AddScoped<ISmsGateway, TestSmsGateway>();
        services.AddScoped<IEmailGateway, SmtpEmailGateway>();
        services.AddScoped<IEmailGateway, TestEmailGateway>();
        services.AddScoped<ReminderService>();
    }
}

/// <summary>SMS vendor. Modica is the TDS default (spec 6); others can be chosen in Setup or on the settings screen without code changes.</summary>
public enum SmsProvider { Modica = 1, GenericHttp = 2, TestOutbox = 3, Off = 4 }
public enum EmailProvider { Smtp = 1, TestOutbox = 2, Off = 3 }
public enum Channel { Sms = 1, Email = 2 }
public enum MessageKind { BookingConfirmation = 1, DayBeforeReminder = 2, FormLink = 3, ReplyConfirmed = 4, ReplyCancelRequested = 5, OptOutConfirmed = 6, Test = 7, PortalLink = 8 }
public enum MessageStatus { Queued = 1, Sending = 2, Sent = 3, Delivered = 4, Failed = 5, Cancelled = 6, Suppressed = 7 }
public enum ReplyMeaning { Confirm = 1, Cancel = 2, OptOut = 3, OptIn = 4, Unknown = 5 }
public enum ReplyStatus { New = 1, Handled = 2 }

/// <summary>Single settings row (Id 1). Secrets are stored encrypted with <see cref="ISecretProtector"/> and never returned to the browser.</summary>
public sealed class ReminderSettings
{
    public int Id { get; set; } = 1;
    public SmsProvider SmsProvider { get; set; } = SmsProvider.Modica;
    public EmailProvider EmailProvider { get; set; } = EmailProvider.Smtp;
    public bool SendBookingConfirmation { get; set; } = true;
    public bool SendDayBeforeReminder { get; set; } = true;
    public bool SendEmailCopies { get; set; } = true;
    /// <summary>Local hour (0–23) from which next-day reminders are sent.</summary>
    public int DayBeforeHour { get; set; } = 14;
    /// <summary>Arrival time shown to patients = planned start minus this many minutes.</summary>
    public int ArrivalLeadMinutes { get; set; } = 60;
    public TimeOnly QuietStart { get; set; } = new(20, 0);
    public TimeOnly QuietEnd { get; set; } = new(8, 0);
    public string ClinicName { get; set; } = "Tasman Day Surgery";
    public string ClinicPhone { get; set; } = "";
    /// <summary>Only bookings created after this moment get automatic confirmations (so switching on does not message every existing booking).</summary>
    public DateTimeOffset ConfirmationsFrom { get; set; }

    // Modica REST gateway (default vendor)
    public string ModicaBaseUrl { get; set; } = ModicaGateway.DefaultBaseUrl;
    public string? ModicaApplication { get; set; }
    public string? ModicaPasswordProtected { get; set; }
    public string? ModicaMask { get; set; }

    // Any other HTTP SMS vendor: POST JSON built from a template.
    public string? GenericUrl { get; set; }
    public string? GenericAuthHeader { get; set; } = "Authorization";
    public string? GenericAuthValueProtected { get; set; }
    public string GenericBodyTemplate { get; set; } = """{"to":"{to}","message":"{message}"}""";

    // Email (SMTP relay, e.g. Microsoft 365)
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpStartTls { get; set; } = true;
    public string? SmtpUser { get; set; }
    public string? SmtpPasswordProtected { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Tasman Day Surgery";

    /// <summary>Secret part of the vendor callback URLs (replies and delivery receipts).</summary>
    public string? WebhookKeyProtected { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

public sealed class MessageTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MessageKind Kind { get; set; }
    public Channel Channel { get; set; }
    /// <summary>"en" or "mi". Te reo Māori templates are written and checked by TDS before they are switched on.</summary>
    public string Language { get; set; } = "en";
    public string? Subject { get; set; }
    public string Body { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

/// <summary>Per-patient communication preferences. No row means: SMS and email allowed, English.</summary>
public sealed class ContactPreference
{
    public Guid PatientId { get; set; }
    public bool AllowSms { get; set; } = true;
    public bool AllowEmail { get; set; } = true;
    public string Language { get; set; } = "en";
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

/// <summary>An address (mobile number or email) that must not be messaged (UEMA 2007 unsubscribe).</summary>
public sealed class OptOut
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Channel Channel { get; set; }
    public string Address { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Source { get; set; } = "";
}

public sealed class OutboundMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>The patient the message is about; null for test messages sent from the settings page (0.12.3).</summary>
    public Guid? PatientId { get; set; }
    public Guid? BookingId { get; set; }
    public DateOnly? AppointmentDate { get; set; }
    public MessageKind Kind { get; set; }
    public Channel Channel { get; set; }
    public string Address { get; set; } = "";
    public string? Subject { get; set; }
    public string Body { get; set; } = "";
    /// <summary>Questionnaire links are secrets: removed from the stored text once sent.</summary>
    public string? SecretLink { get; set; }
    public MessageStatus Status { get; set; } = MessageStatus.Queued;
    /// <summary>Staff-triggered messages go straight away; automatic ones wait for the end of quiet hours.</summary>
    public bool IgnoreQuietHours { get; set; }
    public string? Provider { get; set; }
    public string? ProviderMessageId { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public string? DedupeKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

public sealed class InboundMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string From { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public string Provider { get; set; } = "";
    public string? ProviderMessageId { get; set; }
    public string? ReplyToProviderId { get; set; }
    public Guid? MatchedMessageId { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? BookingId { get; set; }
    public ReplyMeaning Meaning { get; set; }
    public string ActionTaken { get; set; } = "";
    public ReplyStatus Status { get; set; } = ReplyStatus.New;
    public string? HandledBy { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
    public string? Note { get; set; }
}

public sealed class RemindersDbContext(DbContextOptions<RemindersDbContext> options) : DbContext(options)
{
    public const string Schema = "reminders";
    public DbSet<ReminderSettings> Settings => Set<ReminderSettings>();
    public DbSet<MessageTemplate> Templates => Set<MessageTemplate>();
    public DbSet<ContactPreference> Preferences => Set<ContactPreference>();
    public DbSet<OptOut> OptOuts => Set<OptOut>();
    public DbSet<OutboundMessage> Messages => Set<OutboundMessage>();
    public DbSet<InboundMessage> Replies => Set<InboundMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        // 0.10.1: indexes on lookup columns and referential integrity within the schema.
        b.Entity<OutboundMessage>().HasIndex(x => x.BookingId);
        b.Entity<InboundMessage>().HasIndex(x => x.BookingId);
        b.Entity<InboundMessage>().HasIndex(x => x.PatientId);
        b.Entity<InboundMessage>().HasOne<OutboundMessage>().WithMany().HasForeignKey(x => x.MatchedMessageId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<ReminderSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SmsProvider).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EmailProvider).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ClinicName).HasMaxLength(100);
            e.Property(x => x.ClinicPhone).HasMaxLength(30);
            e.Property(x => x.ModicaBaseUrl).HasMaxLength(200);
            e.Property(x => x.ModicaApplication).HasMaxLength(100);
            e.Property(x => x.ModicaPasswordProtected).HasMaxLength(1000);
            e.Property(x => x.ModicaMask).HasMaxLength(20);
            e.Property(x => x.GenericUrl).HasMaxLength(300);
            e.Property(x => x.GenericAuthHeader).HasMaxLength(100);
            e.Property(x => x.GenericAuthValueProtected).HasMaxLength(1000);
            e.Property(x => x.GenericBodyTemplate).HasMaxLength(2000);
            e.Property(x => x.SmtpHost).HasMaxLength(200);
            e.Property(x => x.SmtpUser).HasMaxLength(200);
            e.Property(x => x.SmtpPasswordProtected).HasMaxLength(1000);
            e.Property(x => x.FromAddress).HasMaxLength(200);
            e.Property(x => x.FromName).HasMaxLength(100);
            e.Property(x => x.WebhookKeyProtected).HasMaxLength(1000);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
        b.Entity<MessageTemplate>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Language).HasMaxLength(5);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.Kind, x.Channel, x.Language }).IsUnique();
        });
        b.Entity<ContactPreference>(e =>
        {
            e.HasKey(x => x.PatientId);
            e.Property(x => x.Language).HasMaxLength(5);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
        b.Entity<OptOut>(e =>
        {
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Address).HasMaxLength(200);
            e.Property(x => x.Source).HasMaxLength(100);
            e.HasIndex(x => new { x.Channel, x.Address }).IsUnique();
        });
        b.Entity<OutboundMessage>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Address).HasMaxLength(200);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.SecretLink).HasMaxLength(500);
            e.Property(x => x.Provider).HasMaxLength(30);
            e.Property(x => x.ProviderMessageId).HasMaxLength(100);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.Property(x => x.DedupeKey).HasMaxLength(100);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.DedupeKey).IsUnique().HasFilter("[DedupeKey] IS NOT NULL");
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.ProviderMessageId);
            e.HasIndex(x => new { x.Address, x.CreatedAt });
        });
        b.Entity<InboundMessage>(e =>
        {
            e.Property(x => x.From).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(2000);
            e.Property(x => x.Provider).HasMaxLength(30);
            e.Property(x => x.ProviderMessageId).HasMaxLength(100);
            e.Property(x => x.ReplyToProviderId).HasMaxLength(100);
            e.Property(x => x.Meaning).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ActionTaken).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.HandledBy).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasIndex(x => new { x.Status, x.ReceivedAt });
            e.HasIndex(x => x.ProviderMessageId);
        });
    }
}

public sealed class RemindersDbContextFactory : IDesignTimeDbContextFactory<RemindersDbContext>
{
    public RemindersDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<RemindersDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", RemindersDbContext.Schema)).Options);
}

/// <summary>Encrypts vendor passwords at rest. The web host implements it with ASP.NET Core Data Protection (DPAPI-protected keys).</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    string? Unprotect(string? protectedValue);
}
