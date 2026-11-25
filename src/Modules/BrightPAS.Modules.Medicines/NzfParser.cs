using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BrightPAS.Modules.Medicines;

/// <summary>One line of text from a PDF page: left position (points), font size, bold, and the words joined.</summary>
public sealed record NzfLine(int Page, double X, double Size, bool Bold, string Text);

public sealed record NzfDoseRow(string Indication, string Route, string RouteCodes, string Population, string Dose,
    int? MinAgeYears, int? MaxAgeYears, decimal? MinWeightKg, decimal? MaxWeightKg, bool Neonate,
    string? Unit, decimal? MaxSingle, decimal? MaxSinglePerKg, decimal? MaxDaily, decimal? MaxDailyPerKg, decimal? IntervalHours, bool Titration);

public sealed record NzfPrep(string Form, string Strength, string? Subsidy);

public sealed record NzfMono(string Name, string? Synonyms, string? Chapter, IReadOnlyDictionary<string, string> Sections, IReadOnlyList<NzfDoseRow> Doses, IReadOnlyList<NzfPrep> Preparations);

public sealed record NzfDocumentParsed(string Edition, int? Release, DateOnly? ReleaseDate, IReadOnlyList<NzfMono> Monographs, IReadOnlyList<string> Problems);

/// <summary>
/// Turns the NZ Formulary / NZ Formulary for Children PDF (one drug monograph after another, made from the NZF website) into structured
/// monographs: sections (indications, contra-indications, cautions, hepatic and renal impairment, pregnancy, breast-feeding, adverse effects,
/// patient advice), the dosing regimen (indication → route → population line) with the limits read from each line, and the preparations.
/// Pure – change reading rules here only, with a test (NzfTests). The layout signals are the NZF's own: drug names in large bold type,
/// section headings in bold on their own line, dosing populations in bold and indented under the route.
/// </summary>
public static partial class NzfParser
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly string[] SectionNames =
    [
        "Drug action", "Indications", "Contra-indications", "Cautions", "Interactions", "Hepatic impairment", "Renal impairment", "Pregnancy",
        "Breast-feeding", "Adverse effects", "Dosing regimen", "Patient advice", "Monitoring", "Directions for administration", "Unapproved indications",
        "Prescribing and dispensing information", "Allergy and cross-sensitivity", "Pre-treatment screening", "Treatment cessation", "Effects on laboratory tests",
        "Exceptional circumstances", "Conception and contraception", "Important safety information", "Medicinal forms", "Overdose",
    ];

    [GeneratedRegex(@"New Zealand Formulary(?<child> for Children)? release (?<n>\d+)\s*[—–-]\s*(?<d>\d{1,2} \w+ \d{4})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex ReleaseRx();

    /// <summary>A drug name line: lower-case start, no sentence punctuation, short.</summary>
    [GeneratedRegex(@"^[a-z0-9][a-z0-9 ,+\-()'/.\[\]]{1,120}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex NameRx();

    public static NzfDocumentParsed Parse(IReadOnlyList<NzfLine> input)
    {
        var problems = new List<string>();
        // The NZFC is set slightly larger than the NZF: scale every size so the body text is 7.2 pt (the NZF's), then one set of rules reads both.
        var body = input.Where(l => !l.Bold && l.Size > 5).GroupBy(l => Math.Round(l.Size, 1)).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        var scale = body > 0 ? body / 7.2 : 1.0;
        IReadOnlyList<NzfLine> lines = Math.Abs(scale - 1) < 0.01 ? input : [.. input.Select(l => l with { Size = Math.Round(l.Size / scale, 1) })];
        string edition = "NZF"; int? release = null; DateOnly? date = null;
        foreach (var l in lines.Take(400))
            if (ReleaseRx().Match(l.Text) is { Success: true } m)
            {
                edition = m.Groups["child"].Success ? "NZFC" : "NZF";
                release = int.Parse(m.Groups["n"].Value, Inv);
                if (DateTime.TryParseExact(m.Groups["d"].Value, "d MMMM yyyy", Inv, DateTimeStyles.None, out var d)) date = DateOnly.FromDateTime(d);
                break;
            }
        if (release is null) problems.Add("The release number and date were not found on the first pages – is this the NZF or NZFC PDF?");

        // Monograph starts: the biggest bold type on its own line, lower case (the NZF writes drug names in lower case).
        var big = lines.Where(l => l.Bold && l.Size >= 12.9 && l.Size <= 14.5).ToList();
        var starts = new List<int>();
        string? chapter = null;
        var chapters = new Dictionary<int, string?>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.Bold && l.Size > 9.5 && char.IsDigit(l.Text.FirstOrDefault()) && l.Text.Contains(' ', StringComparison.Ordinal)) chapter = Clip(l.Text.Trim(), 200);
            if (l.Bold && l.Size >= 12.9 && l.Size <= 14.5 && NameRx().IsMatch(l.Text.Trim()))
            {
                starts.Add(i);
                chapters[i] = chapter;
            }
        }
        _ = big;
        var monos = new List<NzfMono>();
        for (var k = 0; k < starts.Count; k++)
        {
            var from = starts[k];
            var to = k + 1 < starts.Count ? starts[k + 1] : lines.Count;
            // A monograph ends where the next large heading (topic or chapter) begins.
            for (var j = from + 1; j < to; j++) if (lines[j].Size >= 11.5 && lines[j].Bold) { to = j; break; }
            var mono = ParseMono(lines, from, to, chapters[from]);
            if (mono is not null) monos.Add(mono);
        }
        if (monos.Count == 0) problems.Add("No drug monographs were found – the file layout may have changed.");
        return new(edition, release, date, monos, problems);
    }

    private static bool IsHeading(NzfLine l, out string name)
    {
        name = "";
        if (!l.Bold || l.Size < 7.3 || l.Size > 8.2) return false;
        var t = l.Text.Trim();
        foreach (var s in SectionNames)
            if (string.Equals(t, s, StringComparison.Ordinal)) { name = s; return true; }
        return false;
    }

    private static NzfMono? ParseMono(IReadOnlyList<NzfLine> lines, int from, int to, string? chapter)
    {
        var name = Clean(lines[from].Text);
        string? syn = null;
        var i = from + 1;
        if (i < to && lines[i].Text.Trim().StartsWith('(') && lines[i].Text.Trim().EndsWith(')')) { syn = lines[i].Text.Trim().Trim('(', ')').Trim(); i++; }
        var sections = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        var dosingLines = new List<NzfLine>();
        var preps = new List<NzfPrep>();
        string? current = null;
        double nameX = lines[from].X;
        for (; i < to; i++)
        {
            var l = lines[i];
            if (IsHeading(l, out var h)) { current = h; if (!sections.ContainsKey(h)) sections[h] = new StringBuilder(); continue; }
            // Preparations block: the drug name again, small bold and indented, followed by forms and strengths.
            if (l.Bold && l.Size < 7.2 && l.X > nameX + 2 && string.Equals(Clean(l.Text), name, StringComparison.Ordinal))
            {
                ParsePreps(lines, i + 1, to, l.X, preps);
                break;
            }
            if (current is null) continue;
            if (current == "Dosing regimen") dosingLines.Add(l);
            else
            {
                var sb = sections[current];
                if (sb.Length > 0) sb.Append(l.X > nameX + 4 ? '\n' : ' ');
                sb.Append(l.Text.Trim());
            }
        }
        if (!sections.ContainsKey("Indications") && dosingLines.Count == 0 && preps.Count == 0) return null;
        var doses = ParseDosing(dosingLines);
        return new(name, syn, chapter, sections.Where(s => s.Key != "Dosing regimen").ToDictionary(s => s.Key, s => s.Value.ToString().Trim(), StringComparer.Ordinal), doses, preps);
    }

    private static void ParsePreps(IReadOnlyList<NzfLine> lines, int from, int to, double nameX, List<NzfPrep> preps)
    {
        string? form = null;
        double? formX = null;
        for (var i = from; i < to; i++)
        {
            var l = lines[i];
            if (l.X <= nameX + 0.5) break; // another preparation name or the end of the block
            var t = l.Text.Trim();
            if (formX is null || l.X <= formX.Value + 0.5)
            {
                if (char.IsDigit(t.FirstOrDefault()) && form is not null) { /* strength without a form line */ }
                else { form = t; formX = l.X; continue; }
            }
            if (form is null) continue;
            var parts = Regex.Split(t, @"\s{2,}", RegexOptions.None, TimeSpan.FromMilliseconds(200));
            var subsidyAt = t.IndexOf("subsidy", StringComparison.OrdinalIgnoreCase);
            string strength = t, subsidy = "";
            var cut = t.IndexOf(" full subsidy", StringComparison.OrdinalIgnoreCase);
            if (cut < 0) cut = t.IndexOf(" partial subsidy", StringComparison.OrdinalIgnoreCase);
            if (cut < 0) cut = t.IndexOf(" not subsidised", StringComparison.OrdinalIgnoreCase);
            if (cut > 0) { strength = t[..cut].Trim(); subsidy = t[cut..].Trim(); }
            else if (parts.Length > 1) { strength = parts[0]; subsidy = string.Join(' ', parts.Skip(1)); }
            _ = subsidyAt;
            if (preps.Count < 200) preps.Add(new(Clip(form, 150), Clip(strength, 150), subsidy.Length == 0 ? null : Clip(subsidy, 150)));
        }
    }

    // ------------------------------------------------------------------ dosing
    private static List<NzfDoseRow> ParseDosing(List<NzfLine> lines)
    {
        var rows = new List<NzfDoseRow>();
        // Levels by indent among the bold lines: indication < route < population.
        var bold = lines.Where(l => l.Bold && l.Size >= 7.3).ToList();
        if (bold.Count == 0) return rows;
        var xs = bold.Select(l => Math.Round(l.X)).Distinct().Order().ToList();
        string indication = "", route = "";
        var routeX = bold.Where(l => l.Size > 7.85 && l.Size < 8.4).Select(l => (double?)l.X).Min();
        var indX = bold.Where(l => l.Size > 8.5).Select(l => (double?)l.X).Min();
        var popX = bold.Where(l => l.Size > 7.3 && l.Size < 7.85 && (routeX is null || l.X > routeX.Value + 2)).Select(l => (double?)l.X).Min();
        _ = xs;
        string? pending = null; double pendingX = 0;
        void Flush()
        {
            if (pending is null) return;
            var row = ReadPopulationLine(indication, route, pending);
            if (row is not null) rows.Add(row);
            pending = null;
        }
        foreach (var l in lines)
        {
            var t = l.Text.Trim();
            if (l.Bold && indX is { } ix && Math.Abs(l.X - ix) < 1.5 && l.Size > 8.5) { Flush(); indication = t; route = ""; continue; }
            if (l.Bold && routeX is { } rx && Math.Abs(l.X - rx) < 1.5 && l.Size > 7.85 && l.Size < 8.4) { Flush(); route = t; continue; }
            if (popX is { } px && Math.Abs(l.X - px) < 1.5)
            {
                if (l.Bold && !t.StartsWith("Note", StringComparison.Ordinal)) { Flush(); pending = t; pendingX = l.X; }
                else if (t.StartsWith("Note", StringComparison.Ordinal)) Flush();
                else if (pending is not null) pending += " " + t; // the line continues
                continue;
            }
            if (pending is not null && Math.Abs(l.X - pendingX) < 1.5) pending += " " + t;
        }
        Flush();
        return rows;
    }

    [GeneratedRegex(@"(?<a>\d+(?:[.,]\d+)?(?:\s\d{3})?)(?:\s*[–-]\s*(?<b>\d+(?:[.,]\d+)?(?:\s\d{3})?))?\s*(?<u>mg|g|micrograms?|microgram|units?|mmol)(?<kg>/kg)?(?![a-zA-Z/])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex AmountRx();

    [GeneratedRegex(@"maximum(?: total| dose| daily dose| of)?\s+(?<a>\d+(?:[.,]\d+)?(?:\s\d{3})?)\s*(?<u>mg|g|micrograms?|units?|mmol)(?<kg>/kg)?(?:\s*(?:/|per)\s*(?:day|24 hours)|\s+(?:daily|a day|per day|in 24 hours|in any 24 hours|each day))", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex MaxDailyRx();

    [GeneratedRegex(@"without exceeding\s+(?<a>\d+(?:[.,]\d+)?)\s*(?<u>mg|g|micrograms?|units?|mmol)(?!/)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex CapRx();

    [GeneratedRegex(@"(?:maximum single dose|maximum\s+(?<a2>\d+(?:[.,]\d+)?)\s*(?<u2>mg|g|micrograms?|units?|mmol)\s*(?:/|per)\s*dose)\s*(?<a>\d+(?:[.,]\d+)?)?\s*(?<u>mg|g|micrograms?|units?|mmol)?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex MaxSingleRx();

    [GeneratedRegex(@"\(maximum\s+(?<a>\d+(?:[.,]\d+)?)\s*(?<u>mg|g|micrograms?|units?|mmol)\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex ParenMaxRx();

    [GeneratedRegex(@"every\s+(?<a>\d+(?:\.\d+)?)(?:\s*[–-]\s*\d+(?:\.\d+)?)?\s*hours?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex EveryRx();

    [GeneratedRegex(@"(?:up to\s+)?(?<n>\d|once|twice|one|two|three|four|five|six)\s+times?\s+(?:daily|a day|per day)|\b(?<w>once|twice)\s+daily", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex TimesRx();

    [GeneratedRegex(@"\b(initially|increased|increase|titrat|then|loading|followed by|adjusted|according to|maintenance|reduce)|over \d+ hours|/hour|per hour|/minute|per minute|/m2|/m²", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex TitrationRx();

    [GeneratedRegex(@"(?<a>\d+)\s*(?<au>months?|years?|weeks?|days?)?\s*[–-]\s*(?<b>\d+)\s*(?<bu>months?|years?|weeks?)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex AgeRangeRx();

    [GeneratedRegex(@"(?:aged\s+)?(?<op>over|under|less than|more than|from|at least)\s+(?<a>\d+)\s*(?<u>months?|years?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex AgeOpRx();

    [GeneratedRegex(@"body-weight\s+(?:(?<op>over|under|less than|more than|up to|at least)\s+)?(?<a>\d+(?:\.\d+)?)\s*(?:[–-]\s*(?<b>\d+(?:\.\d+)?))?\s*kg", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex WeightRx();

    private static decimal Num(string s) => decimal.Parse(s.Replace(" ", "", StringComparison.Ordinal).Replace(',', '.'), NumberStyles.Number, Inv);

    private static string? UnitOf(string u) => u.ToLowerInvariant() switch
    {
        "mg" => "mg",
        "g" => "g",
        "microgram" or "micrograms" => "microgram",
        "unit" or "units" => "unit",
        "mmol" => "mmol",
        _ => null,
    };

    /// <summary>Converts to the row's unit when both are masses; null when they can't be compared.</summary>
    private static decimal? InUnit(decimal v, string from, string to)
    {
        if (from == to) return v;
        var f = DoseParser.ToBase(v, from);
        var t = DoseParser.ToBase(1, to);
        return f is null || t is null || f.Value.Unit != t.Value.Unit ? null : f.Value.Amount / t.Value.Amount;
    }

    /// <summary>Every route named ("Intravenous injection or intramuscular injection" → "IV,IM").</summary>
    public static string RouteCodes(string route)
    {
        var codes = Regex.Split(route, @"\s+or\s+|,\s*|\s+and\s+", RegexOptions.None, TimeSpan.FromMilliseconds(200))
            .Where(x => x.Trim().Length > 0).Select(x => RouteCode(x.Trim())).Distinct(StringComparer.Ordinal).ToList();
        if (codes.Count > 1) codes.Remove("Other");
        return codes.Count == 0 ? RouteCode(route) : string.Join(',', codes);
    }

    private static readonly string[] Salts = ["hydrochloride", "hydrobromide", "sodium", "potassium", "phosphate", "citrate", "sulfate", "sulphate", "salts", "acetate", "maleate",
        "tartrate", "mesilate", "mesylate", "besilate", "bromide", "fumarate", "succinate", "lactate", "gluconate", "hyclate", "monohydrate", "trihydrate", "dihydrate", "disodium", "hydrogen", "base"];

    /// <summary>The ingredient a monograph is about: qualifiers in brackets and salt words removed ("ibuprofen (systemic)" → ibuprofen, "tramadol hydrochloride" → tramadol).</summary>
    public static string BaseName(string name)
    {
        var s = Regex.Replace(name, @"\([^)]*\)", " ", RegexOptions.None, TimeSpan.FromMilliseconds(200));
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && Salts.Contains(words[^1], StringComparer.OrdinalIgnoreCase)) words.RemoveAt(words.Count - 1);
        return string.Join(' ', words).Trim().ToLowerInvariant();
    }

    public static string RouteCode(string route)
    {
        var r = route.ToLowerInvariant();
        if (r.Contains("intravenous", StringComparison.Ordinal)) return "IV";
        if (r.Contains("intramuscular", StringComparison.Ordinal)) return "IM";
        if (r.Contains("subcutaneous", StringComparison.Ordinal)) return "SC";
        if (r.StartsWith("oral", StringComparison.Ordinal) || r.StartsWith("by mouth", StringComparison.Ordinal)) return "Oral";
        if (r.Contains("rectal", StringComparison.Ordinal)) return "PR";
        if (r.Contains("sublingual", StringComparison.Ordinal)) return "SL";
        if (r.Contains("buccal", StringComparison.Ordinal)) return "Buccal";
        if (r.Contains("epidural", StringComparison.Ordinal)) return "Epidural";
        if (r.Contains("intrathecal", StringComparison.Ordinal) || r.Contains("spinal", StringComparison.Ordinal)) return "Intrathecal";
        if (r.Contains("infiltration", StringComparison.Ordinal) || r.Contains("localised injection", StringComparison.Ordinal) || r.Contains("local anaesthesia", StringComparison.Ordinal)) return "Infiltration";
        if (r.Contains("nerve", StringComparison.Ordinal) || r.Contains("perineural", StringComparison.Ordinal) || r.Contains("plexus", StringComparison.Ordinal)) return "Nerve block";
        if (r.Contains("inhal", StringComparison.Ordinal) || r.Contains("nebul", StringComparison.Ordinal)) return "Inhaled";
        if (r.Contains("intranasal", StringComparison.Ordinal) || r.Contains("nasal", StringComparison.Ordinal)) return "Nasal";
        if (r.StartsWith("eye", StringComparison.Ordinal) || r.Contains("ophthalmic", StringComparison.Ordinal)) return "Eye";
        if (r.StartsWith("topical", StringComparison.Ordinal) || r.Contains("transdermal", StringComparison.Ordinal) || r.Contains("cutaneous", StringComparison.Ordinal)) return "Topical";
        return "Other";
    }

    /// <summary>Reads one population line ("Adult, body-weight over 50 kg 0.5–1 g every 4–6 hours; maximum 4 g daily") into limits.</summary>
    public static NzfDoseRow? ReadPopulationLine(string indication, string route, string line)
    {
        var text = Clean(line);
        if (text.Length < 3) return null;
        var first = AmountRx().Match(text);
        // The population is everything before the first dose amount (ages and weights are in kg/years, not dose units).
        var cut = first.Success ? first.Index : Math.Min(text.Length, 60);
        var population = text[..cut].Trim().TrimEnd(',', ';', ':').Trim();
        var dose = text[cut..].Trim();
        if (population.Length == 0) population = "All";
        var p = population.ToLowerInvariant();
        int? minAge = null, maxAge = null; decimal? minKg = null, maxKg = null;
        var neonate = p.Contains("neonate", StringComparison.Ordinal);
        if (p.StartsWith("adult", StringComparison.Ordinal)) minAge = 18;
        if (p.StartsWith("elderly", StringComparison.Ordinal)) minAge = 65;
        if (neonate) maxAge = 1;
        if (AgeRangeRx().Match(population) is { Success: true } ar)
        {
            var aYears = ToYears(int.Parse(ar.Groups["a"].Value, Inv), ar.Groups["au"].Success ? ar.Groups["au"].Value : ar.Groups["bu"].Value);
            var bRaw = int.Parse(ar.Groups["b"].Value, Inv);
            var bUnit = ar.Groups["bu"].Value;
            minAge = aYears;
            maxAge = bUnit.StartsWith("year", StringComparison.Ordinal) ? (bRaw == 18 ? 18 : bRaw + 1) : Math.Max(1, (bRaw + 11) / 12);
        }
        else if (AgeOpRx().Match(population) is { Success: true } ao)
        {
            var v = ToYears(int.Parse(ao.Groups["a"].Value, Inv), ao.Groups["u"].Value);
            if (ao.Groups["op"].Value.ToLowerInvariant() is "under" or "less than") maxAge = Math.Max(v, 1); else minAge = v;
        }
        if (neonate) { minAge = 0; maxAge = 1; }
        else if (p.StartsWith("child", StringComparison.Ordinal) && maxAge is null) { minAge ??= 0; maxAge = 18; }
        if (WeightRx().Match(population) is { Success: true } wr)
        {
            var a = Num(wr.Groups["a"].Value);
            var op = wr.Groups["op"].Value.ToLowerInvariant();
            if (wr.Groups["b"].Success) { minKg = a; maxKg = Num(wr.Groups["b"].Value); }
            else if (op is "under" or "less than" or "up to") maxKg = a;
            else minKg = a;
        }

        string? unit = null; decimal? single = null, singleKg = null, daily = null, dailyKg = null, every = null;
        var titration = TitrationRx().IsMatch(dose);
        var explicitDaily = MaxDailyRx().Matches(dose).Cast<Match>().ToList();
        if (first.Success && first.Index == cut)
        {
            unit = UnitOf(first.Groups["u"].Value);
            var upper = Num(first.Groups["b"].Success ? first.Groups["b"].Value : first.Groups["a"].Value);
            if (!titration)
            {
                if (first.Groups["kg"].Success) singleKg = upper; else single = upper;
            }
        }
        foreach (var m in explicitDaily)
        {
            var u = UnitOf(m.Groups["u"].Value);
            if (u is null) continue;
            unit ??= u;
            var v = InUnit(Num(m.Groups["a"].Value), u, unit);
            if (v is null) continue;
            if (m.Groups["kg"].Success) dailyKg = Math.Max(dailyKg ?? 0, v.Value); else daily = Math.Max(daily ?? 0, v.Value);
        }
        if (CapRx().Match(dose) is { Success: true } cap && UnitOf(cap.Groups["u"].Value) is { } cu && unit is not null && InUnit(Num(cap.Groups["a"].Value), cu, unit) is { } cv)
        {
            // "15 mg/kg … (without exceeding 4 g)" caps the daily total; "(maximum 1 g)" after a per-kg dose caps each dose.
            if (dailyKg is not null || explicitDaily.Count > 0) daily = daily is null ? cv : Math.Min(daily.Value, cv); else single = single is null ? cv : Math.Min(single.Value, cv);
        }
        if (ParenMaxRx().Match(dose) is { Success: true } pm && UnitOf(pm.Groups["u"].Value) is { } pu && unit is not null && InUnit(Num(pm.Groups["a"].Value), pu, unit) is { } pv && singleKg is not null)
            single = single is null ? pv : Math.Min(single.Value, pv);
        if (MaxSingleRx().Match(dose) is { Success: true } ms && unit is not null)
        {
            var aGroup = ms.Groups["a"].Success ? ms.Groups["a"] : ms.Groups["a2"];
            var uGroup = ms.Groups["u"].Success ? ms.Groups["u"] : ms.Groups["u2"];
            if (aGroup.Success && uGroup.Success && UnitOf(uGroup.Value) is { } su && InUnit(Num(aGroup.Value), su, unit) is { } sv) single = sv;
        }
        if (!titration && (single is not null || singleKg is not null) && EveryRx().Match(dose) is { Success: true } ev) every = Num(ev.Groups["a"].Value);
        if (!titration && daily is null && dailyKg is null && TimesRx().Match(dose) is { Success: true } tm)
        {
            var n = (tm.Groups["n"].Success ? tm.Groups["n"].Value : tm.Groups["w"].Value).ToLowerInvariant() switch
            {
                "once" or "one" or "1" => 1,
                "twice" or "two" or "2" => 2,
                "three" or "3" => 3,
                "four" or "4" => 4,
                "five" or "5" => 5,
                "six" or "6" => 6,
                _ => 0,
            };
            if (n > 0 && single is { } s1) daily = s1 * n;
            if (n > 0 && singleKg is { } k1) dailyKg = k1 * n;
        }
        else if (!titration && daily is null && dailyKg is null && every is { } evh && evh > 0)
        {
            var n = (int)Math.Floor(24m / evh);
            if (single is { } s2) daily = s2 * n;
            if (singleKg is { } k2) dailyKg = k2 * n;
        }
        return new(Clip(indication, 300), Clip(route, 150), RouteCodes(route), Clip(population, 200), Clip(dose, 1000), minAge, maxAge, minKg, maxKg, neonate,
            unit, single, singleKg, daily, dailyKg, every, titration);
    }

    private static int ToYears(int v, string unit) => unit.StartsWith("year", StringComparison.Ordinal) ? v : v / 12;

    public static string Clean(string s) => Regex.Replace(s.Replace(' ', ' '), @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(200)).Trim();
    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
