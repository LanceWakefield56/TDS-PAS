using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Quality;

public sealed record IncidentReport(DateTimeOffset OccurredAt, string Location, IncidentCategory Category, string Description, string? ImmediateAction,
    Guid? PatientId, Guid? BookingId, Guid? EquipmentId, string? StaffInvolved, Harm Harm, bool Anonymous);
public sealed record IncidentTriage(int Sac, Harm Harm, bool AlwaysReport, string? Investigator, IReadOnlyList<Agency> Notify);
public sealed record IncidentFindings(string? Findings, string? ContributingFactors, string? LearningSummary);
public sealed record DisclosureEdit(DateTimeOffset At, string PersonTold, string Method, string? Notes);
public sealed record ComplaintEdit(DateOnly ReceivedOn, string ReceivedVia, string Complainant, string? Contact, Guid? PatientId, string Summary, string Category, string Owner, Guid? IncidentId);
public sealed record ComplaintStep(string Step, DateOnly On, string? Text, ComplaintOutcome? Outcome, bool? HdcReferral, bool? AdvocacyInvolved);
public sealed record ComplimentEdit(DateOnly ReceivedOn, string From, string? StaffNamed, string Text);
public sealed record RiskEdit(bool IsHazard, string Title, RiskCategory Category, string Description, string Owner, int Likelihood, int Consequence, string? Controls,
    int? ResidualLikelihood, int? ResidualConsequence, string? Location, Guid? EquipmentId, DateOnly ReviewBy, RiskStatus Status);
public sealed record ActionEdit(ActionSource Source, Guid? SourceId, string? SourceRef, string Title, string? Detail, string Owner, DateOnly DueOn);
public sealed record PolicyEdit(string Code, string Title, string Version, string Owner, string? Link, DateOnly ReviewBy, bool RequiresAcknowledgement, bool IsActive);

/// <summary>Incidents, complaints, compliments, risks and hazards, corrective actions and the policy register (spec 7.12, 7.14.5–7.14.7).</summary>
public sealed class QualityService(QualityDbContext db, AuditWriter audit, TimeProvider clock)
{
    private static readonly TimeZoneInfo Nz = TryZone("Pacific/Auckland");
    private static TimeZoneInfo TryZone(string id) { try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; } }
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Nz).DateTime);

    /// <summary>HQSC Always Report and Review events are reported whatever the harm; these categories default to it.</summary>
    public static readonly IReadOnlyList<string> AlwaysReportExamples =
    [
        "Wrong patient, site or procedure", "Retained item after surgery", "Death or serious harm from a medication error", "Suicide or attempted suicide in care",
        "Serious harm from a fall", "Unexpected death", "Abduction or loss of a patient", "Serious harm from a device",
    ];

    // ------------------------------------------------------------------ numbers
    private async Task<string> NextAsync(string prefix, CancellationToken ct)
    {
        var key = $"{prefix}-{Today.Year}";
        var s = await db.Sequences.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (s is null) { s = new Sequence { Key = key, Next = 1 }; db.Sequences.Add(s); }
        var n = s.Next++;
        return $"{key}-{n:D4}";
    }

    public async Task<QualitySettings> SettingsAsync(CancellationToken ct = default) => await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new QualitySettings();

    public async Task<IReadOnlyList<string>> SaveSettingsAsync(string escalateTo, string? extraDays, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(escalateTo)) return ["Say who is alerted about SAC 1–2 events."];
        var bad = (extraDays ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => !DateOnly.TryParseExact(x, "yyyy-MM-dd", out _)).ToList();
        if (bad.Count > 0) return [$"Not a date (use yyyy-mm-dd): {string.Join(", ", bad)}"];
        var s = await db.Settings.SingleOrDefaultAsync(ct);
        if (s is null) { s = new QualitySettings(); db.Settings.Add(s); }
        s.EscalateTo = Clip(escalateTo, 300)!; s.ExtraNonWorkingDays = Clip(extraDays, 1000); s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Settings", "Quality", "settings", ct: ct);
        return [];
    }

    // ------------------------------------------------------------------ incidents
    /// <summary>Quick report (any staff member, under two minutes). Anonymous reports keep no reporter – not even in the audit.</summary>
    public async Task<(Incident? Incident, IReadOnlyList<string> Errors)> ReportAsync(IncidentReport r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.Description) || r.Description.Trim().Length < 10) errors.Add("Describe what happened (at least a sentence).");
        if (string.IsNullOrWhiteSpace(r.Location)) errors.Add("Say where it happened.");
        if (r.OccurredAt > clock.GetUtcNow().AddMinutes(5)) errors.Add("The time it happened cannot be in the future.");
        if (r.OccurredAt < clock.GetUtcNow().AddYears(-5)) errors.Add("Check the date it happened.");
        if (!Enum.IsDefined(r.Category)) errors.Add("Choose what kind of incident it was.");
        if (errors.Count > 0) return (null, errors);
        var i = new Incident
        {
            Number = await NextAsync("INC", ct),
            ReportedAt = clock.GetUtcNow(),
            ReportedBy = r.Anonymous ? null : user,
            OccurredAt = r.OccurredAt,
            Location = Clip(r.Location, 100)!,
            Category = r.Category,
            Description = Clip(r.Description, 4000)!,
            ImmediateAction = Clip(r.ImmediateAction, 2000),
            PatientId = r.PatientId,
            BookingId = r.BookingId,
            EquipmentId = r.EquipmentId,
            StaffInvolved = Clip(r.StaffInvolved, 300),
            Harm = r.Harm,
        };
        // Death or severe harm is escalated at once, before triage.
        if (r.Harm is Harm.Severe or Harm.Death) i.EscalatedAt = clock.GetUtcNow();
        db.Incidents.Add(i);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(r.Anonymous ? "anonymous" : user, "ReportIncident", "Incident", i.Id.ToString(), r.PatientId?.ToString(), reason: $"{i.Number} {i.Category}, harm {i.Harm}", ct: ct);
        return (i, []);
    }

    public async Task<Incident?> IncidentAsync(Guid id, CancellationToken ct = default) =>
        await db.Incidents.AsNoTracking().Include(x => x.Disclosures).Include(x => x.Notifications).SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Incident>> IncidentsAsync(bool open, CancellationToken ct = default) =>
        await db.Incidents.AsNoTracking().Where(x => !open || x.Status != IncidentStatus.Closed).OrderByDescending(x => x.ReportedAt).Take(500).ToListAsync(ct);

    /// <summary>Triage: SAC rating (HQSC), confirmed harm, Always Report and Review, investigator and the external notifications needed. SAC 1–2 escalates.</summary>
    public async Task<IReadOnlyList<string>> TriageAsync(Guid id, IncidentTriage t, string user, CancellationToken ct = default)
    {
        var i = await db.Incidents.Include(x => x.Notifications).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Incident not found.");
        if (i.Status == IncidentStatus.Closed) return ["This incident is closed."];
        if (t.Sac is < 1 or > 4) return ["SAC rating is 1 (most severe) to 4."];
        // SAC follows harm: death or severe harm cannot be rated SAC 3–4.
        if (t.Harm is Harm.Death && t.Sac != 1) return ["A death is SAC 1."];
        if (t.Harm is Harm.Severe && t.Sac > 2) return ["Severe harm is SAC 1 or 2."];
        i.Sac = t.Sac; i.Harm = t.Harm; i.AlwaysReport = t.AlwaysReport; i.Investigator = Clip(t.Investigator, 100);
        i.TriagedBy = user; i.TriagedAt = clock.GetUtcNow();
        if (i.Status == IncidentStatus.Reported) i.Status = IncidentStatus.Triaged;
        if ((t.Sac <= 2 || t.AlwaysReport) && i.EscalatedAt is null) i.EscalatedAt = clock.GetUtcNow();
        foreach (var a in t.Notify.Distinct().Where(a => i.Notifications.All(n => n.Agency != a))) db.Notifications.Add(new Notification { IncidentId = i.Id, Agency = a });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "TriageIncident", "Incident", i.Id.ToString(), i.PatientId?.ToString(), reason: $"{i.Number} SAC {t.Sac}{(t.AlwaysReport ? ", Always Report and Review" : "")}", ct: ct);
        return [];
    }

    public async Task<IReadOnlyList<string>> FindingsAsync(Guid id, IncidentFindings f, string user, CancellationToken ct = default)
    {
        var i = await db.Incidents.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Incident not found.");
        if (i.Status == IncidentStatus.Closed) return ["This incident is closed."];
        if (i.Status == IncidentStatus.Reported) return ["Triage the incident (SAC rating) first."];
        i.Findings = Clip(f.Findings, 8000); i.ContributingFactors = Clip(f.ContributingFactors, 4000); i.LearningSummary = Clip(f.LearningSummary, 4000);
        if (i.Status == IncidentStatus.Triaged) i.Status = IncidentStatus.Investigating;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "InvestigateIncident", "Incident", i.Id.ToString(), i.PatientId?.ToString(), reason: i.Number, ct: ct);
        return [];
    }

    public async Task<IReadOnlyList<string>> DiscloseAsync(Guid id, DisclosureEdit d, string user, CancellationToken ct = default)
    {
        var i = await db.Incidents.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Incident not found.");
        if (string.IsNullOrWhiteSpace(d.PersonTold) || string.IsNullOrWhiteSpace(d.Method)) return ["Say who was told and how (in person, phone, letter)."];
        if (d.At > clock.GetUtcNow().AddMinutes(5)) return ["The disclosure time cannot be in the future."];
        db.Disclosures.Add(new Disclosure { IncidentId = i.Id, At = d.At, By = user, PersonTold = Clip(d.PersonTold, 150)!, Method = Clip(d.Method, 60)!, Notes = Clip(d.Notes, 2000) });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "OpenDisclosure", "Incident", i.Id.ToString(), i.PatientId?.ToString(), reason: $"{i.Number}: told {d.PersonTold}", ct: ct);
        return [];
    }

    public async Task<IReadOnlyList<string>> NotifiedAsync(Guid id, Agency agency, string? reference, string user, CancellationToken ct = default)
    {
        var n = await db.Notifications.SingleOrDefaultAsync(x => x.IncidentId == id && x.Agency == agency, ct);
        if (n is null) { n = new Notification { IncidentId = id, Agency = agency }; db.Notifications.Add(n); }
        n.NotifiedAt = clock.GetUtcNow(); n.NotifiedBy = user; n.Reference = Clip(reference, 100);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ExternalNotification", "Incident", id.ToString(), reason: $"{agency} {reference}", ct: ct);
        return [];
    }

    /// <summary>Closing needs: triage done, findings recorded, every required notification sent, open disclosure recorded when the patient was harmed, and no open actions.</summary>
    public async Task<IReadOnlyList<string>> CloseIncidentAsync(Guid id, string user, CancellationToken ct = default)
    {
        var i = await db.Incidents.Include(x => x.Notifications).Include(x => x.Disclosures).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Incident not found.");
        var errors = new List<string>();
        if (i.Sac is null) errors.Add("Triage the incident (SAC rating) first.");
        if (string.IsNullOrWhiteSpace(i.Findings)) errors.Add("Record the findings of the review.");
        if (i.Notifications.Any(n => n.NotifiedAt is null)) errors.Add("Record every external notification: " + string.Join(", ", i.Notifications.Where(n => n.NotifiedAt is null).Select(n => n.Agency)) + ".");
        if (i.PatientId is not null && i.Harm >= Harm.Minor && i.Disclosures.Count == 0) errors.Add("Record the open disclosure to the patient or their whānau.");
        if (await db.Actions.AnyAsync(a => a.SourceId == i.Id && a.Status == ActionStatus.Open, ct)) errors.Add("Complete the open corrective actions first.");
        if (errors.Count > 0) return errors;
        i.Status = IncidentStatus.Closed; i.ClosedAt = clock.GetUtcNow(); i.ClosedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "CloseIncident", "Incident", i.Id.ToString(), i.PatientId?.ToString(), reason: i.Number, ct: ct);
        return [];
    }

    // ------------------------------------------------------------------ complaints (Right 10)
    public async Task<IReadOnlySet<DateOnly>> ExtraDaysAsync(CancellationToken ct) => WorkingDays.ParseExtra((await SettingsAsync(ct)).ExtraNonWorkingDays);

    public async Task<(Complaint? Complaint, IReadOnlyList<string> Errors)> NewComplaintAsync(ComplaintEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.Complainant)) errors.Add("Who is complaining?");
        if (string.IsNullOrWhiteSpace(e.Summary)) errors.Add("Summarise the complaint.");
        if (string.IsNullOrWhiteSpace(e.Owner)) errors.Add("Who is handling it?");
        if (e.ReceivedOn > Today) errors.Add("The date received cannot be in the future.");
        if (errors.Count > 0) return (null, errors);
        var extra = await ExtraDaysAsync(ct);
        var c = new Complaint
        {
            Number = await NextAsync("CMP", ct),
            ReceivedOn = e.ReceivedOn,
            ReceivedVia = Clip(e.ReceivedVia, 40) ?? "Letter",
            Complainant = Clip(e.Complainant, 150)!,
            Contact = Clip(e.Contact, 200),
            PatientId = e.PatientId,
            Summary = Clip(e.Summary, 4000)!,
            Category = Clip(e.Category, 60) ?? "Other",
            Owner = Clip(e.Owner, 100)!,
            IncidentId = e.IncidentId,
            AcknowledgeBy = WorkingDays.Add(e.ReceivedOn, 5, extra),
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user,
        };
        db.Complaints.Add(c);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "NewComplaint", "Complaint", c.Id.ToString(), e.PatientId?.ToString(), reason: c.Number, ct: ct);
        return (c, []);
    }

    /// <summary>Steps: acknowledge (sets the 10-working-day decision date), extend (tells the complainant more time is needed, with reasons), respond, close.</summary>
    public async Task<IReadOnlyList<string>> ComplaintStepAsync(Guid id, ComplaintStep s, string user, CancellationToken ct = default)
    {
        var c = await db.Complaints.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Complaint not found.");
        if (c.Status == ComplaintStatus.Closed) return ["This complaint is closed."];
        if (s.On > Today) return ["The date cannot be in the future."];
        if (s.On < c.ReceivedOn) return ["The date cannot be before the complaint was received."];
        var extra = await ExtraDaysAsync(ct);
        switch (s.Step)
        {
            case "acknowledge":
                if (c.AcknowledgedOn is not null) return ["Already acknowledged."];
                c.AcknowledgedOn = s.On; c.DecideBy = WorkingDays.Add(s.On, 10, extra); c.Status = ComplaintStatus.Acknowledged;
                break;
            case "extend":
                if (c.AcknowledgedOn is null) return ["Acknowledge the complaint first."];
                if (string.IsNullOrWhiteSpace(s.Text)) return ["Give the reason more time is needed – the complainant must be told why."];
                c.ExtensionNotifiedOn = s.On; c.ExtensionReason = Clip(s.Text, 500); c.DecideBy = WorkingDays.Add(c.DecideBy ?? s.On, 20, extra); c.Status = ComplaintStatus.Investigating;
                break;
            case "respond":
                if (c.AcknowledgedOn is null) return ["Acknowledge the complaint first."];
                if (s.Outcome is null || string.IsNullOrWhiteSpace(s.Text)) return ["Record the outcome and a summary of the response (including the right to go to the Health and Disability Commissioner and the Advocacy Service)."];
                c.RespondedOn = s.On; c.Outcome = s.Outcome; c.Response = Clip(s.Text, 4000); c.Status = ComplaintStatus.Responded;
                c.HdcReferral = s.HdcReferral ?? c.HdcReferral; c.AdvocacyInvolved = s.AdvocacyInvolved ?? c.AdvocacyInvolved;
                break;
            case "close":
                if (c.RespondedOn is null) return ["Respond to the complainant before closing."];
                if (await db.Actions.AnyAsync(a => a.SourceId == c.Id && a.Status == ActionStatus.Open, ct)) return ["Complete the open corrective actions first."];
                c.Status = ComplaintStatus.Closed;
                break;
            default: return ["Unknown step."];
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Complaint" + char.ToUpperInvariant(s.Step[0]) + s.Step[1..], "Complaint", c.Id.ToString(), c.PatientId?.ToString(), reason: c.Number, ct: ct);
        return [];
    }

    public async Task<IReadOnlyList<Complaint>> ComplaintsAsync(bool open, CancellationToken ct = default) =>
        await db.Complaints.AsNoTracking().Where(x => !open || x.Status != ComplaintStatus.Closed).OrderByDescending(x => x.ReceivedOn).Take(500).ToListAsync(ct);

    /// <summary>Right 10 timeframe state: overdue acknowledgement or decision.</summary>
    public string? Overdue(Complaint c)
    {
        if (c.Status == ComplaintStatus.Closed) return null;
        if (c.AcknowledgedOn is null && Today > c.AcknowledgeBy) return $"Acknowledgement overdue (due {c.AcknowledgeBy:d MMM})";
        if (c.RespondedOn is null && c.DecideBy is { } d && Today > d) return $"Decision overdue (due {d:d MMM})";
        return null;
    }

    public async Task<(Compliment? Compliment, IReadOnlyList<string> Errors)> ComplimentAsync(ComplimentEdit e, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(e.From) || string.IsNullOrWhiteSpace(e.Text)) return (null, ["Who it is from and what they said."]);
        var c = new Compliment { ReceivedOn = e.ReceivedOn, From = Clip(e.From, 150)!, StaffNamed = Clip(e.StaffNamed, 300), Text = Clip(e.Text, 2000)!, CreatedBy = user };
        db.Compliments.Add(c);
        await db.SaveChangesAsync(ct);
        return (c, []);
    }

    public async Task<IReadOnlyList<Compliment>> ComplimentsAsync(CancellationToken ct = default) => await db.Compliments.AsNoTracking().OrderByDescending(x => x.ReceivedOn).Take(200).ToListAsync(ct);

    public async Task<string?> ShareComplimentAsync(Guid id, string user, CancellationToken ct = default)
    {
        var c = await db.Compliments.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Compliment not found.");
        c.SharedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ShareCompliment", "Compliment", id.ToString(), ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ risks and hazards
    public static int Score(int likelihood, int consequence) => likelihood * consequence;
    /// <summary>5×5 matrix bands: 1–4 low, 5–9 moderate, 10–16 high, 20–25 extreme.</summary>
    public static string Band(int score) => score >= 20 ? "Extreme" : score >= 10 ? "High" : score >= 5 ? "Moderate" : "Low";

    public async Task<(Risk? Risk, IReadOnlyList<string> Errors)> SaveRiskAsync(Guid? id, RiskEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.Title)) errors.Add("Give the risk a title.");
        if (string.IsNullOrWhiteSpace(e.Owner)) errors.Add("Who owns this risk?");
        if (e.Likelihood is < 1 or > 5 || e.Consequence is < 1 or > 5) errors.Add("Likelihood and consequence are 1–5.");
        if ((e.ResidualLikelihood is not null) != (e.ResidualConsequence is not null) || e.ResidualLikelihood is < 1 or > 5 || e.ResidualConsequence is < 1 or > 5) errors.Add("Residual likelihood and consequence are both 1–5 (or both blank).");
        if (e.ResidualLikelihood is { } rl && e.ResidualConsequence is { } rc && Score(rl, rc) > Score(e.Likelihood, e.Consequence)) errors.Add("The residual risk (after controls) cannot be higher than the inherent risk.");
        if (e.ReviewBy < Today && e.Status != RiskStatus.Closed) errors.Add("Set a review date that is not in the past.");
        if (errors.Count > 0) return (null, errors);
        Risk? r;
        if (id is { } rid) r = await db.Risks.SingleOrDefaultAsync(x => x.Id == rid, ct) ?? throw new KeyNotFoundException("Risk not found.");
        else { r = new Risk { Number = await NextAsync(e.IsHazard ? "HAZ" : "RSK", ct), IsHazard = e.IsHazard }; db.Risks.Add(r); }
        r.Title = Clip(e.Title, 200)!; r.Category = e.Category; r.Description = Clip(e.Description, 4000) ?? ""; r.Owner = Clip(e.Owner, 100)!;
        r.Likelihood = e.Likelihood; r.Consequence = e.Consequence; r.Controls = Clip(e.Controls, 4000); r.ResidualLikelihood = e.ResidualLikelihood; r.ResidualConsequence = e.ResidualConsequence;
        r.Location = Clip(e.Location, 100); r.EquipmentId = e.EquipmentId; r.ReviewBy = e.ReviewBy; r.Status = e.Status; r.UpdatedAt = clock.GetUtcNow(); r.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, id is null ? "Create" : "Update", r.IsHazard ? "Hazard" : "Risk", r.Id.ToString(), reason: $"{r.Number} {Band(Score(r.Likelihood, r.Consequence))}", ct: ct);
        return (r, []);
    }

    public async Task<IReadOnlyList<Risk>> RisksAsync(bool hazards, CancellationToken ct = default) =>
        await db.Risks.AsNoTracking().Where(x => x.IsHazard == hazards).OrderBy(x => x.Status).ThenByDescending(x => x.Likelihood * x.Consequence).ToListAsync(ct);

    // ------------------------------------------------------------------ corrective actions
    public async Task<(CorrectiveAction? Action, IReadOnlyList<string> Errors)> NewActionAsync(ActionEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.Title)) errors.Add("What needs to be done?");
        if (string.IsNullOrWhiteSpace(e.Owner)) errors.Add("Who will do it?");
        if (e.DueOn < Today) errors.Add("The due date cannot be in the past.");
        if (errors.Count > 0) return (null, errors);
        var ref_ = Clip(e.SourceRef, 40);
        if (e.SourceId is { } sid && ref_ is null)
            ref_ = await db.Incidents.Where(x => x.Id == sid).Select(x => x.Number).FirstOrDefaultAsync(ct)
                   ?? await db.Complaints.Where(x => x.Id == sid).Select(x => x.Number).FirstOrDefaultAsync(ct)
                   ?? await db.Risks.Where(x => x.Id == sid).Select(x => x.Number).FirstOrDefaultAsync(ct);
        var a = new CorrectiveAction
        {
            Number = await NextAsync("CAR", ct),
            Source = e.Source,
            SourceId = e.SourceId,
            SourceRef = ref_,
            Title = Clip(e.Title, 200)!,
            Detail = Clip(e.Detail, 4000),
            Owner = Clip(e.Owner, 100)!,
            DueOn = e.DueOn,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user,
        };
        db.Actions.Add(a);
        if (e.Source == ActionSource.Incident && e.SourceId is { } iid && await db.Incidents.SingleOrDefaultAsync(x => x.Id == iid, ct) is { } inc && inc.Status is IncidentStatus.Triaged or IncidentStatus.Investigating)
            inc.Status = IncidentStatus.Actions;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "NewAction", "CorrectiveAction", a.Id.ToString(), reason: $"{a.Number} from {a.Source} {a.SourceRef}", ct: ct);
        return (a, []);
    }

    public async Task<IReadOnlyList<string>> ActionDoneAsync(Guid id, string evidence, string user, CancellationToken ct = default)
    {
        var a = await db.Actions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Action not found.");
        if (a.Status != ActionStatus.Open) return ["This action is already done."];
        if (string.IsNullOrWhiteSpace(evidence)) return ["Describe the evidence that it was done (and where it is kept)."];
        a.Status = ActionStatus.Done; a.Evidence = Clip(evidence, 4000); a.DoneAt = clock.GetUtcNow(); a.DoneBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ActionDone", "CorrectiveAction", a.Id.ToString(), reason: a.Number, ct: ct);
        return [];
    }

    /// <summary>Closure sign-off: a second person checks the evidence.</summary>
    public async Task<IReadOnlyList<string>> ActionVerifyAsync(Guid id, string user, CancellationToken ct = default)
    {
        var a = await db.Actions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Action not found.");
        if (a.Status != ActionStatus.Done) return ["Only a completed action can be signed off."];
        if (string.Equals(a.DoneBy, user, StringComparison.OrdinalIgnoreCase)) return ["Someone other than the person who did it must sign it off."];
        a.Status = ActionStatus.Verified; a.VerifiedAt = clock.GetUtcNow(); a.VerifiedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ActionVerified", "CorrectiveAction", a.Id.ToString(), reason: a.Number, ct: ct);
        return [];
    }

    public async Task<IReadOnlyList<CorrectiveAction>> ActionsAsync(bool open, Guid? sourceId = null, CancellationToken ct = default) =>
        await db.Actions.AsNoTracking().Where(x => (!open || x.Status != ActionStatus.Verified) && (sourceId == null || x.SourceId == sourceId)).OrderBy(x => x.Status).ThenBy(x => x.DueOn).Take(500).ToListAsync(ct);

    // ------------------------------------------------------------------ policies
    public async Task<(PolicyDocument? Policy, IReadOnlyList<string> Errors)> SavePolicyAsync(Guid? id, PolicyEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var code = (e.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length is 0 or > 30) errors.Add("Give the document its code (e.g. TDS-5.9).");
        if (string.IsNullOrWhiteSpace(e.Title)) errors.Add("Give the document a title.");
        if (string.IsNullOrWhiteSpace(e.Version)) errors.Add("Give the version.");
        if (e.Link is { Length: > 0 } l && !(Uri.TryCreate(l, UriKind.Absolute, out var u) && u.Scheme is "https" or "http" or "file")) errors.Add("The link must be a web address (https://…) or a file path.");
        if (await db.Policies.AnyAsync(x => x.Code == code && x.Id != (id ?? Guid.Empty), ct)) errors.Add($"Another document already uses the code {code}.");
        if (errors.Count > 0) return (null, errors);
        PolicyDocument? p;
        if (id is { } pid) p = await db.Policies.SingleOrDefaultAsync(x => x.Id == pid, ct) ?? throw new KeyNotFoundException("Document not found.");
        else { p = new PolicyDocument(); db.Policies.Add(p); }
        p.Code = code; p.Title = Clip(e.Title, 200)!; p.Version = Clip(e.Version, 20)!; p.Owner = Clip(e.Owner, 100) ?? ""; p.Link = Clip(e.Link, 500); p.ReviewBy = e.ReviewBy;
        p.RequiresAcknowledgement = e.RequiresAcknowledgement; p.IsActive = e.IsActive; p.UpdatedAt = clock.GetUtcNow(); p.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, id is null ? "Create" : "Update", "Policy", p.Id.ToString(), reason: $"{p.Code} v{p.Version}", ct: ct);
        return (p, []);
    }

    public async Task<IReadOnlyList<PolicyDocument>> PoliciesAsync(CancellationToken ct = default) => await db.Policies.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);

    /// <summary>Read-and-acknowledge: one record per person per version (a new version needs a new acknowledgement).</summary>
    public async Task<string?> AcknowledgeAsync(Guid id, string user, CancellationToken ct = default)
    {
        var p = await db.Policies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Document not found.");
        if (!p.RequiresAcknowledgement || !p.IsActive) return "This document does not need acknowledging.";
        if (await db.Acknowledgements.AnyAsync(a => a.DocumentId == id && a.Version == p.Version && a.UserName == user, ct)) return null;
        db.Acknowledgements.Add(new PolicyAcknowledgement { DocumentId = id, Version = p.Version, UserName = user, At = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "AcknowledgePolicy", "Policy", id.ToString(), reason: $"{p.Code} v{p.Version}", ct: ct);
        return null;
    }

    public async Task<IReadOnlyList<PolicyAcknowledgement>> AcknowledgementsAsync(Guid? id = null, string? user = null, CancellationToken ct = default) =>
        await db.Acknowledgements.AsNoTracking().Where(a => (id == null || a.DocumentId == id) && (user == null || a.UserName == user)).OrderByDescending(a => a.At).ToListAsync(ct);

    internal static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];
}
