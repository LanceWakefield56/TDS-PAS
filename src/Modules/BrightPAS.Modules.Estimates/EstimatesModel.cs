using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Estimates;

/// <summary>
/// Procedure estimates (0.21.0, ADR 0034): TDS's patient cost-estimate builder, moved in from the standalone "TDS Estimates" app (v1.5).
/// Procedure price list per specialty (facility, anaesthetist, consumables, special package prices), surgeon fees per procedure and surgeon,
/// add-ons ("surgeon" add-ons add to the surgeon fee, "all-in" add-ons print as their own line; add-ons already included in a procedure can't
/// be charged twice), extended theatre time, "if required" lines, templates, warnings, the printed estimate (PDF), email with a hold period
/// and right-patient checks, the Incisive scans export and the pricing/stock spreadsheet imports with scheduled price changes.
/// </summary>
public sealed class EstimatesModule : IPasModule
{
    public const string Id = "estimates";
    public ModuleManifest Manifest { get; } = new(Id, "Procedure estimates", typeof(EstimatesModule).Assembly.GetName().Version ?? new Version(0, 0),
        EstDbContext.Schema, ["patients"], "Patient cost estimates from the procedure price list, surgeon fees and add-ons; PDF, email and Incisive export.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<EstDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, EstDbContext.Schema));
        services.AddScoped<EstimateService>();
        services.AddScoped<EstimateCatalogue>();
        services.AddScoped<EstimateImport>();
        services.AddScoped<EstimateMail>();
        services.AddScoped<EstimateFollowUpService>();
    }
}

// ------------------------------------------------------------------ enums

/// <summary>Anaesthetic choice. Only a general anaesthetic or sedation by an anaesthetist charges an anaesthetist.</summary>
public enum Anaesthetic { General = 1, SedationAnaesthetist = 2, SedationConsultant = 3, Local = 4, None = 5 }

public enum Funding { SelfFunding = 1, Insurance = 2, NotYetKnown = 3 }

/// <summary>Surgeon add-ons add to the surgeon fee (printed as "Add-on – name" under it); all-in add-ons print as their own line.</summary>
public enum AddOnType { Surgeon = 1, AllIn = 2 }

public enum EstimateStatus { Draft = 1, Issued = 2, Cancelled = 3 }

/// <summary>Where an issued estimate is up to with the patient (0.33.0, ADR 0046).</summary>
public enum EstFollowUp { Waiting = 1, Accepted = 2, Declined = 3, ApprovalRequested = 4, Approved = 5, ApprovalDeclined = 6, DepositPaid = 7, Booked = 8, Invoiced = 9, Expired = 10 }

public enum LineKind { Fee = 1, IfRequired = 2 }

public enum Recipient { Patient = 1, Insurer = 2, Other = 3 }

/// <summary>Password on an emailed PDF. The email states the rule, never the password.</summary>
public enum PdfPassword { DobShort = 1, DobLong = 2, Nhi = 3, Typed = 4, None = 5 }

public enum SendMode { OutlookDraft = 1, SendFromApp = 2 }

public enum OutboxStatus { Held = 1, Sent = 2, Stopped = 3, Failed = 4 }

public static class Anaesthetics
{
    public static readonly IReadOnlyDictionary<Anaesthetic, string> Labels = new Dictionary<Anaesthetic, string>
    {
        [Anaesthetic.General] = "General anaesthetic",
        [Anaesthetic.SedationAnaesthetist] = "Sedation by an anaesthetist",
        [Anaesthetic.SedationConsultant] = "Sedation by the consultant",
        [Anaesthetic.Local] = "Local anaesthetic only",
        [Anaesthetic.None] = "No anaesthetic required",
    };

    public static bool HasAnaesthetist(Anaesthetic a) => a is Anaesthetic.General or Anaesthetic.SedationAnaesthetist;

    /// <summary>Words after the procedure title on the printed estimate.</summary>
    public static string Phrase(Anaesthetic a) => a switch
    {
        Anaesthetic.General => "under General Anaesthetic",
        Anaesthetic.SedationAnaesthetist => "with Sedation by an Anaesthetist",
        Anaesthetic.SedationConsultant => "with Sedation by the Consultant",
        Anaesthetic.Local => "under Local Anaesthetic",
        _ => ""
    };

    /// <summary>Reads the spreadsheet wording ("General anaesthetic", "GA", "Sedation by an anaesthetist" …).</summary>
    public static Anaesthetic? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Anaesthetic.General;
        var t = text.Trim();
        foreach (var (k, v) in Labels) if (string.Equals(v, t, StringComparison.OrdinalIgnoreCase) || string.Equals(k.ToString(), t, StringComparison.OrdinalIgnoreCase)) return k;
        return t.ToUpperInvariant() switch { "GA" => Anaesthetic.General, "LA" => Anaesthetic.Local, _ => null };
    }
}

// ------------------------------------------------------------------ price list

public sealed class EstSpecialty
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>Printed notes put on every new estimate in this specialty (e.g. infusion medication is extra).</summary>
    public string? DefaultNotes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class EstProcedure
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Unique and permanent, e.g. GYN-001, END-001.</summary>
    public string Code { get; set; } = "";
    public Guid SpecialtyId { get; set; }
    public string Group { get; set; } = "";
    public string Name { get; set; } = "";
    public string PrintTitle { get; set; } = "";
    public int Minutes { get; set; }
    public Anaesthetic DefaultAnaesthetic { get; set; } = Anaesthetic.General;
    public decimal? Facility { get; set; }
    public decimal? FacilitySpecial { get; set; }
    public decimal? Anaesthetist { get; set; }
    public decimal? AnaesthetistSpecial { get; set; }
    /// <summary>Printed "Consumables &amp; Equipment" line.</summary>
    public decimal? ConsumablesLine { get; set; }
    /// <summary>Comma-separated add-on codes already included in this procedure's fees (they are hidden so they can't be charged twice).</summary>
    public string? IncludedAddOnCodes { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Source { get; set; }

    public IEnumerable<string> IncludedCodes() =>
        (IncludedAddOnCodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Surgeon on the price list (the people who appear on estimates). Optionally linked to a BrightPAS clinician.</summary>
public sealed class EstSurgeon
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid? ClinicianId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class EstSurgeonSpecialty
{
    public Guid SurgeonId { get; set; }
    public Guid SpecialtyId { get; set; }
}

/// <summary>Surgeon fee for a procedure. No surgeon = "All surgeons" (the default).</summary>
public sealed class EstSurgeonFee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProcedureId { get; set; }
    public Guid? SurgeonId { get; set; }
    public decimal? Fee { get; set; }
    public decimal? SpecialFee { get; set; }
    public string? Notes { get; set; }
}

public sealed class EstAddOn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public Guid SpecialtyId { get; set; }
    /// <summary>Procedure group it applies to; blank = the whole specialty.</summary>
    public string? Group { get; set; }
    public string Name { get; set; } = "";
    public decimal Fee { get; set; }
    public AddOnType Type { get; set; } = AddOnType.AllIn;
    /// <summary>Only for this surgeon (blank = any).</summary>
    public Guid? SurgeonId { get; set; }
    public string? Source { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Supplier price list item (internal costing only – never printed).</summary>
public sealed class EstStockItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Supplier { get; set; } = "";
    public string? Reference { get; set; }
    public string Name { get; set; } = "";
    public decimal? PackPrice { get; set; }
    public decimal? UnitsPerPack { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? InStockQty { get; set; }
    public decimal? NeededInHouse { get; set; }
    public string? WhereUsed { get; set; }
    public string? Issues { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Internal costing line for a procedure (not printed).</summary>
public sealed class EstProcedureConsumable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProcedureId { get; set; }
    public Guid? StockItemId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    /// <summary>Manual unit cost (ex GST) when no stock item is linked.</summary>
    public decimal? UnitCost { get; set; }
}

/// <summary>A price change imported with a future "effective from" date; applied on that date.</summary>
public sealed class EstPendingPrice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProcedureId { get; set; }
    public string Field { get; set; } = "";
    public decimal? NewValue { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}

// ------------------------------------------------------------------ estimates

public sealed class Estimate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>EST-yymmdd-NHI, or the last name in capitals when there is no NHI; -2, -3 … when needed.</summary>
    public string Ref { get; set; } = "";
    public DateOnly EstimateDate { get; set; }
    public EstimateStatus Status { get; set; } = EstimateStatus.Draft;
    /// <summary>BrightPAS patient when the estimate was started from (or linked to) a patient record. Patient details below are a snapshot.</summary>
    public Guid? PatientId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateOnly? DateOfBirth { get; set; }
    public string? Nhi { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Town { get; set; }
    public string? Postcode { get; set; }
    public Funding Funding { get; set; } = Funding.SelfFunding;
    public string? Insurer { get; set; }
    public string? PolicyNumber { get; set; }
    public string? PriorApproval { get; set; }

    public Guid? ProcedureId { get; set; }
    public string? ProcedureCode { get; set; }
    public string? SpecialtyName { get; set; }
    public string? ProcedureName { get; set; }
    public string? Title { get; set; }
    public Guid? SurgeonId { get; set; }
    public string? SurgeonName { get; set; }
    public DateOnly? ProcedureDate { get; set; }
    public Anaesthetic Anaesthetic { get; set; } = Anaesthetic.General;
    public int? PlannedMinutes { get; set; }
    public bool UseSpecial { get; set; }
    /// <summary>Whole-estimate multiplier (100% or 200%).</summary>
    public decimal Multiplier { get; set; } = 1m;
    public decimal? FacilityOverride { get; set; }
    public decimal? SurgeonOverride { get; set; }
    public decimal? AnaesthetistOverride { get; set; }
    /// <summary>JSON of <see cref="EstimateInputs"/>: add-ons, other charges, adjustment and "if required" lines.</summary>
    public string? InputsJson { get; set; }
    public string? Notes { get; set; }
    public string? PreparedBy { get; set; }

    public decimal Subtotal { get; set; }
    public decimal Deposit { get; set; }
    public decimal Balance { get; set; }
    public DateOnly? ValidUntil { get; set; }

    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? EmailedAt { get; set; }
    public DateTimeOffset? SentToIncisiveAt { get; set; }
    public string? CancelReason { get; set; }
    // Follow-up (0.33.0)
    public EstFollowUp? FollowUp { get; set; }
    public DateTimeOffset? FollowUpAt { get; set; }
    public string? FollowUpBy { get; set; }
    /// <summary>When staff should next chase the patient or insurer (blank = the default reminder days after issue).</summary>
    public DateOnly? NextActionOn { get; set; }
    public string? DeclineReason { get; set; }
    public decimal? DepositPaid { get; set; }
    public DateOnly? DepositPaidOn { get; set; }
    public Guid? BookingId { get; set; }
    /// <summary>The Billing invoice made from this estimate.</summary>
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public List<EstimateLine> Lines { get; set; } = [];
}

/// <summary>Each follow-up step on an estimate (who, when, what, note) – the estimate's history.</summary>
public sealed class EstimateEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EstimateId { get; set; }
    public EstFollowUp Status { get; set; }
    public DateTimeOffset At { get; set; }
    public string By { get; set; } = "";
    public string? Note { get; set; }
}

/// <summary>Snapshot of each printed line so an old estimate always reprints exactly as issued.</summary>
public sealed class EstimateLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EstimateId { get; set; }
    public LineKind Kind { get; set; }
    public int Sequence { get; set; }
    public string Label { get; set; } = "";
    public decimal? Amount { get; set; }
    /// <summary>Text printed instead of the amount (e.g. "To be advised").</summary>
    public string? DisplayText { get; set; }
    public string? Note { get; set; }
}

/// <summary>PDF kept when an estimate is issued, and again ("_updated") whenever an issued estimate changes. Never overwritten. Encrypted at rest.</summary>
public sealed class EstimateArchive
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EstimateId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public string Reason { get; set; } = "";
    public string FileName { get; set; } = "";
    public byte[] PdfCipher { get; set; } = [];
}

/// <summary>An email held for the hold period before sending, so it can be stopped. The message (with its PDF) is encrypted at rest.</summary>
public sealed class EstOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EstimateId { get; set; }
    public string EstimateRef { get; set; } = "";
    public string PatientName { get; set; } = "";
    public Recipient Recipient { get; set; }
    public string ToAddress { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string AttachmentName { get; set; } = "";
    public byte[] AttachmentCipher { get; set; } = [];
    public bool PasswordProtected { get; set; }
    public OutboxStatus Status { get; set; } = OutboxStatus.Held;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset SendAfter { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public string? StoppedBy { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
}

/// <summary>A ready-made estimate set-up staff can start from in one click.</summary>
public sealed class EstTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid ProcedureId { get; set; }
    public Guid? SurgeonId { get; set; }
    public Anaesthetic? Anaesthetic { get; set; }
    public int? PlannedMinutes { get; set; }
    public bool UseSpecial { get; set; }
    public decimal? FacilityOverride { get; set; }
    public decimal? SurgeonOverride { get; set; }
    public decimal? AnaesthetistOverride { get; set; }
    public string? InputsJson { get; set; }
    public string? Notes { get; set; }
    public bool Visible { get; set; } = true;
    public bool Favourite { get; set; }
    public int SortOrder { get; set; }
    public int UsedCount { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Surgeons who see a template (none listed = everyone).</summary>
public sealed class EstTemplateSurgeon
{
    public Guid TemplateId { get; set; }
    public Guid SurgeonId { get; set; }
}

/// <summary>Module settings (one row): rates, terms and wording, email safety, exports and costing. Changed in Estimates › Admin › Settings &amp; wording.</summary>
public sealed class EstSettings
{
    public int Id { get; set; } = 1;
    // Rates
    public decimal GstRate { get; set; } = 0.15m;
    /// <summary>Supplied-price charge-out: price × (1 + loading) × (1 + markup) × (1 + GST).</summary>
    public decimal Loading { get; set; } = 0.15m;
    public decimal Markup { get; set; } = 0.25m;
    /// <summary>Extended theatre time per started 15 minutes over the scheduled time (GST incl).</summary>
    public decimal ExtendedTimePer15 { get; set; } = 90m;
    // Terms
    public int ValidityMonths { get; set; } = 3;
    public decimal DepositPercent { get; set; } = 50m;
    public int DepositDaysBefore { get; set; } = 14;
    public int BalanceDaysAfter { get; set; } = 14;
    public string SurgeonFeeLabel { get; set; } = "Consultant Fee";
    // Contact (printed)
    public string BankAccount { get; set; } = "03-0703-0046726-000";
    public string Phone { get; set; } = "0800 827 329";
    public string Email { get; set; } = "enquiries@tasmandaysurgery.co.nz";
    public string Address { get; set; } = "341 Lower Queen Street, Richmond, Tasman 7020";
    // Wording (tokens: {validity} {validuntil} {phone} {deposit} {balancedays} {ref} {first} {last} {procedure} {preparedby} {insurer} {policy})
    public string Terms { get; set; } = DefaultTerms;
    public string SelfFundingText { get; set; } = "If you are self-funding, please indicate that you accept the estimate by return email. A payment of {deposit}% of the estimate is required at least two weeks prior to the procedure and the balance must be paid within {balancedays} days of the surgery.";
    public string InsuranceText { get; set; } = "If you are funding via insurance, please forward this estimate to your provider, and email back their confirmed acceptance of the estimate. If your policy includes a shortfall or excess, the outstanding amount must be paid in full prior to the procedure.";
    public string AcceptanceText { get; set; } = "Email acceptance of this estimate is required prior to confirmation of your booking.";
    // Email
    public SendMode SendMode { get; set; } = SendMode.OutlookDraft;
    public int HoldMinutes { get; set; } = 10;
    public bool RequireIssued { get; set; } = true;
    public PdfPassword PatientPassword { get; set; } = PdfPassword.DobShort;
    /// <summary>False (default) = insurers get no password; true = same rule as patients.</summary>
    public bool InsurerSameAsPatient { get; set; }
    public string EmailSubject { get; set; } = "Your estimate from Tasman Day Surgery – {ref}";
    public string EmailBody { get; set; } = "Dear {first},\n\nPlease find attached your estimate for {procedure}.\n\nIf you are self-funding, please reply to this email to accept the estimate. If you are insured, please forward it to your insurer and send us their approval.\n\nKind regards\n{preparedby}\nTasman Day Surgery\n{phone}";
    public string EmailSubjectInsurer { get; set; } = "Estimate for {first} {last} – {ref} – {insurer} {policy}";
    public string EmailBodyInsurer { get; set; } = "Hello,\n\nPlease find attached our estimate for {first} {last} (policy {policy}) for {procedure}, for prior approval.\n\nKind regards\n{preparedby}\nTasman Day Surgery\n{phone}";
    public string HintDobShort { get; set; } = "This estimate is password protected. The password is your date of birth as 6 digits, DDMMYY (for example 14 March 1965 = 140365).";
    public string HintDobLong { get; set; } = "This estimate is password protected. The password is your date of birth as 8 digits, DDMMYYYY (for example 14 March 1965 = 14031965).";
    public string HintNhi { get; set; } = "This estimate is password protected. The password is your NHI number in capital letters (it is on your hospital letters and prescriptions).";
    public string HintTyped { get; set; } = "This estimate is password protected. We will give you the password separately by phone or text message.";
    public string? FromName { get; set; } = "Tasman Day Surgery";
    public string? BccAddress { get; set; }
    /// <summary>One per line: Insurer name = address. Filled in automatically when the estimate's insurer matches.</summary>
    public string InsurerAddresses { get; set; } = "Southern Cross = \nnib = \nAIA = \nPartners Life = \nAccuro = \nUniMed = ";
    // Exports
    /// <summary>Incisive scans folder (a \\server\share path on the server).</summary>
    public string? IncisiveFolder { get; set; }
    public string IncisiveFileName { get; set; } = "{NHI}_{LAST}_{FIRST}_Estimate_{DATE}";
    public bool ArchiveIssued { get; set; } = true;
    // Costing (internal only)
    public decimal StaffCostPerMinute { get; set; } = 4.45m;
    public decimal OverheadPerCase { get; set; } = 350m;
    public bool TemplatesSeeded { get; set; }
    // Follow-up (0.33.0)
    /// <summary>Chase an issued estimate this many days after it was issued if nothing has been heard.</summary>
    public int FollowUpDays { get; set; } = 7;
    /// <summary>The expiry list shows estimates running out within this many days.</summary>
    public int ExpiryWarningDays { get; set; } = 14;

    public const string DefaultTerms =
        "All estimates are subject to change without prior notice. This estimate is valid for a total of {validity} months (until {validuntil}).\n" +
        "While it is likely to be the total cost, actual fees may vary depending on the length of procedure and/or length of stay, whether special equipment or materials are used, pre-existing conditions or in certain unusual circumstances. This may include an anaesthetic safety pre-assessment if required.\n" +
        "These fees do not include the initial diagnostic consultation or visits more than three months after surgery. Estimates for the facility and anaesthetist are approximate only and if there are any concerns, they can be clarified with Tasman Day Surgery {phone}.\n" +
        "Occasionally unforeseen circumstances may result in unexpected costs not outlined in this estimate. Examples of this include but are not limited to costs related to emergencies such as cardiac arrest, an unexpected allergic reaction during your stay or an unexpected finding during your procedure.";

    public decimal ChargeOut(decimal supplied) => Math.Round(supplied * (1 + Loading) * (1 + Markup) * (1 + GstRate), 2, MidpointRounding.AwayFromZero);
}

// ------------------------------------------------------------------ database

public sealed class EstDbContext(DbContextOptions<EstDbContext> options) : DbContext(options)
{
    public const string Schema = "est";
    public DbSet<EstSpecialty> Specialties => Set<EstSpecialty>();
    public DbSet<EstProcedure> Procedures => Set<EstProcedure>();
    public DbSet<EstSurgeon> Surgeons => Set<EstSurgeon>();
    public DbSet<EstSurgeonSpecialty> SurgeonSpecialties => Set<EstSurgeonSpecialty>();
    public DbSet<EstSurgeonFee> SurgeonFees => Set<EstSurgeonFee>();
    public DbSet<EstAddOn> AddOns => Set<EstAddOn>();
    public DbSet<EstStockItem> Stock => Set<EstStockItem>();
    public DbSet<EstProcedureConsumable> Consumables => Set<EstProcedureConsumable>();
    public DbSet<EstPendingPrice> PendingPrices => Set<EstPendingPrice>();
    public DbSet<Estimate> Estimates => Set<Estimate>();
    public DbSet<EstimateLine> Lines => Set<EstimateLine>();
    public DbSet<EstimateArchive> Archive => Set<EstimateArchive>();
    public DbSet<EstOutbox> Outbox => Set<EstOutbox>();
    public DbSet<EstTemplate> Templates => Set<EstTemplate>();
    public DbSet<EstTemplateSurgeon> TemplateSurgeons => Set<EstTemplateSurgeon>();
    public DbSet<EstSettings> Settings => Set<EstSettings>();
    public DbSet<EstimateEvent> Events => Set<EstimateEvent>();

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
        b.Entity<EstSpecialty>(e =>
        {
            e.ToTable("Specialties");
            e.Property(x => x.Name).HasMaxLength(100); e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.DefaultNotes).HasMaxLength(4000);
        });
        b.Entity<EstProcedure>(e =>
        {
            e.ToTable("Procedures");
            e.Property(x => x.Code).HasMaxLength(20); e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Group).HasMaxLength(100); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.PrintTitle).HasMaxLength(300);
            e.Property(x => x.DefaultAnaesthetic).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.IncludedAddOnCodes).HasMaxLength(500); e.Property(x => x.Source).HasMaxLength(500);
            e.HasOne<EstSpecialty>().WithMany().HasForeignKey(x => x.SpecialtyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SpecialtyId, x.Group });
        });
        b.Entity<EstSurgeon>(e =>
        {
            e.ToTable("Surgeons");
            e.Property(x => x.Name).HasMaxLength(150); e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.ClinicianId);
        });
        b.Entity<EstSurgeonSpecialty>(e =>
        {
            e.ToTable("SurgeonSpecialties");
            e.HasKey(x => new { x.SurgeonId, x.SpecialtyId });
            e.HasOne<EstSurgeon>().WithMany().HasForeignKey(x => x.SurgeonId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<EstSpecialty>().WithMany().HasForeignKey(x => x.SpecialtyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.SpecialtyId);
        });
        b.Entity<EstSurgeonFee>(e =>
        {
            e.ToTable("SurgeonFees");
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasOne<EstProcedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<EstSurgeon>().WithMany().HasForeignKey(x => x.SurgeonId).OnDelete(DeleteBehavior.Restrict);
            // One fee row per procedure and surgeon, and one "All surgeons" row per procedure.
            e.HasIndex(x => new { x.ProcedureId, x.SurgeonId }).IsUnique().HasFilter("[SurgeonId] IS NOT NULL");
            e.HasIndex(x => x.ProcedureId).IsUnique().HasFilter("[SurgeonId] IS NULL").HasDatabaseName("UX_SurgeonFees_AllSurgeons");
            e.HasIndex(x => x.SurgeonId);
        });
        b.Entity<EstAddOn>(e =>
        {
            e.ToTable("AddOns");
            e.Property(x => x.Code).HasMaxLength(20); e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Group).HasMaxLength(100); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Source).HasMaxLength(500);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
            e.HasOne<EstSpecialty>().WithMany().HasForeignKey(x => x.SpecialtyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EstSurgeon>().WithMany().HasForeignKey(x => x.SurgeonId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.SpecialtyId); e.HasIndex(x => x.SurgeonId);
        });
        b.Entity<EstStockItem>(e =>
        {
            e.ToTable("StockItems");
            e.Property(x => x.Supplier).HasMaxLength(150); e.Property(x => x.Reference).HasMaxLength(100); e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.PackPrice).HasPrecision(12, 4); e.Property(x => x.UnitPrice).HasPrecision(12, 4);
            e.Property(x => x.WhereUsed).HasMaxLength(300); e.Property(x => x.Issues).HasMaxLength(500);
            e.HasIndex(x => new { x.Supplier, x.Name });
        });
        b.Entity<EstProcedureConsumable>(e =>
        {
            e.ToTable("ProcedureConsumables");
            e.Property(x => x.Description).HasMaxLength(300); e.Property(x => x.UnitCost).HasPrecision(12, 4);
            e.HasOne<EstProcedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<EstStockItem>().WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.ProcedureId); e.HasIndex(x => x.StockItemId);
        });
        b.Entity<EstPendingPrice>(e =>
        {
            e.ToTable("PendingPrices");
            e.Property(x => x.Field).HasMaxLength(30); e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasOne<EstProcedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.AppliedAt, x.EffectiveFrom }); e.HasIndex(x => x.ProcedureId);
        });
        b.Entity<Estimate>(e =>
        {
            e.ToTable("Estimates");
            e.Property(x => x.Ref).HasMaxLength(40); e.HasIndex(x => x.Ref).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.FirstName).HasMaxLength(100); e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Nhi).HasMaxLength(10); e.Property(x => x.Phone).HasMaxLength(50); e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(200); e.Property(x => x.Town).HasMaxLength(100); e.Property(x => x.Postcode).HasMaxLength(10);
            e.Property(x => x.Funding).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Insurer).HasMaxLength(100); e.Property(x => x.PolicyNumber).HasMaxLength(100); e.Property(x => x.PriorApproval).HasMaxLength(100);
            e.Property(x => x.ProcedureCode).HasMaxLength(20); e.Property(x => x.SpecialtyName).HasMaxLength(100); e.Property(x => x.ProcedureName).HasMaxLength(200);
            e.Property(x => x.Title).HasMaxLength(400); e.Property(x => x.SurgeonName).HasMaxLength(150);
            e.Property(x => x.Anaesthetic).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Multiplier).HasPrecision(5, 2);
            e.Property(x => x.InputsJson).HasMaxLength(8000); e.Property(x => x.Notes).HasMaxLength(4000); e.Property(x => x.PreparedBy).HasMaxLength(100);
            e.Property(x => x.CreatedBy).HasMaxLength(100); e.Property(x => x.UpdatedBy).HasMaxLength(100); e.Property(x => x.CancelReason).HasMaxLength(300);
            e.HasOne<EstProcedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EstSurgeon>().WithMany().HasForeignKey(x => x.SurgeonId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.PatientId); e.HasIndex(x => x.Nhi); e.HasIndex(x => x.Email); e.HasIndex(x => x.EstimateDate);
            e.HasIndex(x => x.ProcedureId); e.HasIndex(x => x.SurgeonId); e.HasIndex(x => new { x.LastName, x.FirstName });
            e.Property(x => x.FollowUp).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FollowUpBy).HasMaxLength(100); e.Property(x => x.DeclineReason).HasMaxLength(300); e.Property(x => x.InvoiceNumber).HasMaxLength(30);
            e.HasIndex(x => new { x.FollowUp, x.ValidUntil }); e.HasIndex(x => x.BookingId); e.HasIndex(x => x.InvoiceId);
        });
        b.Entity<EstimateEvent>(e =>
        {
            e.ToTable("EstimateEvents");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.By).HasMaxLength(100); e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.EstimateId, x.At });
            e.HasOne<Estimate>().WithMany().HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<EstimateLine>(e =>
        {
            e.ToTable("EstimateLines");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Label).HasMaxLength(300); e.Property(x => x.DisplayText).HasMaxLength(200); e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => new { x.EstimateId, x.Sequence }).IsUnique();
        });
        b.Entity<EstimateArchive>(e =>
        {
            e.ToTable("ArchivedPdfs");
            e.Property(x => x.CreatedBy).HasMaxLength(100); e.Property(x => x.Reason).HasMaxLength(100); e.Property(x => x.FileName).HasMaxLength(200);
            e.HasOne<Estimate>().WithMany().HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.EstimateId);
        });
        b.Entity<EstOutbox>(e =>
        {
            e.ToTable("Outbox");
            e.Property(x => x.EstimateRef).HasMaxLength(40); e.Property(x => x.PatientName).HasMaxLength(200);
            e.Property(x => x.Recipient).HasConversion<string>().HasMaxLength(10); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.ToAddress).HasMaxLength(200); e.Property(x => x.Subject).HasMaxLength(300); e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.AttachmentName).HasMaxLength(200);
            e.Property(x => x.CreatedBy).HasMaxLength(100); e.Property(x => x.StoppedBy).HasMaxLength(150); e.Property(x => x.Error).HasMaxLength(1000);
            e.HasOne<Estimate>().WithMany().HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Status, x.SendAfter }); e.HasIndex(x => x.EstimateId);
        });
        b.Entity<EstTemplate>(e =>
        {
            e.ToTable("Templates");
            e.Property(x => x.Name).HasMaxLength(150); e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Anaesthetic).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.InputsJson).HasMaxLength(8000); e.Property(x => x.Notes).HasMaxLength(4000);
            e.Property(x => x.CreatedBy).HasMaxLength(100); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasOne<EstProcedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EstSurgeon>().WithMany().HasForeignKey(x => x.SurgeonId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ProcedureId); e.HasIndex(x => x.SurgeonId);
        });
        b.Entity<EstTemplateSurgeon>(e =>
        {
            e.ToTable("TemplateSurgeons");
            e.HasKey(x => new { x.TemplateId, x.SurgeonId });
            e.HasOne<EstTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<EstSurgeon>().WithMany().HasForeignKey(x => x.SurgeonId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.SurgeonId);
        });
        b.Entity<EstSettings>(e =>
        {
            e.ToTable("Settings", t => t.HasCheckConstraint("CK_EstSettings_SingleRow", "[Id] = 1"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.GstRate).HasPrecision(6, 4); e.Property(x => x.Loading).HasPrecision(6, 4); e.Property(x => x.Markup).HasPrecision(6, 4);
            e.Property(x => x.DepositPercent).HasPrecision(5, 2);
            e.Property(x => x.SurgeonFeeLabel).HasMaxLength(60); e.Property(x => x.BankAccount).HasMaxLength(30); e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(200); e.Property(x => x.Address).HasMaxLength(200);
            e.Property(x => x.Terms).HasMaxLength(4000); e.Property(x => x.SelfFundingText).HasMaxLength(1000); e.Property(x => x.InsuranceText).HasMaxLength(1000);
            e.Property(x => x.AcceptanceText).HasMaxLength(500);
            e.Property(x => x.SendMode).HasConversion<string>().HasMaxLength(12); e.Property(x => x.PatientPassword).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.EmailSubject).HasMaxLength(200); e.Property(x => x.EmailBody).HasMaxLength(2000);
            e.Property(x => x.EmailSubjectInsurer).HasMaxLength(200); e.Property(x => x.EmailBodyInsurer).HasMaxLength(2000);
            e.Property(x => x.HintDobShort).HasMaxLength(300); e.Property(x => x.HintDobLong).HasMaxLength(300); e.Property(x => x.HintNhi).HasMaxLength(300); e.Property(x => x.HintTyped).HasMaxLength(300);
            e.Property(x => x.FromName).HasMaxLength(100); e.Property(x => x.BccAddress).HasMaxLength(200); e.Property(x => x.InsurerAddresses).HasMaxLength(2000);
            e.Property(x => x.IncisiveFolder).HasMaxLength(260); e.Property(x => x.IncisiveFileName).HasMaxLength(100);
            e.Property(x => x.StaffCostPerMinute).HasPrecision(8, 2); e.Property(x => x.OverheadPerCase).HasPrecision(10, 2);
            e.HasData(new EstSettings());
        });
    }
}

public sealed class EstDbContextFactory : IDesignTimeDbContextFactory<EstDbContext>
{
    public EstDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<EstDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", EstDbContext.Schema)).Options);
}
