using System.Globalization;
using BrightPAS.Core;

namespace BrightPAS.Modules.Coding;

public enum CheckLevel { Error = 1, Warning = 2, Advice = 3 }

/// <summary>One edit-check result. <see cref="Rule"/> names the standard or requirement it comes from.</summary>
public sealed record CodingCheck(CheckLevel Level, string Rule, string Message, int? Item = null);

/// <summary>What the checks need to know about the patient and the event.</summary>
public sealed record EventFacts(string Sex, DateOnly DateOfBirth, DateOnly Admission, DateOnly Discharge, string? Anaesthetic, string PurchaserCode, string EventEndType, string AdmissionSource);

/// <summary>
/// Coder edit checks: NMDS load file rules, the Australian Coding Standards that are checkable from the codes (ACS 0031 anaesthesia,
/// ACS 0049 never-assign codes, ACS 2001 external causes with injuries) and the New Zealand Clinical Coding Conventions and NMDS
/// Reporting Requirements (July 2023 v1.1: Z03.6 needs substance, place and activity; free text for complications, injuries and external
/// causes; mesh brand/size/type; gender reaffirming surgery from 1 July 2025). Errors stop the event being marked coded; warnings do not.
/// </summary>
public static class CodingChecks
{
    public static readonly IReadOnlyDictionary<string, string> PurchaserCodes = new Dictionary<string, string>
    {
        ["06"] = "Privately funded",
        ["17"] = "Accredited employer",
        ["19"] = "Overseas chargeable",
        ["20"] = "Overseas eligible",
        ["33"] = "MoH screening pilot or programme",
        ["34"] = "MOH-funded purchase",
        ["35"] = "DHB-funded purchase (Health NZ)",
        ["98"] = "Other/not specified",
        ["A0"] = "ACC – direct purchase",
    };

    public static readonly IReadOnlyDictionary<string, string> EventEndTypes = new Dictionary<string, string>
    {
        ["DR"] = "Ended routinely",
        ["DA"] = "Discharge to acute specialist facility",
        ["DT"] = "Discharge to another healthcare facility",
        ["DW"] = "Discharge to other service within same facility",
        ["DS"] = "Self discharge (no indemnity signed)",
        ["DI"] = "Self discharge, indemnity signed",
        ["DD"] = "Died",
        ["DF"] = "Change of funder",
    };

    public static readonly IReadOnlyDictionary<string, string> DiagnosisTypes = new Dictionary<string, string>
    {
        ["A"] = "Principal diagnosis",
        ["B"] = "Other relevant diagnosis",
        ["O"] = "Operation/procedure",
        ["E"] = "External cause of injury",
        ["M"] = "Morphology (optional in NZ)",
    };

    /// <summary>Code without punctuation, upper case.</summary>
    public static string Normalise(string? code) => new((code ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>ICD-10-AM shown as K63.5; ACHI (7 digits) as 32090-00.</summary>
    public static string Display(string code)
    {
        code = Normalise(code);
        if (code.Length == 7 && code.All(char.IsDigit)) return $"{code[..5]}-{code[5..]}";
        if (code.Length > 3 && char.IsLetter(code[0])) return $"{code[..3]}.{code[3..]}";
        return code;
    }

    private static bool InRange(string code, string from, string to) =>
        string.CompareOrdinal(code, from) >= 0 && string.CompareOrdinal(code[..Math.Min(code.Length, to.Length)], to) <= 0;

    /// <summary>Injury and poisoning (S00–T79, T89–T98): ACS 2001 – needs an external cause code.</summary>
    public static bool IsInjury(string c) => c.Length > 0 && (c[0] == 'S' || (c[0] == 'T' && (InRange(c, "T00", "T79") || InRange(c, "T89", "T98"))));

    /// <summary>Procedural complications listed in the NZ NMDS requirement for free text.</summary>
    public static bool IsProceduralComplication(string c) =>
        InRange(c, "E8971", "E898") || InRange(c, "G9731", "G978") || InRange(c, "H5911", "H5989") || c.StartsWith("H952", StringComparison.Ordinal) || c.StartsWith("H958", StringComparison.Ordinal)
        || InRange(c, "I9731", "I9739") || c.StartsWith("I9789", StringComparison.Ordinal) || InRange(c, "J9561", "J9569") || c.StartsWith("J9589", StringComparison.Ordinal)
        || InRange(c, "K9161", "K9189") || InRange(c, "N9961", "N9989") || InRange(c, "T80", "T88");

    public static bool IsExternalCause(string c) => c.Length > 0 && InRange(c, "U50", "Y98");
    private static bool IsPlace(string c) => c.StartsWith("Y92", StringComparison.Ordinal);
    private static bool IsActivity(string c) => InRange(c, "U50", "U73");

    /// <summary>ACHI anaesthesia codes (blocks [1909] conduction anaesthesia and [1910] cerebral anaesthesia): 92505–92519.</summary>
    public static bool IsAnaesthesia(string c) => c.Length == 7 && c.All(char.IsDigit) && string.CompareOrdinal(c, "9250500") >= 0 && string.CompareOrdinal(c, "9251999") <= 0;

    public static IReadOnlyList<CodingCheck> Check(EventFacts f, IReadOnlyList<CodedItem> items, IReadOnlyDictionary<string, ClinicalCode> table, IReadOnlyList<CodingAdvice> advice, DateOnly today)
    {
        var r = new List<CodingCheck>();
        void E(string rule, string msg, int? i = null) => r.Add(new(CheckLevel.Error, rule, msg, i));
        void W(string rule, string msg, int? i = null) => r.Add(new(CheckLevel.Warning, rule, msg, i));

        if (f.Discharge < f.Admission) E("NMDS", "Discharge date is before the admission date.");
        if (f.Discharge > today) E("NMDS", "Discharge date is in the future – code after discharge.");
        if (!PurchaserCodes.ContainsKey(f.PurchaserCode)) E("NMDS", $"Purchaser code '{f.PurchaserCode}' is not a current code.");
        if (!EventEndTypes.ContainsKey(f.EventEndType)) E("NMDS", $"Event end type '{f.EventEndType}' is not a current code.");
        if (f.AdmissionSource is not ("R" or "T")) E("NMDS", "Admission source must be R (routine) or T (transferred).");
        if (f.Sex is not ("F" or "M")) W("NMDS", "The load file takes sex F or M; this patient is recorded as indeterminate/unknown – check the record before sending.");

        var principal = items.Where(i => i.DiagnosisType == "A").ToList();
        if (principal.Count == 0) E("NMDS", "Add a principal diagnosis (type A).");
        if (principal.Count > 1) E("NMDS", "Only one principal diagnosis (type A) is allowed.");
        if (!items.Any(i => i.DiagnosisType == "O")) W("NMDS", "No procedure coded – a day-surgery event usually has at least one.");

        var codes = items.Select(i => Normalise(i.Code)).ToList();
        var age = NzTime.Age(f.DateOfBirth, f.Admission);
        for (var n = 0; n < items.Count; n++)
        {
            var i = items[n]; var c = codes[n]; var pos = n + 1;
            if (!DiagnosisTypes.ContainsKey(i.DiagnosisType)) { E("NMDS", $"Line {pos}: diagnosis type must be A, B, O, E or M.", pos); continue; }
            if (string.IsNullOrWhiteSpace(i.Description)) E("NMDS", $"Line {pos}: a description is required for every code.", pos);
            else if (i.Description.Trim().Length > 50) E("NMDS", $"Line {pos}: description is longer than 50 characters.", pos);
            if (c.Length == 0) { E("NMDS", $"Line {pos}: no code.", pos); continue; }
            if (c.Length > 8) E("NMDS", $"Line {pos}: code is longer than 8 characters.", pos);
            if (!table.TryGetValue(c, out var code)) { E("Code table", $"Line {pos}: {Display(c)} is not in the clinical code table for the edition in use.", pos); continue; }
            if (!code.IsActive) E("Code table", $"Line {pos}: {Display(c)} is not valid in the edition in use.", pos);
            var typeOk = i.DiagnosisType switch
            {
                "A" or "B" => code.Type is "A" or "B" or "V",
                "O" => code.Type == "O",
                "E" => code.Type == "E",
                "M" => code.Type == "M",
                _ => false,
            };
            if (!typeOk) E("Code table", $"Line {pos}: {Display(c)} is a {TypeName(code.Type)} code and cannot be reported as {DiagnosisTypes[i.DiagnosisType].ToLowerInvariant()}.", pos);
            if (code.NeverAssign) E("ACS 0049", $"Line {pos}: {Display(c)} must never be assigned (ACS 0049) – use a more specific code.", pos);
            if (i.DiagnosisType == "A" && code.NotPrincipal) E("Code table", $"Line {pos}: {Display(c)} is not acceptable as a principal diagnosis.", pos);
            if (code.Sex is { Length: 1 } s && f.Sex is "F" or "M" && s != f.Sex) E("Sex edit", $"Line {pos}: {Display(c)} is for {(s == "F" ? "females" : "males")} only.", pos);
            if (code.AgeFromYears is { } a1 && age < a1 || code.AgeToYears is { } a2 && age > a2) E("Age edit", $"Line {pos}: {Display(c)} is not valid at age {age}.", pos);
            if (i.DiagnosisType is "O" or "E")
            {
                if (i.Date is null) E("NMDS", $"Line {pos}: an operation or accident date is required.", pos);
                else if (i.DiagnosisType == "O" && (i.Date < f.Admission || i.Date > f.Discharge)) E("NMDS", $"Line {pos}: procedure date is outside the admission.", pos);
                else if (i.DiagnosisType == "E" && i.Date > f.Discharge) E("NMDS", $"Line {pos}: accident date is after discharge.", pos);
            }
            if (c.StartsWith("92513", StringComparison.Ordinal)) E("ACS 0031", $"Line {pos}: do not assign 92513 Infiltration of local anaesthetic (ACS 0031, second exception).", pos);
            if ((IsProceduralComplication(c) || IsInjury(c) || IsExternalCause(c)) && string.Equals(i.Description.Trim(), code.Description.Trim(), StringComparison.OrdinalIgnoreCase))
                W("NZ NMDS", $"Line {pos}: use free text from the clinical record for complications, injuries and external causes, not the classification description.", pos);
            if (i.DiagnosisType == "O" && code.Description.Contains("mesh", StringComparison.OrdinalIgnoreCase) && !i.Description.Any(char.IsDigit))
                W("NZ NMDS", $"Line {pos}: mesh procedure – record the brand, size and type of mesh in the description.", pos);
        }

        // ACS 2001 / NZ convention: injuries need an external cause; Z03.6 needs substance, place and activity.
        var external = codes.Where((c, n) => items[n].DiagnosisType == "E").ToList();
        if (codes.Where((c, n) => items[n].DiagnosisType is "A" or "B").Any(IsInjury) && external.Count == 0)
            E("ACS 2001", "An injury or poisoning code needs an external cause code (type E).");
        if (codes.Any(c => c.StartsWith("Z036", StringComparison.Ordinal))
            && (!external.Any(c => !IsPlace(c) && !IsActivity(c)) || !external.Any(IsPlace) || !external.Any(IsActivity)))
            E("NZ convention (ACS 2001)", "Z03.6 Observation for suspected toxic effect needs external cause codes for the substance, the place of occurrence (Y92) and the activity (U50–U73).");

        // ACS 0031: anaesthesia is coded when given for a procedure.
        if (f.Anaesthetic is { } an && !an.Equals("None", StringComparison.OrdinalIgnoreCase) && !an.Equals("Local", StringComparison.OrdinalIgnoreCase)
            && items.Any(i => i.DiagnosisType == "O") && !codes.Any(IsAnaesthesia))
            W("ACS 0031", $"The booking records {an.ToLowerInvariant()} anaesthesia but no anaesthesia code [1909]/[1910] is assigned.");

        // NZ convention on gender reaffirming surgery (event end on or after 1 July 2025).
        if (f.Discharge >= new DateOnly(2025, 7, 1) && codes.Any(c => c.StartsWith("F64", StringComparison.Ordinal)) && items.Any(i => i.DiagnosisType == "O")
            && principal.Count == 1 && Normalise(principal[0].Code) != "Z4189")
            W("NZ convention", "Gender reaffirming surgery: Z41.89 is the principal diagnosis with F64 as an additional diagnosis (NZ convention from 1 July 2025).");

        // Advice linked to the codes used (only advice that applies in NZ).
        foreach (var a in advice.Where(a => a.IsActive && a.AppliesToNz))
        {
            var prefixes = a.CodePrefixes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Normalise).Where(p => p.Length > 0).ToList();
            if (prefixes.Count > 0 && codes.Any(c => prefixes.Any(p => c.StartsWith(p, StringComparison.Ordinal))))
                r.Add(new(CheckLevel.Advice, a.Reference, $"{a.Title}: {a.Summary}"));
        }
        return r;
    }

    public static string TypeName(string t) => t switch
    {
        "A" => "diagnosis",
        "B" => "injury",
        "E" => "external cause",
        "M" => "morphology",
        "O" => "procedure",
        "V" => "supplementary",
        _ => t,
    };

    /// <summary>Two-digit NMDS ethnic code from a HISO 10001 level-4 code (its first two digits), when the load file table has it.</summary>
    public static string? EthnicCode(string level4)
    {
        if (level4.Length < 2) return null;
        var two = level4[..2];
        return two is "10" or "11" or "12" or "21" or "30" or "31" or "32" or "33" or "34" or "35" or "36" or "37" or "40" or "41" or "42" or "43" or "44" or "51" or "52" or "53" ? two
            : two[0] == '9' ? "99" : null;
    }

    public static string Ymd(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
}
