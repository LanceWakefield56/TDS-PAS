using BrightPAS.Modules.Quality;

namespace BrightPAS.Modules.FollowUp;

/// <summary>A question in the call script or the patient check-in. Type: yesno, scale (0–10), choice, text.</summary>
public sealed record Question(string Id, string Text, string Type, IReadOnlyList<string>? Choices = null, bool PatientForm = true, bool Wound = false);

/// <summary>
/// The follow-up script, the warning rules and the wound assessment (pure – change here only, with a test). Warning rules decide whether a patient's
/// own answer alerts the duty nurse; the wound assessment decides whether to suggest an SSI surveillance record. Clinical wording to be approved by TDS.
/// </summary>
public static class FollowUpScript
{
    public static readonly IReadOnlyList<Question> Questions =
    [
        new("pain", "Pain now, from 0 (none) to 10 (worst)", "scale"),
        new("pain_relief_working", "Are your pain medicines working?", "yesno"),
        new("nausea", "Feeling sick or vomiting?", "choice", ["No", "A little", "Can't keep fluids down"]),
        new("bleeding", "Any bleeding?", "choice", ["None", "A little spotting", "Soaking the dressing", "Heavy or won't stop"]),
        new("wound_red", "Is the skin around the wound red, hot or spreading?", "yesno", Wound: true),
        new("wound_swollen", "Is the wound swollen or hard?", "yesno", Wound: true),
        new("wound_discharge", "Anything coming out of the wound?", "choice", ["Nothing", "Clear or slightly pink", "Cloudy, yellow/green or smelly"], Wound: true),
        new("wound_open", "Has the wound opened?", "yesno", Wound: true),
        new("fever", "Fever, chills or feeling hot and shivery?", "yesno"),
        new("eating", "Eating and drinking normally?", "yesno"),
        new("bowels_bladder", "Passing urine and bowels as normal for you?", "yesno"),
        new("mobility", "Up and walking about?", "yesno"),
        new("medicines", "Any problems with your medicines?", "yesno"),
        new("antibiotics", "Has a doctor given you antibiotics for the wound since your operation?", "yesno", Wound: true),
        new("seen_doctor", "Have you seen a GP, been to an emergency department or been admitted to hospital since?", "yesno"),
        new("work", "Back to work or usual activities?", "choice", ["Yes", "Not yet", "Not applicable"], PatientForm: false),
        new("satisfaction", "How happy were you with your care, 0 to 10?", "scale"),
        new("concerns", "Anything else worrying you?", "text"),
    ];

    private static readonly HashSet<string> Known = [.. Questions.Select(q => q.Id), "concerns"];

    public static Dictionary<string, string> Clean(IReadOnlyDictionary<string, string>? answers) =>
        (answers ?? new Dictionary<string, string>()).Where(kv => Known.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim().Length > 1000 ? kv.Value.Trim()[..1000] : kv.Value.Trim(), StringComparer.Ordinal);

    private static bool Yes(IReadOnlyDictionary<string, string> a, string k) => a.TryGetValue(k, out var v) && (v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("true", StringComparison.OrdinalIgnoreCase));
    private static string Get(IReadOnlyDictionary<string, string> a, string k) => a.TryGetValue(k, out var v) ? v : "";

    /// <summary>Answers that need a nurse to call today. Empty = no concern.</summary>
    public static IReadOnlyList<string> Warnings(IReadOnlyDictionary<string, string> a)
    {
        var w = new List<string>();
        if (int.TryParse(Get(a, "pain"), out var pain) && pain >= 7) w.Add($"Pain {pain}/10");
        if (Get(a, "pain_relief_working").Equals("no", StringComparison.OrdinalIgnoreCase) && pain >= 5) w.Add("Pain medicines not working");
        if (Get(a, "nausea").StartsWith("Can't", StringComparison.Ordinal)) w.Add("Can't keep fluids down");
        if (Get(a, "bleeding") is "Soaking the dressing" or "Heavy or won't stop") w.Add("Bleeding: " + Get(a, "bleeding"));
        if (Yes(a, "fever")) w.Add("Fever or chills");
        if (Yes(a, "wound_open")) w.Add("Wound has opened");
        if (Get(a, "wound_discharge").StartsWith("Cloudy", StringComparison.Ordinal)) w.Add("Cloudy or smelly wound discharge");
        if (Yes(a, "wound_red") && Yes(a, "wound_swollen")) w.Add("Wound red and swollen");
        if (Get(a, "bowels_bladder").Equals("no", StringComparison.OrdinalIgnoreCase)) w.Add("Not passing urine or bowels as normal");
        if (Yes(a, "seen_doctor")) w.Add("Has seen a GP / ED / hospital since the operation");
        if (Yes(a, "antibiotics")) w.Add("Given antibiotics for the wound");
        return w;
    }

    /// <summary>
    /// Suggests an SSI surveillance record (CDC: purulent drainage, or signs of infection with the wound opened, or a doctor's diagnosis/antibiotics).
    /// The classification itself is confirmed by the infection prevention lead.
    /// </summary>
    public static (bool Suspected, string Signs) Wound(IReadOnlyDictionary<string, string> a)
    {
        var signs = new List<string>();
        if (Get(a, "wound_discharge").StartsWith("Cloudy", StringComparison.Ordinal)) signs.Add("purulent discharge");
        if (Yes(a, "wound_red")) signs.Add("redness/heat");
        if (Yes(a, "wound_swollen")) signs.Add("swelling");
        if (Yes(a, "wound_open")) signs.Add("wound opened");
        if (Yes(a, "fever")) signs.Add("fever");
        if (Yes(a, "antibiotics")) signs.Add("antibiotics for the wound");
        var suspected = signs.Contains("purulent discharge") || signs.Contains("antibiotics for the wound")
            || signs.Contains("wound opened") && (signs.Contains("redness/heat") || signs.Contains("swelling") || signs.Contains("fever"))
            || signs.Contains("redness/heat") && signs.Contains("swelling") && signs.Contains("fever");
        return (suspected, string.Join(", ", signs));
    }

    /// <summary>
    /// A day TDS is open for follow-up calls: a weekday that is not a national public holiday, not Nelson Anniversary Day (Monday nearest 1 February)
    /// and not one of the extra closed days set in Quality. (The 25 Dec–15 Jan rule in <see cref="Quality.WorkingDays"/> is for complaints only.)
    /// </summary>
    public static bool IsClinicDay(DateOnly d, IReadOnlySet<DateOnly>? extraClosed = null)
    {
        if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        if (Quality.WorkingDays.PublicHolidays(d.Year).Contains(d) || d == NelsonAnniversary(d.Year)) return false;
        return extraClosed is null || !extraClosed.Contains(d);
    }

    public static DateOnly NelsonAnniversary(int year)
    {
        var feb1 = new DateOnly(year, 2, 1);
        var offset = ((int)DayOfWeek.Monday - (int)feb1.DayOfWeek + 7) % 7; // days forward to Monday
        return offset <= 3 ? feb1.AddDays(offset) : feb1.AddDays(offset - 7);
    }

    public static DateOnly AddClinicDays(DateOnly from, int days, IReadOnlySet<DateOnly>? extraClosed = null)
    {
        var d = from;
        for (var n = 0; n < days;) { d = d.AddDays(1); if (IsClinicDay(d, extraClosed)) n++; }
        return d;
    }

    /// <summary>The due date of a plan step: calendar days or NZ working days after the operation (weekends and public holidays moved forward).</summary>
    public static DateOnly DueOn(DateOnly operation, PlanStep s, bool implant, IReadOnlySet<DateOnly>? extraClosed = null)
    {
        var days = implant && s.DaysWithImplant is { } di ? di : s.Days;
        if (s.WorkingDays) return AddClinicDays(operation, Math.Max(days, 1), extraClosed);
        var d = operation.AddDays(days);
        // A phone call that falls on a closed day moves to the next working day; SSI checks are just "on or after".
        while (s.Kind != ContactKind.SsiCheck && !IsClinicDay(d, extraClosed)) d = d.AddDays(1);
        return d;
    }

    /// <summary>The starter plan (example, to be agreed by TDS): next-working-day call, day-5 wound check by text, day-30 SSI check (90 with an implant).</summary>
    public static Plan ExamplePlan(string user, DateTimeOffset now) => new()
    {
        Name = "Standard day surgery",
        IsDefault = true,
        MaxAttempts = 3,
        SortOrder = 100,
        UpdatedBy = user,
        UpdatedAt = now,
        Notes = "Example plan – agree the contacts with the clinical team.",
        Steps =
        [
            new() { Sequence = 1, Kind = ContactKind.Call, Method = ContactMethod.Phone, Days = 1, WorkingDays = true, Label = "Next working day phone call" },
            new() { Sequence = 2, Kind = ContactKind.WoundCheck, Method = ContactMethod.PatientForm, Days = 5, Label = "Day 5 wound check (text message)" },
            new() { Sequence = 3, Kind = ContactKind.Review, Method = ContactMethod.Phone, Days = 3, WorkingDays = true, OnlyIfHighRisk = true, Label = "Extra call – high risk" },
            new() { Sequence = 4, Kind = ContactKind.SsiCheck, Method = ContactMethod.PatientForm, Days = 30, DaysWithImplant = 90, Label = "SSI check (day 30 / 90 with implant)" },
        ]
    };

    public static Plan EndoscopyPlan(string user, DateTimeOffset now) => new()
    {
        Name = "Endoscopy",
        ProcedureContains = "colonoscop, gastroscop, sigmoidoscop, endoscop",
        MaxAttempts = 2,
        SortOrder = 10,
        UpdatedBy = user,
        UpdatedAt = now,
        Notes = "Example plan – no wound, so no SSI check.",
        Steps = [new() { Sequence = 1, Kind = ContactKind.Call, Method = ContactMethod.Phone, Days = 1, WorkingDays = true, Label = "Next working day phone call" }]
    };

    public static readonly IReadOnlyDictionary<Escalation, string> EscalationLabels = new Dictionary<Escalation, string>
    {
        [Escalation.None] = "None",
        [Escalation.Advice] = "Advice given",
        [Escalation.GpReview] = "GP review",
        [Escalation.ReturnToTds] = "Return to TDS",
        [Escalation.SurgeonContacted] = "Surgeon contacted",
        [Escalation.EmergencyDepartment] = "Emergency department",
    };

    public static readonly IReadOnlyDictionary<SsiClass, string> ClassLabels = new Dictionary<SsiClass, string>
    {
        [SsiClass.Suspected] = "Suspected – to confirm",
        [SsiClass.Superficial] = "Superficial incisional",
        [SsiClass.Deep] = "Deep incisional",
        [SsiClass.OrganSpace] = "Organ/space",
        [SsiClass.NotSsi] = "Not an SSI",
    };
}
