using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Forms;
using BrightPAS.Modules.Labs;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Triage;

public sealed record StaffInput(string Fact, string? Value, string? Reason);

public sealed record SignOffRequest(TriageOutcome Outcome, int? ConfirmedAsa, string? Precautions, string? Notes, string? OverrideReason);

public sealed record WorkItem(Guid Id, Guid PatientId, string Patient, string? Nhi, string Procedure, DateOnly? ProcedureDate, TriageOutcome Suggested, TriageOutcome? Final,
    AssessmentStatus Status, TriageTrigger Trigger, bool Worse, string? Missing, DateTimeOffset CreatedAt, bool Overdue, bool RulesApproved);

/// <summary>Triage assessments: build facts, calculate, re-triage, sign off, worklists, the booking gate and the re-triage scan.</summary>
public sealed class TriageService(TriageDbContext db, PatientsDbContext patients, FormsDbContext forms, TheatreDbContext theatre, LabsDbContext labs,
    AuditWriter audit, TimeProvider clock)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Staff findings a nurse or anaesthetist can enter (fact → label). They override the patient's answer for the same fact.</summary>
    public static readonly IReadOnlyDictionary<string, string> StaffFacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["height_cm"] = "Height (cm)",
        ["weight_kg"] = "Weight (kg)",
        ["neck_cm"] = "Neck circumference (cm)",
        ["cfs"] = "Clinical frailty score (1–9)",
        ["asa_confirmed"] = "ASA grade (confirmed by anaesthetist)",
        ["bp_sys"] = "Blood pressure – systolic",
        ["bp_dia"] = "Blood pressure – diastolic",
        ["mallampati"] = "Mallampati (1–4)",
        ["hb"] = "Haemoglobin (g/L)",
        ["hba1c"] = "HbA1c (mmol/mol)",
        ["creatinine"] = "Creatinine (µmol/L)",
        ["egfr"] = "eGFR",
        ["snoring_loud"] = "Snores loudly (Yes/No)",
        ["daytime_tired"] = "Tired during the day (Yes/No)",
        ["observed_apnoea"] = "Observed stopping breathing (Yes/No)",
        ["stairs"] = "Can climb two flights of stairs (Yes/No)",
        ["past_vte"] = "Past DVT or PE (Yes/No)",
        ["heart_failure"] = "Heart failure (Yes/No)",
        ["cancer"] = "Active cancer (Yes/No)",
        ["high_risk_surgery"] = "High-risk surgery (Yes/No)",
        ["escort"] = "Escort and carer confirmed (name/phone)",
        ["cpap_use"] = "Uses CPAP every night (Yes/No/Not applicable)",
        ["ecg"] = "ECG findings",
        ["clinic_notes"] = "Clinic findings",
    };

    private static readonly string[] YesNoFacts = ["snoring_loud", "daytime_tired", "observed_apnoea", "stairs", "past_vte", "heart_failure", "cancer", "high_risk_surgery"];

    public async Task<TriageSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is not null) return s;
        s = new TriageSettings();
        db.Settings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<RuleSet?> ApprovedRulesAsync(CancellationToken ct = default) =>
        await db.RuleSets.AsNoTracking().Include(r => r.Rules).Include(r => r.Medicines).SingleOrDefaultAsync(r => r.Status == RuleSetStatus.Approved, ct);

    // ------------------------------------------------------------------ facts
    private sealed record Planned(Guid? BookingId, string Procedure, string? Anaesthetic, DateOnly? Date, int? Minutes);

    private async Task<Planned> PlannedAsync(Guid patientId, Guid? bookingId, CancellationToken ct)
    {
        var q = from b in theatre.Bookings.AsNoTracking()
                join s in theatre.Sessions.AsNoTracking() on b.SessionId equals s.Id
                where b.PatientId == patientId && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.DidNotAttend
                select new { b.Id, b.Procedure, b.Anaesthetic, s.Date, b.DurationMinutes, b.Status };
        var today = NzTime.Today(clock);
        var row = bookingId is not null
            ? await q.SingleOrDefaultAsync(x => x.Id == bookingId, ct)
            : await q.Where(x => x.Date >= today && x.Status < BookingStatus.Discharged).OrderBy(x => x.Date).FirstOrDefaultAsync(ct);
        return row is null ? new(bookingId, "", null, null, null) : new(row.Id, row.Procedure, row.Anaesthetic.ToString(), row.Date, row.DurationMinutes);
    }

    private async Task<(TriageFacts Facts, Guid? FormId)> FactsAsync(Guid patientId, Guid? formId, Planned planned, Dictionary<string, string> staff, CancellationToken ct)
    {
        var f = new TriageFacts();
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct) ?? throw new KeyNotFoundException("Patient not found.");
        f["age"] = NzTime.Age(p.DateOfBirth, planned.Date ?? NzTime.Today(clock)).ToString(Inv);
        f["sex"] = p.Sex.ToString();
        var form = await forms.Requests.AsNoTracking().Include(r => r.Answers)
            .Where(r => r.PatientId == patientId && (r.Status == FormStatus.Submitted || r.Status == FormStatus.Triaged) && (formId == null || r.Id == formId))
            .OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync(ct);
        if (form is not null)
        {
            foreach (var a in form.Answers) f[a.QuestionKey] = a.Value;
            f["questionnaire"] = "Yes";
        }
        // Lab results from the last 6 months (most recent wins).
        var since = clock.GetUtcNow().AddMonths(-6);
        var obs = await (from r in labs.Reports.AsNoTracking()
                         where r.PatientId == patientId && r.ReceivedAt >= since && r.SupersededById == null && r.ResultStatus != LabResultStatus.Cancelled
                         join o in labs.Observations.AsNoTracking() on r.Id equals o.ReportId
                         orderby r.ReceivedAt
                         select new { o.Code, o.Name, o.Value }).ToListAsync(ct);
        foreach (var o in obs)
        {
            var key = LabFact(o.Code, o.Name);
            if (key is not null && decimal.TryParse(o.Value, NumberStyles.Number, Inv, out _)) f[key] = o.Value;
        }
        foreach (var (k, v) in staff) f[k] = v;
        return (f, form?.Id);
    }

    /// <summary>Maps common NZ lab codes/names to triage facts.</summary>
    public static string? LabFact(string code, string name)
    {
        var c = code.ToUpperInvariant(); var n = name.ToLowerInvariant();
        if (c is "HB" or "HGB" or "718-7" || n == "haemoglobin" || n == "hemoglobin") return "hb";
        if (c is "HBA1C" or "4548-4" or "59261-8" || n.Contains("hba1c", StringComparison.Ordinal)) return "hba1c";
        if (c is "CREAT" or "CR" or "2160-0" or "14682-9" || n.StartsWith("creatinine", StringComparison.Ordinal)) return "creatinine";
        if (c is "EGFR" or "33914-3" || n.Contains("egfr", StringComparison.Ordinal)) return "egfr";
        return null;
    }

    private static Dictionary<string, string> ReadStaff(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web) ?? [], StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ assessments
    private IQueryable<Assessment> Current(Guid patientId, Guid? bookingId) =>
        db.Assessments.Where(a => a.PatientId == patientId && a.BookingId == bookingId && (a.Status == AssessmentStatus.Open || a.Status == AssessmentStatus.SignedOff));

    /// <summary>Runs triage (first time or again). The previous assessment for the same patient and booking is superseded and the changes listed.</summary>
    public async Task<Assessment> RunAsync(Guid patientId, Guid? bookingId, TriageTrigger trigger, string? detail, string user, IReadOnlyList<StaffInput>? inputs = null, Guid? formId = null, CancellationToken ct = default)
    {
        var planned = await PlannedAsync(patientId, bookingId, ct);
        var previous = await Current(patientId, planned.BookingId).Include(a => a.Findings).OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        var staff = ReadStaff(previous?.StaffInputsJson);
        var notes = new List<string>();
        foreach (var i in inputs ?? [])
        {
            if (!StaffFacts.ContainsKey(i.Fact)) throw new InvalidOperationException($"Unknown finding '{i.Fact}'.");
            var v = i.Value?.Trim();
            if (v is { Length: > 200 }) throw new InvalidOperationException("Finding too long.");
            if (YesNoFacts.Contains(i.Fact, StringComparer.OrdinalIgnoreCase) && v is not (null or "" or "Yes" or "No")) throw new InvalidOperationException($"{StaffFacts[i.Fact]}: answer Yes or No.");
            if (i.Fact is "cfs" && v is { Length: > 0 } && (!int.TryParse(v, out var c) || c is < 1 or > 9)) throw new InvalidOperationException("Clinical frailty score is 1–9.");
            if (i.Fact is "asa_confirmed" && v is { Length: > 0 } && (!int.TryParse(v, out var asaIn) || asaIn is < 1 or > 6)) throw new InvalidOperationException("ASA grade is 1–6.");
            if (string.IsNullOrEmpty(v)) staff.Remove(i.Fact); else staff[i.Fact] = v;
            if (!string.IsNullOrWhiteSpace(i.Reason)) notes.Add($"{StaffFacts[i.Fact]}: {i.Reason.Trim()}");
        }
        var (facts, usedForm) = await FactsAsync(patientId, formId, planned, staff, ct);
        var rules = await ApprovedRulesAsync(ct);
        var result = TriageScoring.Calculate(facts, rules, planned.Procedure, planned.Anaesthetic, planned.Minutes);
        if (rules is null)
        {
            result.Findings.Insert(0, new("INFO:rules", TriageOutcome.MoreInformation, "No clinically approved rule set – scores only; a clinician must assess suitability.", null));
            if (result.Suggested < TriageOutcome.MoreInformation) result.Suggested = TriageOutcome.MoreInformation;
        }
        var (summary, worse) = TriageScoring.Compare(previous, result);
        var now = clock.GetUtcNow();
        var a = new Assessment
        {
            PatientId = patientId,
            BookingId = planned.BookingId,
            FormRequestId = usedForm,
            PreviousId = previous?.Id,
            Trigger = trigger,
            TriggerDetail = Cut(detail, 300),
            Procedure = Cut(planned.Procedure, 200) ?? "",
            Anaesthetic = planned.Anaesthetic,
            ProcedureDate = planned.Date,
            RuleSetId = rules?.Id,
            RuleSetVersion = rules?.Version,
            RulesApproved = rules is not null,
            FactsJson = Cut(JsonSerializer.Serialize(result.Facts.All, JsonSerializerOptions.Web), 8000) ?? "{}",
            StaffInputsJson = staff.Count == 0 ? null : JsonSerializer.Serialize(staff, JsonSerializerOptions.Web),
            Bmi = result.Bmi,
            StopBang = result.StopBang,
            StopBangKnown = result.StopBangKnown,
            Frailty = result.Frailty,
            Vte = result.Vte,
            Rcri = result.Rcri,
            SuggestedAsa = result.SuggestedAsa,
            ConfirmedAsa = int.TryParse(staff.GetValueOrDefault("asa_confirmed"), out var ca) ? ca : null,
            SuggestedOutcome = result.Suggested,
            Precautions = Cut(string.Join("\n", result.Precautions), 1000),
            Missing = Cut(string.Join("\n", result.Missing), 1000),
            ChangeSummary = Cut(summary, 2000),
            Worse = worse,
            Notes = notes.Count > 0 ? Cut(string.Join("\n", notes), 4000) : null,
            CreatedBy = user,
            CreatedAt = now
        };
        var seq = 0;
        foreach (var x in result.Findings) a.Findings.Add(new Finding { AssessmentId = a.Id, Sequence = seq++, Source = Cut(x.Source, 40)!, Outcome = x.Outcome, Text = x.Text, Precaution = Cut(x.Precaution, 300) });
        if (previous is not null) previous.Status = AssessmentStatus.Superseded;
        db.Assessments.Add(a);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, trigger == TriageTrigger.Initial ? "Triage" : "Retriage", "TriageAssessment", a.Id.ToString(), patientId.ToString(),
            reason: $"{trigger}{(detail is null ? "" : ": " + detail)} → {Outcomes.Labels[a.SuggestedOutcome]}{(worse ? " (worse)" : "")}", ct: ct);
        return a;
    }

    private static string? Cut(string? s, int n) => s is null ? null : s.Length > n ? s[..n] : s;

    public async Task<string?> SignOffAsync(Guid id, SignOffRequest r, bool canReview, string user, string role, CancellationToken ct = default)
    {
        var a = await db.Assessments.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return "Assessment not found.";
        if (a.Status != AssessmentStatus.Open) return a.Status == AssessmentStatus.SignedOff ? "Already signed off – run triage again to change it." : "This assessment has been replaced by a newer one.";
        if (!Enum.IsDefined(r.Outcome)) return "Choose the outcome.";
        if (r.Outcome != a.SuggestedOutcome && string.IsNullOrWhiteSpace(r.OverrideReason)) return $"The suggestion was ‘{Outcomes.Labels[a.SuggestedOutcome]}’ – enter the reason for a different outcome.";
        if (!canReview && (a.SuggestedOutcome >= TriageOutcome.AnaesthetistReview || r.Outcome == TriageOutcome.AnaesthetistReview) && r.Outcome <= TriageOutcome.SuitableWithPrecautions)
            return "This needs an anaesthetist's review – only an anaesthetist (or another reviewer) can sign it off as suitable.";
        if (r.ConfirmedAsa is not null && !canReview) return "Only an anaesthetist can confirm the ASA grade.";
        if (r.ConfirmedAsa is < 1 or > 6) return "ASA grade is 1–6.";
        if (r.Outcome <= TriageOutcome.SuitableWithPrecautions && !a.RulesApproved && string.IsNullOrWhiteSpace(r.Notes))
            return "There is no approved rule set – record your clinical assessment in the notes before signing off as suitable.";
        a.FinalOutcome = r.Outcome;
        a.ConfirmedAsa = r.ConfirmedAsa ?? a.ConfirmedAsa;
        if (!string.IsNullOrWhiteSpace(r.Precautions)) a.Precautions = Cut(r.Precautions.Trim(), 1000);
        a.Notes = Cut(string.Join("\n", new[] { a.Notes, r.Notes?.Trim() }.Where(x => !string.IsNullOrWhiteSpace(x))), 4000);
        a.OverrideReason = Cut(r.OverrideReason?.Trim(), 1000);
        (a.Status, a.SignedOffBy, a.SignedOffRole, a.SignedOffAt) = (AssessmentStatus.SignedOff, user, Cut(role, 100), clock.GetUtcNow());
        // Keep the questionnaire's nurse triage in step (ADR 0011): Green / Amber / Red.
        if (a.FormRequestId is not null && await forms.Requests.SingleOrDefaultAsync(x => x.Id == a.FormRequestId, ct) is { } form && form.Status == FormStatus.Submitted)
        {
            form.TriageCategory = r.Outcome switch { TriageOutcome.Suitable => TriageCategory.Green, TriageOutcome.SuitableWithPrecautions or TriageOutcome.MoreInformation => TriageCategory.Amber, _ => TriageCategory.Red };
            (form.TriageOutcome, form.TriagedBy, form.TriagedAt, form.Status) = (Outcomes.Labels[r.Outcome], user, a.SignedOffAt, FormStatus.Triaged);
            await forms.SaveChangesAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "TriageSignOff", "TriageAssessment", a.Id.ToString(), a.PatientId.ToString(),
            reason: $"{Outcomes.Labels[r.Outcome]} (suggested {Outcomes.Labels[a.SuggestedOutcome]}){(a.ConfirmedAsa is null ? "" : $", ASA {a.ConfirmedAsa}")}{(r.OverrideReason is null ? "" : " – " + r.OverrideReason)}", ct: ct);
        return null;
    }

    public async Task<Assessment?> GetAsync(Guid id, string user, string? ws, CancellationToken ct = default)
    {
        var a = await db.Assessments.AsNoTracking().Include(x => x.Findings).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return null;
        a.Findings = [.. a.Findings.OrderBy(f => f.Outcome == TriageOutcome.Suitable ? 9 : -(int)f.Outcome).ThenBy(f => f.Sequence)];
        await audit.WriteAsync(user, "View", "TriageAssessment", a.Id.ToString(), a.PatientId.ToString(), workstation: ws, ct: ct);
        return a;
    }

    public async Task<List<Assessment>> HistoryAsync(Guid patientId, CancellationToken ct = default) =>
        await db.Assessments.AsNoTracking().Where(a => a.PatientId == patientId).OrderByDescending(a => a.CreatedAt).Take(50).ToListAsync(ct);

    public static IReadOnlyList<ScoreView> ScoresOf(Assessment a)
    {
        var f = TriageFacts.From(JsonSerializer.Deserialize<Dictionary<string, string>>(a.FactsJson, JsonSerializerOptions.Web) ?? []);
        var r = TriageScoring.Calculate(f, null, a.Procedure, a.Anaesthetic, null);
        return [.. r.Scores.Select(s => new ScoreView(s.Name, s.Value, s.Detail))];
    }

    // ------------------------------------------------------------------ worklists
    public async Task<List<WorkItem>> WorklistAsync(string view, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var today = NzTime.Today(clock);
        var q = db.Assessments.AsNoTracking();
        q = view switch
        {
            "review" => q.Where(a => a.Status == AssessmentStatus.Open && (a.SuggestedOutcome == TriageOutcome.AnaesthetistReview || a.SuggestedOutcome == TriageOutcome.NotSuitable)),
            "moreinfo" => q.Where(a => a.Status == AssessmentStatus.Open && a.SuggestedOutcome == TriageOutcome.MoreInformation),
            "signedoff" => q.Where(a => a.Status == AssessmentStatus.SignedOff),
            "overdue" => q.Where(a => a.Status == AssessmentStatus.Open && a.ProcedureDate != null && a.ProcedureDate <= today.AddDays(s.SignOffDaysBefore)),
            "worse" => q.Where(a => a.Status == AssessmentStatus.Open && a.Worse),
            _ => q.Where(a => a.Status == AssessmentStatus.Open),
        };
        var rows = await q.OrderBy(a => a.ProcedureDate == null).ThenBy(a => a.ProcedureDate).ThenByDescending(a => a.CreatedAt).Take(300).ToListAsync(ct);
        var ids = rows.Select(r => r.PatientId).Distinct().ToList();
        var pts = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.FamilyName, p.GivenNames, p.Nhi }).ToDictionaryAsync(p => p.Id, ct);
        return [.. rows.Select(a =>
        {
            var p = pts.GetValueOrDefault(a.PatientId);
            return new WorkItem(a.Id, a.PatientId, p is null ? "?" : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p?.Nhi, a.Procedure, a.ProcedureDate, a.SuggestedOutcome, a.FinalOutcome,
                a.Status, a.Trigger, a.Worse, a.Missing, a.CreatedAt, a.Status == AssessmentStatus.Open && a.ProcedureDate <= today.AddDays(s.SignOffDaysBefore), a.RulesApproved);
        })];
    }

    /// <summary>Latest triage for each booking (for badges on the theatre list).</summary>
    public async Task<Dictionary<Guid, Assessment>> ForBookingsAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken ct = default) =>
        (await db.Assessments.AsNoTracking().Where(a => a.BookingId != null && bookingIds.Contains(a.BookingId.Value) && (a.Status == AssessmentStatus.Open || a.Status == AssessmentStatus.SignedOff))
            .ToListAsync(ct)).GroupBy(a => a.BookingId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.CreatedAt).First());

    // ------------------------------------------------------------------ booking gate
    /// <summary>Null when the booking may be confirmed; otherwise why not.</summary>
    public async Task<string?> CanConfirmAsync(Guid bookingId, Guid patientId, string procedure, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (!s.HoldBookingUntilSignedOff) return null;
        if (!string.IsNullOrWhiteSpace(s.HoldProcedures) && !s.HoldProcedures.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(w => procedure.Contains(w, StringComparison.OrdinalIgnoreCase)))
            return null;
        var a = await db.Assessments.AsNoTracking().Where(x => x.PatientId == patientId && (x.BookingId == bookingId || x.BookingId == null) && (x.Status == AssessmentStatus.Open || x.Status == AssessmentStatus.SignedOff))
            .OrderByDescending(x => x.BookingId == bookingId).ThenByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (a is null) return "Triage has not been done for this booking – complete triage before confirming.";
        if (a.Status != AssessmentStatus.SignedOff) return $"Triage is waiting for sign-off ({Outcomes.Labels[a.SuggestedOutcome]}) – it must be signed off before the booking is confirmed.";
        if (a.FinalOutcome > TriageOutcome.SuitableWithPrecautions) return $"Triage outcome is ‘{Outcomes.Labels[a.FinalOutcome!.Value]}’ – the booking can't be confirmed.";
        return null;
    }

    // ------------------------------------------------------------------ re-triage scan
    /// <summary>
    /// Looks at upcoming bookings and starts triage where none exists, or re-triage when new answers or results arrived, the booking changed,
    /// or the sign-off is older than the set number of days on the day of surgery. Runs every few minutes from the worker.
    /// </summary>
    public async Task<int> ScanAsync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var today = NzTime.Today(clock);
        var upcoming = await (from b in theatre.Bookings.AsNoTracking()
                              join se in theatre.Sessions.AsNoTracking() on b.SessionId equals se.Id
                              where se.Date >= today && se.Date <= today.AddDays(180) && !se.IsCancelled && (b.Status == BookingStatus.Booked || b.Status == BookingStatus.Confirmed)
                              select new { b.Id, b.PatientId, b.Procedure, b.Anaesthetic, se.Date }).Take(2000).ToListAsync(ct);
        var n = 0;
        foreach (var b in upcoming)
        {
            var latest = await Current(b.PatientId, b.Id).AsNoTracking().OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
            if (latest is null)
            {
                await RunAsync(b.PatientId, b.Id, TriageTrigger.Initial, "Booking made", "system", ct: ct);
                n++;
                continue;
            }
            string? why = null; var trigger = TriageTrigger.Manual;
            var newForm = await forms.Requests.AsNoTracking().AnyAsync(r => r.PatientId == b.PatientId && r.SubmittedAt > latest.CreatedAt && (r.Status == FormStatus.Submitted || r.Status == FormStatus.Triaged), ct);
            if (newForm) (trigger, why) = (TriageTrigger.NewAnswers, "New questionnaire answers");
            else if (await labs.Reports.AsNoTracking().AnyAsync(r => r.PatientId == b.PatientId && r.ReceivedAt > latest.CreatedAt, ct)) (trigger, why) = (TriageTrigger.NewResult, "New lab results");
            else if (!string.Equals(latest.Procedure, b.Procedure.Length > 200 ? b.Procedure[..200] : b.Procedure, StringComparison.Ordinal) || latest.Anaesthetic != b.Anaesthetic.ToString() || latest.ProcedureDate != b.Date)
                (trigger, why) = (TriageTrigger.BookingChanged, $"Booking changed: {b.Procedure}, {b.Anaesthetic}, {b.Date:d MMM yyyy}");
            else if (latest.Status == AssessmentStatus.SignedOff && latest.SignedOffAt is { } so && b.Date.DayNumber - so.NzDate().DayNumber > s.RetriageAfterDays)
                (trigger, why) = (TriageTrigger.Stale, $"Signed off more than {s.RetriageAfterDays} days before surgery");
            if (why is null) continue;
            await RunAsync(b.PatientId, b.Id, trigger, why, "system", formId: null, ct: ct);
            n++;
        }
        s.LastScanAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return n;
    }

    public async Task<IReadOnlyList<string>> SaveSettingsAsync(TriageSettings input, string user, CancellationToken ct = default)
    {
        if (input.RetriageAfterDays is < 7 or > 365) return ["Re-triage after must be 7–365 days."];
        if (input.SignOffDaysBefore is < 0 or > 60) return ["Sign-off days before surgery must be 0–60."];
        var s = await SettingsAsync(ct);
        (s.HoldBookingUntilSignedOff, s.HoldProcedures, s.RetriageAfterDays, s.SignOffDaysBefore) =
            (input.HoldBookingUntilSignedOff, string.IsNullOrWhiteSpace(input.HoldProcedures) ? null : input.HoldProcedures.Trim(), input.RetriageAfterDays, input.SignOffDaysBefore);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "TriageSettings", "1", reason: $"hold {s.HoldBookingUntilSignedOff} ({s.HoldProcedures ?? "all"}); re-triage {s.RetriageAfterDays} d; sign-off {s.SignOffDaysBefore} d before", ct: ct);
        return [];
    }

    // ------------------------------------------------------------------ reports
    public sealed record OutcomeCount(string Outcome, int Count);

    public async Task<object> ReportAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var start = NzTime.StartOfDay(from); var end = NzTime.StartOfDay(to.AddDays(1));
        var signed = await db.Assessments.AsNoTracking().Where(a => a.Status != AssessmentStatus.Cancelled && a.SignedOffAt >= start && a.SignedOffAt < end).ToListAsync(ct);
        var firsts = await db.Assessments.AsNoTracking().Where(a => a.PreviousId == null && a.CreatedAt >= start && a.CreatedAt < end).Select(a => new { a.PatientId, a.BookingId, a.CreatedAt }).ToListAsync(ct);
        var days = signed.Select(a => (a.SignedOffAt!.Value - (firsts.Find(f => f.PatientId == a.PatientId && f.BookingId == a.BookingId)?.CreatedAt ?? a.CreatedAt)).TotalDays).Where(d => d >= 0).ToList();
        return new
        {
            signedOff = signed.Count,
            outcomes = signed.GroupBy(a => a.FinalOutcome!.Value).OrderBy(g => g.Key).Select(g => new OutcomeCount(Outcomes.Labels[g.Key], g.Count())),
            overridden = signed.Count(a => a.FinalOutcome != a.SuggestedOutcome),
            byProcedure = signed.GroupBy(a => a.Procedure).OrderByDescending(g => g.Count()).Take(20).Select(g => new { procedure = g.Key, count = g.Count(), notSuitable = g.Count(a => a.FinalOutcome == TriageOutcome.NotSuitable), review = g.Count(a => a.FinalOutcome == TriageOutcome.AnaesthetistReview) }),
            medianDaysToSignOff = days.Count == 0 ? (double?)null : Math.Round(days.Order().ElementAt(days.Count / 2), 1),
            retriaged = await db.Assessments.AsNoTracking().CountAsync(a => a.PreviousId != null && a.CreatedAt >= start && a.CreatedAt < end, ct),
            worse = await db.Assessments.AsNoTracking().CountAsync(a => a.Worse && a.CreatedAt >= start && a.CreatedAt < end, ct),
        };
    }
}

public sealed record ScoreView(string Name, string Value, string Detail);
