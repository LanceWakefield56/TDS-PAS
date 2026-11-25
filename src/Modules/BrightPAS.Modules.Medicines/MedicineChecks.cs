using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BrightPAS.Modules.Medicines;

/// <summary>An ingredient amount in a base unit: mg (all masses), unit or mmol. Amount is null when the dose could not be worked out.</summary>
public sealed record IngredientAmount(string Ingredient, decimal? Amount, string? Unit);

/// <summary>The medicine about to be given, already resolved to ingredients and amounts.</summary>
public sealed record ProposedMedicine(string Text, string Dose, string Route, IReadOnlyList<IngredientAmount> Ingredients, DateTimeOffset At, string? DoseProblem);

/// <summary>A medicine already given to the patient (any episode, last 24 hours), resolved to one ingredient.</summary>
public sealed record GivenBefore(string Text, string Ingredient, decimal? Amount, string? Unit, DateTimeOffset At);

/// <summary>An active allergy alert, with the ingredient or "group:CODE" it is linked to (if linked).</summary>
public sealed record PatientAllergy(string Text, string? Term);

public sealed record RegularMedicine(string Ingredient, string Source);

public sealed record CheckContext(int? AgeYears, decimal? WeightKg, IReadOnlyList<PatientAllergy> Allergies, IReadOnlyList<GivenBefore> Given, IReadOnlyList<RegularMedicine> Regular, int DuplicateWithinMinutes, TimeZoneInfo Zone);

public sealed record Finding(FindingKind Kind, CheckSeverity Severity, string Text, string? RuleRef);

/// <summary>
/// Finds ingredient names (and their aliases – brand and older names) in free text such as an allergy alert or the questionnaire's medicines list.
/// Matching is whole words, case and punctuation insensitive.
/// </summary>
public sealed class TermIndex
{
    private readonly Dictionary<string, string> _terms = new(StringComparer.Ordinal);
    private readonly int _longest;

    public TermIndex(IEnumerable<(string Term, string Ingredient)> terms)
    {
        foreach (var (t, i) in terms)
        {
            var n = Normalise(t);
            if (n.Length < 3) continue;
            _terms.TryAdd(n, i);
        }
        _longest = _terms.Count == 0 ? 1 : _terms.Keys.Max(k => k.Split(' ').Length);
    }

    public int Count => _terms.Count;

    /// <summary>Lower case, letters and digits only, single spaces.</summary>
    public static string Normalise(string text)
    {
        var sb = new StringBuilder(text.Length);
        var space = true;
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); space = false; }
            else if (!space) { sb.Append(' '); space = true; }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Ingredients named in the text (whole words, longest phrase first).</summary>
    public IReadOnlyList<string> Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || _terms.Count == 0) return [];
        var words = Normalise(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = new List<string>();
        for (var i = 0; i < words.Length; i++)
            for (var len = Math.Min(_longest, words.Length - i); len >= 1; len--)
            {
                var phrase = string.Join(' ', words, i, len);
                if (_terms.TryGetValue(phrase, out var ing))
                {
                    if (!found.Contains(ing, StringComparer.Ordinal)) found.Add(ing);
                    i += len - 1;
                    break;
                }
            }
        return found;
    }

    /// <summary>True when the normalised phrase appears as whole words in the text.</summary>
    public static bool ContainsWords(string normalisedText, string phrase)
    {
        var p = Normalise(phrase);
        return p.Length > 0 && (" " + normalisedText + " ").Contains(" " + p + " ", StringComparison.Ordinal);
    }
}

/// <summary>Reads a dose as written ("1 g", "500mg", "2 tablets", "10 mL", "1-2 tabs", "50 micrograms").</summary>
public static partial class DoseParser
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [GeneratedRegex(@"^\s*(?:(\d+(?:[.,]\d+)?)\s*(?:-|to|–)\s*)?(\d+(?:[.,]\d+)?)\s*([a-zA-Zµμ]+(?:\s+units?)?)?", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex DoseRx();

    /// <summary>Amount (the upper end of a range) and a normalised unit: mg, microgram, g, unit, mmol, mL, or "each" for tablets, capsules, vials and similar.</summary>
    public static (decimal? Amount, string? Unit) Parse(string? dose)
    {
        if (string.IsNullOrWhiteSpace(dose)) return (null, null);
        var m = DoseRx().Match(dose);
        if (!m.Success) return (null, null);
        if (!decimal.TryParse(m.Groups[2].Value.Replace(',', '.'), NumberStyles.Number, Inv, out var amount)) return (null, null);
        var u = NormaliseUnit(m.Groups[3].Success ? m.Groups[3].Value : null);
        return (amount, u);
    }

    public static string? NormaliseUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return null;
        var u = unit.Trim().ToLowerInvariant().Replace('μ', 'µ');
        return u switch
        {
            "mg" or "milligram" or "milligrams" or "mgs" => "mg",
            "g" or "gm" or "gram" or "grams" or "gms" => "g",
            "mcg" or "microgram" or "micrograms" or "µg" or "ug" or "microg" => "microgram",
            "unit" or "units" or "iu" or "u" or "international units" or "international unit" => "unit",
            "mmol" or "millimole" or "millimoles" => "mmol",
            "ml" or "mls" or "millilitre" or "millilitres" or "milliliter" or "milliliters" or "cc" => "mL",
            "tablet" or "tablets" or "tab" or "tabs" or "capsule" or "capsules" or "cap" or "caps" or "vial" or "vials" or "ampoule" or "ampoules"
                or "amp" or "amps" or "suppository" or "suppositories" or "supp" or "patch" or "patches" or "sachet" or "sachets" or "dose" or "doses"
                or "puff" or "puffs" or "drop" or "drops" or "lozenge" or "lozenges" or "wafer" or "wafers" or "syringe" or "syringes" => "each",
            _ => null,
        };
    }

    /// <summary>Converts to the base unit: masses to mg; unit and mmol unchanged. Null for anything else.</summary>
    public static (decimal Amount, string Unit)? ToBase(decimal amount, string? unit) => unit switch
    {
        "mg" => (amount, "mg"),
        "g" => (amount * 1000m, "mg"),
        "microgram" => (amount / 1000m, "mg"),
        "unit" => (amount, "unit"),
        "mmol" => (amount, "mmol"),
        _ => null,
    };

    /// <summary>Plain amount for messages: 1000 mg → "1 g", 0.05 mg → "50 microgram".</summary>
    public static string Format(decimal amount, string unit)
    {
        if (unit == "mg")
        {
            if (amount >= 1000m) return (amount / 1000m).ToString("0.###", Inv) + " g";
            if (amount > 0 && amount < 1m) return (amount * 1000m).ToString("0.###", Inv) + " microgram";
        }
        return amount.ToString("0.###", Inv) + " " + unit;
    }
}

/// <summary>Route names as charted, reduced to a short standard set so rules match ("intravenous", "IV bolus" → IV).</summary>
public static class Routes
{
    public static readonly IReadOnlyList<string> All = ["Oral", "IV", "IM", "SC", "PR", "SL", "Buccal", "Topical", "Inhaled", "Nasal", "Eye", "Infiltration", "Epidural", "Intrathecal", "Nerve block", "Other"];

    public static string Normal(string? route)
    {
        var r = TermIndex.Normalise(route ?? "");
        if (r.Length == 0) return "";
        if (r is "iv" or "intravenous" or "intravenously" || r.StartsWith("iv ", StringComparison.Ordinal) || r.Contains("intraven", StringComparison.Ordinal)) return "IV";
        if (r is "im" or "intramuscular" || r.StartsWith("im ", StringComparison.Ordinal)) return "IM";
        if (r is "sc" or "subcut" or "subcutaneous" or "sq" or "s c") return "SC";
        if (r is "po" or "oral" or "orally" or "by mouth" or "enteral" or "ng") return "Oral";
        if (r is "pr" or "rectal" or "rectally") return "PR";
        if (r is "sl" or "sublingual") return "SL";
        if (r is "buccal") return "Buccal";
        if (r is "top" or "topical" or "cutaneous" or "transdermal") return "Topical";
        if (r is "inh" or "inhaled" or "neb" or "nebulised" or "nebulized") return "Inhaled";
        if (r is "nasal" or "intranasal" or "in") return "Nasal";
        if (r is "eye" or "ophthalmic" or "topical eye") return "Eye";
        if (r.Contains("infiltrat", StringComparison.Ordinal) || r is "local" or "local infiltration" or "wound") return "Infiltration";
        if (r.Contains("epidural", StringComparison.Ordinal)) return "Epidural";
        if (r.Contains("intrathecal", StringComparison.Ordinal) || r is "spinal") return "Intrathecal";
        if (r.Contains("block", StringComparison.Ordinal) || r.Contains("nerve", StringComparison.Ordinal)) return "Nerve block";
        return route!.Trim();
    }

    public static bool Matches(string? ruleRoute, string route) =>
        string.IsNullOrWhiteSpace(ruleRoute) || string.Equals(Normal(ruleRoute), Normal(route), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The medicine safety checks (pure – no database). Change check behaviour here only, with a test (MedicinesTests).
/// Allergies are always checked by name; dose limits, allergy groups, cross-reactions and interactions come from the approved rule set.
/// </summary>
public static class MedicineChecks
{
    private const string GroupPrefix = "group:";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static List<Finding> Check(ProposedMedicine p, CheckContext c, MedRuleSet? rules, TermIndex index)
    {
        var f = new List<Finding>();
        var name = string.IsNullOrWhiteSpace(p.Text) ? "this medicine" : p.Text.Trim();
        if (p.Ingredients.Count == 0)
            f.Add(new(FindingKind.NotChecked, CheckSeverity.Warn, $"BrightPAS could not tell what is in \"{name}\" – it is not in the medicines list. Check allergies, dose and interactions yourself.", null));

        Allergies(p, c, rules, index, name, f);
        if (rules is null)
        {
            f.Add(new(FindingKind.NotChecked, CheckSeverity.Info, "There are no approved medicine rules yet, so only allergies to the same medicine were checked – not doses, allergy groups or interactions.", null));
        }
        else
        {
            Doses(p, c, rules, f);
            Interactions(p, c, rules, f);
        }
        Duplicates(p, c, rules, f);
        return [.. f.DistinctBy(x => x.Text).OrderByDescending(x => x.Severity).ThenBy(x => x.Kind)];
    }

    public static CheckSeverity? Worst(IReadOnlyList<Finding> findings) => findings.Count == 0 ? null : findings.Max(x => x.Severity);

    // ------------------------------------------------------------------ allergies
    private sealed record Allergen(PatientAllergy Alert, HashSet<string> Ingredients, HashSet<string> Groups);

    private static List<Allergen> ResolveAllergies(CheckContext c, MedRuleSet? rules, TermIndex index)
    {
        var list = new List<Allergen>();
        foreach (var a in c.Allergies)
        {
            var ings = new HashSet<string>(StringComparer.Ordinal);
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(a.Term))
            {
                if (a.Term.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase)) groups.Add(a.Term[GroupPrefix.Length..].Trim());
                else ings.Add(a.Term.Trim().ToLowerInvariant());
            }
            foreach (var i in index.Find(a.Text)) ings.Add(i);
            if (rules is not null)
            {
                var text = TermIndex.Normalise(a.Text);
                foreach (var g in rules.Groups.Where(g => g.AllergyClass))
                {
                    if (TermIndex.ContainsWords(text, g.Name) || g.Members.Any(m => TermIndex.ContainsWords(text, m.Term))) groups.Add(g.Code);
                    // An allergy to a member counts as an allergy to the class.
                    if (g.Members.Any(m => !m.IsKeyword && ings.Contains(m.Term))) groups.Add(g.Code);
                }
            }
            list.Add(new(a, ings, groups));
        }
        return list;
    }

    private static void Allergies(ProposedMedicine p, CheckContext c, MedRuleSet? rules, TermIndex index, string name, List<Finding> f)
    {
        var allergens = ResolveAllergies(c, rules, index);
        foreach (var al in allergens.Where(x => x.Ingredients.Count == 0 && x.Groups.Count == 0))
            f.Add(new(FindingKind.NotChecked, CheckSeverity.Info, $"Allergy \"{al.Alert.Text}\" is not linked to a medicine, so it was not checked automatically. Check it yourself, or link it on the patient page.", null));
        foreach (var ing in p.Ingredients.Select(x => x.Ingredient).Distinct(StringComparer.Ordinal))
        {
            var groupsOfIng = rules?.Groups.Where(g => g.Members.Any(m => !m.IsKeyword && m.Term == ing)).ToList() ?? [];
            foreach (var al in allergens)
            {
                if (al.Ingredients.Contains(ing))
                {
                    f.Add(new(FindingKind.Allergy, CheckSeverity.Stop, $"Allergy: \"{al.Alert.Text}\". {Cap(name)} contains {ing}.", null));
                    continue;
                }
                var same = groupsOfIng.FirstOrDefault(g => g.AllergyClass && al.Groups.Contains(g.Code));
                if (same is not null)
                {
                    f.Add(new(FindingKind.Allergy, same.SameGroupSeverity, $"Allergy: \"{al.Alert.Text}\" ({same.Name.ToLowerInvariant()}). {Cap(ing)} is also one of the {same.Name.ToLowerInvariant()}.", $"group:{same.Code}"));
                    continue;
                }
                if (rules is null) continue;
                foreach (var x in rules.CrossReactions)
                    if (al.Groups.Contains(x.FromGroup) && groupsOfIng.Any(g => string.Equals(g.Code, x.ToGroup, StringComparison.OrdinalIgnoreCase)))
                    {
                        var from = rules.Groups.FirstOrDefault(g => string.Equals(g.Code, x.FromGroup, StringComparison.OrdinalIgnoreCase))?.Name ?? x.FromGroup;
                        var to = rules.Groups.FirstOrDefault(g => string.Equals(g.Code, x.ToGroup, StringComparison.OrdinalIgnoreCase))?.Name ?? x.ToGroup;
                        f.Add(new(FindingKind.Allergy, x.Severity, $"Possible cross-reaction: \"{al.Alert.Text}\" ({from.ToLowerInvariant()}) and {ing} is one of the {to.ToLowerInvariant()}. {x.Message}".TrimEnd(), $"cross:{x.FromGroup}>{x.ToGroup}"));
                    }
            }
        }
    }

    // ------------------------------------------------------------------ doses
    private static decimal? RuleBase(decimal? v, string unit) => v is { } d ? DoseParser.ToBase(d, DoseParser.NormaliseUnit(unit) ?? unit)?.Amount : null;
    private static string? RuleBaseUnit(string unit) => DoseParser.ToBase(1, DoseParser.NormaliseUnit(unit) ?? unit)?.Unit;

    private static string Band(DoseRule r)
    {
        var parts = new List<string>();
        if (r.MinAgeYears is { } lo && r.MaxAgeYears is { } hi) parts.Add($"age {lo}–{hi - 1}");
        else if (r.MinAgeYears is { } lo2) parts.Add(lo2 >= 16 ? "adults" : $"age {lo2}+");
        else if (r.MaxAgeYears is { } hi2) parts.Add($"under {hi2}");
        if (r.MinWeightKg is { } wl && r.MaxWeightKg is { } wh) parts.Add($"{wl.ToString("0.#", Inv)}–{wh.ToString("0.#", Inv)} kg");
        else if (r.MinWeightKg is { } wl2) parts.Add($"{wl2.ToString("0.#", Inv)} kg and over");
        else if (r.MaxWeightKg is { } wh2) parts.Add($"under {wh2.ToString("0.#", Inv)} kg");
        if (!string.IsNullOrWhiteSpace(r.Route)) parts.Add(Routes.Normal(r.Route));
        return parts.Count == 0 ? "" : " (" + string.Join(", ", parts) + ")";
    }

    private static void Doses(ProposedMedicine p, CheckContext c, MedRuleSet rules, List<Finding> f)
    {
        var notUnderstood = false;
        foreach (var ia in p.Ingredients)
        {
            var byAge = rules.DoseRules.Where(r => string.Equals(r.Ingredient, ia.Ingredient, StringComparison.OrdinalIgnoreCase) && Routes.Matches(r.Route, p.Route)
                && (r.MinAgeYears is null || (c.AgeYears is { } a1 && a1 >= r.MinAgeYears)) && (r.MaxAgeYears is null || (c.AgeYears is { } a2 && a2 < r.MaxAgeYears))).ToList();
            var banded = byAge.Where(r => r.MinWeightKg is not null || r.MaxWeightKg is not null).ToList();
            var applicable = byAge.Where(r => (r.MinWeightKg is null && r.MaxWeightKg is null)
                || (c.WeightKg is { } w && (r.MinWeightKg is null || w >= r.MinWeightKg) && (r.MaxWeightKg is null || w < r.MaxWeightKg))).ToList();
            if (banded.Count > 0 && c.WeightKg is null)
                f.Add(new(FindingKind.NotChecked, c.AgeYears is < 16 ? CheckSeverity.Warn : CheckSeverity.Info, $"Weight not known – the {ia.Ingredient} dose limits that depend on body-weight were not checked. Enter the weight.", banded[0].Code));
            if (applicable.Count == 0) continue;
            if (ia.Amount is null || ia.Unit is null)
            {
                notUnderstood = true;
                continue;
            }
            var amount = ia.Amount.Value;
            var since = p.At.AddHours(-24);
            var earlier = c.Given.Where(g => string.Equals(g.Ingredient, ia.Ingredient, StringComparison.OrdinalIgnoreCase) && g.At > since && g.At <= p.At).ToList();
            var counted = earlier.Where(g => g.Amount is not null && g.Unit == ia.Unit).ToList();
            var uncounted = earlier.Count - counted.Count;
            var dayTotal = amount + counted.Sum(g => g.Amount!.Value);
            var unc = uncounted > 0 ? $" ({uncounted} earlier dose{(uncounted == 1 ? "" : "s")} could not be counted)" : "";
            foreach (var r in applicable)
            {
                var ru = RuleBaseUnit(r.Unit);
                if (ru is null || ru != ia.Unit)
                {
                    f.Add(new(FindingKind.NotChecked, CheckSeverity.Info, $"The {ia.Ingredient} dose is in {ia.Unit} but rule {r.Code} is in {r.Unit} – dose not checked against it.", r.Code));
                    continue;
                }
                var label = $"{ia.Ingredient}{Band(r)}";
                var refText = string.IsNullOrWhiteSpace(r.Note) ? "" : " " + r.Note.Trim();
                if (RuleBase(r.MaxSingle, r.Unit) is { } ms && amount > ms)
                    f.Add(new(FindingKind.Dose, r.Severity, $"Dose too high: {DoseParser.Format(amount, ru)} {ia.Ingredient} is more than the maximum single dose of {DoseParser.Format(ms, ru)} for {label}.{refText}", r.Code));
                if (RuleBase(r.MaxDaily, r.Unit) is { } md && dayTotal > md)
                    f.Add(new(FindingKind.Dose, r.Severity, $"Too much in 24 hours: {DoseParser.Format(dayTotal, ru)} {ia.Ingredient} including this dose – the maximum is {DoseParser.Format(md, ru)} for {label}{unc}.{refText}", r.Code));
                if (r.MaxSinglePerKg is not null || r.MaxDailyPerKg is not null)
                {
                    if (c.WeightKg is { } kg && kg > 0)
                    {
                        if (RuleBase(r.MaxSinglePerKg, r.Unit) is { } spk && amount > Math.Round(spk * kg, 4))
                            f.Add(new(FindingKind.Dose, r.Severity, $"Dose too high for weight: {DoseParser.Format(amount, ru)} {ia.Ingredient} is {Per(amount / kg, ru)}/kg for {kg.ToString("0.#", Inv)} kg – the maximum is {Per(spk, ru)}/kg ({DoseParser.Format(spk * kg, ru)}) for {label}.{refText}", r.Code));
                        if (RuleBase(r.MaxDailyPerKg, r.Unit) is { } dpk && dayTotal > Math.Round(dpk * kg, 4))
                            f.Add(new(FindingKind.Dose, r.Severity, $"Too much in 24 hours for weight: {DoseParser.Format(dayTotal, ru)} {ia.Ingredient} is {Per(dayTotal / kg, ru)}/kg for {kg.ToString("0.#", Inv)} kg – the maximum is {Per(dpk, ru)}/kg ({DoseParser.Format(dpk * kg, ru)}) a day{unc}.{refText}", r.Code));
                    }
                    else
                    {
                        var child = c.AgeYears is < 16;
                        f.Add(new(FindingKind.NotChecked, child ? CheckSeverity.Warn : CheckSeverity.Info, $"Weight not known – the per-kg limit for {label} was not checked. Enter the weight.", r.Code));
                    }
                }
                if (r.MinIntervalHours is { } hrs && earlier.Where(g => g.At > p.At.AddHours((double)-hrs)).OrderByDescending(g => g.At).FirstOrDefault() is { } last)
                {
                    var ago = p.At - last.At;
                    f.Add(new(FindingKind.Duplicate, r.Severity, $"Too soon: {ia.Ingredient} was given at {Local(last.At, c.Zone)} ({Ago(ago)} ago, as {last.Text}) – at least {hrs.ToString("0.#", Inv)} hours between doses for {label}.", r.Code));
                }
            }
        }
        if (notUnderstood)
            f.Add(new(FindingKind.NotChecked, CheckSeverity.Warn, $"Dose \"{p.Dose}\" could not be worked out{(p.DoseProblem is null ? "" : " – " + p.DoseProblem)}, so the dose limits were not checked. Write it as e.g. \"1 g\", \"50 microgram\" or \"2 tablets\".", null));
    }

    private static string Per(decimal v, string unit) => unit == "mg" && v < 1m && v > 0 ? (v * 1000m).ToString("0.##", Inv) + " microgram" : v.ToString("0.##", Inv) + " " + unit;

    private static void Duplicates(ProposedMedicine p, CheckContext c, MedRuleSet? rules, List<Finding> f)
    {
        if (c.DuplicateWithinMinutes <= 0) return;
        foreach (var ing in p.Ingredients.Select(x => x.Ingredient).Distinct(StringComparer.Ordinal))
        {
            var hasInterval = rules?.DoseRules.Any(r => string.Equals(r.Ingredient, ing, StringComparison.OrdinalIgnoreCase) && r.MinIntervalHours is not null && Routes.Matches(r.Route, p.Route)) == true;
            if (hasInterval) continue;
            var last = c.Given.Where(g => string.Equals(g.Ingredient, ing, StringComparison.OrdinalIgnoreCase) && g.At <= p.At && g.At > p.At.AddMinutes(-c.DuplicateWithinMinutes))
                .OrderByDescending(g => g.At).FirstOrDefault();
            if (last is not null)
                f.Add(new(FindingKind.Duplicate, CheckSeverity.Warn, $"{Cap(ing)} was already given at {Local(last.At, c.Zone)} ({last.Text}). Check this is not a double dose.", null));
        }
    }

    // ------------------------------------------------------------------ interactions
    private static bool TermMatches(string term, string ingredient, MedRuleSet rules)
    {
        var t = term.Trim();
        if (t.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = t[GroupPrefix.Length..].Trim();
            return rules.Groups.Any(g => string.Equals(g.Code, code, StringComparison.OrdinalIgnoreCase) && g.Members.Any(m => !m.IsKeyword && m.Term == ingredient));
        }
        return string.Equals(t, ingredient, StringComparison.OrdinalIgnoreCase);
    }

    private static void Interactions(ProposedMedicine p, CheckContext c, MedRuleSet rules, List<Finding> f)
    {
        var others = new List<(string Ingredient, string Where)>();
        var since = p.At.AddHours(-24);
        foreach (var g in c.Given.Where(g => g.At > since && g.At <= p.At).OrderByDescending(g => g.At))
            if (!others.Any(o => o.Ingredient == g.Ingredient)) others.Add((g.Ingredient, $"given at {Local(g.At, c.Zone)}"));
        foreach (var r in c.Regular)
            if (!others.Any(o => o.Ingredient == r.Ingredient)) others.Add((r.Ingredient, r.Source));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ing in p.Ingredients.Select(x => x.Ingredient).Distinct(StringComparer.Ordinal))
            foreach (var (o, where) in others)
            {
                if (o == ing) continue;
                foreach (var r in rules.Interactions)
                {
                    var hit = (TermMatches(r.TermA, ing, rules) && TermMatches(r.TermB, o, rules)) || (TermMatches(r.TermB, ing, rules) && TermMatches(r.TermA, o, rules));
                    if (!hit || !seen.Add(r.Code + "|" + o)) continue;
                    var advice = string.IsNullOrWhiteSpace(r.Advice) ? "" : " " + r.Advice.Trim();
                    f.Add(new(FindingKind.Interaction, r.Severity, $"Interaction: {ing} with {o} ({where}) – {r.Effect.Trim().TrimEnd('.')}.{advice}", r.Code));
                }
            }
    }

    // ------------------------------------------------------------------ helpers
    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    private static string Local(DateTimeOffset at, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(at, zone).ToString("HH:mm", Inv);
    private static string Ago(TimeSpan t) => t.TotalMinutes < 90 ? $"{Math.Max(0, (int)t.TotalMinutes)} min" : $"{t.TotalHours.ToString("0.#", Inv)} h";

    /// <summary>
    /// Works out the amount of each ingredient from the dose as written and the medicine's strengths. Masses, units and mmol apply to a
    /// single-ingredient medicine; mL uses the strength per mL; tablets, capsules, vials etc. multiply the strength per unit.
    /// </summary>
    public static (List<IngredientAmount> Amounts, string? Problem) Amounts(IReadOnlyList<(string Ingredient, decimal? Amount, string? Unit, decimal? PerMl)> ingredients, string dose)
    {
        var (n, unit) = DoseParser.Parse(dose);
        var result = new List<IngredientAmount>();
        string? problem = null;
        if (n is null) problem = "no number found";
        foreach (var i in ingredients)
        {
            decimal? amt = null; string? u = null;
            if (n is { } q)
            {
                if (DoseParser.ToBase(q, unit) is { } direct)
                {
                    if (ingredients.Count == 1) (amt, u) = (direct.Amount, direct.Unit);
                    else problem = "for a combination medicine give the number of tablets or mL";
                }
                else if (unit == "mL")
                {
                    if (i.Amount is { } a && i.PerMl is { } per && per > 0 && DoseParser.ToBase(a * q / per, DoseParser.NormaliseUnit(i.Unit)) is { } b) (amt, u) = (b.Amount, b.Unit);
                    else problem = "the strength per mL is not in the medicines list";
                }
                else if (unit is null) problem = "no unit (mg, mL, tablets…)";
                else if (unit == "each")
                {
                    if (i.Amount is { } a && i.PerMl is null && DoseParser.ToBase(a * q, DoseParser.NormaliseUnit(i.Unit)) is { } b) (amt, u) = (b.Amount, b.Unit);
                    else problem = "the strength per tablet, capsule or vial is not in the medicines list";
                }
                else problem = "unknown unit";
            }
            result.Add(new(i.Ingredient, amt, u));
        }
        return (result, result.All(r => r.Amount is not null) ? null : problem);
    }
}
