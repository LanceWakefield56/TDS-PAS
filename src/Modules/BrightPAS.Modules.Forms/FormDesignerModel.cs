using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Modules.Forms;

public enum FormCategory { Clinical = 1, Consent = 2, Audit = 3, Cssd = 4, Admin = 5, PatientQuestionnaire = 6 }
public enum DesignStatus { Draft = 1, Testing = 2, Published = 3, Retired = 4 }
public enum DesignSource { Designer = 1, Csv = 2, Fhir = 3, Pdf = 4, Copy = 5 }
public enum FieldType { Heading = 1, Text = 2, LongText = 3, Number = 4, Date = 5, YesNo = 6, Choice = 7, MultiChoice = 8, Signature = 9, Calculated = 10, Nhi = 11 }
public enum EntryStatus { InProgress = 1, Completed = 2, Voided = 3 }

/// <summary>A form (spec 7.23) – the code stays the same across versions.</summary>
public sealed class FormDesign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public FormCategory Category { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<FormDesignVersion> Versions { get; set; } = [];
}

/// <summary>One version: draft → testing → published (clinical forms need Clinical Governance approval) → retired. Entries keep the version they started on.</summary>
public sealed class FormDesignVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DesignId { get; set; }
    public int Version { get; set; }
    public DesignStatus Status { get; set; } = DesignStatus.Draft;
    public DesignSource Source { get; set; } = DesignSource.Designer;
    public string? Header { get; set; }
    public string? Footer { get; set; }
    /// <summary>Third-party or licensed content (e.g. a licensed outcome measure): who holds the licence. Publishing a flagged form needs it.</summary>
    public bool ThirdParty { get; set; }
    public string? LicenceNote { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovalNote { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public List<FormField> Fields { get; set; } = [];
}

public sealed class FormField
{
    public Guid VersionId { get; set; }
    public string Key { get; set; } = "";
    public int Sequence { get; set; }
    public string? Section { get; set; }
    public string Label { get; set; } = "";
    public FieldType Type { get; set; }
    public bool Required { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    /// <summary>Show only when the field <see cref="ShowIfKey"/> has the value <see cref="ShowIfValue"/>.</summary>
    public string? ShowIfKey { get; set; }
    public string? ShowIfValue { get; set; }
    /// <summary>Calculated fields: bmi(weight,height) · sum(a,b,…) (scores of choices, yes = 1) · count(a,b,…) (answers that are yes).</summary>
    public string? Calculation { get; set; }
    /// <summary>The record data element the answer flows into (see <see cref="FormLogic.DataElements"/>).</summary>
    public string? MapsTo { get; set; }
    public string? Help { get; set; }
    public List<FieldOption> Options { get; set; } = [];
}

public sealed class FieldOption
{
    public Guid VersionId { get; set; }
    public string FieldKey { get; set; } = "";
    public int Sequence { get; set; }
    public string Value { get; set; } = "";
    public int? Score { get; set; }
}

/// <summary>A filled-in form for a patient (or not, e.g. a CSSD checklist), on the version it started on.</summary>
public sealed class FormEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? BookingId { get; set; }
    public EntryStatus Status { get; set; } = EntryStatus.InProgress;
    public string StartedBy { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public string? CompletedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? VoidReason { get; set; }
    public List<EntryValue> Values { get; set; } = [];
}

public sealed class EntryValue
{
    public Guid EntryId { get; set; }
    public string FieldKey { get; set; } = "";
    public string Value { get; set; } = "";
}

public static class FormDesignerModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<FormDesign>(e =>
        {
            e.ToTable("Designs");
            e.Property(x => x.Code).HasMaxLength(30); e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Category).HasConversion<string>().HasMaxLength(24); e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.DesignId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<FormDesignVersion>(e =>
        {
            e.ToTable("DesignVersions");
            e.HasIndex(x => new { x.DesignId, x.Version }).IsUnique();
            e.HasIndex(x => x.DesignId).IsUnique().HasFilter("[Status] = 'Published'").HasDatabaseName("UX_DesignVersions_OnePublished");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Source).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Header).HasMaxLength(500); e.Property(x => x.Footer).HasMaxLength(500); e.Property(x => x.LicenceNote).HasMaxLength(500);
            e.Property(x => x.ApprovedBy).HasMaxLength(100); e.Property(x => x.ApprovalNote).HasMaxLength(1000); e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.HasMany(x => x.Fields).WithOne().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<FormField>(e =>
        {
            e.ToTable("Fields");
            e.HasKey(x => new { x.VersionId, x.Key });
            e.HasIndex(x => new { x.VersionId, x.Sequence }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(40); e.Property(x => x.Section).HasMaxLength(100); e.Property(x => x.Label).HasMaxLength(300);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Min).HasPrecision(12, 3); e.Property(x => x.Max).HasPrecision(12, 3);
            e.Property(x => x.ShowIfKey).HasMaxLength(40); e.Property(x => x.ShowIfValue).HasMaxLength(100); e.Property(x => x.Calculation).HasMaxLength(200);
            e.Property(x => x.MapsTo).HasMaxLength(30); e.Property(x => x.Help).HasMaxLength(500);
            e.HasMany(x => x.Options).WithOne().HasForeignKey(x => new { x.VersionId, x.FieldKey }).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<FieldOption>(e =>
        {
            e.ToTable("FieldOptions");
            e.HasKey(x => new { x.VersionId, x.FieldKey, x.Sequence });
            e.Property(x => x.FieldKey).HasMaxLength(40); e.Property(x => x.Value).HasMaxLength(200);
        });
        b.Entity<FormEntry>(e =>
        {
            e.ToTable("Entries");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.StartedBy).HasMaxLength(100); e.Property(x => x.CompletedBy).HasMaxLength(100); e.Property(x => x.VoidReason).HasMaxLength(500);
            e.HasIndex(x => x.VersionId); e.HasIndex(x => x.PatientId); e.HasIndex(x => x.BookingId);
            e.HasOne<FormDesignVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Values).WithOne().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<EntryValue>(e =>
        {
            e.ToTable("EntryValues");
            e.HasKey(x => new { x.EntryId, x.FieldKey });
            e.Property(x => x.FieldKey).HasMaxLength(40); e.Property(x => x.Value).HasMaxLength(4000);
        });
    }
}

/// <summary>Show/hide, validation and calculations (pure – change here only, with a test).</summary>
public static class FormLogic
{
    /// <summary>Record data elements a field can flow into (spec 7.23.1 "bound to the data model").</summary>
    public static readonly IReadOnlyDictionary<string, string> DataElements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["weight_kg"] = "Weight (kg) → triage",
        ["height_cm"] = "Height (cm) → triage",
        ["neck_cm"] = "Neck (cm) → triage",
        ["cfs"] = "Clinical frailty score → triage",
        ["bp_sys"] = "Blood pressure systolic → triage",
        ["bp_dia"] = "Blood pressure diastolic → triage",
        ["allergy"] = "Allergy → patient alert",
    };

    public static bool Visible(FormField f, IReadOnlyDictionary<string, string> values) =>
        f.ShowIfKey is null || values.TryGetValue(f.ShowIfKey, out var v) && string.Equals(v, f.ShowIfValue, StringComparison.OrdinalIgnoreCase);

    private static decimal? Num(IReadOnlyDictionary<string, string> v, string k) => v.TryGetValue(k, out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>Works out every calculated field from the answers (choices score their option score; yes = 1).</summary>
    public static Dictionary<string, string> Calculate(IReadOnlyList<FormField> fields, IReadOnlyDictionary<string, string> answers)
    {
        var v = new Dictionary<string, string>(answers, StringComparer.Ordinal);
        foreach (var f in fields.Where(f => f.Type == FieldType.Calculated && f.Calculation is not null).OrderBy(f => f.Sequence))
        {
            var calc = f.Calculation!.Trim();
            var open = calc.IndexOf('(', StringComparison.Ordinal);
            if (open < 1 || !calc.EndsWith(')')) continue;
            var fn = calc[..open].Trim().ToLowerInvariant();
            var args = calc[(open + 1)..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            decimal? result = null;
            if (fn == "bmi" && args.Length == 2 && Num(v, args[0]) is { } w && Num(v, args[1]) is { } h && h > 0) result = Math.Round(w / (h / 100m * (h / 100m)), 1);
            else if (fn is "sum" or "count")
            {
                decimal total = 0;
                foreach (var a in args)
                {
                    var src = fields.FirstOrDefault(x => x.Key == a);
                    if (!v.TryGetValue(a, out var s)) continue;
                    if (fn == "count") { if (s.Equals("yes", StringComparison.OrdinalIgnoreCase)) total++; continue; }
                    if (src?.Type is FieldType.Choice && src.Options.FirstOrDefault(o => o.Value == s)?.Score is { } sc) total += sc;
                    else if (src?.Type is FieldType.MultiChoice) total += s.Split('|').Sum(x => src.Options.FirstOrDefault(o => o.Value == x)?.Score ?? 0);
                    else if (s.Equals("yes", StringComparison.OrdinalIgnoreCase)) total++;
                    else if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) total += n;
                }
                result = total;
            }
            if (result is { } r) v[f.Key] = r.ToString(CultureInfo.InvariantCulture); else v.Remove(f.Key);
        }
        return v;
    }

    /// <summary>Checks the answers: required visible fields, number ranges, dates, choices, NHI checksum. Hidden fields' answers are dropped.</summary>
    public static (Dictionary<string, string> Clean, List<string> Errors) Validate(IReadOnlyList<FormField> fields, IReadOnlyDictionary<string, string> answers, bool complete)
    {
        var errors = new List<string>();
        var clean = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in fields.Where(f => f.Type is not (FieldType.Heading or FieldType.Calculated)).OrderBy(f => f.Sequence))
        {
            if (!Visible(f, answers)) continue;
            answers.TryGetValue(f.Key, out var raw);
            var s = raw?.Trim();
            if (string.IsNullOrEmpty(s)) { if (complete && f.Required) errors.Add($"{f.Label}: please answer."); continue; }
            if (s.Length > 4000) s = s[..4000];
            switch (f.Type)
            {
                case FieldType.Number:
                    if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) { errors.Add($"{f.Label}: enter a number."); continue; }
                    if (f.Min is { } mn && n < mn || f.Max is { } mx && n > mx) { errors.Add($"{f.Label}: must be between {f.Min} and {f.Max}."); continue; }
                    break;
                case FieldType.Date: if (!DateOnly.TryParse(s, CultureInfo.InvariantCulture, out _)) { errors.Add($"{f.Label}: enter a date."); continue; } break;
                case FieldType.YesNo: s = s.ToLowerInvariant(); if (s is not ("yes" or "no")) { errors.Add($"{f.Label}: answer yes or no."); continue; } break;
                case FieldType.Choice: if (f.Options.All(o => o.Value != s)) { errors.Add($"{f.Label}: choose one of the options."); continue; } break;
                case FieldType.MultiChoice: if (s.Split('|').Any(x => f.Options.All(o => o.Value != x))) { errors.Add($"{f.Label}: choose from the options."); continue; } break;
                case FieldType.Nhi: if (!BrightPAS.Core.Patients.NhiValidator.Validate(s).IsValid) { errors.Add($"{f.Label}: not a valid NHI."); continue; } s = s.ToUpperInvariant(); break;
            }
            clean[f.Key] = s;
        }
        return (clean, errors);
    }

    public static string KeyFrom(string label, ISet<string> used)
    {
        var k = new string([.. label.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_')]).Trim('_');
        while (k.Contains("__", StringComparison.Ordinal)) k = k.Replace("__", "_", StringComparison.Ordinal);
        if (k.Length == 0) k = "q";
        if (k.Length > 36) k = k[..36].TrimEnd('_');
        if (char.IsDigit(k[0])) k = "q_" + k;
        var key = k; var i = 2;
        while (!used.Add(key)) key = $"{k}_{i++}";
        return key;
    }
}
