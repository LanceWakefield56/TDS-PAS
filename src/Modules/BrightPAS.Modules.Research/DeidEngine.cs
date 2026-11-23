using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BrightPAS.Modules.Research;

/// <summary>
/// What a column holds, which decides how each level treats it (spec 7.21.2). DirectId: removed (name, NHI, MRN, address, phone, email).
/// BirthDate: becomes an age band. Date: kept (pseudonymised) or month/year. Place: reduced to region. Clinician: coded A, B, C… (or removed).
/// FreeText: removed. Ethnicity: released only with a Māori data governance note. Category/Number: kept.
/// </summary>
public enum ColumnKind { DirectId, BirthDate, Date, Place, Clinician, FreeText, Ethnicity, Category, Number }

public sealed record ColumnDef(string Key, string Label, ColumnKind Kind, bool QuasiIdentifier = false);

/// <summary>A dataset row: the patient it belongs to (never output) and its values by column key.</summary>
public sealed record DataRow(Guid PatientId, IReadOnlyDictionary<string, string?> Values);

public sealed record DeidResult(IReadOnlyList<string> Headers, IReadOnlyList<string?[]> Rows, int MinGroupSize, int AtRiskRows, int SuppressedCells, int RemovedRows, IReadOnlyList<string> Notes);

/// <summary>De-identification, small-number suppression and the k-anonymity check (pure – change here only, with a test).</summary>
public static partial class DeidEngine
{
    public const string Suppressed = "<5";

    public static string AgeBand(int age) => age >= 90 ? "90+" : $"{age / 5 * 5}-{age / 5 * 5 + 4}";

    /// <summary>Nelson/Tasman/Marlborough towns keep their region; everything else becomes the island – enough for benchmarking, too coarse to identify.</summary>
    public static string Region(string? city)
    {
        if (string.IsNullOrWhiteSpace(city)) return "Not stated";
        var c = city.Trim().ToUpperInvariant();
        string[] nelson = ["NELSON", "STOKE", "RICHMOND", "MOTUEKA", "TAKAKA", "BRIGHTWATER", "WAKEFIELD", "MAPUA", "TAHUNANUI", "COLLINGWOOD", "MURCHISON", "ST ARNAUD", "HOPE", "RUBY BAY", "UPPER MOUTERE"];
        string[] marl = ["BLENHEIM", "PICTON", "HAVELOCK", "RENWICK", "SEDDON", "KAIKOURA"];
        string[] west = ["WESTPORT", "GREYMOUTH", "HOKITIKA", "KARAMEA", "REEFTON"];
        string[] north = ["AUCKLAND", "WELLINGTON", "HAMILTON", "TAURANGA", "NAPIER", "HASTINGS", "PALMERSTON NORTH", "NEW PLYMOUTH", "WHANGAREI", "ROTORUA", "WHANGANUI", "GISBORNE", "LOWER HUTT", "UPPER HUTT", "PORIRUA", "MASTERTON", "TAUPO", "LEVIN"];
        if (nelson.Any(t => c.StartsWith(t, StringComparison.Ordinal))) return "Nelson Tasman";
        if (marl.Any(t => c.StartsWith(t, StringComparison.Ordinal))) return "Marlborough";
        if (west.Any(t => c.StartsWith(t, StringComparison.Ordinal))) return "West Coast";
        if (north.Any(t => c.StartsWith(t, StringComparison.Ordinal))) return "North Island";
        return "Rest of South Island";
    }

    public static string NewStudyCode() => "S" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4))[..7];

    /// <summary>Removes names, NHIs, phone numbers, emails and dates from free text (used only where free text must be kept for review).</summary>
    public static string Scrub(string text, IEnumerable<string> names)
    {
        var s = Nhi().Replace(text, "[NHI]");
        s = Email().Replace(s, "[email]");
        s = Phone().Replace(s, "[phone]");
        s = DateRx().Replace(s, "[date]");
        foreach (var n in names.Where(n => n.Length > 2)) s = Regex.Replace(s, $@"\b{Regex.Escape(n)}\b", "[name]", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return s;
    }

    [GeneratedRegex(@"\b[A-HJ-NP-Z]{3}\d{2}[0-9A-HJ-NP-Z]{2}\b")] private static partial Regex Nhi();
    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.]+")] private static partial Regex Email();
    [GeneratedRegex(@"(\+64|0)[\d\s-]{7,12}\d")] private static partial Regex Phone();
    [GeneratedRegex(@"\b\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}\b|\b\d{4}-\d{2}-\d{2}\b")] private static partial Regex DateRx();

    /// <summary>
    /// Applies a level to the chosen columns. Pseudonymised: direct identifiers replaced by a study code, dates kept, ages in years.
    /// De-identified: no study code, dates to month/year, ages in 5-year bands, places to region, clinicians coded, free text removed; groups of
    /// quasi-identifiers smaller than <paramref name="k"/> are counted (and removed when asked). Aggregate: counts by the chosen columns with
    /// small cells suppressed, plus secondary suppression so a suppressed cell cannot be worked out from a total.
    /// </summary>
    public static DeidResult Apply(DeidLevel level, IReadOnlyList<ColumnDef> defs, IReadOnlyList<string> chosen, IReadOnlyList<DataRow> rows, DateOnly today,
        Func<Guid, string> studyCode, int k = 5, bool removeSmallGroups = false, bool allowEthnicity = false)
    {
        var notes = new List<string>();
        var cols = chosen.Select(c => defs.FirstOrDefault(d => d.Key == c)).Where(d => d is not null).Select(d => d!).ToList();
        if (cols.Any(c => c.Kind == ColumnKind.Ethnicity) && !allowEthnicity) { cols.RemoveAll(c => c.Kind == ColumnKind.Ethnicity); notes.Add("Ethnicity left out: record the Māori data governance agreement to include it."); }
        if (cols.RemoveAll(c => c.Kind == ColumnKind.DirectId) > 0) notes.Add("Direct identifiers (name, NHI, MRN, address, phone, email) are never released.");
        if (level != DeidLevel.Pseudonymised && cols.RemoveAll(c => c.Kind == ColumnKind.FreeText) > 0) notes.Add("Free text removed.");
        var clinicianCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? Transform(ColumnDef c, string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            switch (c.Kind)
            {
                case ColumnKind.BirthDate:
                    if (!DateOnly.TryParse(v, CultureInfo.InvariantCulture, out var dob)) return null;
                    var age = BrightPAS.Core.NzTime.Age(dob, today);
                    return level == DeidLevel.Pseudonymised ? age.ToString(CultureInfo.InvariantCulture) : AgeBand(age);
                case ColumnKind.Date:
                    if (!DateOnly.TryParse(v, CultureInfo.InvariantCulture, out var d)) return null;
                    return level == DeidLevel.Pseudonymised ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                case ColumnKind.Place: return Region(v);
                case ColumnKind.Clinician:
                    if (!clinicianCodes.TryGetValue(v, out var code)) { code = "Clinician " + (char)('A' + clinicianCodes.Count % 26) + (clinicianCodes.Count >= 26 ? (clinicianCodes.Count / 26).ToString(CultureInfo.InvariantCulture) : ""); clinicianCodes[v] = code; }
                    return code;
                case ColumnKind.FreeText: return Scrub(v, []);
                default: return v.Length > 200 ? v[..200] : v;
            }
        }
        var transformed = rows.Select(r => (r.PatientId, Values: cols.Select(c => Transform(c, r.Values.GetValueOrDefault(c.Key))).ToArray())).ToList();
        if (cols.Any(c => c.Kind == ColumnKind.Clinician)) notes.Add("Clinician names replaced by codes.");

        if (level == DeidLevel.Aggregate)
        {
            var groups = transformed.GroupBy(t => string.Join("\u001f", t.Values.Select(v => v ?? "")), StringComparer.Ordinal)
                .Select(g => (Key: g.First().Values, Count: g.Count())).OrderBy(g => string.Join("|", g.Key.Select(v => v ?? "")), StringComparer.Ordinal).ToList();
            var counts = groups.Select(g => (int?)g.Count).ToArray();
            var suppressed = 0;
            for (var i = 0; i < counts.Length; i++) if (counts[i] < k) { counts[i] = null; suppressed++; }
            // Secondary suppression: within each value of the first column, a single suppressed cell could be worked out from the total – suppress the next smallest too.
            if (cols.Count > 1)
                foreach (var block in Enumerable.Range(0, groups.Count).GroupBy(i => groups[i].Key[0] ?? ""))
                {
                    var idx = block.ToList();
                    if (idx.Count(i => counts[i] is null) == 1 && idx.Count > 1)
                    {
                        var next = idx.Where(i => counts[i] is not null).OrderBy(i => counts[i]).FirstOrDefault(-1);
                        if (next >= 0) { counts[next] = null; suppressed++; }
                    }
                }
            if (suppressed > 0) notes.Add($"{suppressed} cell(s) under {k} (or that would reveal one) shown as {Suppressed}.");
            var outRows = groups.Select((g, i) => (string?[])[.. g.Key, counts[i]?.ToString(CultureInfo.InvariantCulture) ?? Suppressed]).ToList();
            var total = transformed.Count;
            outRows.Add([.. cols.Select((_, i) => i == 0 ? "Total" : ""), total < k ? Suppressed : total.ToString(CultureInfo.InvariantCulture)]);
            return new DeidResult([.. cols.Select(c => c.Label), "Count"], outRows, groups.Count == 0 ? 0 : groups.Min(g => g.Count), 0, suppressed, 0, notes);
        }

        // Patient-level: k-anonymity on the quasi-identifiers present.
        var qi = cols.Select((c, i) => (c, i)).Where(x => x.c.QuasiIdentifier || x.c.Kind is ColumnKind.BirthDate or ColumnKind.Place or ColumnKind.Date).Select(x => x.i).ToList();
        var groupSize = transformed.GroupBy(t => string.Join("\u001f", qi.Select(i => t.Values[i] ?? "")), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        int Size((Guid, string?[] Values) t) => groupSize[string.Join("\u001f", qi.Select(i => t.Values[i] ?? ""))];
        var atRisk = transformed.Count(t => Size(t) < k);
        var minGroup = transformed.Count == 0 ? 0 : transformed.Min(Size);
        var removed = 0;
        if (atRisk > 0 && level == DeidLevel.DeIdentified)
        {
            if (removeSmallGroups) { removed = transformed.RemoveAll(t => Size(t) < k); minGroup = transformed.Count == 0 ? 0 : transformed.Min(Size); notes.Add($"{removed} row(s) in groups smaller than {k} removed."); }
            else notes.Add($"Re-identification risk: {atRisk} row(s) are in groups smaller than {k} (age band, sex, procedure, region, month). Remove small groups or drop a column before release.");
        }
        var headers = level == DeidLevel.Pseudonymised ? ["Study ID", .. cols.Select(c => c.Label)] : cols.Select(c => c.Label).ToList();
        var result = transformed.Select(t => level == DeidLevel.Pseudonymised ? (string?[])[studyCode(t.PatientId), .. t.Values] : t.Values).ToList();
        return new DeidResult(headers, result, minGroup, atRisk - removed, 0, removed, notes);
    }

    public static byte[] Csv(DeidResult r, string watermark)
    {
        static string Q(string? s) => s is null ? "" : s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : s;
        var sb = new System.Text.StringBuilder();
        sb.Append("# ").Append(watermark.Replace('\n', ' ')).Append("\r\n");
        sb.Append(string.Join(",", r.Headers.Select(Q))).Append("\r\n");
        foreach (var row in r.Rows) sb.Append(string.Join(",", row.Select(Q))).Append("\r\n");
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }
}
