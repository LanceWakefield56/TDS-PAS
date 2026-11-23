using System.Globalization;

namespace BrightPAS.Modules.Triage;

/// <summary>
/// Everything known about the patient for triage, as text facts keyed like the questionnaire (height_cm, weight_kg, heart …) plus
/// age, sex, procedure details, staff findings (cfs, bp_sys, hb, creatinine, neck_cm, past_vte, heart_failure, cancer …) and lab values.
/// Staff findings override the patient's answer for the same fact.
/// </summary>
public sealed class TriageFacts
{
    private readonly Dictionary<string, string> f = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> All => f;
    public string? this[string key] { get => f.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null; set { if (value is null) f.Remove(key); else f[key] = value; } }
    public bool Has(string key) => this[key] is not null;
    public bool Yes(string key) => string.Equals(this[key], "Yes", StringComparison.OrdinalIgnoreCase);
    public bool No(string key) => string.Equals(this[key], "No", StringComparison.OrdinalIgnoreCase);
    public decimal? Num(string key) => decimal.TryParse(this[key], NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    public string Text(params string[] keys) => string.Join(" ", keys.Select(k => this[k] ?? "")).ToLowerInvariant();

    public static TriageFacts From(IEnumerable<KeyValuePair<string, string>> values)
    {
        var t = new TriageFacts();
        foreach (var (k, v) in values) t[k] = v;
        return t;
    }
}

public sealed record ScoreLine(string Name, string Value, string Detail);

public sealed record FindingResult(string Source, TriageOutcome Outcome, string Text, string? Precaution);

public sealed class TriageResult
{
    public decimal? Bmi { get; set; }
    public int StopBang { get; set; }
    public int StopBangKnown { get; set; }
    public int? Frailty { get; set; }
    public int Vte { get; set; }
    public int Rcri { get; set; }
    public int SuggestedAsa { get; set; }
    public List<string> AsaReasons { get; } = [];
    public List<ScoreLine> Scores { get; } = [];
    public List<FindingResult> Findings { get; } = [];
    public List<string> Missing { get; } = [];
    public TriageOutcome Suggested { get; set; } = TriageOutcome.Suitable;
    public IEnumerable<string> Precautions => Findings.Where(x => x.Precaution is not null).Select(x => x.Precaution!).Distinct();
    /// <summary>Facts with the calculated scores added, as rules see them.</summary>
    public TriageFacts Facts { get; set; } = new();
}

/// <summary>
/// The triage calculation: scores, suggested ASA, medicine prompts and suitability rules. Pure – no database or clock – so the same
/// result comes out on screen, in re-triage and in the tests. Scores use only the information available and say so; nothing here decides
/// for a clinician.
/// </summary>
public static class TriageScoring
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Facts a rule can use, with a plain-English name (shown when building rules).</summary>
    public static readonly IReadOnlyDictionary<string, string> RuleFacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["bmi"] = "BMI",
        ["age"] = "Age (years)",
        ["asa"] = "ASA grade (confirmed, else suggested)",
        ["stopbang"] = "STOP-BANG score (0–8)",
        ["cfs"] = "Clinical frailty score (1–9)",
        ["vte"] = "VTE risk score",
        ["rcri"] = "Cardiac risk index (0–6)",
        ["weight_kg"] = "Weight (kg)",
        ["pacemaker"] = "Pacemaker or implanted defibrillator",
        ["recent_cardiac_stroke"] = "Heart attack, stent, heart surgery or stroke in the last 6 months",
        ["home_oxygen"] = "Home oxygen",
        ["osa_untreated"] = "Sleep apnoea without nightly CPAP",
        ["sleep_apnoea"] = "Sleep apnoea / heavy snoring / CPAP",
        ["insulin"] = "Insulin-treated diabetes",
        ["anticoagulant"] = "Takes blood thinners",
        ["no_escort"] = "No adult named to take them home and stay 24 hours",
        ["lives_alone"] = "Lives alone",
        ["family_mh"] = "Family history of anaesthetic problems",
        ["anaesthetic_problems"] = "Previous anaesthetic problems",
        ["airway"] = "Limited neck movement or mouth opening",
        ["low_mets"] = "Cannot climb two flights of stairs",
        ["infection"] = "Resistant organism or overseas hospital stay",
        ["pregnant"] = "Pregnant or possibly pregnant",
        ["heart"] = "Heart condition",
        ["lung"] = "Lung condition",
        ["kidney_liver"] = "Kidney or liver disease",
        ["stroke"] = "Stroke, TIA or epilepsy",
        ["latex"] = "Latex allergy",
        ["hba1c"] = "HbA1c (mmol/mol)",
        ["hb"] = "Haemoglobin (g/L)",
        ["bp_sys"] = "Systolic blood pressure",
        ["creatinine"] = "Creatinine (µmol/L)",
        ["questionnaire"] = "Pre-admission questionnaire completed",
    };

    public static TriageResult Calculate(TriageFacts input, RuleSet? rules, string procedure, string? anaesthetic, int? durationMinutes)
    {
        var r = new TriageResult();
        var f = TriageFacts.From(input.All);
        r.Facts = f;
        var age = f.Num("age");

        // BMI
        var h = f.Num("height_cm"); var w = f.Num("weight_kg");
        if (h is > 50 and < 260 && w is > 0 and < 400)
        {
            r.Bmi = Math.Round(w.Value / (h.Value / 100 * (h.Value / 100)), 1);
            f["bmi"] = r.Bmi.Value.ToString(Inv);
            r.Scores.Add(new("BMI", r.Bmi.Value.ToString("0.0", Inv), $"{w:0.#} kg, {h:0.#} cm"));
        }
        else r.Missing.Add("Height and weight (for BMI)");

        // STOP-BANG – counted from what is known; unknown items are listed.
        var sb = new (string Item, bool? Value)[]
        {
            ("Snoring loudly", f.Has("snoring_loud") ? f.Yes("snoring_loud") : null),
            ("Tired during the day", f.Has("daytime_tired") ? f.Yes("daytime_tired") : null),
            ("Observed stopping breathing", f.Has("observed_apnoea") ? f.Yes("observed_apnoea") : null),
            ("High blood pressure", f.Has("high_bp") ? f.Yes("high_bp") : null),
            ("BMI over 35", r.Bmi is null ? null : r.Bmi > 35),
            ("Age over 50", age is null ? null : age > 50),
            ("Neck over 40 cm", f.Num("neck_cm") is { } n ? n > 40 : null),
            ("Male", f.Has("sex") ? string.Equals(f["sex"], "Male", StringComparison.OrdinalIgnoreCase) : null),
        };
        r.StopBang = sb.Count(x => x.Value == true);
        r.StopBangKnown = sb.Count(x => x.Value is not null);
        f["stopbang"] = r.StopBang.ToString(Inv);
        r.Scores.Add(new("STOP-BANG", $"{r.StopBang} of 8", (r.StopBang >= 5 ? "high risk of sleep apnoea" : r.StopBang >= 3 ? "intermediate risk" : "low risk") +
            (r.StopBangKnown < 8 ? $" – {8 - r.StopBangKnown} item(s) unknown: {string.Join(", ", sb.Where(x => x.Value is null).Select(x => x.Item))}" : "")));
        if (r.StopBangKnown < 8) r.Missing.Add($"STOP-BANG: {string.Join(", ", sb.Where(x => x.Value is null).Select(x => x.Item))}");

        // Clinical frailty (staff judgement, 1–9)
        if (f.Num("cfs") is { } cfs and >= 1 and <= 9)
        {
            r.Frailty = (int)cfs;
            r.Scores.Add(new("Clinical frailty", r.Frailty.Value.ToString(Inv), r.Frailty >= 5 ? "frail" : r.Frailty == 4 ? "vulnerable" : "fit"));
        }
        else if (age >= 65) r.Missing.Add("Clinical frailty score (patient is 65 or over)");

        // Derived yes/no facts used by rules
        var insulin = string.Equals(f["diabetes"], "Yes – insulin", StringComparison.Ordinal);
        if (insulin) f["insulin"] = "Yes";
        if (f.Yes("blood_thinners")) f["anticoagulant"] = "Yes";
        if (!f.Has("support_person") && !f.Has("escort") && f.Has("questionnaire")) f["no_escort"] = "Yes";
        if (f.Yes("neck")) f["airway"] = "Yes";
        if (f.No("stairs")) f["low_mets"] = "Yes";
        if (f.Yes("sleep_apnoea") && string.Equals(f["cpap_use"], "No", StringComparison.Ordinal)) f["osa_untreated"] = "Yes";
        if (f["pregnant"] is "Yes" or "Possibly") f["pregnant"] = "Yes";
        var pastVte = f.Yes("past_vte") || (f.Yes("bleeding") && f.Text("bleeding_details").Split(' ', ',', '.', '/').Any(x => x is "dvt" or "pe" or "clot" or "clots" or "thrombosis" or "embolism"));

        // VTE risk (Caprini items available)
        var vte = 0;
        var vteWhy = new List<string>();
        void V(int pts, string why) { vte += pts; vteWhy.Add($"{why} +{pts}"); }
        if (age is >= 41 and <= 60) V(1, "age 41–60"); else if (age is >= 61 and <= 74) V(2, "age 61–74"); else if (age >= 75) V(3, "age 75+");
        if (r.Bmi > 25) V(1, "BMI over 25");
        if (durationMinutes > 45) V(2, "surgery over 45 minutes"); else V(1, "minor surgery");
        if (pastVte) V(3, "past DVT/PE");
        if (f.Yes("pregnant")) V(1, "pregnancy");
        if (string.Equals(f["mobility"], "Wheelchair", StringComparison.Ordinal)) V(2, "confined to a wheelchair");
        if (f.Yes("cancer")) V(2, "cancer");
        if (f.Yes("heart_failure")) V(1, "heart failure");
        r.Vte = vte;
        f["vte"] = vte.ToString(Inv);
        r.Scores.Add(new("VTE risk", vte.ToString(Inv), (vte >= 5 ? "high" : vte >= 3 ? "moderate" : vte >= 1 ? "low" : "very low") + " – " + string.Join(", ", vteWhy)));

        // Revised cardiac risk index
        var rcri = new List<string>();
        if (f.Yes("heart") || f.Yes("recent_cardiac_stroke")) rcri.Add("ischaemic or other heart disease");
        if (f.Yes("heart_failure") || f.Text("heart_details").Contains("failure", StringComparison.Ordinal)) rcri.Add("heart failure");
        if (f.Yes("stroke")) rcri.Add("stroke or TIA");
        if (insulin) rcri.Add("insulin");
        if (f.Num("creatinine") > 177) rcri.Add("creatinine over 177");
        if (f.Yes("high_risk_surgery")) rcri.Add("high-risk surgery");
        r.Rcri = rcri.Count;
        f["rcri"] = r.Rcri.ToString(Inv);
        r.Scores.Add(new("Cardiac risk index", r.Rcri.ToString(Inv), rcri.Count == 0 ? "no risk factors" : string.Join(", ", rcri)));

        // Suggested ASA (the anaesthetist confirms)
        var iv = new List<string>(); var iii = new List<string>(); var ii = new List<string>();
        if (f.Yes("recent_cardiac_stroke")) iv.Add("recent heart attack, stent, heart surgery or stroke");
        if (f.Yes("home_oxygen")) iv.Add("home oxygen");
        if (r.Bmi >= 40) iii.Add("BMI 40 or more");
        if (insulin) iii.Add("insulin-treated diabetes");
        if (f.Yes("pacemaker")) iii.Add("pacemaker or defibrillator");
        if (f.Yes("heart")) iii.Add("heart condition");
        if (f.Yes("stroke")) iii.Add("stroke, TIA or epilepsy");
        if (f.Yes("kidney_liver")) iii.Add("kidney or liver disease");
        if (f["alcohol"] == "More than 20") iii.Add("alcohol over 20 drinks a week");
        if (f.Yes("low_mets")) iii.Add("low functional capacity");
        if (r.Frailty >= 5) iii.Add("frailty");
        if (r.Bmi is >= 30 and < 40) ii.Add("BMI 30–39.9");
        if (f.Yes("high_bp")) ii.Add("high blood pressure");
        if (f["diabetes"] is { } dm && dm.StartsWith("Yes", StringComparison.Ordinal) && !insulin) ii.Add("diabetes (not insulin)");
        if (f["smoking"] is "Smoke" or "Vape") ii.Add("smoking or vaping");
        if (f["alcohol"] == "11–20") ii.Add("alcohol 11–20 drinks a week");
        if (f.Yes("lung")) ii.Add("lung condition");
        if (f.Yes("sleep_apnoea")) ii.Add("sleep apnoea");
        if (f.Yes("pregnant")) ii.Add("pregnancy");
        (r.SuggestedAsa, var why) = iv.Count > 0 ? (4, iv) : iii.Count > 0 ? (3, iii) : ii.Count > 0 ? (2, ii) : (1, ["no systemic disease reported"]);
        r.AsaReasons.AddRange(why);
        var asa = f.Num("asa_confirmed") is { } ca and >= 1 and <= 6 ? (int)ca : r.SuggestedAsa;
        f["asa"] = asa.ToString(Inv);
        r.Scores.Add(new("ASA", f.Has("asa_confirmed") ? $"{asa} (confirmed)" : $"{r.SuggestedAsa} (suggested)", string.Join(", ", why)));

        if (!f.Has("questionnaire")) { r.Missing.Insert(0, "Pre-admission questionnaire"); Add(r, "INFO:questionnaire", TriageOutcome.MoreInformation, "The pre-admission questionnaire has not been completed.", null); }
        if (r.Bmi is null) Add(r, "INFO:bmi", TriageOutcome.MoreInformation, "BMI unknown – height and weight are needed.", null);

        if (rules is not null)
        {
            // Medicine prompts
            var meds = f.Text("medications", "blood_thinners_details", "glp1_details", "herbal_details", "diabetes_medicines");
            foreach (var m in rules.Medicines.Where(m => m.IsActive))
            {
                var words = m.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var hit = words.FirstOrDefault(k => meds.Contains(k.ToLowerInvariant(), StringComparison.Ordinal));
                if (hit is null && m.Name.Contains("GLP", StringComparison.OrdinalIgnoreCase) && f.Yes("glp1")) hit = "weekly GLP-1 injection";
                if (hit is null && m.Name.Contains("insulin", StringComparison.OrdinalIgnoreCase) && insulin) hit = "insulin";
                if (hit is not null) Add(r, "MED:" + Trunc(m.Name, 36), m.Outcome, $"{m.Name} ({hit}): {m.Instruction}", m.Instruction);
            }
            // Suitability rules
            foreach (var rule in rules.Rules.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Code, StringComparer.Ordinal))
            {
                if (!Applies(rule, procedure, anaesthetic)) continue;
                if (Fires(rule, f, out var shown))
                    Add(r, rule.Code, rule.Outcome, $"{rule.Name}{(shown is null ? "" : $" ({shown})")}{(string.IsNullOrWhiteSpace(rule.Explanation) ? "" : " – " + rule.Explanation)}", rule.Precaution);
            }
        }
        if (r.Findings.Count > 0) r.Suggested = r.Findings.Max(x => x.Outcome);
        if (r.Suggested == TriageOutcome.Suitable && r.Findings.Exists(x => x.Precaution is not null)) r.Suggested = TriageOutcome.SuitableWithPrecautions;
        return r;
    }

    private static string Trunc(string s, int n) => s.Length > n ? s[..n] : s;

    private static void Add(TriageResult r, string source, TriageOutcome o, string text, string? precaution) =>
        r.Findings.Add(new(source, o, text.Length > 500 ? text[..500] : text, precaution));

    public static bool Applies(SuitabilityRule rule, string procedure, string? anaesthetic)
    {
        if (!string.IsNullOrWhiteSpace(rule.Anaesthetic) && !string.Equals(rule.Anaesthetic, anaesthetic, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(rule.ProcedureContains)) return true;
        return rule.ProcedureContains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(w => procedure.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    public static bool Fires(SuitabilityRule rule, TriageFacts f, out string? shown)
    {
        shown = null;
        var v = f[rule.Fact];
        switch (rule.Operator)
        {
            case RuleOperator.Missing: return v is null;
            case RuleOperator.IsYes: return f.Yes(rule.Fact);
            case RuleOperator.Equals: shown = v; return v is not null && string.Equals(v, rule.TextValue, StringComparison.OrdinalIgnoreCase);
        }
        if (f.Num(rule.Fact) is not { } n || rule.Threshold is not { } t) return false;
        shown = $"{RuleFacts.GetValueOrDefault(rule.Fact, rule.Fact)} {n.ToString("0.#", Inv)}";
        return rule.Operator switch
        {
            RuleOperator.AtLeast => n >= t,
            RuleOperator.AtMost => n <= t,
            RuleOperator.Above => n > t,
            RuleOperator.Below => n < t,
            _ => false
        };
    }

    /// <summary>What changed since the previous triage, and whether it got worse.</summary>
    public static (string Summary, bool Worse) Compare(Assessment? previous, TriageResult now)
    {
        if (previous is null) return ("First triage.", false);
        var lines = new List<string>();
        var before = previous.FinalOutcome ?? previous.SuggestedOutcome;
        if (before != now.Suggested) lines.Add($"Outcome {Outcomes.Labels[before]} → {Outcomes.Labels[now.Suggested]}");
        void Cmp(string name, object? a, object? b) { if (!Equals(a, b)) lines.Add($"{name} {a ?? "–"} → {b ?? "–"}"); }
        Cmp("BMI", previous.Bmi, now.Bmi);
        Cmp("STOP-BANG", previous.StopBang, now.StopBang);
        Cmp("Suggested ASA", previous.SuggestedAsa, now.SuggestedAsa);
        Cmp("Frailty", previous.Frailty, now.Frailty);
        Cmp("VTE", previous.Vte, now.Vte);
        Cmp("Cardiac risk", previous.Rcri, now.Rcri);
        var old = previous.Findings.Select(x => x.Text).ToHashSet(StringComparer.Ordinal);
        var added = now.Findings.Where(x => !old.Contains(x.Text)).ToList();
        foreach (var a in added) lines.Add("New: " + a.Text);
        foreach (var gone in previous.Findings.Where(x => now.Findings.TrueForAll(n => n.Text != x.Text))) lines.Add("No longer: " + gone.Text);
        var worse = now.Suggested > before || added.Exists(a => a.Outcome >= TriageOutcome.AnaesthetistReview);
        return (lines.Count == 0 ? "No change." : string.Join("\n", lines), worse);
    }

    // ------------------------------------------------------------------ example rules (must be clinically approved before use)
    public static RuleSet ExampleRuleSet(int version, string user, DateTimeOffset now)
    {
        var rs = new RuleSet
        {
            Version = version,
            Name = "Example day-surgery rules (not yet clinically approved)",
            IsExample = true,
            CreatedBy = user,
            CreatedAt = now,
            Notes = "Starting point only, based on common day-surgery criteria. TDS's anaesthetists must review and change every limit before approving."
        };
        var o = 0;
        SuitabilityRule R(string code, string name, string fact, RuleOperator op, decimal? t, TriageOutcome outcome, string? precaution = null, string? why = null, string? anaes = null, string? proc = null) =>
            new() { RuleSetId = rs.Id, Code = code, Name = name, Fact = fact, Operator = op, Threshold = t, Outcome = outcome, Precaution = precaution, Explanation = why, Anaesthetic = anaes, ProcedureContains = proc, SortOrder = o += 10 };
        rs.Rules.AddRange(
        [
            R("BMI-45", "BMI 45 or more", "bmi", RuleOperator.AtLeast, 45, TriageOutcome.NotSuitable, why: "Above TDS's day-surgery BMI limit."),
            R("BMI-40", "BMI 40 or more", "bmi", RuleOperator.AtLeast, 40, TriageOutcome.AnaesthetistReview, "Bariatric table and equipment", "Anaesthetist to review airway and sleep apnoea risk."),
            R("BMI-35", "BMI 35 or more", "bmi", RuleOperator.AtLeast, 35, TriageOutcome.SuitableWithPrecautions, "Check table weight limit and large cuff"),
            R("ASA-4", "ASA 4 or more", "asa", RuleOperator.AtLeast, 4, TriageOutcome.NotSuitable, why: "Not suitable for a day-stay facility without on-site critical care."),
            R("ASA-3", "ASA 3", "asa", RuleOperator.AtLeast, 3, TriageOutcome.AnaesthetistReview, null, "Anaesthetist to confirm the ASA grade and plan."),
            R("OSA-HI", "STOP-BANG 5 or more", "stopbang", RuleOperator.AtLeast, 5, TriageOutcome.AnaesthetistReview, "Extended recovery observation", "High risk of undiagnosed sleep apnoea.", anaes: "General"),
            R("OSA-UNTR", "Sleep apnoea without nightly CPAP", "osa_untreated", RuleOperator.IsYes, null, TriageOutcome.AnaesthetistReview, "Bring CPAP machine on the day"),
            R("CARD-RCT", "Recent heart attack, stent, heart surgery or stroke", "recent_cardiac_stroke", RuleOperator.IsYes, null, TriageOutcome.NotSuitable, why: "Elective surgery is usually deferred for at least 6 months."),
            R("ICD", "Pacemaker or implanted defibrillator", "pacemaker", RuleOperator.IsYes, null, TriageOutcome.AnaesthetistReview, "Device check and magnet / programming plan"),
            R("O2", "Home oxygen", "home_oxygen", RuleOperator.IsYes, null, TriageOutcome.NotSuitable),
            R("MH", "Family history of anaesthetic problems", "family_mh", RuleOperator.IsYes, null, TriageOutcome.AnaesthetistReview, "Possible MH – volatile-free machine, dantrolene available"),
            R("ESC", "No adult named to take them home and stay 24 hours", "no_escort", RuleOperator.IsYes, null, TriageOutcome.MoreInformation, why: "An escort and overnight carer are needed after sedation or anaesthetic."),
            R("FRAIL", "Clinical frailty 6 or more", "cfs", RuleOperator.AtLeast, 6, TriageOutcome.AnaesthetistReview),
            R("AGE-85", "Age 85 or over", "age", RuleOperator.AtLeast, 85, TriageOutcome.AnaesthetistReview, "First on the list"),
            R("METS", "Cannot climb two flights of stairs", "low_mets", RuleOperator.IsYes, null, TriageOutcome.AnaesthetistReview, null, "Low functional capacity.", anaes: "General"),
            R("HBA1C", "HbA1c over 75 mmol/mol", "hba1c", RuleOperator.Above, 75, TriageOutcome.AnaesthetistReview, null, "Poorly controlled diabetes."),
            R("HB", "Haemoglobin under 100 g/L", "hb", RuleOperator.Below, 100, TriageOutcome.AnaesthetistReview),
            R("BP", "Systolic blood pressure 180 or more", "bp_sys", RuleOperator.AtLeast, 180, TriageOutcome.AnaesthetistReview, null, "Recheck and refer to GP before surgery."),
            R("INS", "Insulin-treated diabetes", "insulin", RuleOperator.IsYes, null, TriageOutcome.SuitableWithPrecautions, "Morning list; diabetes plan and glucose checks"),
            R("LATEX", "Latex allergy", "latex", RuleOperator.IsYes, null, TriageOutcome.SuitableWithPrecautions, "Latex-free theatre – first on the list"),
            R("INF", "Resistant organism or overseas hospital stay", "infection", RuleOperator.IsYes, null, TriageOutcome.SuitableWithPrecautions, "Infection control precautions – last on the list"),
            R("PREG", "Pregnant or possibly pregnant", "pregnant", RuleOperator.IsYes, null, TriageOutcome.AnaesthetistReview, null, "Pregnancy test on the day; surgeon to confirm."),
            R("VTE", "VTE risk score 5 or more", "vte", RuleOperator.AtLeast, 5, TriageOutcome.SuitableWithPrecautions, "VTE prophylaxis plan"),
        ]);
        MedicineRule M(string name, string words, string instruction, TriageOutcome outcome = TriageOutcome.SuitableWithPrecautions) =>
            new() { RuleSetId = rs.Id, Name = name, Keywords = words, Instruction = instruction, Outcome = outcome };
        rs.Medicines.AddRange(
        [
            M("Warfarin", "warfarin, marevan, coumadin", "Stop 5 days before; INR on the day – confirm with the prescriber and surgeon."),
            M("DOAC", "dabigatran, pradaxa, rivaroxaban, xarelto, apixaban, eliquis", "Usually stop 2–3 days before depending on kidney function and bleeding risk – surgeon/anaesthetist to confirm."),
            M("Antiplatelet", "clopidogrel, plavix, ticagrelor, brilinta, prasugrel", "Do not stop without the cardiologist if there is a stent – surgeon to decide.", TriageOutcome.AnaesthetistReview),
            M("Aspirin", "aspirin, cartia", "Usually continue – surgeon to confirm."),
            M("SGLT2 inhibitor", "empagliflozin, jardiance, dapagliflozin, forxiga, jardiamet", "Stop the day before and the day of surgery (risk of ketoacidosis)."),
            M("GLP-1 agonist", "semaglutide, ozempic, wegovy, dulaglutide, trulicity, liraglutide, victoza, saxenda, tirzepatide, mounjaro", "Follow the current ANZCA/ADS guidance: consider holding and a longer fluid fast – anaesthetist to decide.", TriageOutcome.AnaesthetistReview),
            M("Insulin", "insulin, lantus, novorapid, humalog, levemir, toujeo, protaphane, penmix", "Diabetes plan needed: dose changes the night before and morning of surgery."),
            M("Metformin", "metformin", "Usually omit on the morning of surgery."),
            M("MAOI", "phenelzine, nardil, tranylcypromine, moclobemide", "Anaesthetist must know – drug interactions.", TriageOutcome.AnaesthetistReview),
        ]);
        return rs;
    }
}
