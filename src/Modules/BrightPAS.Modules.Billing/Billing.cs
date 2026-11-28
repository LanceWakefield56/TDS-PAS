using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Modules;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Billing;

public sealed class BillingModule : IPasModule
{
    public const string Id = "billing";
    public ModuleManifest Manifest { get; } = new(Id, "Estimates & invoices", typeof(BillingModule).Assembly.GetName().Version ?? new Version(0, 0),
        BillingDbContext.Schema, ["patients"], "Fee schedule, estimates, invoices, payments and fee splits.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<BillingDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, BillingDbContext.Schema));
        services.AddScoped<BillingService>();
    }
}

public sealed class BillingSettings
{
    public int Id { get; set; } = 1;
    public decimal SurgeonPercent { get; set; } = 40m;
    public decimal AnaesthetistPercent { get; set; } = 20m;
    public decimal GstPercent { get; set; } = 15m;
    public int EstimateValidDays { get; set; } = 30;
    public int PaymentTermsDays { get; set; } = 20;
    public string? GstNumber { get; set; }
    public string? BankAccount { get; set; }
    public int NextEstimateNumber { get; set; } = 1;
    public int NextInvoiceNumber { get; set; } = 1;
}

public sealed class FeeItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public bool GstApplicable { get; set; } = true;
    /// <summary>Procedure fees are split between surgeon, anaesthetist and the facility; consumables and facility fees are not.</summary>
    public bool Split { get; set; }
    /// <summary>Optional per-item split overriding the default percentages.</summary>
    public decimal? SurgeonPercent { get; set; }
    public decimal? AnaesthetistPercent { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum DocumentKind { Estimate = 1, Invoice = 2 }
public enum DocumentStatus { Draft = 1, Issued = 2, Accepted = 3, Declined = 4, Expired = 5, Invoiced = 6, PartPaid = 7, Paid = 8, Void = 9 }
public enum Payer { SelfFunded = 1, Insurer = 2, Acc = 3, HealthNz = 4, Other = 5 }
public enum PaymentMethod { Eftpos = 1, CreditCard = 2, BankTransfer = 3, Insurer = 4, Acc = 5, Cash = 6, Other = 7 }

/// <summary>An estimate or an invoice.</summary>
public sealed class BillingDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DocumentKind Kind { get; set; }
    public string? Number { get; set; }
    public Guid PatientId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? SourceEstimateId { get; set; }
    public Payer Payer { get; set; }
    public string? PayerName { get; set; }
    public string? PayerReference { get; set; }
    public string? Surgeon { get; set; }
    public string? Anaesthetist { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public decimal GstPercent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateOnly? IssuedOn { get; set; }
    /// <summary>Estimate: valid until. Invoice: due date.</summary>
    public DateOnly? DueOn { get; set; }
    public string? Notes { get; set; }
    public string? VoidReason { get; set; }
    public List<BillingLine> Lines { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}

public sealed class BillingLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public int Sequence { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public bool GstApplicable { get; set; } = true;
    public decimal SurgeonPercent { get; set; }
    public decimal AnaesthetistPercent { get; set; }
}

public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public DateOnly PaidOn { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string RecordedBy { get; set; } = "";
}

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public const string Schema = "billing";
    public DbSet<BillingSettings> Settings => Set<BillingSettings>();
    public DbSet<FeeItem> Fees => Set<FeeItem>();
    public DbSet<BillingDocument> Documents => Set<BillingDocument>();
    public DbSet<BillingLine> Lines => Set<BillingLine>();
    public DbSet<Payment> Payments => Set<Payment>();

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
        // 0.10.1: indexes on lookup columns and referential integrity within the schema.
        b.Entity<BillingDocument>().HasIndex(x => x.BookingId);
        b.Entity<BillingDocument>().HasOne<BillingDocument>().WithMany().HasForeignKey(x => x.SourceEstimateId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<BillingSettings>(e =>
        {
            e.ToTable("Settings", t => t.HasCheckConstraint("CK_Settings_SingleRow", "[Id] = 1"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SurgeonPercent).HasPrecision(5, 2);
            e.Property(x => x.AnaesthetistPercent).HasPrecision(5, 2);
            e.Property(x => x.GstPercent).HasPrecision(5, 2);
            e.Property(x => x.GstNumber).HasMaxLength(20);
            e.Property(x => x.BankAccount).HasMaxLength(30);
            e.Property(x => x.NextEstimateNumber).IsConcurrencyToken();
            e.Property(x => x.NextInvoiceNumber).IsConcurrencyToken();
            e.HasData(new BillingSettings());
        });
        b.Entity<FeeItem>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.SurgeonPercent).HasPrecision(5, 2);
            e.Property(x => x.AnaesthetistPercent).HasPrecision(5, 2);
        });
        b.Entity<BillingDocument>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Payer).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            e.Property(x => x.PayerName).HasMaxLength(100);
            e.Property(x => x.PayerReference).HasMaxLength(50);
            e.Property(x => x.Surgeon).HasMaxLength(150);
            e.Property(x => x.Anaesthetist).HasMaxLength(150);
            e.Property(x => x.GstPercent).HasPrecision(5, 2);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.VoidReason).HasMaxLength(300);
            e.HasIndex(x => x.PatientId);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<BillingLine>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.Quantity).HasPrecision(9, 2);
            e.Property(x => x.SurgeonPercent).HasPrecision(5, 2);
            e.Property(x => x.AnaesthetistPercent).HasPrecision(5, 2);
        });
        b.Entity<Payment>(e =>
        {
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.Reference).HasMaxLength(50);
            e.Property(x => x.RecordedBy).HasMaxLength(100);
        });
    }
}

public sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<BillingDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", BillingDbContext.Schema)).Options);
}

public sealed record Totals(decimal Subtotal, decimal Gst, decimal Total, decimal Paid, decimal Balance, decimal SurgeonShare, decimal AnaesthetistShare, decimal FacilityShare);
public sealed record LineInput(string? Code, string? Description, decimal Quantity, decimal? UnitPrice);
public sealed record NewDocument(Guid PatientId, Payer Payer, string? PayerName, string? PayerReference, string? Surgeon, string? Anaesthetist,
    string? Notes, IReadOnlyList<LineInput> Lines, Guid? BookingId = null);
public sealed record NewPayment(DateOnly PaidOn, decimal Amount, PaymentMethod Method, string? Reference);

public sealed class BillingService(BillingDbContext db, PatientsDbContext patients, AuditWriter audit, TimeProvider clock)
{
    public static decimal Money(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static Totals Calculate(BillingDocument d)
    {
        decimal sub = 0, gst = 0, surgeon = 0, anaes = 0;
        foreach (var l in d.Lines)
        {
            var net = Money(l.Quantity * l.UnitPrice);
            sub += net;
            if (l.GstApplicable) gst += Money(net * d.GstPercent / 100m);
            surgeon += Money(net * l.SurgeonPercent / 100m);
            anaes += Money(net * l.AnaesthetistPercent / 100m);
        }
        var paid = d.Payments.Sum(p => p.Amount);
        var total = sub + gst;
        return new Totals(sub, gst, total, paid, total - paid, surgeon, anaes, sub - surgeon - anaes);
    }

    public async Task<BillingSettings> SettingsAsync(CancellationToken ct = default) =>
        await db.Settings.SingleOrDefaultAsync(s => s.Id == 1, ct) ?? (await Seed(ct));

    private async Task<BillingSettings> Seed(CancellationToken ct)
    {
        var s = new BillingSettings();
        db.Settings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<string?> UpdateSettingsAsync(decimal surgeon, decimal anaesthetist, decimal gst, int validDays, int termsDays, string? gstNumber, string? bank, string user, CancellationToken ct = default)
    {
        if (surgeon < 0 || anaesthetist < 0 || surgeon + anaesthetist > 100) return "The surgeon and anaesthetist percentages must add up to 100 or less.";
        if (gst is < 0 or > 50) return "Check the GST rate.";
        var s = await SettingsAsync(ct);
        (s.SurgeonPercent, s.AnaesthetistPercent, s.GstPercent, s.EstimateValidDays, s.PaymentTermsDays, s.GstNumber, s.BankAccount) =
            (surgeon, anaesthetist, gst, Math.Clamp(validDays, 1, 365), Math.Clamp(termsDays, 0, 180), Blank(gstNumber), Blank(bank));
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "BillingSettings", "1", reason: $"Split {surgeon}/{anaesthetist}/rest; GST {gst}%", ct: ct);
        return null;
    }

    public Task<List<FeeItem>> FeesAsync(bool activeOnly, CancellationToken ct = default) =>
        db.Fees.AsNoTracking().Where(f => !activeOnly || f.IsActive).OrderBy(f => f.Code).ToListAsync(ct);

    public async Task<string?> SaveFeeAsync(FeeItem input, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Description)) return "Enter a code and description.";
        if (input.Price < 0) return "The price cannot be negative.";
        if ((input.SurgeonPercent ?? 0) + (input.AnaesthetistPercent ?? 0) > 100) return "The split percentages add up to more than 100.";
        var code = input.Code.Trim().ToUpperInvariant();
        var f = await db.Fees.SingleOrDefaultAsync(x => x.Id == input.Id, ct);
        if (await db.Fees.AnyAsync(x => x.Code == code && x.Id != input.Id, ct)) return $"Code {code} is already used.";
        if (f is null) { f = new FeeItem { Id = input.Id == Guid.Empty ? Guid.NewGuid() : input.Id }; db.Fees.Add(f); }
        (f.Code, f.Description, f.Price, f.GstApplicable, f.Split, f.SurgeonPercent, f.AnaesthetistPercent, f.IsActive) =
            (code, input.Description.Trim(), Money(input.Price), input.GstApplicable, input.Split, input.SurgeonPercent, input.AnaesthetistPercent, input.IsActive);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Save", "FeeItem", f.Id.ToString(), reason: $"{f.Code} {f.Description} ${f.Price}", ct: ct);
        return null;
    }

    /// <summary>Example fee schedule for the sandbox only – clearly marked so it is replaced with TDS's real prices.</summary>
    public async Task SeedExampleFeesAsync(CancellationToken ct = default)
    {
        if (await db.Fees.AnyAsync(ct)) return;
        db.Fees.AddRange(
            new FeeItem { Code = "FAC-DAY", Description = "Facility fee – day stay (EXAMPLE PRICE)", Price = 650m },
            new FeeItem { Code = "FAC-TH30", Description = "Theatre time per 30 minutes (EXAMPLE PRICE)", Price = 480m },
            new FeeItem { Code = "COLON", Description = "Colonoscopy – procedure fee (EXAMPLE PRICE)", Price = 1450m, Split = true },
            new FeeItem { Code = "GASTRO", Description = "Gastroscopy – procedure fee (EXAMPLE PRICE)", Price = 950m, Split = true },
            new FeeItem { Code = "CTR", Description = "Carpal tunnel release – procedure fee (EXAMPLE PRICE)", Price = 1800m, Split = true },
            new FeeItem { Code = "HERNIA", Description = "Inguinal hernia repair – procedure fee (EXAMPLE PRICE)", Price = 3200m, Split = true },
            new FeeItem { Code = "HISTO", Description = "Histology (EXAMPLE PRICE)", Price = 180m },
            new FeeItem { Code = "CONS", Description = "Consumables and dressings (EXAMPLE PRICE)", Price = 95m });
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<BillingLine>> BuildLines(IReadOnlyList<LineInput> input, BillingSettings s, List<string> errors, CancellationToken ct)
    {
        var fees = await db.Fees.AsNoTracking().ToDictionaryAsync(f => f.Code, ct);
        var lines = new List<BillingLine>();
        foreach (var (l, i) in input.Select((l, i) => (l, i)))
        {
            if (l.Quantity <= 0 || l.Quantity > 1000) { errors.Add($"Line {i + 1}: check the quantity."); continue; }
            var code = l.Code?.Trim().ToUpperInvariant() ?? "";
            if (code.Length > 0 && fees.TryGetValue(code, out var f))
            {
                if (!f.IsActive) { errors.Add($"Line {i + 1}: {code} is no longer used."); continue; }
                lines.Add(new BillingLine
                {
                    Sequence = i + 1,
                    Code = f.Code,
                    Description = string.IsNullOrWhiteSpace(l.Description) ? f.Description : l.Description.Trim(),
                    Quantity = l.Quantity,
                    UnitPrice = Money(l.UnitPrice ?? f.Price),
                    GstApplicable = f.GstApplicable,
                    SurgeonPercent = f.Split ? f.SurgeonPercent ?? s.SurgeonPercent : 0,
                    AnaesthetistPercent = f.Split ? f.AnaesthetistPercent ?? s.AnaesthetistPercent : 0
                });
            }
            else if (code.Length == 0 && !string.IsNullOrWhiteSpace(l.Description) && l.UnitPrice is not null)
                lines.Add(new BillingLine { Sequence = i + 1, Code = "", Description = l.Description.Trim(), Quantity = l.Quantity, UnitPrice = Money(l.UnitPrice.Value) });
            else errors.Add($"Line {i + 1}: choose a fee item, or enter a description and price.");
        }
        if (lines.Count == 0 && errors.Count == 0) errors.Add("Add at least one line.");
        return lines;
    }

    public async Task<(BillingDocument? Doc, IReadOnlyList<string> Errors)> CreateAsync(DocumentKind kind, NewDocument r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!await patients.Patients.AnyAsync(p => p.Id == r.PatientId, ct)) errors.Add("Patient not found.");
        if (r.Payer == Payer.Insurer && string.IsNullOrWhiteSpace(r.PayerName)) errors.Add("Enter the insurer's name.");
        var s = await SettingsAsync(ct);
        var lines = await BuildLines(r.Lines, s, errors, ct);
        if (errors.Count > 0) return (null, errors);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var d = new BillingDocument
        {
            Kind = kind,
            PatientId = r.PatientId,
            BookingId = r.BookingId,
            Payer = r.Payer,
            PayerName = Blank(r.PayerName),
            PayerReference = Blank(r.PayerReference),
            Surgeon = Blank(r.Surgeon),
            Anaesthetist = Blank(r.Anaesthetist),
            Notes = Blank(r.Notes),
            GstPercent = s.GstPercent,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user,
            Lines = lines
        };
        if (kind == DocumentKind.Estimate) { d.Number = $"EST-{s.NextEstimateNumber++:D6}"; d.DueOn = today.AddDays(s.EstimateValidDays); }
        db.Documents.Add(d);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", kind.ToString(), d.Id.ToString(), r.PatientId.ToString(), reason: $"{d.Number ?? "draft"} total {Calculate(d).Total:0.00}", ct: ct);
        return (d, []);
    }

    public async Task<(BillingDocument? Doc, string? Error)> InvoiceFromEstimateAsync(Guid estimateId, string user, CancellationToken ct = default)
    {
        var e = await db.Documents.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == estimateId && x.Kind == DocumentKind.Estimate, ct);
        if (e is null) return (null, "Estimate not found.");
        if (e.Status is DocumentStatus.Invoiced or DocumentStatus.Declined) return (null, $"This estimate is {e.Status.ToString().ToLowerInvariant()}.");
        var inv = new BillingDocument
        {
            Kind = DocumentKind.Invoice,
            PatientId = e.PatientId,
            BookingId = e.BookingId,
            SourceEstimateId = e.Id,
            Payer = e.Payer,
            PayerName = e.PayerName,
            PayerReference = e.PayerReference,
            Surgeon = e.Surgeon,
            Anaesthetist = e.Anaesthetist,
            GstPercent = e.GstPercent,
            Notes = e.Notes,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user,
            Lines = e.Lines.OrderBy(l => l.Sequence).Select(l => new BillingLine
            {
                Sequence = l.Sequence,
                Code = l.Code,
                Description = l.Description,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                GstApplicable = l.GstApplicable,
                SurgeonPercent = l.SurgeonPercent,
                AnaesthetistPercent = l.AnaesthetistPercent
            }).ToList()
        };
        e.Status = DocumentStatus.Invoiced;
        db.Documents.Add(inv);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "Invoice", inv.Id.ToString(), inv.PatientId.ToString(), reason: $"From estimate {e.Number}", ct: ct);
        return (inv, null);
    }

    public async Task<string?> SetStatusAsync(Guid id, DocumentStatus status, string? reason, string user, CancellationToken ct = default)
    {
        var d = await db.Documents.Include(x => x.Payments).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return "Not found.";
        var s = await SettingsAsync(ct);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var ok = (d.Kind, d.Status, status) switch
        {
            (_, DocumentStatus.Draft, DocumentStatus.Issued) => true,
            (DocumentKind.Estimate, DocumentStatus.Issued, DocumentStatus.Accepted or DocumentStatus.Declined) => true,
            (DocumentKind.Invoice, DocumentStatus.Draft or DocumentStatus.Issued, DocumentStatus.Void) => d.Payments.Count == 0,
            (DocumentKind.Estimate, DocumentStatus.Draft or DocumentStatus.Issued or DocumentStatus.Accepted, DocumentStatus.Void) => true,
            _ => false
        };
        if (!ok) return d.Payments.Count > 0 && status == DocumentStatus.Void ? "An invoice with payments cannot be voided. Record a refund instead." : $"A {d.Status.ToString().ToLowerInvariant()} {d.Kind.ToString().ToLowerInvariant()} cannot be marked {status.ToString().ToLowerInvariant()}.";
        if (status == DocumentStatus.Void && string.IsNullOrWhiteSpace(reason)) return "Enter the reason.";
        if (status == DocumentStatus.Issued)
        {
            d.IssuedOn = today;
            if (d.Kind == DocumentKind.Invoice) { d.Number ??= $"INV-{s.NextInvoiceNumber++:D6}"; d.DueOn = today.AddDays(s.PaymentTermsDays); }
            else d.DueOn = today.AddDays(s.EstimateValidDays);
        }
        if (status == DocumentStatus.Void) d.VoidReason = reason!.Trim();
        d.Status = status;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "StatusChange", d.Kind.ToString(), d.Id.ToString(), d.PatientId.ToString(), reason: $"{d.Number} -> {status}{(reason is null ? "" : ": " + reason)}", ct: ct);
        return null;
    }

    public async Task<string?> RecordPaymentAsync(Guid invoiceId, NewPayment p, string user, CancellationToken ct = default)
    {
        var d = await db.Documents.Include(x => x.Payments).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == invoiceId && x.Kind == DocumentKind.Invoice, ct);
        if (d is null) return "Invoice not found.";
        if (d.Status is not (DocumentStatus.Issued or DocumentStatus.PartPaid)) return "Issue the invoice before recording payments.";
        if (p.Amount <= 0) return "Enter the amount paid.";
        var bal = Calculate(d).Balance;
        if (Money(p.Amount) > bal) return $"The payment is more than the balance owing ({bal:0.00}).";
        var payment = new Payment { DocumentId = d.Id, PaidOn = p.PaidOn, Amount = Money(p.Amount), Method = p.Method, Reference = Blank(p.Reference), RecordedAt = clock.GetUtcNow(), RecordedBy = user };
        db.Payments.Add(payment);
        d.Status = Calculate(d).Balance == 0 ? DocumentStatus.Paid : DocumentStatus.PartPaid;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Payment", "Invoice", d.Id.ToString(), d.PatientId.ToString(), reason: $"{d.Number} {p.Method} {p.Amount:0.00}", ct: ct);
        return null;
    }

    /// <summary>Money given back on an invoice (0.32.0, e.g. an online card refund): a negative payment. It cannot be more than has been paid.</summary>
    public async Task<string?> RecordRefundAsync(Guid invoiceId, decimal amount, PaymentMethod method, string? reference, string user, CancellationToken ct = default)
    {
        var d = await db.Documents.Include(x => x.Payments).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == invoiceId && x.Kind == DocumentKind.Invoice, ct);
        if (d is null) return "Invoice not found.";
        if (d.Status is not (DocumentStatus.Issued or DocumentStatus.PartPaid or DocumentStatus.Paid)) return "Refunds can only be recorded on an issued invoice.";
        amount = Money(amount);
        if (amount <= 0) return "Enter the amount refunded.";
        var paid = Calculate(d).Paid;
        if (amount > paid) return $"The refund is more than has been paid ({paid:0.00}).";
        db.Payments.Add(new Payment { DocumentId = d.Id, PaidOn = NzTime.Today(clock), Amount = -amount, Method = method, Reference = Blank(reference), RecordedAt = clock.GetUtcNow(), RecordedBy = user });
        await db.SaveChangesAsync(ct);
        var t = Calculate(d);
        d.Status = t.Balance == 0 ? DocumentStatus.Paid : t.Paid > 0 ? DocumentStatus.PartPaid : DocumentStatus.Issued;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Refund", "Invoice", d.Id.ToString(), d.PatientId.ToString(), reason: $"{d.Number} {method} -{amount:0.00}", ct: ct);
        return null;
    }

    public async Task<BillingDocument?> GetAsync(Guid id, string user, string? ws, CancellationToken ct = default)
    {
        var d = await db.Documents.AsNoTracking().Include(x => x.Lines).Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (d is not null)
        {
            d.Lines = [.. d.Lines.OrderBy(l => l.Sequence)];
            await audit.WriteAsync(user, "View", d.Kind.ToString(), d.Id.ToString(), d.PatientId.ToString(), workstation: ws, ct: ct);
        }
        return d;
    }

    public async Task<List<BillingDocument>> ListAsync(DocumentKind? kind, Guid? patientId, bool outstandingOnly, CancellationToken ct = default)
    {
        var q = db.Documents.AsNoTracking().Include(x => x.Lines).Include(x => x.Payments).AsQueryable();
        if (kind is not null) q = q.Where(d => d.Kind == kind);
        if (patientId is not null) q = q.Where(d => d.PatientId == patientId);
        if (outstandingOnly) q = q.Where(d => d.Status == DocumentStatus.Issued || d.Status == DocumentStatus.PartPaid);
        return await q.OrderByDescending(d => d.CreatedAt).Take(300).ToListAsync(ct);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
