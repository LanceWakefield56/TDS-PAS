using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Payments;

/// <summary>
/// Online payments and statements (0.30.0, ADR 0043). Staff send a patient a secret payment link for an invoice (or a statement covering all their
/// self-funded invoices). The patient opens it on any device, confirms their date of birth, sees what is owing and pays by card on the payment
/// provider's own hosted page (Windcave), so card numbers never touch BrightPAS. The result is checked with the provider server-to-server and the
/// payment is recorded on the invoice(s) through Billing, oldest first. Statements: Finance prepares a run of statements for every patient with a
/// self-funded balance; each goes by email/SMS (with a payment link) or is printed for posting. Simulator provider for training and the robot.
/// </summary>
public sealed class PaymentsModule : IPasModule
{
    public const string Id = "payments";
    public ModuleManifest Manifest { get; } = new(Id, "Online payments and statements", typeof(PaymentsModule).Assembly.GetName().Version ?? new Version(0, 0),
        PaymentsDbContext.Schema, ["billing", "reminders"], "Patients pay invoices by card from a secure link; monthly statements by email, SMS or post.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<PaymentsDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, PaymentsDbContext.Schema));
        services.AddScoped<PaymentLinkService>();
        services.AddScoped<StatementService>();
        services.AddScoped<RefundService>();
        services.AddScoped<PlanService>();
        services.AddScoped<AutomationService>();
    }
}

/// <summary>Off: no online payments. Simulator: training and the test robot (no money moves). Windcave: Windcave hosted payment page (REST API).</summary>
public enum GatewayProvider { Off = 1, Simulator = 2, Windcave = 3 }

public enum PayLinkStatus { Open = 1, Paid = 2, Cancelled = 3 }

public enum OnlinePaymentStatus { Started = 1, Approved = 2, Declined = 3, Cancelled = 4, Failed = 5 }

public enum StatementDelivery { Email = 1, Sms = 2, Post = 3 }

public enum StatementStatus { Prepared = 1, Sent = 2, Printed = 3, NotSent = 4 }

public enum RefundStatus { Done = 1, Failed = 2 }

public enum PlanFrequency { Weekly = 1, Fortnightly = 2, Monthly = 3 }

public enum PlanStatus { Active = 1, Completed = 2, Cancelled = 3 }

public sealed class PaymentSettings
{
    public int Id { get; set; } = 1;
    public GatewayProvider Provider { get; set; } = GatewayProvider.Off;
    /// <summary>The address patients use from the internet, e.g. https://pay.tasmandaysurgery.co.nz (links are {PublicUrl}/pay/{token}).</summary>
    public string? PublicUrl { get; set; }
    public int LinkDays { get; set; } = 30;
    /// <summary>Smallest part-payment a patient may make online (they can always pay the whole balance).</summary>
    public decimal MinimumPayment { get; set; } = 10m;
    public string WindcaveUrl { get; set; } = "https://uat.windcave.com";
    public string? WindcaveUser { get; set; }
    public string? WindcaveKeyProtected { get; set; }
    /// <summary>Statements are not made for balances below this.</summary>
    public decimal StatementMinimum { get; set; } = 5m;
    /// <summary>Optional line printed on every statement (e.g. payment options or a seasonal message).</summary>
    public string? StatementMessage { get; set; }
    public int NextStatementNumber { get; set; } = 1;
    // 0.32.0 (ADR 0045)
    public int NextRefundNumber { get; set; } = 1;
    public int NextPlanNumber { get; set; } = 1;
    /// <summary>Day of the month (1–28) statements are prepared automatically; 0 = only by hand.</summary>
    public int AutoStatementDay { get; set; }
    /// <summary>Automatic runs are also sent (email/SMS); otherwise they wait for Finance to check and send.</summary>
    public bool AutoSendStatements { get; set; }
    /// <summary>First overdue reminder this many days after an invoice's due date (0 = off).</summary>
    public int OverdueFirstDays { get; set; }
    /// <summary>Second overdue reminder this many days after the due date (0 = off).</summary>
    public int OverdueSecondDays { get; set; }
    /// <summary>Put insurer shortfalls (what the insurer did not pay on an insurer invoice) on statements and overdue reminders.</summary>
    public bool IncludeShortfalls { get; set; }
    /// <summary>Payment plan reminders go this many days before each instalment is due.</summary>
    public int PlanReminderDays { get; set; } = 2;
    public DateOnly? LastAutomationOn { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>A secret link a patient uses to pay one invoice, or everything on a statement. Only a hash of the token is stored.</summary>
public sealed class PayLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? StatementId { get; set; }
    /// <summary>A payment plan link (0.32.0): covers the plan's invoices; the instalment amount is suggested.</summary>
    public Guid? PlanId { get; set; }
    public string TokenHash { get; set; } = "";
    public PayLinkStatus Status { get; set; } = PayLinkStatus.Open;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
}

/// <summary>One attempt to pay online. The card number never reaches BrightPAS – only card type and last four digits from the provider.</summary>
public sealed class OnlinePayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PayLinkId { get; set; }
    public Guid PatientId { get; set; }
    public decimal Amount { get; set; }
    public OnlinePaymentStatus Status { get; set; } = OnlinePaymentStatus.Started;
    public GatewayProvider Provider { get; set; }
    /// <summary>The provider's session id (Windcave) or a simulator id.</summary>
    public string? SessionId { get; set; }
    /// <summary>The provider's transaction id (needed to refund through Windcave).</summary>
    public string? TransactionId { get; set; }
    /// <summary>Total refunded to the card so far (0.32.0).</summary>
    public decimal Refunded { get; set; }
    public string MerchantReference { get; set; } = "";
    public string? CardType { get; set; }
    public string? Last4 { get; set; }
    public string? AuthCode { get; set; }
    public string? ResponseText { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>Money was taken but could not all be applied to invoices (e.g. paid at the desk meanwhile): Finance must refund or apply it.</summary>
    public bool NeedsAttention { get; set; }
    public string? AttentionReason { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolvedNote { get; set; }
    public List<PaymentAllocation> Allocations { get; set; } = [];
}

/// <summary>How much of an online payment went on which invoice (one Billing payment each).</summary>
public sealed class PaymentAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OnlinePaymentId { get; set; }
    public Guid InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal Amount { get; set; }
    /// <summary>How much of this allocation has been refunded (and taken off the invoice).</summary>
    public decimal Refunded { get; set; }
}

/// <summary>Money given back to the patient's card for an online payment (0.32.0). Taken first from money that was never applied, then off the
/// invoices it paid (newest first) as a negative payment in Billing.</summary>
public sealed class Refund
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid OnlinePaymentId { get; set; }
    public Guid PatientId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Part of the refund that came from money never applied to an invoice.</summary>
    public decimal FromUnapplied { get; set; }
    public string Reason { get; set; } = "";
    public RefundStatus Status { get; set; }
    public string? ProviderReference { get; set; }
    public string? Error { get; set; }
    /// <summary>Which invoices it was taken off, e.g. "INV-000123 $20.00".</summary>
    public string? AppliedTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

/// <summary>An agreed payment plan (0.32.0): the patient pays a balance off in regular instalments. Progress is measured from payments made on the
/// plan's invoices since the plan started, by any method. Patients on a plan that is up to date get instalment reminders instead of statements and
/// overdue reminders.</summary>
public sealed class PaymentPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid PatientId { get; set; }
    public decimal Total { get; set; }
    public decimal Instalment { get; set; }
    public PlanFrequency Frequency { get; set; }
    public DateOnly FirstDue { get; set; }
    /// <summary>What had already been paid on the plan's invoices when the plan began.</summary>
    public decimal PaidAtStart { get; set; }
    public PlanStatus Status { get; set; } = PlanStatus.Active;
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? EndedAt { get; set; }
    public string? EndedBy { get; set; }
    public string? EndReason { get; set; }
    /// <summary>Due date of the last instalment a reminder was sent for.</summary>
    public DateOnly? LastReminderFor { get; set; }
    public List<PlanInvoice> Invoices { get; set; } = [];
}

public sealed class PlanInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public Guid InvoiceId { get; set; }
}

/// <summary>An overdue reminder sent for an invoice (stage 1 or 2), so each is sent only once.</summary>
public sealed class OverdueNotice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid PatientId { get; set; }
    public string? InvoiceNumber { get; set; }
    public int Stage { get; set; }
    public decimal Balance { get; set; }
    public DateTimeOffset SentAt { get; set; }
    /// <summary>How many messages were queued (0 = no consent or no valid address – nothing sent).</summary>
    public int Messages { get; set; }
}

public sealed class StatementRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly StatementDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public int Count { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? SentBy { get; set; }
    public List<Statement> Statements { get; set; } = [];
}

/// <summary>A patient's statement: a snapshot of their self-funded invoices with a balance on the statement date.</summary>
public sealed class Statement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public string Number { get; set; } = "";
    public Guid PatientId { get; set; }
    public DateOnly StatementDate { get; set; }
    public decimal TotalDue { get; set; }
    public decimal Overdue { get; set; }
    public StatementDelivery Delivery { get; set; }
    public StatementStatus Status { get; set; } = StatementStatus.Prepared;
    /// <summary>JSON list of the invoices on the statement (number, dates, total, paid, balance) as they stood on the statement date.</summary>
    public string Snapshot { get; set; } = "[]";
    public DateTimeOffset? SentAt { get; set; }
    public string? Note { get; set; }
}

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public const string Schema = "payments";
    public DbSet<PaymentSettings> Settings => Set<PaymentSettings>();
    public DbSet<PayLink> Links => Set<PayLink>();
    public DbSet<OnlinePayment> Payments => Set<OnlinePayment>();
    public DbSet<PaymentAllocation> Allocations => Set<PaymentAllocation>();
    public DbSet<StatementRun> Runs => Set<StatementRun>();
    public DbSet<Statement> Statements => Set<Statement>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<PaymentPlan> Plans => Set<PaymentPlan>();
    public DbSet<PlanInvoice> PlanInvoices => Set<PlanInvoice>();
    public DbSet<OverdueNotice> Overdue => Set<OverdueNotice>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<decimal>().HavePrecision(12, 2);
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
            c.Properties<decimal>().HaveConversion<double>();
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<PaymentSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PublicUrl).HasMaxLength(200);
            e.Property(x => x.WindcaveUrl).HasMaxLength(200);
            e.Property(x => x.WindcaveUser).HasMaxLength(100);
            e.Property(x => x.WindcaveKeyProtected).HasMaxLength(1000);
            e.Property(x => x.StatementMessage).HasMaxLength(300);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
        b.Entity<PayLink>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.InvoiceId);
            e.HasIndex(x => x.StatementId);
            e.HasIndex(x => x.PlanId);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.CancelledBy).HasMaxLength(100);
        });
        b.Entity<OnlinePayment>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SessionId).HasMaxLength(100);
            e.HasIndex(x => x.SessionId);
            e.Property(x => x.TransactionId).HasMaxLength(100);
            e.Property(x => x.MerchantReference).HasMaxLength(64);
            e.HasIndex(x => x.MerchantReference).IsUnique();
            e.Property(x => x.CardType).HasMaxLength(30);
            e.Property(x => x.Last4).HasMaxLength(4);
            e.Property(x => x.AuthCode).HasMaxLength(20);
            e.Property(x => x.ResponseText).HasMaxLength(300);
            e.Property(x => x.AttentionReason).HasMaxLength(500);
            e.Property(x => x.ResolvedBy).HasMaxLength(100);
            e.Property(x => x.ResolvedNote).HasMaxLength(500);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.PayLinkId);
            e.HasIndex(x => new { x.Status, x.StartedAt });
            e.HasOne<PayLink>().WithMany().HasForeignKey(x => x.PayLinkId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Allocations).WithOne().HasForeignKey(a => a.OnlinePaymentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PaymentAllocation>(e =>
        {
            e.Property(x => x.InvoiceNumber).HasMaxLength(30);
            e.HasIndex(x => x.InvoiceId);
        });
        b.Entity<StatementRun>(e =>
        {
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.SentBy).HasMaxLength(100);
            e.HasIndex(x => x.StatementDate);
            e.HasMany(x => x.Statements).WithOne().HasForeignKey(s => s.RunId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Statement>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Delivery).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Snapshot).HasMaxLength(8000);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => x.PatientId);
        });
        b.Entity<Refund>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Reason).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ProviderReference).HasMaxLength(100);
            e.Property(x => x.Error).HasMaxLength(300);
            e.Property(x => x.AppliedTo).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.OnlinePaymentId);
            e.HasIndex(x => x.PatientId);
            e.HasOne<OnlinePayment>().WithMany().HasForeignKey(x => x.OnlinePaymentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PaymentPlan>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.EndedBy).HasMaxLength(100);
            e.Property(x => x.EndReason).HasMaxLength(300);
            e.HasIndex(x => x.PatientId);
            e.HasMany(x => x.Invoices).WithOne().HasForeignKey(i => i.PlanId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PlanInvoice>(e =>
        {
            e.HasIndex(x => new { x.PlanId, x.InvoiceId }).IsUnique();
            e.HasIndex(x => x.InvoiceId);
        });
        b.Entity<OverdueNotice>(e =>
        {
            e.Property(x => x.InvoiceNumber).HasMaxLength(30);
            e.HasIndex(x => new { x.InvoiceId, x.Stage }).IsUnique();
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.SentAt);
        });
    }
}

public sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<PaymentsDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", PaymentsDbContext.Schema)).Options);
}
