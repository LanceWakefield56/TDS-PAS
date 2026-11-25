using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Medicines;

/// <summary>
/// Medicines and medicine safety checks (0.27.0, ADR 0040). The medicines list (NZULM monthly files and TDS's own list – names, NZMT codes,
/// ingredients, strengths, barcodes) and TDS's clinician-approved, versioned medicine rules (dose limits, allergy groups and cross-reactions,
/// interactions). Every medicine charted in the clinical record is checked against the patient's allergies, age, weight, what has already been
/// given and their regular medicines. NZULM carries no dosing or interaction information, so the rules are TDS's own until a licensed source
/// (e.g. the NZ Formulary) is added. BrightPAS suggests; the clinician decides.
/// </summary>
public sealed class MedicinesModule : IPasModule
{
    public const string Id = "medicines";
    public ModuleManifest Manifest { get; } = new(Id, "Medicines and medicine safety", typeof(MedicinesModule).Assembly.GetName().Version ?? new Version(0, 0),
        MedicinesDbContext.Schema, [], "Medicines list (NZULM and TDS), clinician-approved dose limits, allergy groups and interactions, checks when medicines are given.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<MedicinesDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, MedicinesDbContext.Schema));
        services.AddScoped<MedicineCatalogue>();
        services.AddScoped<MedicineRulesService>();
        services.AddScoped<MedicineSafetyService>();
        services.AddScoped<NzfService>();
    }
}

/// <summary>How serious a finding is. Stop needs a reason to go ahead (when the checks are enforced); Warn is shown; Info explains what was not checked.</summary>
public enum CheckSeverity { Info = 1, Warn = 2, Stop = 3 }

public enum FindingKind { Allergy = 1, Dose = 2, Interaction = 3, Duplicate = 4, NotChecked = 5 }

public enum MedRuleSetStatus { Draft = 1, Approved = 2, Retired = 3 }

/// <summary>Off: no checks. Advise: findings are shown, nothing is blocked. Enforce: a Stop finding needs an override reason.</summary>
public enum CheckMode { Off = 0, Advise = 1, Enforce = 2 }

public enum CheckResult { Clear = 1, Warnings = 2, Overridden = 3, Stopped = 4, NotChecked = 5 }

// ---------------------------------------------------------------------------------------------------- medicines list

/// <summary>An active ingredient (substance), named as in the NZMT, lower case (e.g. "paracetamol"). Rules refer to ingredients by name.</summary>
public sealed class Ingredient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>NZMT substance id (SCTID), when known.</summary>
    public string? NzmtId { get; set; }
    public List<IngredientAlias> Aliases { get; set; } = [];
}

/// <summary>Another name for an ingredient – brand names, older or overseas names (lignocaine, Panadol, acetaminophen). Lower case, unique.</summary>
public sealed class IngredientAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IngredientId { get; set; }
    public string Alias { get; set; } = "";
}

/// <summary>A medicine as it is given: product, strength and form (e.g. "Paracetamol 1 g/100 mL injection").</summary>
public sealed class Medicine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>NZMT id (MPUU/TPUU SCTID) when imported from the NZULM.</summary>
    public string? NzmtId { get; set; }
    public string? Form { get; set; }
    public string? DefaultRoute { get; set; }
    /// <summary>"TDS" (entered or imported from TDS's list), "NZULM" (monthly file) or "Example" (starter list).</summary>
    public string Source { get; set; } = "TDS";
    /// <summary>On TDS's formulary – shown first when charting.</summary>
    public bool OnFormulary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public List<MedicineIngredient> Ingredients { get; set; } = [];
    public List<MedicineGtin> Gtins { get; set; } = [];
}

/// <summary>An ingredient of a medicine with its strength per dose unit (tablet, capsule, vial) or per <see cref="PerMl"/> mL for liquids.</summary>
public sealed class MedicineIngredient
{
    public Guid MedicineId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal? Amount { get; set; }
    /// <summary>mg, microgram, g, unit or mmol.</summary>
    public string? Unit { get; set; }
    /// <summary>For liquids: the amount is in this many mL (10 mg/mL → Amount 10, PerMl 1; 1 g in 100 mL → 1000 mg, PerMl 100).</summary>
    public decimal? PerMl { get; set; }
}

/// <summary>A pack barcode (GTIN-8/12/13/14) for a medicine – scanned when giving it.</summary>
public sealed class MedicineGtin
{
    public string Gtin { get; set; } = "";
    public Guid MedicineId { get; set; }
}

/// <summary>A saved column layout for importing a medicines file (e.g. the NZULM monthly file), so each month is one click.</summary>
public sealed class ImportProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Source { get; set; } = "NZULM";
    public string NameColumn { get; set; } = "";
    public string? NzmtColumn { get; set; }
    public string? FormColumn { get; set; }
    public string? RouteColumn { get; set; }
    public string? IngredientColumn { get; set; }
    public string? AmountColumn { get; set; }
    public string? UnitColumn { get; set; }
    public string? PerMlColumn { get; set; }
    public string? GtinColumn { get; set; }
    /// <summary>Medicines from this source that are not in the new file are made inactive (monthly full files).</summary>
    public bool DeactivateMissing { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

/// <summary>One import of a medicines file – what it did, kept for the audit trail.</summary>
public sealed class ImportRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string Source { get; set; } = "";
    public int Rows { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Deactivated { get; set; }
    public int Skipped { get; set; }
    public string? Problems { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public string ImportedBy { get; set; } = "";
}

// ---------------------------------------------------------------------------------------------------- rules (versioned)

/// <summary>A version of TDS's medicine rules. One approved set is used for patients; an approved set never changes – edits start a new draft.</summary>
public sealed class MedRuleSet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public MedRuleSetStatus Status { get; set; } = MedRuleSetStatus.Draft;
    /// <summary>Built from BrightPAS's example rules – must be reviewed before use.</summary>
    public bool IsExample { get; set; }
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public string? ApprovedRole { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovalNote { get; set; }
    public List<DoseRule> DoseRules { get; set; } = [];
    public List<MedicineGroup> Groups { get; set; } = [];
    public List<CrossReaction> CrossReactions { get; set; } = [];
    public List<InteractionRule> Interactions { get; set; } = [];
}

/// <summary>
/// A dose limit for an ingredient, optionally by route and age band. Limits are in <see cref="Unit"/> (mg, microgram, unit or mmol); any can be blank.
/// Age band: MinAgeYears ≤ age &lt; MaxAgeYears.
/// </summary>
public sealed class DoseRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleSetId { get; set; }
    public string Code { get; set; } = "";
    public string Ingredient { get; set; } = "";
    /// <summary>Blank = any route. Otherwise e.g. Oral, IV, IM, SC, PR, Topical, Infiltration.</summary>
    public string? Route { get; set; }
    public int? MinAgeYears { get; set; }
    public int? MaxAgeYears { get; set; }
    /// <summary>Weight band (0.27.0, from the NZ Formulary): MinWeightKg ≤ weight &lt; MaxWeightKg. A banded rule needs the weight to apply.</summary>
    public decimal? MinWeightKg { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public string Unit { get; set; } = "mg";
    public decimal? MaxSingle { get; set; }
    public decimal? MaxDaily { get; set; }
    public decimal? MaxSinglePerKg { get; set; }
    public decimal? MaxDailyPerKg { get; set; }
    public decimal? MinIntervalHours { get; set; }
    public CheckSeverity Severity { get; set; } = CheckSeverity.Stop;
    public string? Reference { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// A group of medicines – an allergy class (penicillins, cephalosporins, NSAIDs) and/or a class used in interaction rules (QT-prolonging,
/// anticoagulants). Members are ingredient names; keywords are extra words that identify the group in allergy text ("penicillin", "sulpha").
/// </summary>
public sealed class MedicineGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleSetId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>An allergy to one member is checked against the other members.</summary>
    public bool AllergyClass { get; set; }
    /// <summary>For allergy classes: how serious giving another member is (penicillins Stop; opioids, often intolerance, Warn).</summary>
    public CheckSeverity SameGroupSeverity { get; set; } = CheckSeverity.Stop;
    public List<MedicineGroupMember> Members { get; set; } = [];
}

public sealed class MedicineGroupMember
{
    public Guid GroupId { get; set; }
    /// <summary>Ingredient name (lower case), or a keyword when <see cref="IsKeyword"/>.</summary>
    public string Term { get; set; } = "";
    public bool IsKeyword { get; set; }
}

/// <summary>An allergy to one group that may also apply to another (penicillins → cephalosporins).</summary>
public sealed class CrossReaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleSetId { get; set; }
    public string FromGroup { get; set; } = "";
    public string ToGroup { get; set; } = "";
    public CheckSeverity Severity { get; set; } = CheckSeverity.Warn;
    public string Message { get; set; } = "";
}

/// <summary>
/// Two medicines that should not be given together, or only with care. Each side is an ingredient name or "group:CODE". The same group on both
/// sides means any two different members (two QT-prolonging medicines).
/// </summary>
public sealed class InteractionRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleSetId { get; set; }
    public string Code { get; set; } = "";
    public string TermA { get; set; } = "";
    public string TermB { get; set; } = "";
    public CheckSeverity Severity { get; set; } = CheckSeverity.Warn;
    public string Effect { get; set; } = "";
    public string? Advice { get; set; }
    public string? Reference { get; set; }
}

// ---------------------------------------------------------------------------------------------------- patient links, settings, check log

/// <summary>Links a patient allergy alert to what it is an allergy to – an ingredient or "group:CODE" – so checks do not rely on reading free text.</summary>
public sealed class AllergyLink
{
    public Guid AlertId { get; set; }
    public Guid PatientId { get; set; }
    public string Term { get; set; } = "";
    public string LinkedBy { get; set; } = "";
    public DateTimeOffset LinkedAt { get; set; }
}

public sealed class MedSettings
{
    public int Id { get; set; } = 1;
    /// <summary>New installs and upgrades start at Advise (shown, never blocked) so nothing is held up until the rules are approved.</summary>
    public CheckMode Mode { get; set; } = CheckMode.Advise;
    /// <summary>Check against the medicines the patient listed in the pre-admission questionnaire.</summary>
    public bool CheckRegularMedicines { get; set; } = true;
    /// <summary>The same ingredient given again within this many minutes (with no interval rule) is flagged as a possible double dose.</summary>
    public int DuplicateWithinMinutes { get; set; } = 60;
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>A check of one medicine for one patient: what was found and what the clinician did.</summary>
public sealed class MedicineCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? EpisodeId { get; set; }
    /// <summary>The eMR medicine row it was recorded with.</summary>
    public Guid? MedicationId { get; set; }
    public Guid? MedicineId { get; set; }
    public string MedicineText { get; set; } = "";
    public string Dose { get; set; } = "";
    public string Route { get; set; } = "";
    public CheckResult Result { get; set; }
    public string? OverrideReason { get; set; }
    public int? RuleSetVersion { get; set; }
    public CheckMode Mode { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public string CheckedBy { get; set; } = "";
    public List<MedicineCheckFinding> Findings { get; set; } = [];
}

public sealed class MedicineCheckFinding
{
    public Guid CheckId { get; set; }
    public int Sequence { get; set; }
    public FindingKind Kind { get; set; }
    public CheckSeverity Severity { get; set; }
    public string Text { get; set; } = "";
    public string? RuleRef { get; set; }
}

// ---------------------------------------------------------------------------------------------------- NZ Formulary (NZF and NZFC)

/// <summary>One imported NZ Formulary PDF (monthly, first working day). Only the current release of each edition keeps its monographs.</summary>
public sealed class NzfRelease
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>NZF (adults) or NZFC (children).</summary>
    public string Edition { get; set; } = "NZF";
    public int? ReleaseNumber { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int Monographs { get; set; }
    public int Doses { get; set; }
    public bool IsCurrent { get; set; }
    public string? Problems { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public string ImportedBy { get; set; } = "";
}

/// <summary>A drug monograph from the NZF or NZFC: the sections a clinician checks, as published.</summary>
public sealed class NzfMonograph
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReleaseId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>The ingredient it is about (qualifiers and salt words removed) – how BrightPAS finds it.</summary>
    public string BaseName { get; set; } = "";
    public string? Synonyms { get; set; }
    public string? Chapter { get; set; }
    public string? Indications { get; set; }
    public string? ContraIndications { get; set; }
    public string? Cautions { get; set; }
    public string? HepaticImpairment { get; set; }
    public string? RenalImpairment { get; set; }
    public string? Pregnancy { get; set; }
    public string? BreastFeeding { get; set; }
    public string? AdverseEffects { get; set; }
    public string? PatientAdvice { get; set; }
    public string? OtherSections { get; set; }
    public List<NzfDose> Doses { get; set; } = [];
    public List<NzfPreparation> Preparations { get; set; } = [];
}

/// <summary>One dosing line: indication, route, population and dose as published, with the limits BrightPAS read from it.</summary>
public sealed class NzfDose
{
    public Guid MonographId { get; set; }
    public int Sequence { get; set; }
    public string Indication { get; set; } = "";
    public string Route { get; set; } = "";
    public string RouteCodes { get; set; } = "";
    public string Population { get; set; } = "";
    public string Dose { get; set; } = "";
    public int? MinAgeYears { get; set; }
    public int? MaxAgeYears { get; set; }
    public decimal? MinWeightKg { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public bool Neonate { get; set; }
    public string? Unit { get; set; }
    public decimal? MaxSingle { get; set; }
    public decimal? MaxSinglePerKg { get; set; }
    public decimal? MaxDaily { get; set; }
    public decimal? MaxDailyPerKg { get; set; }
    public decimal? IntervalHours { get; set; }
    public bool Titration { get; set; }
}

public sealed class NzfPreparation
{
    public Guid MonographId { get; set; }
    public int Sequence { get; set; }
    public string Form { get; set; } = "";
    public string Strength { get; set; } = "";
    public string? Subsidy { get; set; }
}

/// <summary>Where to download the NZF PDFs from each month (optional – files can always be uploaded).</summary>
public sealed class NzfSettings
{
    /// <summary>The NZF's download address (from Lance, Nov 2026). Automatic download stays off until an administrator turns it on.</summary>
    public const string DefaultUrl = "https://www.nzf.org.nz/home/downloadpdf";
    public int Id { get; set; } = 1;
    public string? NzfUrl { get; set; }
    public string? NzfcUrl { get; set; }
    /// <summary>Download and import on the first working day of each month (the NZF release day), and daily after that until it succeeds.</summary>
    public bool AutoDownload { get; set; }
    /// <summary>A folder (network share or USB) checked every hour: any new NZF/NZFC PDF put there is imported, then moved to "imported".</summary>
    public string? WatchFolder { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string? LastProblem { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

// ---------------------------------------------------------------------------------------------------- database

public sealed class MedicinesDbContext(DbContextOptions<MedicinesDbContext> options) : DbContext(options)
{
    public const string Schema = "meds";
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<IngredientAlias> Aliases => Set<IngredientAlias>();
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<MedicineIngredient> MedicineIngredients => Set<MedicineIngredient>();
    public DbSet<MedicineGtin> Gtins => Set<MedicineGtin>();
    public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();
    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();
    public DbSet<MedRuleSet> RuleSets => Set<MedRuleSet>();
    public DbSet<DoseRule> DoseRules => Set<DoseRule>();
    public DbSet<MedicineGroup> Groups => Set<MedicineGroup>();
    public DbSet<MedicineGroupMember> GroupMembers => Set<MedicineGroupMember>();
    public DbSet<CrossReaction> CrossReactions => Set<CrossReaction>();
    public DbSet<InteractionRule> Interactions => Set<InteractionRule>();
    public DbSet<AllergyLink> AllergyLinks => Set<AllergyLink>();
    public DbSet<MedSettings> Settings => Set<MedSettings>();
    public DbSet<MedicineCheck> Checks => Set<MedicineCheck>();
    public DbSet<MedicineCheckFinding> CheckFindings => Set<MedicineCheckFinding>();
    public DbSet<NzfRelease> NzfReleases => Set<NzfRelease>();
    public DbSet<NzfMonograph> NzfMonographs => Set<NzfMonograph>();
    public DbSet<NzfDose> NzfDoses => Set<NzfDose>();
    public DbSet<NzfPreparation> NzfPreparations => Set<NzfPreparation>();
    public DbSet<NzfSettings> NzfSettings => Set<NzfSettings>();

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
        b.Entity<Ingredient>(e =>
        {
            e.ToTable("Ingredients");
            e.Property(x => x.Name).HasMaxLength(150); e.Property(x => x.NzmtId).HasMaxLength(20);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.NzmtId).HasFilter("[NzmtId] IS NOT NULL");
            e.HasMany(x => x.Aliases).WithOne().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<IngredientAlias>(e =>
        {
            e.ToTable("IngredientAliases");
            e.Property(x => x.Alias).HasMaxLength(150);
            e.HasIndex(x => x.Alias).IsUnique();
            e.HasIndex(x => x.IngredientId);
        });
        b.Entity<Medicine>(e =>
        {
            e.ToTable("Medicines");
            e.Property(x => x.Name).HasMaxLength(250); e.Property(x => x.NzmtId).HasMaxLength(20);
            e.Property(x => x.Form).HasMaxLength(60); e.Property(x => x.DefaultRoute).HasMaxLength(30);
            e.Property(x => x.Source).HasMaxLength(10); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Name); e.HasIndex(x => new { x.OnFormulary, x.IsActive });
            e.HasIndex(x => x.NzmtId).IsUnique().HasFilter("[NzmtId] IS NOT NULL");
            e.HasMany(x => x.Ingredients).WithOne().HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Gtins).WithOne().HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<MedicineIngredient>(e =>
        {
            e.ToTable("MedicineIngredients");
            e.HasKey(x => new { x.MedicineId, x.IngredientId });
            e.Property(x => x.Amount).HasPrecision(12, 4); e.Property(x => x.Unit).HasMaxLength(12); e.Property(x => x.PerMl).HasPrecision(10, 3);
            e.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.IngredientId);
        });
        b.Entity<MedicineGtin>(e =>
        {
            e.ToTable("MedicineGtins");
            e.HasKey(x => x.Gtin);
            e.Property(x => x.Gtin).HasMaxLength(14);
            e.HasIndex(x => x.MedicineId);
        });
        b.Entity<ImportProfile>(e =>
        {
            e.ToTable("ImportProfiles");
            e.Property(x => x.Name).HasMaxLength(100); e.Property(x => x.Source).HasMaxLength(10);
            foreach (var p in new[] { nameof(ImportProfile.NameColumn), nameof(ImportProfile.NzmtColumn), nameof(ImportProfile.FormColumn), nameof(ImportProfile.RouteColumn), nameof(ImportProfile.IngredientColumn),
                         nameof(ImportProfile.AmountColumn), nameof(ImportProfile.UnitColumn), nameof(ImportProfile.PerMlColumn), nameof(ImportProfile.GtinColumn) })
                e.Property(p).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
        });
        b.Entity<ImportRun>(e =>
        {
            e.ToTable("ImportRuns");
            e.Property(x => x.FileName).HasMaxLength(260); e.Property(x => x.Source).HasMaxLength(10);
            e.Property(x => x.Problems).HasMaxLength(4000); e.Property(x => x.ImportedBy).HasMaxLength(100);
            e.HasIndex(x => x.ImportedAt);
        });
        b.Entity<MedRuleSet>(e =>
        {
            e.ToTable("RuleSets");
            e.HasIndex(x => x.Version).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150); e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.CreatedBy).HasMaxLength(100); e.Property(x => x.ApprovedBy).HasMaxLength(150); e.Property(x => x.ApprovedRole).HasMaxLength(100);
            e.Property(x => x.ApprovalNote).HasMaxLength(1000);
            e.HasMany(x => x.DoseRules).WithOne().HasForeignKey(x => x.RuleSetId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Groups).WithOne().HasForeignKey(x => x.RuleSetId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.CrossReactions).WithOne().HasForeignKey(x => x.RuleSetId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Interactions).WithOne().HasForeignKey(x => x.RuleSetId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Status).IsUnique().HasFilter("[Status] = 'Approved'").HasDatabaseName("UX_MedRuleSets_OneApproved");
        });
        b.Entity<DoseRule>(e =>
        {
            e.ToTable("DoseRules");
            e.Property(x => x.Code).HasMaxLength(20); e.Property(x => x.Ingredient).HasMaxLength(150); e.Property(x => x.Route).HasMaxLength(30);
            e.Property(x => x.Unit).HasMaxLength(12);
            e.Property(x => x.MaxSingle).HasPrecision(12, 4); e.Property(x => x.MaxDaily).HasPrecision(12, 4);
            e.Property(x => x.MaxSinglePerKg).HasPrecision(10, 4); e.Property(x => x.MaxDailyPerKg).HasPrecision(10, 4); e.Property(x => x.MinIntervalHours).HasPrecision(6, 2);
            e.Property(x => x.MinWeightKg).HasPrecision(6, 2); e.Property(x => x.MaxWeightKg).HasPrecision(6, 2);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(6);
            e.Property(x => x.Reference).HasMaxLength(300); e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.RuleSetId, x.Code }).IsUnique();
        });
        b.Entity<MedicineGroup>(e =>
        {
            e.ToTable("Groups");
            e.Property(x => x.Code).HasMaxLength(20); e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.SameGroupSeverity).HasConversion<string>().HasMaxLength(6);
            e.HasIndex(x => new { x.RuleSetId, x.Code }).IsUnique();
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<MedicineGroupMember>(e =>
        {
            e.ToTable("GroupMembers");
            e.HasKey(x => new { x.GroupId, x.Term });
            e.Property(x => x.Term).HasMaxLength(150);
        });
        b.Entity<CrossReaction>(e =>
        {
            e.ToTable("CrossReactions");
            e.Property(x => x.FromGroup).HasMaxLength(20); e.Property(x => x.ToGroup).HasMaxLength(20);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(6); e.Property(x => x.Message).HasMaxLength(500);
            e.HasIndex(x => new { x.RuleSetId, x.FromGroup, x.ToGroup }).IsUnique();
        });
        b.Entity<InteractionRule>(e =>
        {
            e.ToTable("Interactions");
            e.Property(x => x.Code).HasMaxLength(20); e.Property(x => x.TermA).HasMaxLength(150); e.Property(x => x.TermB).HasMaxLength(150);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(6);
            e.Property(x => x.Effect).HasMaxLength(300); e.Property(x => x.Advice).HasMaxLength(500); e.Property(x => x.Reference).HasMaxLength(300);
            e.HasIndex(x => new { x.RuleSetId, x.Code }).IsUnique();
        });
        b.Entity<AllergyLink>(e =>
        {
            e.ToTable("AllergyLinks");
            e.HasKey(x => x.AlertId);
            e.Property(x => x.AlertId).ValueGeneratedNever();
            e.Property(x => x.Term).HasMaxLength(160); e.Property(x => x.LinkedBy).HasMaxLength(100);
            e.HasIndex(x => x.PatientId);
        });
        b.Entity<MedSettings>(e =>
        {
            e.ToTable("Settings", t => t.HasCheckConstraint("CK_MedSettings_SingleRow", "[Id] = 1"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasData(new MedSettings());
        });
        b.Entity<MedicineCheck>(e =>
        {
            e.ToTable("Checks");
            e.Property(x => x.MedicineText).HasMaxLength(150); e.Property(x => x.Dose).HasMaxLength(60); e.Property(x => x.Route).HasMaxLength(30);
            e.Property(x => x.Result).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.OverrideReason).HasMaxLength(500); e.Property(x => x.CheckedBy).HasMaxLength(100);
            e.HasOne<Medicine>().WithMany().HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Findings).WithOne().HasForeignKey(x => x.CheckId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.PatientId, x.CheckedAt }); e.HasIndex(x => x.EpisodeId); e.HasIndex(x => x.MedicationId); e.HasIndex(x => x.MedicineId);
            e.HasIndex(x => new { x.Result, x.CheckedAt });
        });
        b.Entity<MedicineCheckFinding>(e =>
        {
            e.ToTable("CheckFindings");
            e.HasKey(x => new { x.CheckId, x.Sequence });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(6);
            e.Property(x => x.Text).HasMaxLength(500); e.Property(x => x.RuleRef).HasMaxLength(60);
        });
        b.Entity<NzfRelease>(e =>
        {
            e.ToTable("NzfReleases");
            e.Property(x => x.Edition).HasMaxLength(6); e.Property(x => x.FileName).HasMaxLength(260); e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.Problems).HasMaxLength(2000); e.Property(x => x.ImportedBy).HasMaxLength(100);
            e.HasIndex(x => x.Edition).IsUnique().HasFilter("[IsCurrent] = 1").HasDatabaseName("UX_NzfReleases_OneCurrent");
            e.HasIndex(x => x.ImportedAt);
        });
        b.Entity<NzfMonograph>(e =>
        {
            e.ToTable("NzfMonographs");
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.BaseName).HasMaxLength(200); e.Property(x => x.Synonyms).HasMaxLength(300); e.Property(x => x.Chapter).HasMaxLength(200);
            e.HasOne<NzfRelease>().WithMany().HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Doses).WithOne().HasForeignKey(x => x.MonographId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Preparations).WithOne().HasForeignKey(x => x.MonographId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ReleaseId, x.BaseName }); e.HasIndex(x => new { x.ReleaseId, x.Name });
        });
        b.Entity<NzfDose>(e =>
        {
            e.ToTable("NzfDoses");
            e.HasKey(x => new { x.MonographId, x.Sequence });
            e.Property(x => x.Indication).HasMaxLength(300); e.Property(x => x.Route).HasMaxLength(150); e.Property(x => x.RouteCodes).HasMaxLength(60);
            e.Property(x => x.Population).HasMaxLength(200); e.Property(x => x.Dose).HasMaxLength(1000); e.Property(x => x.Unit).HasMaxLength(12);
            e.Property(x => x.MinWeightKg).HasPrecision(6, 2); e.Property(x => x.MaxWeightKg).HasPrecision(6, 2);
            e.Property(x => x.MaxSingle).HasPrecision(12, 4); e.Property(x => x.MaxSinglePerKg).HasPrecision(10, 4); e.Property(x => x.MaxDaily).HasPrecision(12, 4);
            e.Property(x => x.MaxDailyPerKg).HasPrecision(10, 4); e.Property(x => x.IntervalHours).HasPrecision(6, 2);
        });
        b.Entity<NzfPreparation>(e =>
        {
            e.ToTable("NzfPreparations");
            e.HasKey(x => new { x.MonographId, x.Sequence });
            e.Property(x => x.Form).HasMaxLength(150); e.Property(x => x.Strength).HasMaxLength(150); e.Property(x => x.Subsidy).HasMaxLength(150);
        });
        b.Entity<NzfSettings>(e =>
        {
            e.ToTable("NzfSettings", t => t.HasCheckConstraint("CK_NzfSettings_SingleRow", "[Id] = 1"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.NzfUrl).HasMaxLength(500); e.Property(x => x.NzfcUrl).HasMaxLength(500); e.Property(x => x.WatchFolder).HasMaxLength(500);
            e.Property(x => x.LastProblem).HasMaxLength(1000); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasData(new NzfSettings { NzfUrl = BrightPAS.Modules.Medicines.NzfSettings.DefaultUrl });
        });
    }
}

public sealed class MedicinesDbContextFactory : IDesignTimeDbContextFactory<MedicinesDbContext>
{
    public MedicinesDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<MedicinesDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", MedicinesDbContext.Schema)).Options);
}
