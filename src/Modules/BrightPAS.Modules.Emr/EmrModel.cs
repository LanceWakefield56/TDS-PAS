using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Emr;

/// <summary>
/// Perioperative and clinical record (spec 7.6, ADR 0026): one clinical episode per theatre booking with nursing admission and risk screens,
/// the WHO/HQSC Surgical Safety Checklist as hard-stop steps, the intraoperative record (positioning, prep, diathermy, tourniquet, counts with a
/// discrepancy workflow, specimens with chain of custody, wound class, drains/dressings), observations with the NZ Early Warning Score,
/// medicines given and the controlled drug register, fluid balance, blood product traceability, clinical notes and operation notes, and
/// discharge criteria with the escort hard stop. Observations can come from bedside monitors (Source = Device) – foundation for 0.17.0.
/// </summary>
public sealed class EmrModule : IPasModule
{
    public const string Id = "emr";
    public ModuleManifest Manifest { get; } = new(Id, "Clinical record (eMR)", typeof(EmrModule).Assembly.GetName().Version ?? new Version(0, 0),
        EmrDbContext.Schema, [], "Perioperative, nursing and clinical record per theatre episode.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<EmrDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, EmrDbContext.Schema));
        services.AddScoped<EmrService>();
    }
}

public enum EpisodeStage { PreAdmission = 1, Admitted = 2, InTheatre = 3, Recovery = 4, ReadyForDischarge = 5, Discharged = 6, Transferred = 7 }

/// <summary>The clinical record of one theatre/procedure booking.</summary>
public sealed class Episode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Guid PatientId { get; set; }
    public EpisodeStage Stage { get; set; } = EpisodeStage.PreAdmission;
    public DateTimeOffset? AdmittedAt { get; set; }
    public string? AdmittedBy { get; set; }
    /// <summary>Sedation or anaesthesia given – the discharge escort hard stop applies.</summary>
    public bool SedationOrAnaesthesia { get; set; } = true;
    public string? EscortName { get; set; }
    public string? EscortPhone { get; set; }
    public string? OvernightCarer { get; set; }
    public bool EscortConfirmed { get; set; }
    public string? EscortOverrideReason { get; set; }
    public DateTimeOffset? DischargedAt { get; set; }
    public string? DischargedBy { get; set; }
    public string? DischargeDestination { get; set; }
    public DateOnly? FollowUpCallOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum AssessmentKind { NursingAdmission = 1, Vte = 2, Falls = 3, PressureInjury = 4, Frailty = 5, Delirium = 6 }

public sealed class Assessment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public AssessmentKind Kind { get; set; }
    public int Score { get; set; }
    /// <summary>Low, Moderate or High (plain words shown to staff).</summary>
    public string Risk { get; set; } = "";
    public string? Actions { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
    public List<AssessmentAnswer> Answers { get; set; } = [];
}

public sealed class AssessmentAnswer
{
    public Guid AssessmentId { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public enum CheckPhase { SignIn = 1, TimeOut = 2, SignOut = 3 }

/// <summary>One phase of the Surgical Safety Checklist, confirmed item by item with the team named.</summary>
public sealed class SafetyCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public CheckPhase Phase { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public string LedBy { get; set; } = "";
    public string? Notes { get; set; }
    public List<SafetyCheckItem> Items { get; set; } = [];
    public List<SafetyCheckMember> Team { get; set; } = [];
}

public sealed class SafetyCheckItem
{
    public Guid SafetyCheckId { get; set; }
    public string Key { get; set; } = "";
    public bool Confirmed { get; set; }
}

public sealed class SafetyCheckMember
{
    public Guid SafetyCheckId { get; set; }
    public string Role { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>The intraoperative nursing record (ACORN-aligned).</summary>
public sealed class IntraopRecord
{
    public Guid EpisodeId { get; set; }
    public DateTimeOffset? InTheatreAt { get; set; }
    public DateTimeOffset? AnaestheticStartAt { get; set; }
    public DateTimeOffset? KnifeToSkinAt { get; set; }
    public DateTimeOffset? ClosureAt { get; set; }
    public DateTimeOffset? OutOfTheatreAt { get; set; }
    public string? Position { get; set; }
    public string? PositioningAids { get; set; }
    public string? SkinPrep { get; set; }
    public string? DiathermyUnit { get; set; }
    public string? DiathermyPadSite { get; set; }
    public bool? PadSiteSkinIntactAfter { get; set; }
    public string? TourniquetSite { get; set; }
    public int? TourniquetPressure { get; set; }
    public DateTimeOffset? TourniquetOnAt { get; set; }
    public DateTimeOffset? TourniquetOffAt { get; set; }
    public bool SmokeEvacuation { get; set; }
    /// <summary>Clean, CleanContaminated, Contaminated, Dirty.</summary>
    public string? WoundClass { get; set; }
    public string? Drains { get; set; }
    public string? Dressings { get; set; }
    public string? Notes { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum CountStage { Initial = 1, Additional = 2, Closing = 3, Final = 4 }

public sealed class SurgicalCount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public CountStage Stage { get; set; }
    /// <summary>Swabs, Sharps, Instruments, Other.</summary>
    public string Item { get; set; } = "";
    public int Expected { get; set; }
    public int Counted { get; set; }
    public string CountedBy { get; set; } = "";
    public string CheckedBy { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public bool Resolved { get; set; }
    public string? ResolutionNote { get; set; }
}

public enum SpecimenStatus { Collected = 1, Sent = 2, ReceivedByLab = 3 }

public sealed class Specimen
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public int Number { get; set; }
    public string Site { get; set; } = "";
    public string Test { get; set; } = "Histology";
    public string? Container { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
    public string CollectedBy { get; set; } = "";
    public SpecimenStatus Status { get; set; } = SpecimenStatus.Collected;
    public DateTimeOffset? SentAt { get; set; }
    public string? SentBy { get; set; }
    public string? Courier { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
}

public sealed class Observation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public DateTimeOffset At { get; set; }
    public int? RespiratoryRate { get; set; }
    public int? SpO2 { get; set; }
    public bool OnOxygen { get; set; }
    public decimal? OxygenLitres { get; set; }
    public int? SystolicBp { get; set; }
    public int? DiastolicBp { get; set; }
    public int? HeartRate { get; set; }
    public decimal? TemperatureC { get; set; }
    /// <summary>A (alert), V, P, U, or C (new confusion).</summary>
    public string? Consciousness { get; set; }
    public int? Pain { get; set; }
    public int? Sedation { get; set; }
    public int? Ews { get; set; }
    public string? Escalation { get; set; }
    /// <summary>"Manual" or "Device" (bedside monitor via the integration).</summary>
    public string Source { get; set; } = "Manual";
    /// <summary>The monitor (assets.Equipment) the values came from, when Source = Device.</summary>
    public Guid? DeviceId { get; set; }
    public string RecordedBy { get; set; } = "";
}

public sealed class MedicationGiven
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public string Medicine { get; set; } = "";
    public string Dose { get; set; } = "";
    public string Route { get; set; } = "";
    public DateTimeOffset GivenAt { get; set; }
    public string GivenBy { get; set; } = "";
    public string? WitnessedBy { get; set; }
    /// <summary>Barcode scanned from the medicine pack (GTIN), if scanned.</summary>
    public string? Barcode { get; set; }
    public Guid? ControlledDrugId { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A controlled drug held at TDS (Misuse of Drugs Regulations 1977): one register per drug, strength and form.</summary>
public sealed class ControlledDrug
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Strength { get; set; } = "";
    public string Form { get; set; } = "ampoule";
    public int Balance { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum CdMovement { Received = 1, Administered = 2, Wasted = 3, Returned = 4, Correction = 5 }

public sealed class ControlledDrugEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ControlledDrugId { get; set; }
    public CdMovement Movement { get; set; }
    public int Quantity { get; set; }
    public int BalanceAfter { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? EpisodeId { get; set; }
    public string? Reference { get; set; }
    public string By { get; set; } = "";
    public string WitnessedBy { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class FluidEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public DateTimeOffset At { get; set; }
    public bool IsInput { get; set; }
    /// <summary>IV, Oral, Urine, Drain, BloodLoss, Vomit, Other.</summary>
    public string Kind { get; set; } = "";
    public int Millilitres { get; set; }
    public string By { get; set; } = "";
}

/// <summary>A blood product unit given (e.g. under the emergency blood collection procedure TDS-5.9).</summary>
public sealed class BloodUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public string UnitNumber { get; set; } = "";
    public string Product { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public bool ColdChainOk { get; set; }
    public string CheckedBy1 { get; set; } = "";
    public string CheckedBy2 { get; set; } = "";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public bool Reaction { get; set; }
    public string? ReactionDetails { get; set; }
    public bool Returned { get; set; }
    public string RecordedBy { get; set; } = "";
}

public enum NoteKind { Nursing = 1, Medical = 2, OperationNote = 3, Anaesthetic = 4, Discharge = 5, PatientInstructions = 6 }

public sealed class ClinicalNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EpisodeId { get; set; }
    public NoteKind Kind { get; set; }
    public string Text { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTimeOffset At { get; set; }
    /// <summary>Signed notes cannot be changed – an amendment is a new note that points to the one it corrects.</summary>
    public bool Signed { get; set; }
    public Guid? AmendsNoteId { get; set; }
}

public sealed class NoteTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public NoteKind Kind { get; set; }
    public string Body { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class EmrDbContext(DbContextOptions<EmrDbContext> options) : DbContext(options)
{
    public const string Schema = "emr";
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<AssessmentAnswer> Answers => Set<AssessmentAnswer>();
    public DbSet<SafetyCheck> SafetyChecks => Set<SafetyCheck>();
    public DbSet<SafetyCheckItem> SafetyCheckItems => Set<SafetyCheckItem>();
    public DbSet<SafetyCheckMember> SafetyCheckTeam => Set<SafetyCheckMember>();
    public DbSet<IntraopRecord> Intraop => Set<IntraopRecord>();
    public DbSet<SurgicalCount> Counts => Set<SurgicalCount>();
    public DbSet<Specimen> Specimens => Set<Specimen>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<MedicationGiven> Medications => Set<MedicationGiven>();
    public DbSet<ControlledDrug> ControlledDrugs => Set<ControlledDrug>();
    public DbSet<ControlledDrugEntry> CdRegister => Set<ControlledDrugEntry>();
    public DbSet<FluidEntry> Fluids => Set<FluidEntry>();
    public DbSet<BloodUnit> BloodUnits => Set<BloodUnit>();
    public DbSet<ClinicalNote> Notes => Set<ClinicalNote>();
    public DbSet<NoteTemplate> Templates => Set<NoteTemplate>();

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
        b.Entity<Episode>(e =>
        {
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AdmittedBy).HasMaxLength(100); e.Property(x => x.EscortName).HasMaxLength(100); e.Property(x => x.EscortPhone).HasMaxLength(40);
            e.Property(x => x.OvernightCarer).HasMaxLength(100); e.Property(x => x.EscortOverrideReason).HasMaxLength(300); e.Property(x => x.DischargedBy).HasMaxLength(100);
            e.Property(x => x.DischargeDestination).HasMaxLength(100);
            e.HasIndex(x => x.BookingId).IsUnique(); e.HasIndex(x => x.PatientId); e.HasIndex(x => x.Stage);
        });
        b.Entity<Assessment>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Risk).HasMaxLength(20); e.Property(x => x.Actions).HasMaxLength(500);
            e.Property(x => x.RecordedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.EpisodeId, x.Kind });
            e.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Answers).WithOne().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AssessmentAnswer>(e => { e.ToTable("AssessmentAnswers"); e.HasKey(x => new { x.AssessmentId, x.Key }); e.Property(x => x.Key).HasMaxLength(40); e.Property(x => x.Value).HasMaxLength(300); });
        b.Entity<SafetyCheck>(e =>
        {
            e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(10); e.Property(x => x.LedBy).HasMaxLength(100); e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => new { x.EpisodeId, x.Phase }).IsUnique();
            e.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SafetyCheckId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Team).WithOne().HasForeignKey(x => x.SafetyCheckId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SafetyCheckItem>(e => { e.ToTable("SafetyCheckItems"); e.HasKey(x => new { x.SafetyCheckId, x.Key }); e.Property(x => x.Key).HasMaxLength(40); });
        b.Entity<SafetyCheckMember>(e => { e.ToTable("SafetyCheckTeam"); e.HasKey(x => new { x.SafetyCheckId, x.Role }); e.Property(x => x.Role).HasMaxLength(40); e.Property(x => x.Name).HasMaxLength(100); });
        b.Entity<IntraopRecord>(e =>
        {
            e.ToTable("IntraopRecords");
            e.HasKey(x => x.EpisodeId);
            foreach (var p in new[] { nameof(IntraopRecord.Position), nameof(IntraopRecord.PositioningAids), nameof(IntraopRecord.SkinPrep), nameof(IntraopRecord.DiathermyUnit),
                         nameof(IntraopRecord.DiathermyPadSite), nameof(IntraopRecord.TourniquetSite), nameof(IntraopRecord.Drains), nameof(IntraopRecord.Dressings) })
                e.Property(p).HasMaxLength(200);
            e.Property(x => x.WoundClass).HasMaxLength(30); e.Property(x => x.Notes).HasMaxLength(2000); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasOne<Episode>().WithOne().HasForeignKey<IntraopRecord>(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SurgicalCount>(e =>
        {
            e.ToTable("Counts");
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Item).HasMaxLength(40); e.Property(x => x.CountedBy).HasMaxLength(100);
            e.Property(x => x.CheckedBy).HasMaxLength(100); e.Property(x => x.ResolutionNote).HasMaxLength(500);
            e.HasIndex(x => new { x.EpisodeId, x.Stage });
            e.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Specimen>(e =>
        {
            e.Property(x => x.Site).HasMaxLength(200); e.Property(x => x.Test).HasMaxLength(60); e.Property(x => x.Container).HasMaxLength(60);
            e.Property(x => x.CollectedBy).HasMaxLength(100); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.SentBy).HasMaxLength(100); e.Property(x => x.Courier).HasMaxLength(100);
            e.HasIndex(x => new { x.EpisodeId, x.Number }).IsUnique(); e.HasIndex(x => x.Status);
            e.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Observation>(e =>
        {
            e.Property(x => x.OxygenLitres).HasPrecision(4, 1); e.Property(x => x.TemperatureC).HasPrecision(4, 1); e.Property(x => x.Consciousness).HasMaxLength(1);
            e.Property(x => x.Escalation).HasMaxLength(300); e.Property(x => x.Source).HasMaxLength(10); e.Property(x => x.RecordedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.EpisodeId, x.At }); e.HasIndex(x => x.DeviceId);
            e.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<MedicationGiven>(e =>
        {
            e.ToTable("MedicationsGiven");
            e.Property(x => x.Medicine).HasMaxLength(150); e.Property(x => x.Dose).HasMaxLength(60); e.Property(x => x.Route).HasMaxLength(30);
            e.Property(x => x.GivenBy).HasMaxLength(100); e.Property(x => x.WitnessedBy).HasMaxLength(100); e.Property(x => x.Barcode).HasMaxLength(60); e.Property(x => x.Notes).HasMaxLength(300);
            e.HasIndex(x => new { x.EpisodeId, x.GivenAt }); e.HasIndex(x => x.ControlledDrugId);
            e.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ControlledDrug>().WithMany().HasForeignKey(x => x.ControlledDrugId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ControlledDrug>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100); e.Property(x => x.Strength).HasMaxLength(40); e.Property(x => x.Form).HasMaxLength(30);
            e.HasIndex(x => new { x.Name, x.Strength, x.Form }).IsUnique();
        });
        b.Entity<ControlledDrugEntry>(e =>
        {
            e.ToTable("ControlledDrugRegister");
            e.Property(x => x.Movement).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.By).HasMaxLength(100); e.Property(x => x.WitnessedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.ControlledDrugId, x.At }); e.HasIndex(x => x.PatientId); e.HasIndex(x => x.EpisodeId);
            e.HasOne<ControlledDrug>().WithMany().HasForeignKey(x => x.ControlledDrugId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<FluidEntry>(e =>
        {
            e.ToTable("FluidBalance");
            e.Property(x => x.Kind).HasMaxLength(20); e.Property(x => x.By).HasMaxLength(100);
            e.HasIndex(x => new { x.EpisodeId, x.At });
            e.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<BloodUnit>(e =>
        {
            e.Property(x => x.UnitNumber).HasMaxLength(40); e.Property(x => x.Product).HasMaxLength(60); e.Property(x => x.CheckedBy1).HasMaxLength(100); e.Property(x => x.CheckedBy2).HasMaxLength(100);
            e.Property(x => x.ReactionDetails).HasMaxLength(500); e.Property(x => x.RecordedBy).HasMaxLength(100);
            e.HasIndex(x => x.EpisodeId); e.HasIndex(x => x.UnitNumber);
            e.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ClinicalNote>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30); e.Property(x => x.Text).HasMaxLength(20000); e.Property(x => x.Author).HasMaxLength(100);
            e.HasIndex(x => new { x.EpisodeId, x.At }); e.HasIndex(x => x.AmendsNoteId);
            e.HasOne<Episode>().WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ClinicalNote>().WithMany().HasForeignKey(x => x.AmendsNoteId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<NoteTemplate>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30); e.Property(x => x.Body).HasMaxLength(10000);
            e.HasIndex(x => x.Name).IsUnique();
        });
    }
}

public sealed class EmrDbContextFactory : IDesignTimeDbContextFactory<EmrDbContext>
{
    public EmrDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<EmrDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", EmrDbContext.Schema)).Options);
}
