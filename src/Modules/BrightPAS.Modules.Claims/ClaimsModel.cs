using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Claims;

public sealed class ClaimsModule : IPasModule
{
    public const string Id = "claims";
    public ModuleManifest Manifest { get; } = new(Id, "Claims & payments", typeof(ClaimsModule).Assembly.GetName().Version ?? new Version(0, 0),
        ClaimsDbContext.Schema, ["patients", "billing"], "ACC and insurer prior approvals and claims, claim files and remittances, Xero, EFTPOS terminals.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<ClaimsDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, ClaimsDbContext.Schema));
        services.AddScoped<ClaimsService>();
        services.AddScoped<XeroService>();
        services.AddScoped<TerminalService>();
        services.AddScoped<DirectClaimService>();
        services.AddSingleton<IFunderApi, NoFunderApi>();
        services.AddSingleton<ITerminalApi, NoTerminalApi>(); // the web app replaces this with the FHIR client
        services.AddSingleton<IXeroApi, NoXeroApi>(); // the web app replaces this with the HTTP client
    }
}

/// <summary>Placeholder until the web app registers the real Xero client.</summary>
public sealed class NoXeroApi : IXeroApi
{
    private static InvalidOperationException Off() => new("The Xero connection is not available in this host.");
    public Task<XeroTokens> RefreshAsync(string clientId, string clientSecret, string refreshToken, CancellationToken ct) => throw Off();
    public Task<string> CreateInvoiceAsync(string accessToken, string tenantId, XeroInvoice invoice, CancellationToken ct) => throw Off();
    public Task<string> CreatePaymentAsync(string accessToken, string tenantId, XeroPayment payment, CancellationToken ct) => throw Off();
    public Task<string?> FindContactAsync(string accessToken, string tenantId, string name, string? email, CancellationToken ct) => throw Off();
    public Task<string> CreateContactAsync(string accessToken, string tenantId, string name, string? email, CancellationToken ct) => throw Off();
    public Task<string> CreateCreditNoteAsync(string accessToken, string tenantId, XeroCreditNote note, CancellationToken ct) => throw Off();
}

/// <summary>Placeholder until the web app registers the Windcave HIT client.</summary>
public sealed class NoTerminalApi : ITerminalApi
{
    public Task<TerminalStart> StartAsync(string url, string user, string key, string station, bool refund, decimal amount, string txnRef, CancellationToken ct) =>
        Task.FromResult(new TerminalStart(false, "The terminal connection is not available in this host."));
    public Task<TerminalPoll> StatusAsync(string url, string user, string key, string station, string txnRef, CancellationToken ct) => Task.FromResult(new TerminalPoll(false, null));
}

public enum FunderKind { Acc = 1, Insurer = 2, HealthNz = 3 }
/// <summary>How claims reach the funder. File: BrightPAS makes the claim file to upload/email; Portal: staff enter it on the funder's site; Api: direct (needs a provider agreement).</summary>
public enum SubmitBy { File = 1, Portal = 2, Api = 3 }
public enum ApprovalStatus { Draft = 1, Requested = 2, Approved = 3, PartApproved = 4, Declined = 5, Expired = 6 }
public enum ClaimStatus { Draft = 1, Ready = 2, Submitted = 3, Accepted = 4, Rejected = 5, PartPaid = 6, Paid = 7, WrittenOff = 8 }
public enum XeroMode { Off = 0, File = 1, Api = 2 }
public enum TerminalProvider { Manual = 1, Simulator = 2, Windcave = 3 }
/// <summary>How BrightPAS talks to a funder's claiming system (0.34.0): off (file/portal), the simulator, or a FHIR R4 Claim API.</summary>
public enum FunderApi { Off = 0, Simulator = 1, Fhir = 2 }
public enum TerminalStatus { Pending = 1, Approved = 2, Declined = 3, Cancelled = 4, Failed = 5 }
public enum IdentityMode { Off = 0, Simulator = 1, Live = 2 }

/// <summary>Health NZ NHI and HPI FHIR APIs (client-credentials). Off: file import only; Simulator: training data.</summary>
public sealed class IdentitySettings
{
    public int Id { get; set; } = 1;
    public IdentityMode Mode { get; set; } = IdentityMode.Off;
    public string? NhiBaseUrl { get; set; }
    public string? HpiBaseUrl { get; set; }
    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecretProtected { get; set; }
    public DateTimeOffset? LastOkAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class Funder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public FunderKind Kind { get; set; }
    public SubmitBy SubmitBy { get; set; } = SubmitBy.File;
    public bool PriorApprovalRequired { get; set; }
    public string? ClaimsEmail { get; set; }
    public string? ProviderNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    // Direct claiming (0.34.0)
    public FunderApi Api { get; set; } = FunderApi.Off;
    public string? ApiUrl { get; set; }
    public string? ApiKeyProtected { get; set; }
    public DateTimeOffset? LastApiAt { get; set; }
    public string? LastApiError { get; set; }
}

/// <summary>Prior approval (insurer pre-approval / ACC approval) for a planned procedure.</summary>
public sealed class PriorApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid PatientId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid FunderId { get; set; }
    public string? MemberNumber { get; set; }
    public string? AccClaimNumber { get; set; }
    public string Procedure { get; set; } = "";
    public decimal AmountRequested { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Draft;
    public string? ApprovalReference { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    /// <summary>The funder's reference when the request went by API (0.34.0).</summary>
    public string? ExternalReference { get; set; }
}

public sealed class Claim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid InvoiceId { get; set; }
    public Guid PatientId { get; set; }
    public Guid FunderId { get; set; }
    public Guid? PriorApprovalId { get; set; }
    public string? MemberNumber { get; set; }
    public string? AccClaimNumber { get; set; }
    public ClaimStatus Status { get; set; } = ClaimStatus.Draft;
    public decimal Amount { get; set; }
    public decimal Paid { get; set; }
    public Guid? BatchId { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTimeOffset? ResponseAt { get; set; }
    public string? ResponseText { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    /// <summary>The funder's claim reference when sent by API (0.34.0).</summary>
    public string? ExternalReference { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public List<ClaimLine> Lines { get; set; } = [];
}

public sealed class ClaimLine
{
    public Guid ClaimId { get; set; }
    public int Sequence { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
}

public sealed class ClaimBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FunderId { get; set; }
    public string FileName { get; set; } = "";
    public int Claims { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed class Remittance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FunderId { get; set; }
    public string FileName { get; set; } = "";
    public string FileHash { get; set; } = "";
    public int LineCount { get; set; }
    public int Matched { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public string ImportedBy { get; set; } = "";
    public List<RemittanceLine> Lines { get; set; } = [];
}

public sealed class RemittanceLine
{
    public Guid RemittanceId { get; set; }
    public int Sequence { get; set; }
    public string ClaimReference { get; set; } = "";
    public decimal Amount { get; set; }
    public string Outcome { get; set; } = "";
    public string? Reason { get; set; }
    public Guid? ClaimId { get; set; }
    public string Result { get; set; } = "";
}

public sealed class XeroSettings
{
    public int Id { get; set; } = 1;
    public XeroMode Mode { get; set; } = XeroMode.Off;
    public string SalesAccountCode { get; set; } = "200";
    public string GstTaxType { get; set; } = "OUTPUT2";
    public string NoGstTaxType { get; set; } = "NONE";
    public string? PaymentAccountCode { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecretProtected { get; set; }
    public string? RefreshTokenProtected { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class XeroExport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public XeroMode Mode { get; set; }
    public int Invoices { get; set; }
    public int Payments { get; set; }
    public string? FileName { get; set; }
    public string? Errors { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

/// <summary>Which BrightPAS invoice or payment went to Xero, when, and its Xero ID (API mode).</summary>
public sealed class XeroLink
{
    public Guid SourceId { get; set; }
    public string SourceType { get; set; } = "";
    public Guid ExportId { get; set; }
    public string? XeroId { get; set; }
    public DateTimeOffset ExportedAt { get; set; }
}

/// <summary>An EFTPOS terminal transaction. No card numbers are ever stored – only the card type and the last four digits the terminal prints.</summary>
public sealed class TerminalTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public TerminalProvider Provider { get; set; }
    public string? TerminalId { get; set; }
    public string MerchantReference { get; set; } = "";
    public TerminalStatus Status { get; set; } = TerminalStatus.Pending;
    public string? AuthCode { get; set; }
    public string? CardType { get; set; }
    public string? Last4 { get; set; }
    public string? ResponseText { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string StartedBy { get; set; } = "";
    public DateTimeOffset? CompletedAt { get; set; }
    public bool PaymentRecorded { get; set; }
    /// <summary>A refund back to the card (0.34.0); RefundOfId is the original purchase.</summary>
    public bool IsRefund { get; set; }
    public Guid? RefundOfId { get; set; }
    public string? Reason { get; set; }
    /// <summary>Amount refunded so far on this purchase.</summary>
    public decimal Refunded { get; set; }
}

/// <summary>A Xero contact matched to a patient or payer (0.34.0), so invoices go to the same contact instead of making duplicates.</summary>
public sealed class XeroContact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>"NAME IN CAPITALS|EMAIL" – how BrightPAS knows it has already matched this contact.</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string XeroContactId { get; set; } = "";
    /// <summary>Matched (an existing Xero contact with the same email or name) or Created by BrightPAS.</summary>
    public string How { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class ClaimsSettings
{
    public int Id { get; set; } = 1;
    public int NextClaim { get; set; } = 1;
    public int NextApproval { get; set; } = 1;
    public TerminalProvider TerminalProvider { get; set; } = TerminalProvider.Manual;
    public string? TerminalId { get; set; }
    // Windcave integrated terminal (0.34.0)
    public string? WindcaveUrl { get; set; }
    public string? WindcaveUser { get; set; }
    public string? WindcaveKeyProtected { get; set; }
}

public sealed class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public const string Schema = "claims";
    public DbSet<Funder> Funders => Set<Funder>();
    public DbSet<PriorApproval> Approvals => Set<PriorApproval>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimLine> ClaimLines => Set<ClaimLine>();
    public DbSet<ClaimBatch> Batches => Set<ClaimBatch>();
    public DbSet<Remittance> Remittances => Set<Remittance>();
    public DbSet<RemittanceLine> RemittanceLines => Set<RemittanceLine>();
    public DbSet<XeroSettings> Xero => Set<XeroSettings>();
    public DbSet<XeroExport> XeroExports => Set<XeroExport>();
    public DbSet<XeroLink> XeroLinks => Set<XeroLink>();
    public DbSet<TerminalTransaction> Terminal => Set<TerminalTransaction>();
    public DbSet<XeroContact> XeroContacts => Set<XeroContact>();
    public DbSet<ClaimsSettings> Settings => Set<ClaimsSettings>();
    public DbSet<IdentitySettings> Identity => Set<IdentitySettings>();

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
        b.Entity<Funder>(e =>
        {
            e.ToTable("Funders"); e.Property(x => x.Code).HasMaxLength(20); e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10); e.Property(x => x.SubmitBy).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.ClaimsEmail).HasMaxLength(200); e.Property(x => x.ProviderNumber).HasMaxLength(50); e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Api).HasConversion<string>().HasMaxLength(10); e.Property(x => x.ApiUrl).HasMaxLength(300); e.Property(x => x.ApiKeyProtected).HasMaxLength(1000); e.Property(x => x.LastApiError).HasMaxLength(500);
        });
        b.Entity<PriorApproval>(e =>
        {
            e.ToTable("PriorApprovals"); e.Property(x => x.Number).HasMaxLength(20); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.MemberNumber).HasMaxLength(50); e.Property(x => x.AccClaimNumber).HasMaxLength(20); e.Property(x => x.Procedure).HasMaxLength(200);
            e.Property(x => x.AmountRequested).HasPrecision(12, 2); e.Property(x => x.ApprovedAmount).HasPrecision(12, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(14); e.Property(x => x.ApprovalReference).HasMaxLength(50); e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.CreatedBy).HasMaxLength(100); e.Property(x => x.DecidedBy).HasMaxLength(100); e.Property(x => x.ExternalReference).HasMaxLength(100);
            e.HasIndex(x => x.PatientId); e.HasIndex(x => x.BookingId); e.HasIndex(x => x.FunderId);
            e.HasOne<Funder>().WithMany().HasForeignKey(x => x.FunderId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Claim>(e =>
        {
            e.ToTable("Claims"); e.Property(x => x.Number).HasMaxLength(20); e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.MemberNumber).HasMaxLength(50); e.Property(x => x.AccClaimNumber).HasMaxLength(20); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Amount).HasPrecision(12, 2); e.Property(x => x.Paid).HasPrecision(12, 2); e.Property(x => x.SubmittedBy).HasMaxLength(100); e.Property(x => x.ResponseText).HasMaxLength(1000); e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.ExternalReference).HasMaxLength(100); e.HasIndex(x => x.ExternalReference);
            e.HasIndex(x => x.InvoiceId).IsUnique().HasFilter("[Status] <> 'WrittenOff'"); e.HasIndex(x => x.PatientId); e.HasIndex(x => x.FunderId); e.HasIndex(x => x.PriorApprovalId); e.HasIndex(x => x.BatchId); e.HasIndex(x => new { x.Status, x.SubmittedAt });
            e.HasOne<Funder>().WithMany().HasForeignKey(x => x.FunderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PriorApproval>().WithMany().HasForeignKey(x => x.PriorApprovalId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ClaimBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ClaimLine>(e => { e.ToTable("ClaimLines"); e.HasKey(x => new { x.ClaimId, x.Sequence }); e.Property(x => x.Code).HasMaxLength(30); e.Property(x => x.Description).HasMaxLength(300); e.Property(x => x.Amount).HasPrecision(12, 2); });
        b.Entity<ClaimBatch>(e =>
        {
            e.ToTable("ClaimBatches"); e.Property(x => x.FileName).HasMaxLength(200); e.Property(x => x.Total).HasPrecision(12, 2); e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.FunderId); e.HasOne<Funder>().WithMany().HasForeignKey(x => x.FunderId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Remittance>(e =>
        {
            e.ToTable("Remittances"); e.Property(x => x.FileName).HasMaxLength(200); e.Property(x => x.FileHash).HasMaxLength(64); e.HasIndex(x => x.FileHash).IsUnique();
            e.Property(x => x.Total).HasPrecision(12, 2); e.Property(x => x.ImportedBy).HasMaxLength(100);
            e.HasIndex(x => x.FunderId); e.HasOne<Funder>().WithMany().HasForeignKey(x => x.FunderId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RemittanceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<RemittanceLine>(e =>
        {
            e.ToTable("RemittanceLines"); e.HasKey(x => new { x.RemittanceId, x.Sequence });
            e.Property(x => x.ClaimReference).HasMaxLength(50); e.Property(x => x.Amount).HasPrecision(12, 2); e.Property(x => x.Outcome).HasMaxLength(20); e.Property(x => x.Reason).HasMaxLength(300); e.Property(x => x.Result).HasMaxLength(200);
            e.HasIndex(x => x.ClaimId); e.HasOne<Claim>().WithMany().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<XeroSettings>(e =>
        {
            e.ToTable("XeroSettings", t => t.HasCheckConstraint("CK_XeroSettings_SingleRow", "[Id] = 1")); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(6); e.Property(x => x.SalesAccountCode).HasMaxLength(10); e.Property(x => x.GstTaxType).HasMaxLength(20); e.Property(x => x.NoGstTaxType).HasMaxLength(20);
            e.Property(x => x.PaymentAccountCode).HasMaxLength(10); e.Property(x => x.TenantId).HasMaxLength(50); e.Property(x => x.ClientId).HasMaxLength(100);
            e.Property(x => x.ClientSecretProtected).HasMaxLength(1000); e.Property(x => x.RefreshTokenProtected).HasMaxLength(2000); e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasData(new XeroSettings());
        });
        b.Entity<XeroExport>(e => { e.ToTable("XeroExports"); e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(6); e.Property(x => x.FileName).HasMaxLength(200); e.Property(x => x.Errors).HasMaxLength(4000); e.Property(x => x.CreatedBy).HasMaxLength(100); });
        b.Entity<XeroLink>(e =>
        {
            e.ToTable("XeroLinks"); e.HasKey(x => new { x.SourceType, x.SourceId }); e.Property(x => x.SourceType).HasMaxLength(10); e.Property(x => x.XeroId).HasMaxLength(50);
            e.HasIndex(x => x.ExportId); e.HasOne<XeroExport>().WithMany().HasForeignKey(x => x.ExportId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TerminalTransaction>(e =>
        {
            e.ToTable("TerminalTransactions"); e.Property(x => x.Amount).HasPrecision(12, 2); e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(10); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.TerminalId).HasMaxLength(30); e.Property(x => x.MerchantReference).HasMaxLength(40); e.HasIndex(x => x.MerchantReference).IsUnique();
            e.Property(x => x.AuthCode).HasMaxLength(20); e.Property(x => x.CardType).HasMaxLength(20); e.Property(x => x.Last4).HasMaxLength(4).IsFixedLength(); e.Property(x => x.ResponseText).HasMaxLength(300); e.Property(x => x.StartedBy).HasMaxLength(100);
            e.HasIndex(x => x.InvoiceId); e.HasIndex(x => x.StartedAt);
            e.Property(x => x.Refunded).HasPrecision(12, 2); e.Property(x => x.Reason).HasMaxLength(300); e.HasIndex(x => x.RefundOfId);
            e.HasOne<TerminalTransaction>().WithMany().HasForeignKey(x => x.RefundOfId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<XeroContact>(e =>
        {
            e.ToTable("XeroContacts"); e.Property(x => x.Key).HasMaxLength(200); e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.XeroContactId).HasMaxLength(50); e.Property(x => x.How).HasMaxLength(10);
        });
        b.Entity<IdentitySettings>(e =>
        {
            e.ToTable("IdentitySettings", t => t.HasCheckConstraint("CK_IdentitySettings_SingleRow", "[Id] = 1")); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(10); e.Property(x => x.NhiBaseUrl).HasMaxLength(300); e.Property(x => x.HpiBaseUrl).HasMaxLength(300);
            e.Property(x => x.TokenUrl).HasMaxLength(300); e.Property(x => x.ClientId).HasMaxLength(100); e.Property(x => x.ClientSecretProtected).HasMaxLength(1000); e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasData(new IdentitySettings());
        });
        b.Entity<ClaimsSettings>(e =>
        {
            e.ToTable("Settings", t => t.HasCheckConstraint("CK_ClaimsSettings_SingleRow", "[Id] = 1")); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TerminalProvider).HasConversion<string>().HasMaxLength(10); e.Property(x => x.TerminalId).HasMaxLength(30);
            e.Property(x => x.WindcaveUrl).HasMaxLength(200); e.Property(x => x.WindcaveUser).HasMaxLength(100); e.Property(x => x.WindcaveKeyProtected).HasMaxLength(1000);
            e.HasData(new ClaimsSettings());
        });
    }
}

public sealed class ClaimsDbContextFactory : IDesignTimeDbContextFactory<ClaimsDbContext>
{
    public ClaimsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ClaimsDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", ClaimsDbContext.Schema)).Options);
}
