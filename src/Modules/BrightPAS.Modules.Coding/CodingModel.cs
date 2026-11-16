using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Coding;

/// <summary>
/// Clinical coding and statutory reporting (0.20.0, ADR 0033; spec 7.11): a coder workbench for ICD-10-AM/ACHI/ACS (NZ edition, Twelfth
/// Edition from 1 July 2023 – clinical coding system 16) with Australian Coding Standard and New Zealand convention edit checks,
/// procedure picklists, a coding advice library (IHACPA rules and FAQs with their NZ review status, NZ conventions), the National
/// Minimum Dataset private hospital load file (NMDS) with submission log and resubmission, and the National Booking Reporting System
/// batch file (NBRS file version V04.0, specification 4.5) with pre-submission checks and acknowledgement (.NBK) import.
/// </summary>
public sealed class CodingModule : IPasModule
{
    public const string Id = "reporting";
    public ModuleManifest Manifest { get; } = new(Id, "Coding & statutory reporting", typeof(CodingModule).Assembly.GetName().Version ?? new Version(0, 0),
        CodingDbContext.Schema, [], "Clinical coding, NMDS and NBRS reporting to Health NZ.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<CodingDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, CodingDbContext.Schema));
        services.AddScoped<CodingService>();
        services.AddScoped<NmdsService>();
        services.AddScoped<NbrsService>();
    }
}

// ------------------------------------------------------------------ code tables
/// <summary>
/// One clinical code from the Health NZ clinical code table (ICD-10-AM diagnoses, external causes and morphology; ACHI procedures).
/// Imported by an administrator from the file Health NZ supplies (the classification is licensed – BrightPAS does not ship it).
/// </summary>
public sealed class ClinicalCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Clinical coding system id, e.g. "16" = ICD-10-AM/ACHI Twelfth Edition.</summary>
    public string System { get; set; } = "16";
    /// <summary>Code without the dot (K635, 32090-00 stored as 3209000).</summary>
    public string Code { get; set; } = "";
    /// <summary>A diagnosis, B injury, E external cause, M morphology, O procedure (NMDS clinical code type).</summary>
    public string Type { get; set; } = "A";
    public string Description { get; set; } = "";
    /// <summary>"M" or "F" when the code is valid for one sex only.</summary>
    public string? Sex { get; set; }
    public int? AgeFromYears { get; set; }
    public int? AgeToYears { get; set; }
    /// <summary>ACS 0049 (Twelfth Edition): a code that must never be assigned.</summary>
    public bool NeverAssign { get; set; }
    /// <summary>Not acceptable as principal diagnosis (e.g. most Z codes in some contexts, external causes).</summary>
    public bool NotPrincipal { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Surgeon-friendly term → codes (the theatre procedure list maps through its code).</summary>
public sealed class ProcedureMap
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>The theatre procedure list code (Organisation › Procedures), or a free term.</summary>
    public string Term { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Space-separated ACHI procedure codes (formatted, e.g. "32090-00 92515-99").</summary>
    public string ProcedureCodes { get; set; } = "";
    /// <summary>Usual principal diagnosis code to offer (the coder confirms against the record).</summary>
    public string? DiagnosisCode { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum AdviceSource { IhacpaRule = 1, IhacpaFaq = 2, NzConvention = 3, NmdsRequirement = 4, Local = 5 }

/// <summary>
/// Coding advice library. IHACPA advice published for Thirteenth Edition (July 2025 on) does not apply in NZ until Health NZ reviews it;
/// <see cref="AppliesToNz"/> records that review. Advice is linked to code prefixes and shown to the coder when one of them is used.
/// </summary>
public sealed class CodingAdvice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>IHACPA reference (Q3725) or the ACS / convention it relates to.</summary>
    public string Reference { get; set; } = "";
    public string Title { get; set; } = "";
    public AdviceSource Source { get; set; }
    public string Edition { get; set; } = "";
    public bool AppliesToNz { get; set; } = true;
    public DateOnly? ReviewedOn { get; set; }
    /// <summary>Space-separated code prefixes the advice concerns (K635, 92509, S, U50-Y98 ranges as "U5 V W X Y").</summary>
    public string CodePrefixes { get; set; } = "";
    /// <summary>Summary in TDS's own words; the published text stays with its publisher (link).</summary>
    public string Summary { get; set; } = "";
    public string? Link { get; set; }
    public bool IsActive { get; set; } = true;
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Medical Council (or other board) registration for NBRS clinical responsibility and assessor codes.</summary>
public sealed class ClinicianRegistration
{
    public Guid ClinicianId { get; set; }
    /// <summary>NBRS professional group code: MC Medical Council of NZ, HB District.</summary>
    public string ProfessionalGroup { get; set; } = "MC";
    public string RegistrationNumber { get; set; } = "";
}

// ------------------------------------------------------------------ coded episodes
public enum CodingStatus { ToCode = 1, InProgress = 2, Coded = 3, Submitted = 4, Rejected = 5, NotReportable = 6 }

/// <summary>One hospital event (an eMR episode) to be coded and reported.</summary>
public sealed class CodedEpisode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public Guid PatientId { get; set; }
    public Guid BookingId { get; set; }
    public DateOnly AdmissionDate { get; set; }
    public DateOnly DischargeDate { get; set; }
    /// <summary>NMDS purchaser code (from the booking's payment type through the settings).</summary>
    public string PurchaserCode { get; set; } = "";
    /// <summary>R routine, T transferred from another facility.</summary>
    public string AdmissionSource { get; set; } = "R";
    /// <summary>NMDS event end type (DR ended routinely, DA discharge to acute facility, DD died …).</summary>
    public string EventEndType { get; set; } = "DR";
    public CodingStatus Status { get; set; } = CodingStatus.ToCode;
    /// <summary>Why the event is not reported by TDS (e.g. contracted event the District reports – NZ convention on ACS 0029).</summary>
    public string? NotReportableReason { get; set; }
    public string? CodedBy { get; set; }
    public DateTimeOffset? CodedAt { get; set; }
    public string? CoderNote { get; set; }
    /// <summary>The last NMDS file the event was sent in.</summary>
    public Guid? SubmissionId { get; set; }
    /// <summary>Error reported back by Health NZ (entered from their error report) – the event goes in the next file once corrected.</summary>
    public string? HealthNzError { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<CodedItem> Items { get; set; } = [];
}

/// <summary>A = principal diagnosis, B = other diagnosis, O = procedure, E = external cause (NMDS diagnosis type).</summary>
public sealed class CodedItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CodedEpisodeId { get; set; }
    public int Sequence { get; set; }
    public string DiagnosisType { get; set; } = "B";
    public string Code { get; set; } = "";
    /// <summary>Free text from the clinical record (NZ NMDS requirement: free text rather than the classification description).</summary>
    public string Description { get; set; } = "";
    public DateOnly? Date { get; set; }
}

public enum SubmissionStatus { Created = 1, Sent = 2, Accepted = 3, Errors = 4, Replaced = 5 }

/// <summary>An NMDS load file (AAA0YYMM.CSV) and its history.</summary>
public sealed class NmdsSubmission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public DateOnly PeriodTo { get; set; }
    public int Records { get; set; }
    public byte[] Content { get; set; } = [];
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Created;
    public bool IsResubmission { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? SentBy { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? Notes { get; set; }
}

public sealed class NmdsSubmissionEvent
{
    public Guid SubmissionId { get; set; }
    public Guid CodedEpisodeId { get; set; }
    public string RecordId { get; set; } = "";
}

// ------------------------------------------------------------------ NBRS
/// <summary>A booking system entry reported to NBRS (publicly funded elective outsourced to TDS).</summary>
public sealed class NbrsEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Local booking system entry identifier (max 14), unique within the facility.</summary>
    public string LocalId { get; set; } = "";
    public Guid PatientId { get; set; }
    public Guid? WaitlistEntryId { get; set; }
    public string FacilityCode { get; set; } = "";
    /// <summary>Booking source code (NBRS data dictionary, e.g. 2 public specialist, 4 primary care).</summary>
    public string BookingSource { get; set; } = "";
    public DateOnly? ReferralDate { get; set; }
    public DateOnly? FirstAssessmentDate { get; set; }
    public DateOnly? CpacDate { get; set; }
    public string? CpacScore { get; set; }
    public string? CpacSystem { get; set; }
    public string PurchaserCode { get; set; } = "";
    public string HealthSpecialty { get; set; } = "";
    public string ClinicalCode { get; set; } = "";
    public string ClinicalCodeType { get; set; } = "O";
    public string? DomicileCode { get; set; }
    public string? ProfessionalGroup { get; set; }
    public string? ResponsibilityCode { get; set; }
    public string? AssessorCode { get; set; }
    public string? AssessorGroup { get; set; }
    /// <summary>1 normal, 2 staged, 3 planned, 4 surveillance.</summary>
    public string? StagedFlag { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<NbrsEvent> Events { get; set; } = [];
}

public enum NbrsResult { Pending = 0, Sent = 1, Accepted = 2, Rejected = 3 }

/// <summary>One BE record: a status change (01 booked, 20 exited …) or a data change / delete / erase.</summary>
public sealed class NbrsEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntryId { get; set; }
    /// <summary>A add, C change, D delete, E erase.</summary>
    public string Action { get; set; } = "A";
    public string? StatusCode { get; set; }
    public DateOnly? StatusDate { get; set; }
    public DateOnly? CertaintyDate { get; set; }
    public DateOnly? BookingMadeDate { get; set; }
    public DateOnly? BookedForDate { get; set; }
    public string? DeferredBy { get; set; }
    public DateOnly? ExitDate { get; set; }
    public string? ExitCategory { get; set; }
    public string? ContractAgency { get; set; }
    public string? TreatmentFacility { get; set; }
    public string? ClientSystemId { get; set; }
    public int EventLocalId { get; set; }
    public Guid? BatchId { get; set; }
    public int? LineNumber { get; set; }
    public NbrsResult Result { get; set; }
    public string? Messages { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NbrsBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>CMPL (compliance testing) or PROD.</summary>
    public string Environment { get; set; } = "CMPL";
    public int Number { get; set; }
    public string FileName { get; set; } = "";
    public int Records { get; set; }
    public byte[] Content { get; set; } = [];
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Created;
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public int Warnings { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
}

// ------------------------------------------------------------------ settings
public sealed class CodingSettings
{
    public int Id { get; set; } = 1;
    /// <summary>Clinical coding system id in use (16 = ICD-10-AM/ACHI/ACS Twelfth Edition, NZ from 1 July 2023).</summary>
    public string CodingSystem { get; set; } = "16";
    public string EditionName { get; set; } = "ICD-10-AM/ACHI/ACS Twelfth Edition";
    /// <summary>NMDS health agency code and health agency facility (HAF) code allocated by Health NZ.</summary>
    public string? HealthAgencyCode { get; set; }
    public string? FacilityCode { get; set; }
    /// <summary>Three-letter file acronym allocated by Health NZ (NMDS and NBRS).</summary>
    public string? FileAcronym { get; set; }
    public string? PurchaserSelfFunded { get; set; }
    public string? PurchaserInsurer { get; set; }
    public string? PurchaserAcc { get; set; }
    public string? PurchaserHealthNz { get; set; }
    public string? PurchaserNoCharge { get; set; }
    /// <summary>
    /// NZ convention on ACS 0029: only one hospital reports a contracted event, preferably the public hospital – the contract says which.
    /// When false, Health NZ funded events are marked not reportable by TDS.
    /// </summary>
    public bool TdsReportsContractedEvents { get; set; }
    /// <summary>NMDS reporting requirement: events reported within 21 days of discharge.</summary>
    public int ReportWithinDays { get; set; } = 21;
    public string? NbrsAgencyCode { get; set; }
    public string NbrsEnvironment { get; set; } = "CMPL";
    public int NextNbrsBatchCmpl { get; set; } = 1;
    public int NextNbrsBatchProd { get; set; } = 1;
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class CodingDbContext(DbContextOptions<CodingDbContext> options) : DbContext(options)
{
    public const string Schema = "coding";
    public DbSet<ClinicalCode> Codes => Set<ClinicalCode>();
    public DbSet<ProcedureMap> ProcedureMaps => Set<ProcedureMap>();
    public DbSet<CodingAdvice> Advice => Set<CodingAdvice>();
    public DbSet<ClinicianRegistration> Registrations => Set<ClinicianRegistration>();
    public DbSet<CodedEpisode> Episodes => Set<CodedEpisode>();
    public DbSet<CodedItem> Items => Set<CodedItem>();
    public DbSet<NmdsSubmission> Submissions => Set<NmdsSubmission>();
    public DbSet<NmdsSubmissionEvent> SubmissionEvents => Set<NmdsSubmissionEvent>();
    public DbSet<NbrsEntry> NbrsEntries => Set<NbrsEntry>();
    public DbSet<NbrsEvent> NbrsEvents => Set<NbrsEvent>();
    public DbSet<NbrsBatch> NbrsBatches => Set<NbrsBatch>();
    public DbSet<CodingSettings> Settings => Set<CodingSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<ClinicalCode>(e =>
        {
            e.Property(x => x.System).HasMaxLength(2); e.Property(x => x.Code).HasMaxLength(8); e.Property(x => x.Type).HasMaxLength(1);
            e.Property(x => x.Description).HasMaxLength(250); e.Property(x => x.Sex).HasMaxLength(1);
            e.HasIndex(x => new { x.System, x.Code, x.Type }).IsUnique();
            e.HasIndex(x => x.Description);
        });
        b.Entity<ProcedureMap>(e =>
        {
            e.Property(x => x.Term).HasMaxLength(60); e.Property(x => x.Label).HasMaxLength(150); e.Property(x => x.ProcedureCodes).HasMaxLength(200);
            e.Property(x => x.DiagnosisCode).HasMaxLength(10); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Term).IsUnique();
        });
        b.Entity<CodingAdvice>(e =>
        {
            e.Property(x => x.Reference).HasMaxLength(40); e.Property(x => x.Title).HasMaxLength(200); e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Edition).HasMaxLength(60); e.Property(x => x.CodePrefixes).HasMaxLength(400); e.Property(x => x.Summary).HasMaxLength(2000);
            e.Property(x => x.Link).HasMaxLength(400); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Reference);
        });
        b.Entity<ClinicianRegistration>(e =>
        {
            e.HasKey(x => x.ClinicianId); e.Property(x => x.ClinicianId).ValueGeneratedNever();
            e.Property(x => x.ProfessionalGroup).HasMaxLength(2); e.Property(x => x.RegistrationNumber).HasMaxLength(10);
        });
        b.Entity<CodedEpisode>(e =>
        {
            e.Property(x => x.PurchaserCode).HasMaxLength(2); e.Property(x => x.AdmissionSource).HasMaxLength(1); e.Property(x => x.EventEndType).HasMaxLength(2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(15); e.Property(x => x.NotReportableReason).HasMaxLength(300);
            e.Property(x => x.CodedBy).HasMaxLength(100); e.Property(x => x.CoderNote).HasMaxLength(2000); e.Property(x => x.HealthNzError).HasMaxLength(1000);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.EpisodeId).IsUnique();
            e.HasIndex(x => new { x.Status, x.DischargeDate });
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.SubmissionId);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.CodedEpisodeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CodedItem>(e =>
        {
            e.Property(x => x.DiagnosisType).HasMaxLength(1); e.Property(x => x.Code).HasMaxLength(8); e.Property(x => x.Description).HasMaxLength(50);
            e.HasIndex(x => new { x.CodedEpisodeId, x.Sequence }).IsUnique();
        });
        b.Entity<NmdsSubmission>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(20); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.CreatedBy).HasMaxLength(100); e.Property(x => x.SentBy).HasMaxLength(100); e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasIndex(x => x.CreatedAt);
        });
        b.Entity<NmdsSubmissionEvent>(e =>
        {
            e.HasKey(x => new { x.SubmissionId, x.CodedEpisodeId }); e.Property(x => x.RecordId).HasMaxLength(14);
            e.HasIndex(x => x.CodedEpisodeId);
            e.HasOne<NmdsSubmission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CodedEpisode>().WithMany().HasForeignKey(x => x.CodedEpisodeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<NbrsEntry>(e =>
        {
            e.Property(x => x.LocalId).HasMaxLength(14); e.Property(x => x.FacilityCode).HasMaxLength(4); e.Property(x => x.BookingSource).HasMaxLength(1);
            e.Property(x => x.CpacScore).HasMaxLength(5); e.Property(x => x.CpacSystem).HasMaxLength(4); e.Property(x => x.PurchaserCode).HasMaxLength(2);
            e.Property(x => x.HealthSpecialty).HasMaxLength(3); e.Property(x => x.ClinicalCode).HasMaxLength(8); e.Property(x => x.ClinicalCodeType).HasMaxLength(1);
            e.Property(x => x.DomicileCode).HasMaxLength(4); e.Property(x => x.ProfessionalGroup).HasMaxLength(2); e.Property(x => x.ResponsibilityCode).HasMaxLength(10);
            e.Property(x => x.AssessorCode).HasMaxLength(10); e.Property(x => x.AssessorGroup).HasMaxLength(2); e.Property(x => x.StagedFlag).HasMaxLength(1);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.FacilityCode, x.LocalId }).IsUnique();
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.WaitlistEntryId);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<NbrsEvent>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(1); e.Property(x => x.StatusCode).HasMaxLength(2); e.Property(x => x.DeferredBy).HasMaxLength(1);
            e.Property(x => x.ExitCategory).HasMaxLength(2); e.Property(x => x.ContractAgency).HasMaxLength(4); e.Property(x => x.TreatmentFacility).HasMaxLength(4);
            e.Property(x => x.ClientSystemId).HasMaxLength(14); e.Property(x => x.Result).HasConversion<string>().HasMaxLength(10); e.Property(x => x.Messages).HasMaxLength(2000);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.BatchId);
            e.HasIndex(x => new { x.Result, x.CreatedAt });
            e.HasOne<NbrsBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<NbrsBatch>(e =>
        {
            e.Property(x => x.Environment).HasMaxLength(4); e.Property(x => x.FileName).HasMaxLength(20); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.Environment, x.Number }).IsUnique();
        });
        b.Entity<CodingSettings>(e =>
        {
            e.ToTable("Settings"); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.CodingSystem).HasMaxLength(2); e.Property(x => x.EditionName).HasMaxLength(80);
            e.Property(x => x.HealthAgencyCode).HasMaxLength(4); e.Property(x => x.FacilityCode).HasMaxLength(4); e.Property(x => x.FileAcronym).HasMaxLength(3);
            e.Property(x => x.PurchaserSelfFunded).HasMaxLength(2); e.Property(x => x.PurchaserInsurer).HasMaxLength(2); e.Property(x => x.PurchaserAcc).HasMaxLength(2);
            e.Property(x => x.PurchaserHealthNz).HasMaxLength(2); e.Property(x => x.PurchaserNoCharge).HasMaxLength(2);
            e.Property(x => x.NbrsAgencyCode).HasMaxLength(4); e.Property(x => x.NbrsEnvironment).HasMaxLength(4); e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
    }
}

public sealed class CodingDbContextFactory : IDesignTimeDbContextFactory<CodingDbContext>
{
    public CodingDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<CodingDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", CodingDbContext.Schema)).Options);
}
