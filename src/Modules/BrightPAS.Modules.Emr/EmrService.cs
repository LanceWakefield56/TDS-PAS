using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Emr;

public sealed record AssessmentRequest(AssessmentKind Kind, IReadOnlyDictionary<string, string> Answers, string? Actions);
public sealed record CheckRequest(CheckPhase Phase, IReadOnlyList<string> Confirmed, IReadOnlyDictionary<string, string> Team, string? Notes);
public sealed record IntraopEdit(DateTimeOffset? InTheatreAt, DateTimeOffset? AnaestheticStartAt, DateTimeOffset? KnifeToSkinAt, DateTimeOffset? ClosureAt, DateTimeOffset? OutOfTheatreAt,
    string? Position, string? PositioningAids, string? SkinPrep, string? DiathermyUnit, string? DiathermyPadSite, bool? PadSiteSkinIntactAfter, string? TourniquetSite, int? TourniquetPressure,
    DateTimeOffset? TourniquetOnAt, DateTimeOffset? TourniquetOffAt, bool SmokeEvacuation, string? WoundClass, string? Drains, string? Dressings, string? Notes);
public sealed record CountRequest(CountStage Stage, string Item, int Expected, int Counted, string CheckedBy);
public sealed record SpecimenRequest(string Site, string Test, string? Container);
public sealed record ObservationRequest(DateTimeOffset? At, int? RespiratoryRate, int? SpO2, bool OnOxygen, decimal? OxygenLitres, int? SystolicBp, int? DiastolicBp, int? HeartRate,
    decimal? TemperatureC, string? Consciousness, int? Pain, int? Sedation, string Source = "Manual", Guid? DeviceId = null);
public sealed record MedicationRequest(string Medicine, string Dose, string Route, DateTimeOffset? GivenAt, string? Barcode, Guid? ControlledDrugId, string? WitnessedBy, string? Notes);
public sealed record CdRequest(Guid ControlledDrugId, CdMovement Movement, int Quantity, string WitnessedBy, string? Reference);
public sealed record FluidRequest(DateTimeOffset? At, bool IsInput, string Kind, int Millilitres);
public sealed record BloodRequest(string UnitNumber, string Product, DateTimeOffset ReceivedAt, bool ColdChainOk, string CheckedBy1, string CheckedBy2);
public sealed record BloodUpdate(DateTimeOffset? StartedAt, DateTimeOffset? StoppedAt, bool Reaction, string? ReactionDetails, bool Returned);
public sealed record NoteRequest(NoteKind Kind, string Text, bool Sign, Guid? AmendsNoteId);
public sealed record DischargeRequest(string? EscortName, string? EscortPhone, string? OvernightCarer, bool EscortConfirmed, string? EscortOverrideReason, bool CriteriaMet,
    string? Destination, DateOnly? FollowUpCallOn, bool Transfer = false);
public sealed record ScoreResult(int Score, string Risk, string Advice);

/// <summary>The perioperative and clinical record for an episode (one theatre booking). Every write is audited against the patient.</summary>
public sealed class EmrService(EmrDbContext db, AuditWriter audit, TimeProvider clock)
{
    // ---------------------------------------------------------------- checklist content (WHO / HQSC Surgical Safety Checklist)
    public static readonly IReadOnlyDictionary<CheckPhase, (string Key, string Text)[]> CheckItems = new Dictionary<CheckPhase, (string, string)[]>
    {
        [CheckPhase.SignIn] =
        [
            ("identity", "Patient has confirmed identity (name, date of birth, NHI on the band)"), ("procedure", "Procedure and site confirmed with the patient"),
            ("consent", "Consent form signed and matches"), ("site_marked", "Site marked (or not applicable)"), ("anaesthesia_check", "Anaesthesia machine and medicine check complete"),
            ("oximeter", "Pulse oximeter on and working"), ("allergies", "Known allergies checked"), ("airway", "Difficult airway / aspiration risk assessed – equipment available"),
            ("blood_loss", "Risk of >500 ml blood loss assessed (IV access and fluids planned)"),
        ],
        [CheckPhase.TimeOut] =
        [
            ("team", "All team members introduced by name and role"), ("confirm", "Patient, procedure, site and side confirmed aloud"),
            ("antibiotic", "Antibiotic prophylaxis given within the last 60 minutes (or not needed)"), ("surgeon_events", "Surgeon: critical steps, duration, expected blood loss"),
            ("anaesthetist_events", "Anaesthetist: patient-specific concerns"), ("nursing_events", "Nursing: sterility (indicators) confirmed, equipment issues"),
            ("imaging", "Essential imaging displayed (or not needed)"), ("vte", "VTE prophylaxis in place as planned"),
        ],
        [CheckPhase.SignOut] =
        [
            ("procedure_recorded", "Name of the procedure recorded"), ("counts", "Instrument, swab and sharps counts correct"), ("specimens", "Specimens labelled (read aloud with the patient's name)"),
            ("equipment", "Any equipment problems to be addressed"), ("recovery", "Key concerns for recovery and management handed over"),
        ],
    };

    public static readonly IReadOnlyDictionary<CheckPhase, string[]> CheckRoles = new Dictionary<CheckPhase, string[]>
    {
        [CheckPhase.SignIn] = ["Nurse", "Anaesthetist"],
        [CheckPhase.TimeOut] = ["Surgeon", "Anaesthetist", "Nurse"],
        [CheckPhase.SignOut] = ["Nurse", "Surgeon"],
    };

    // ---------------------------------------------------------------- episodes
    public async Task<Episode> EpisodeAsync(Guid bookingId, Guid patientId, string user, CancellationToken ct = default)
    {
        var e = await db.Episodes.SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (e is not null) return e;
        e = new Episode { BookingId = bookingId, PatientId = patientId, CreatedAt = clock.GetUtcNow() };
        db.Episodes.Add(e);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "Episode", e.Id.ToString(), patientId.ToString(), reason: $"booking {bookingId}", ct: ct);
        return e;
    }

    public async Task<Episode?> FindAsync(Guid episodeId, CancellationToken ct = default) => await db.Episodes.SingleOrDefaultAsync(x => x.Id == episodeId, ct);

    private async Task<Episode> OpenAsync(Guid episodeId, CancellationToken ct)
    {
        var e = await db.Episodes.SingleOrDefaultAsync(x => x.Id == episodeId, ct) ?? throw new KeyNotFoundException("Episode not found.");
        if (e.Stage is EpisodeStage.Discharged or EpisodeStage.Transferred) throw new InvalidOperationException("This episode is closed (the patient has gone). Add a note instead.");
        return e;
    }

    private Task Audit(string user, string action, string what, Episode e, string? reason, CancellationToken ct) =>
        audit.WriteAsync(user, action, what, e.Id.ToString(), e.PatientId.ToString(), reason: reason, ct: ct);

    public async Task<string?> AdmitAsync(Guid episodeId, bool sedationOrAnaesthesia, string user, CancellationToken ct = default)
    {
        var e = await OpenAsync(episodeId, ct);
        if (e.Stage != EpisodeStage.PreAdmission) return "The patient has already been admitted.";
        var admission = await db.Assessments.AnyAsync(a => a.EpisodeId == e.Id && a.Kind == AssessmentKind.NursingAdmission, ct);
        if (!admission) return "Complete the nursing admission first.";
        e.Stage = EpisodeStage.Admitted; e.AdmittedAt = clock.GetUtcNow(); e.AdmittedBy = user; e.SedationOrAnaesthesia = sedationOrAnaesthesia;
        await db.SaveChangesAsync(ct);
        await Audit(user, "Admit", "Episode", e, null, ct);
        return null;
    }

    public async Task<string?> SetStageAsync(Guid episodeId, EpisodeStage stage, string user, CancellationToken ct = default)
    {
        var e = await OpenAsync(episodeId, ct);
        if (stage is EpisodeStage.Discharged or EpisodeStage.Transferred or EpisodeStage.PreAdmission) return "Use Discharge or Transfer.";
        if (stage == EpisodeStage.InTheatre && !await db.SafetyChecks.AnyAsync(c => c.EpisodeId == e.Id && c.Phase == CheckPhase.SignIn, ct)) return "Complete Sign In before the patient goes into theatre.";
        if (stage == EpisodeStage.Recovery && await db.SafetyChecks.AnyAsync(c => c.EpisodeId == e.Id && c.Phase == CheckPhase.TimeOut, ct)
            && !await db.SafetyChecks.AnyAsync(c => c.EpisodeId == e.Id && c.Phase == CheckPhase.SignOut, ct)) return "Complete Sign Out before the patient leaves theatre.";
        e.Stage = stage;
        await db.SaveChangesAsync(ct);
        await Audit(user, "Stage", "Episode", e, stage.ToString(), ct);
        return null;
    }

    // ---------------------------------------------------------------- assessments and risk screens
    private static int A(IReadOnlyDictionary<string, string> a, string k) => a.TryGetValue(k, out var v) && int.TryParse(v, out var n) ? n : 0;
    private static bool Y(IReadOnlyDictionary<string, string> a, string k) => a.TryGetValue(k, out var v) && (v == "yes" || v == "true" || v == "1");

    /// <summary>
    /// Scores a risk screen. VTE: simplified Caprini-based points; falls: TDS day-surgery screen; pressure injury: Braden scale (lower = higher risk);
    /// frailty: Clinical Frailty Scale 1–9; delirium: 4AT 0–12. The bands must be confirmed against TDS clinical policy before live use.
    /// </summary>
    public static ScoreResult Score(AssessmentKind kind, IReadOnlyDictionary<string, string> a)
    {
        switch (kind)
        {
            case AssessmentKind.Vte:
                {
                    var age = A(a, "age");
                    var s = (age >= 75 ? 3 : age >= 61 ? 2 : age >= 41 ? 1 : 0) + (Y(a, "major_surgery") ? 2 : 1) + (Y(a, "bmi_over_25") ? 1 : 0) + (Y(a, "previous_vte") ? 3 : 0)
                            + (Y(a, "cancer") ? 2 : 0) + (Y(a, "oestrogen") ? 1 : 0) + (Y(a, "immobile") ? 2 : 0) + (Y(a, "thrombophilia") ? 3 : 0);
                    return s >= 5 ? new(s, "High", "Mechanical and drug prophylaxis per policy; discuss with surgeon.") : s >= 3 ? new(s, "Moderate", "Mechanical prophylaxis (stockings/IPC); consider drug prophylaxis.")
                        : new(s, "Low", "Early mobilisation.");
                }
            case AssessmentKind.Falls:
                {
                    var s = (Y(a, "fallen_last_year") ? 2 : 0) + (Y(a, "mobility_aid") ? 1 : 0) + (Y(a, "sedation") ? 1 : 0) + (Y(a, "confused") ? 2 : 0) + (Y(a, "toileting") ? 1 : 0) + (A(a, "age") >= 65 ? 1 : 0)
                            + (Y(a, "vision") ? 1 : 0);
                    return s >= 3 ? new(s, "High", "Bed low, call bell in reach, assist to mobilise, falls sign on.") : s >= 1 ? new(s, "Moderate", "Assist first mobilisation after anaesthesia.") : new(s, "Low", "Standard care.");
                }
            case AssessmentKind.PressureInjury:
                {
                    string[] keys = ["sensory", "moisture", "activity", "mobility", "nutrition", "friction"];
                    var s = keys.Sum(k => Math.Clamp(A(a, k), 1, k == "friction" ? 3 : 4));
                    return s <= 12 ? new(s, "High", "Pressure-relieving mattress/gel pads, reposition every 2 h, check heels and sacrum.")
                        : s <= 14 ? new(s, "Moderate", "Gel pads in theatre, reposition, check skin after surgery.") : s <= 18 ? new(s, "Low", "Check pressure areas after surgery.") : new(s, "Low", "Standard care.");
                }
            case AssessmentKind.Frailty:
                {
                    var s = Math.Clamp(A(a, "cfs"), 1, 9);
                    return s >= 7 ? new(s, "High", "Severely frail: anaesthetist review, discharge planning, consider overnight care.") : s >= 5 ? new(s, "Moderate", "Frail: delirium prevention, early mobilisation, discharge support.")
                        : new(s, "Low", "Not frail.");
                }
            case AssessmentKind.Delirium:
                {
                    var s = Math.Clamp(A(a, "alertness"), 0, 4) + Math.Clamp(A(a, "amt4"), 0, 2) + Math.Clamp(A(a, "attention"), 0, 2) + Math.Clamp(A(a, "acute_change"), 0, 4);
                    return s >= 4 ? new(s, "High", "Possible delirium: medical review, look for causes, do not discharge without review.") : s >= 1 ? new(s, "Moderate", "Possible cognitive impairment: re-check, involve family.")
                        : new(s, "Low", "Delirium or severe cognitive impairment unlikely.");
                }
            default:
                return new(0, "", "");
        }
    }

    public async Task<(Assessment? Assessment, IReadOnlyList<string> Errors)> AssessAsync(Guid episodeId, AssessmentRequest r, string user, CancellationToken ct = default)
    {
        var e = await OpenAsync(episodeId, ct);
        var answers = r.Answers.Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Key.Length <= 40).ToDictionary(x => x.Key, x => (x.Value ?? "").Trim());
        var errors = new List<string>();
        if (r.Kind == AssessmentKind.NursingAdmission)
        {
            foreach (var (k, label) in new[] { ("id_band", "the identity band"), ("allergies_checked", "allergies"), ("fasting_from", "fasting since"), ("escort_name", "the escort") })
                if (!answers.TryGetValue(k, out var v) || v.Length == 0) errors.Add($"Record {label}.");
        }
        else if (r.Kind == AssessmentKind.Frailty && A(answers, "cfs") is < 1 or > 9) errors.Add("Choose the Clinical Frailty Scale level (1–9).");
        if (errors.Count > 0) return (null, errors);
        var score = Score(r.Kind, answers);
        var row = new Assessment { EpisodeId = e.Id, Kind = r.Kind, Score = score.Score, Risk = score.Risk, Actions = Clip(r.Actions, 500) ?? (score.Advice.Length > 0 ? score.Advice : null), RecordedBy = user, RecordedAt = clock.GetUtcNow() };
        row.Answers = answers.Select(x => new AssessmentAnswer { AssessmentId = row.Id, Key = x.Key, Value = Clip(x.Value, 300) ?? "" }).ToList();
        db.Assessments.Add(row);
        if (r.Kind == AssessmentKind.NursingAdmission)
        {
            e.EscortName ??= Clip(answers.GetValueOrDefault("escort_name"), 100);
            e.EscortPhone ??= Clip(answers.GetValueOrDefault("escort_phone"), 40);
        }
        await db.SaveChangesAsync(ct);
        await Audit(user, "Assess", "Episode", e, $"{r.Kind} {score.Score} {score.Risk}", ct);
        return (row, []);
    }

    // ---------------------------------------------------------------- Surgical Safety Checklist (hard stops)
    public async Task<(SafetyCheck? Check, IReadOnlyList<string> Errors)> CompleteCheckAsync(Guid episodeId, CheckRequest r, string user, CancellationToken ct = default)
    {
        var e = await OpenAsync(episodeId, ct);
        var errors = new List<string>();
        var done = await db.SafetyChecks.Where(c => c.EpisodeId == e.Id).Select(c => c.Phase).ToListAsync(ct);
        if (done.Contains(r.Phase)) return (null, [$"{Label(r.Phase)} is already complete."]);
        if (r.Phase == CheckPhase.SignIn && e.Stage == EpisodeStage.PreAdmission) errors.Add("Admit the patient before Sign In.");
        if (r.Phase == CheckPhase.TimeOut && !done.Contains(CheckPhase.SignIn)) errors.Add("Sign In must be completed before Time Out.");
        if (r.Phase == CheckPhase.SignOut && !done.Contains(CheckPhase.TimeOut)) errors.Add("Time Out must be completed before Sign Out.");
        var missing = CheckItems[r.Phase].Where(i => !r.Confirmed.Contains(i.Key)).Select(i => i.Text).ToList();
        if (missing.Count > 0) errors.Add("Not yet confirmed: " + string.Join("; ", missing) + ".");
        foreach (var role in CheckRoles[r.Phase].Where(role => !r.Team.TryGetValue(role, out var n) || string.IsNullOrWhiteSpace(n)))
            errors.Add($"Name the {role.ToLowerInvariant()} taking part.");
        if (r.Phase == CheckPhase.SignOut)
        {
            var countProblems = await CountProblemsAsync(e.Id, ct);
            if (countProblems.Count > 0) errors.Add("Counts: " + string.Join(" ", countProblems));
            if (await db.Specimens.AnyAsync(s => s.EpisodeId == e.Id && s.Site == "", ct)) errors.Add("Every specimen needs its site recorded.");
        }
        if (errors.Count > 0) return (null, errors);
        var row = new SafetyCheck { EpisodeId = e.Id, Phase = r.Phase, CompletedAt = clock.GetUtcNow(), LedBy = user, Notes = Clip(r.Notes, 500) };
        row.Items = CheckItems[r.Phase].Select(i => new SafetyCheckItem { SafetyCheckId = row.Id, Key = i.Key, Confirmed = true }).ToList();
        row.Team = r.Team.Where(t => !string.IsNullOrWhiteSpace(t.Value)).Select(t => new SafetyCheckMember { SafetyCheckId = row.Id, Role = Clip(t.Key, 40)!, Name = Clip(t.Value, 100)! }).ToList();
        db.SafetyChecks.Add(row);
        if (r.Phase == CheckPhase.SignIn && e.Stage == EpisodeStage.Admitted) e.Stage = EpisodeStage.InTheatre;
        await db.SaveChangesAsync(ct);
        await Audit(user, "SafetyCheck", "Episode", e, $"{r.Phase}: {string.Join(", ", row.Team.Select(t => $"{t.Role} {t.Name}"))}", ct);
        return (row, []);
    }

    public static string Label(CheckPhase p) => p switch { CheckPhase.SignIn => "Sign In", CheckPhase.TimeOut => "Time Out", _ => "Sign Out" };

    // ---------------------------------------------------------------- intraoperative record
    public async Task<(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)> SaveIntraopAsync(Guid episodeId, IntraopEdit r, string user, CancellationToken ct = default)
    {
        var e = await OpenAsync(episodeId, ct);
        var errors = new List<string>(); var warnings = new List<string>();
        var timeOut = await db.SafetyChecks.AnyAsync(c => c.EpisodeId == e.Id && c.Phase == CheckPhase.TimeOut, ct);
        if (r.KnifeToSkinAt is not null && !timeOut) errors.Add("Time Out must be completed before knife to skin.");
        void Order(DateTimeOffset? a, DateTimeOffset? b, string what) { if (a is { } x && b is { } y && y < x) errors.Add(what); }
        Order(r.InTheatreAt, r.KnifeToSkinAt, "Knife to skin cannot be before the patient was in theatre.");
        Order(r.KnifeToSkinAt, r.ClosureAt, "Closure cannot be before knife to skin.");
        Order(r.ClosureAt, r.OutOfTheatreAt, "Out of theatre cannot be before closure.");
        Order(r.TourniquetOnAt, r.TourniquetOffAt, "Tourniquet off cannot be before it went on.");
        if (r.TourniquetPressure is < 0 or > 500) errors.Add("Tourniquet pressure must be 0–500 mmHg.");
        if (r.WoundClass is not null && !new[] { "Clean", "CleanContaminated", "Contaminated", "Dirty" }.Contains(r.WoundClass)) errors.Add("Choose the wound class.");
        if (errors.Count > 0) return (errors, warnings);
        if (r.TourniquetOnAt is { } on && (r.TourniquetOffAt ?? clock.GetUtcNow()) - on > TimeSpan.FromMinutes(120)) warnings.Add("Tourniquet time over 120 minutes – tell the surgeon.");
        if (r.DiathermyUnit is not null && r.DiathermyPadSite is null) warnings.Add("Record the diathermy pad site.");
        if (r.OutOfTheatreAt is not null && r.DiathermyPadSite is not null && r.PadSiteSkinIntactAfter is null) warnings.Add("Check and record the skin under the diathermy pad.");
        var row = await db.Intraop.SingleOrDefaultAsync(x => x.EpisodeId == e.Id, ct);
        if (row is null) { row = new IntraopRecord { EpisodeId = e.Id }; db.Intraop.Add(row); }
        row.InTheatreAt = r.InTheatreAt; row.AnaestheticStartAt = r.AnaestheticStartAt; row.KnifeToSkinAt = r.KnifeToSkinAt; row.ClosureAt = r.ClosureAt; row.OutOfTheatreAt = r.OutOfTheatreAt;
        row.Position = Clip(r.Position, 200); row.PositioningAids = Clip(r.PositioningAids, 200); row.SkinPrep = Clip(r.SkinPrep, 200); row.DiathermyUnit = Clip(r.DiathermyUnit, 200);
        row.DiathermyPadSite = Clip(r.DiathermyPadSite, 200); row.PadSiteSkinIntactAfter = r.PadSiteSkinIntactAfter; row.TourniquetSite = Clip(r.TourniquetSite, 200); row.TourniquetPressure = r.TourniquetPressure;
        row.TourniquetOnAt = r.TourniquetOnAt; row.TourniquetOffAt = r.TourniquetOffAt; row.SmokeEvacuation = r.SmokeEvacuation; row.WoundClass = r.WoundClass; row.Drains = Clip(r.Drains, 200);
        row.Dressings = Clip(r.Dressings, 200); row.Notes = Clip(r.Notes, 2000); row.UpdatedBy = user; row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await Audit(user, "Update", "IntraopRecord", e, null, ct);
        return ([], warnings);
    }

    // ---------------------------------------------------------------- counts and specimens
    public static readonly string[] CountItems = ["Swabs", "Sharps", "Instruments", "Other"];

    public async Task<(SurgicalCount? Count, string? Error, string? Warning)> CountAsync(Guid episodeId, CountRequest r, string user, CancellationToken ct = default)
    {
        var e = await OpenAsync(episodeId, ct);
        if (!CountItems.Contains(r.Item)) return (null, "Choose swabs, sharps, instruments or other.", null);
        if (r.Expected < 0 || r.Counted < 0 || r.Expected > 1000) return (null, "Enter the numbers counted.", null);
        if (string.IsNullOrWhiteSpace(r.CheckedBy) || string.Equals(r.CheckedBy.Trim(), user, StringComparison.OrdinalIgnoreCase)) return (null, "Counts are done by two people – name the second person.", null);
        if (r.Stage is CountStage.Closing or CountStage.Final && !await db.Counts.AnyAsync(c => c.EpisodeId == e.Id && c.Stage == CountStage.Initial && c.Item == r.Item, ct))
            return (null, $"There is no initial {r.Item.ToLowerInvariant()} count.", null);
        var row = new SurgicalCount { EpisodeId = e.Id, Stage = r.Stage, Item = r.Item, Expected = r.Expected, Counted = r.Counted, CountedBy = user, CheckedBy = Clip(r.CheckedBy, 100)!, At = clock.GetUtcNow() };
        db.Counts.Add(row);
        await db.SaveChangesAsync(ct);
        await Audit(user, "Count", "Episode", e, $"{r.Stage} {r.Item} {r.Counted}/{r.Expected}", ct);
        return (row, null, r.Counted != r.Expected ? $"{r.Item}: counted {r.Counted}, expected {r.Expected}. Stop, tell the surgeon, search, recount; if still wrong, X-ray before the patient leaves, then record how it was resolved." : null);
    }

    public async Task<string?> ResolveCountAsync(Guid countId, string note, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note)) return "Record how the discrepancy was resolved (found, X-ray result, surgeon informed).";
        var c = await db.Counts.SingleOrDefaultAsync(x => x.Id == countId, ct);
        if (c is null) return "Count not found.";
        var e = await OpenAsync(c.EpisodeId, ct);
        c.Resolved = true; c.ResolutionNote = Clip(note, 500);
        await db.SaveChangesAsync(ct);
        await Audit(user, "ResolveCount", "Episode", e, $"{c.Stage} {c.Item}: {note}", ct);
        return null;
    }

    /// <summary>For each item counted at the start, the latest closing/final count must match, or the discrepancy must be resolved.</summary>
    public async Task<IReadOnlyList<string>> CountProblemsAsync(Guid episodeId, CancellationToken ct = default)
    {
        var counts = await db.Counts.AsNoTracking().Where(c => c.EpisodeId == episodeId).OrderBy(c => c.At).ToListAsync(ct);
        var problems = new List<string>();
        foreach (var item in counts.Where(c => c.Stage == CountStage.Initial).Select(c => c.Item).Distinct())
        {
            var last = counts.LastOrDefault(c => c.Item == item && c.Stage is CountStage.Closing or CountStage.Final);
            if (last is null) problems.Add($"No closing count for {item.ToLowerInvariant()}.");
            else if (last.Counted != last.Expected && !last.Resolved) problems.Add($"{item} count is wrong ({last.Counted} of {last.Expected}) and not resolved.");
        }
        return problems;
    }

    public async Task<(Specimen? Specimen, string? Error)> AddSpecimenAsync(Guid episodeId, SpecimenRequest r, string user, CancellationToken ct = default)
    {
        var e = await OpenAsync(episodeId, ct);
        if (string.IsNullOrWhiteSpace(r.Site)) return (null, "Record the specimen site (e.g. 'Gallbladder').");
        var n = (await db.Specimens.Where(s => s.EpisodeId == e.Id).MaxAsync(s => (int?)s.Number, ct) ?? 0) + 1;
        var row = new Specimen { EpisodeId = e.Id, Number = n, Site = Clip(r.Site, 200)!, Test = Clip(r.Test, 60) ?? "Histology", Container = Clip(r.Container, 60), CollectedAt = clock.GetUtcNow(), CollectedBy = user };
        db.Specimens.Add(row);
        await db.SaveChangesAsync(ct);
        await Audit(user, "Specimen", "Episode", e, $"#{n} {row.Site} {row.Test}", ct);
        return (row, null);
    }

    /// <summary>Chain of custody: who sent it (and by which courier) and when the laboratory received it.</summary>
    public async Task<string?> SpecimenStepAsync(Guid specimenId, SpecimenStatus to, string? courier, string user, CancellationToken ct = default)
    {
        var s = await db.Specimens.SingleOrDefaultAsync(x => x.Id == specimenId, ct);
        if (s is null) return "Specimen not found.";
        if (to == SpecimenStatus.Sent && s.Status != SpecimenStatus.Collected) return "Already sent.";
        if (to == SpecimenStatus.ReceivedByLab && s.Status != SpecimenStatus.Sent) return "Record it as sent first.";
        if (to == SpecimenStatus.Sent && string.IsNullOrWhiteSpace(courier)) return "Record the courier or who took it.";
        s.Status = to;
        if (to == SpecimenStatus.Sent) { s.SentAt = clock.GetUtcNow(); s.SentBy = user; s.Courier = Clip(courier, 100); }
        if (to == SpecimenStatus.ReceivedByLab) s.ReceivedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Specimen", "Episode", s.EpisodeId.ToString(), reason: $"#{s.Number} {to}", ct: ct);
        return null;
    }

    // ---------------------------------------------------------------- observations and early warning score
    /// <summary>
    /// Early warning score from the NEWS-style bands used on the HQSC adult vital signs chart: respiratory rate, SpO2, supplemental oxygen,
    /// temperature, systolic blood pressure, heart rate and level of consciousness. Bands and escalation must be confirmed against the
    /// current HQSC NZ EWS chart and TDS policy before clinical use.
    /// </summary>
    public static (int? Score, bool AnyThree) Ews(ObservationRequest o)
    {
        if (o.RespiratoryRate is null || o.SpO2 is null || o.SystolicBp is null || o.HeartRate is null || o.TemperatureC is null || o.Consciousness is null) return (null, false);
        int rr = o.RespiratoryRate.Value switch { <= 8 => 3, <= 11 => 1, <= 20 => 0, <= 24 => 2, _ => 3 };
        int sp = o.SpO2.Value switch { <= 91 => 3, <= 93 => 2, <= 95 => 1, _ => 0 };
        int ox = o.OnOxygen ? 2 : 0;
        int tp = o.TemperatureC.Value switch { <= 35.0m => 3, <= 36.0m => 1, <= 38.0m => 0, <= 39.0m => 1, _ => 2 };
        int bp = o.SystolicBp.Value switch { <= 90 => 3, <= 100 => 2, <= 110 => 1, <= 219 => 0, _ => 3 };
        int hr = o.HeartRate.Value switch { <= 40 => 3, <= 50 => 1, <= 90 => 0, <= 110 => 1, <= 130 => 2, _ => 3 };
        int lc = string.Equals(o.Consciousness, "A", StringComparison.OrdinalIgnoreCase) ? 0 : 3;
        var parts = new[] { rr, sp, ox, tp, bp, hr, lc };
        return (parts.Sum(), parts.Any(p => p == 3));
    }

    public static string? Escalation(int? score, bool anyThree) => score switch
    {
        null => null,
        >= 8 => "EMERGENCY: call the resuscitation team/ambulance now, stay with the patient.",
        >= 6 => "Urgent medical review within 15 minutes (anaesthetist/surgeon); observations every 15 minutes.",
        _ when anyThree => "A single parameter scored 3: urgent medical review; observations every 15–30 minutes.",
        >= 1 => "Tell the nurse in charge; increase observations.",
        _ => null,
    };

    public async Task<(Observation? Obs, IReadOnlyList<string> Errors)> ObserveAsync(Guid episodeId, ObservationRequest r, string user, CancellationToken ct = default)
    {
        var e = await OpenAsync(episodeId, ct);
        var errors = new List<string>();
        if (r.RespiratoryRate is < 0 or > 80) errors.Add("Respiratory rate looks wrong.");
        if (r.SpO2 is < 50 or > 100) errors.Add("SpO2 must be 50–100 %.");
        if (r.SystolicBp is < 30 or > 300 || r.DiastolicBp is < 10 or > 200) errors.Add("Blood pressure looks wrong.");
        if (r.HeartRate is < 10 or > 250) errors.Add("Heart rate looks wrong.");
        if (r.TemperatureC is < 25 or > 45) errors.Add("Temperature looks wrong.");
        if (r.Consciousness is not null && !"AVPUC".Contains(r.Consciousness.ToUpperInvariant(), StringComparison.Ordinal)) errors.Add("Consciousness is A, V, P, U or C (new confusion).");
        if (r.Pain is < 0 or > 10) errors.Add("Pain score is 0–10.");
        if (new object?[] { r.RespiratoryRate, r.SpO2, r.SystolicBp, r.HeartRate, r.TemperatureC, r.Consciousness }.All(x => x is null)) errors.Add("Enter at least one observation.");
        if (r.At is { } at && at > clock.GetUtcNow().AddMinutes(5)) errors.Add("The time cannot be in the future.");
        if (errors.Count > 0) return (null, errors);
        var (ews, three) = Ews(r);
        var row = new Observation
        {
            EpisodeId = e.Id,
            At = r.At ?? clock.GetUtcNow(),
            RespiratoryRate = r.RespiratoryRate,
            SpO2 = r.SpO2,
            OnOxygen = r.OnOxygen,
            OxygenLitres = r.OxygenLitres,
            SystolicBp = r.SystolicBp,
            DiastolicBp = r.DiastolicBp,
            HeartRate = r.HeartRate,
            TemperatureC = r.TemperatureC,
            Consciousness = r.Consciousness?.ToUpperInvariant(),
            Pain = r.Pain,
            Sedation = r.Sedation,
            Ews = ews,
            Escalation = Escalation(ews, three),
            Source = r.Source == "Device" ? "Device" : "Manual",
            DeviceId = r.DeviceId,
            RecordedBy = user,
        };
        db.Observations.Add(row);
        await db.SaveChangesAsync(ct);
        await Audit(user, "Observe", "Episode", e, $"EWS {ews?.ToString() ?? "-"} ({row.Source})", ct);
        return (row, []);
    }

    // ---------------------------------------------------------------- medicines, controlled drugs, fluids, blood
    public async Task<(MedicationGiven? Med, string? Error)> GiveAsync(Guid episodeId, MedicationRequest r, string user, CancellationToken ct = default)
    {
        var e = await OpenAsync(episodeId, ct);
        if (string.IsNullOrWhiteSpace(r.Medicine) || string.IsNullOrWhiteSpace(r.Dose) || string.IsNullOrWhiteSpace(r.Route)) return (null, "Enter the medicine, dose and route.");
        if (r.GivenAt is { } at && at > clock.GetUtcNow().AddMinutes(5)) return (null, "The time cannot be in the future.");
        ControlledDrug? cd = null;
        if (r.ControlledDrugId is { } cid)
        {
            cd = await db.ControlledDrugs.SingleOrDefaultAsync(x => x.Id == cid && x.IsActive, ct);
            if (cd is null) return (null, "Choose the controlled drug from the register.");
            if (string.IsNullOrWhiteSpace(r.WitnessedBy) || string.Equals(r.WitnessedBy.Trim(), user, StringComparison.OrdinalIgnoreCase)) return (null, "Controlled drugs need a second person to witness – name them.");
            if (cd.Balance < 1) return (null, $"The register shows no {cd.Name} {cd.Strength} in stock – check the balance.");
        }
        var row = new MedicationGiven
        {
            EpisodeId = e.Id,
            Medicine = Clip(r.Medicine, 150)!,
            Dose = Clip(r.Dose, 60)!,
            Route = Clip(r.Route, 30)!,
            GivenAt = r.GivenAt ?? clock.GetUtcNow(),
            GivenBy = user,
            WitnessedBy = Clip(r.WitnessedBy, 100),
            Barcode = Clip(r.Barcode, 60),
            ControlledDrugId = cd?.Id,
            Notes = Clip(r.Notes, 300)
        };
        db.Medications.Add(row);
        if (cd is not null)
        {
            cd.Balance--;
            db.CdRegister.Add(new ControlledDrugEntry
            {
                ControlledDrugId = cd.Id,
                Movement = CdMovement.Administered,
                Quantity = 1,
                BalanceAfter = cd.Balance,
                PatientId = e.PatientId,
                EpisodeId = e.Id,
                By = user,
                WitnessedBy = Clip(r.WitnessedBy, 100)!,
                At = row.GivenAt,
                Reference = $"{row.Dose} {row.Route}"
            });
        }
        await db.SaveChangesAsync(ct);
        await Audit(user, "Medication", "Episode", e, $"{row.Medicine} {row.Dose} {row.Route}{(cd is null ? "" : " (CD, witnessed " + row.WitnessedBy + ")")}", ct);
        return (row, null);
    }

    /// <summary>
    /// Files medicines recorded in another system (e.g. the SaferSleep anaesthetic record) so "medicines given today" is complete.
    /// Replaces any earlier copy from the same external record (corrections). Allowed after discharge – it is filing, not charting.
    /// Controlled drugs are not taken off the register here: the anaesthetist's register entry stays the record of truth.
    /// </summary>
    public async Task<int> FileExternalMedicationsAsync(Guid episodeId, string source, string externalRef, IReadOnlyList<(string Medicine, string Dose, string Route, DateTimeOffset At, string GivenBy, string? Notes)> items, string user, CancellationToken ct = default)
    {
        var e = await db.Episodes.SingleOrDefaultAsync(x => x.Id == episodeId, ct) ?? throw new KeyNotFoundException("Episode not found.");
        db.Medications.RemoveRange(await db.Medications.Where(m => m.EpisodeId == e.Id && m.Source == source && m.ExternalRef == externalRef).ToListAsync(ct));
        foreach (var i in items)
            db.Medications.Add(new MedicationGiven
            {
                EpisodeId = e.Id,
                Medicine = Clip(i.Medicine, 150)!,
                Dose = Clip(i.Dose, 60) ?? "",
                Route = Clip(i.Route, 30) ?? "",
                GivenAt = i.At,
                GivenBy = Clip(i.GivenBy, 100)!,
                Notes = Clip(i.Notes, 300),
                Source = Clip(source, 20)!,
                ExternalRef = Clip(externalRef, 64)
            });
        await db.SaveChangesAsync(ct);
        await Audit(user, "Medication", "Episode", e, $"{items.Count} medicine(s) filed from {source} ({externalRef})", ct);
        return items.Count;
    }

    public async Task<IReadOnlyList<ControlledDrug>> ControlledDrugsAsync(CancellationToken ct = default) => await db.ControlledDrugs.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Strength).ToListAsync(ct);

    public async Task<(ControlledDrug? Drug, string? Error)> AddControlledDrugAsync(string name, string strength, string form, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(strength)) return (null, "Enter the drug name and strength.");
        var f = Clip(form, 30) ?? "ampoule";
        if (await db.ControlledDrugs.AnyAsync(x => x.Name == name.Trim() && x.Strength == strength.Trim() && x.Form == f, ct)) return (null, "That drug is already in the register.");
        var d = new ControlledDrug { Name = Clip(name, 100)!, Strength = Clip(strength, 40)!, Form = f };
        db.ControlledDrugs.Add(d);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "ControlledDrug", d.Id.ToString(), reason: $"{d.Name} {d.Strength} {d.Form}", ct: ct);
        return (d, null);
    }

    /// <summary>Register movements other than giving to a patient: received from the pharmacy, wasted (part ampoule), returned, correction after a stock check. Always witnessed.</summary>
    public async Task<string?> RegisterAsync(CdRequest r, string user, CancellationToken ct = default)
    {
        var d = await db.ControlledDrugs.SingleOrDefaultAsync(x => x.Id == r.ControlledDrugId, ct);
        if (d is null) return "Drug not found in the register.";
        if (r.Movement == CdMovement.Administered) return "Record giving to a patient from the patient's clinical record.";
        if (string.IsNullOrWhiteSpace(r.WitnessedBy) || string.Equals(r.WitnessedBy.Trim(), user, StringComparison.OrdinalIgnoreCase)) return "A second person must witness every register entry – name them.";
        if (r.Movement != CdMovement.Correction && r.Quantity < 1) return "Enter the quantity.";
        if (r.Movement == CdMovement.Correction && string.IsNullOrWhiteSpace(r.Reference)) return "Corrections need the reason and the stock-check reference.";
        var change = r.Movement switch { CdMovement.Received => r.Quantity, CdMovement.Correction => r.Quantity, _ => -r.Quantity };
        if (d.Balance + change < 0) return $"That would make the balance negative (now {d.Balance}).";
        d.Balance += change;
        db.CdRegister.Add(new ControlledDrugEntry { ControlledDrugId = d.Id, Movement = r.Movement, Quantity = r.Quantity, BalanceAfter = d.Balance, By = user, WitnessedBy = Clip(r.WitnessedBy, 100)!, Reference = Clip(r.Reference, 100), At = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ControlledDrug", "ControlledDrug", d.Id.ToString(), reason: $"{r.Movement} {r.Quantity} → balance {d.Balance}, witness {r.WitnessedBy}", ct: ct);
        return null;
    }

    public async Task<IReadOnlyList<ControlledDrugEntry>> RegisterEntriesAsync(Guid drugId, CancellationToken ct = default) =>
        await db.CdRegister.AsNoTracking().Where(x => x.ControlledDrugId == drugId).OrderByDescending(x => x.At).Take(500).ToListAsync(ct);

    public async Task<string?> FluidAsync(Guid episodeId, FluidRequest r, string user, CancellationToken ct = default)
    {
        var e = await OpenAsync(episodeId, ct);
        if (r.Millilitres is < 1 or > 10000) return "Enter the volume in ml.";
        if (string.IsNullOrWhiteSpace(r.Kind)) return "Choose what it was (IV, oral, urine, drain, blood loss…).";
        db.Fluids.Add(new FluidEntry { EpisodeId = e.Id, At = r.At ?? clock.GetUtcNow(), IsInput = r.IsInput, Kind = Clip(r.Kind, 20)!, Millilitres = r.Millilitres, By = user });
        await db.SaveChangesAsync(ct);
        await Audit(user, "Fluid", "Episode", e, $"{(r.IsInput ? "in" : "out")} {r.Kind} {r.Millilitres} ml", ct);
        return null;
    }

    public async Task<(BloodUnit? Unit, string? Error)> AddBloodAsync(Guid episodeId, BloodRequest r, string user, CancellationToken ct = default)
    {
        var e = await OpenAsync(episodeId, ct);
        if (string.IsNullOrWhiteSpace(r.UnitNumber) || string.IsNullOrWhiteSpace(r.Product)) return (null, "Enter the unit number and product.");
        if (string.IsNullOrWhiteSpace(r.CheckedBy1) || string.IsNullOrWhiteSpace(r.CheckedBy2) || string.Equals(r.CheckedBy1.Trim(), r.CheckedBy2.Trim(), StringComparison.OrdinalIgnoreCase))
            return (null, "The bedside check is done by two different people – name both.");
        if (!r.ColdChainOk) return (null, "Cold chain not confirmed – do not give the unit. Return it to the blood bank and raise an incident.");
        var u = new BloodUnit
        {
            EpisodeId = e.Id,
            UnitNumber = Clip(r.UnitNumber, 40)!,
            Product = Clip(r.Product, 60)!,
            ReceivedAt = r.ReceivedAt,
            ColdChainOk = r.ColdChainOk,
            CheckedBy1 = Clip(r.CheckedBy1, 100)!,
            CheckedBy2 = Clip(r.CheckedBy2, 100)!,
            RecordedBy = user
        };
        db.BloodUnits.Add(u);
        await db.SaveChangesAsync(ct);
        await Audit(user, "BloodUnit", "Episode", e, $"{u.UnitNumber} {u.Product}", ct);
        return (u, null);
    }

    public async Task<string?> UpdateBloodAsync(Guid unitId, BloodUpdate r, string user, CancellationToken ct = default)
    {
        var u = await db.BloodUnits.SingleOrDefaultAsync(x => x.Id == unitId, ct);
        if (u is null) return "Unit not found.";
        if (r.StoppedAt is { } stop && r.StartedAt is { } start && stop < start) return "Stop cannot be before start.";
        if (r.Reaction && string.IsNullOrWhiteSpace(r.ReactionDetails)) return "Describe the reaction (and report it to NZ Blood Service).";
        u.StartedAt = r.StartedAt; u.StoppedAt = r.StoppedAt; u.Reaction = r.Reaction; u.ReactionDetails = Clip(r.ReactionDetails, 500); u.Returned = r.Returned;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "BloodUnit", "Episode", u.EpisodeId.ToString(), reason: $"{u.UnitNumber}{(r.Reaction ? " REACTION" : "")}{(r.Returned ? " returned" : "")}", ct: ct);
        return null;
    }

    // ---------------------------------------------------------------- notes and templates
    public async Task<(ClinicalNote? Note, string? Error)> NoteAsync(Guid episodeId, NoteRequest r, string user, CancellationToken ct = default)
    {
        var e = await db.Episodes.SingleOrDefaultAsync(x => x.Id == episodeId, ct);
        if (e is null) return (null, "Episode not found.");
        if (string.IsNullOrWhiteSpace(r.Text)) return (null, "Write the note.");
        if (r.AmendsNoteId is { } aid && !await db.Notes.AnyAsync(n => n.Id == aid && n.EpisodeId == e.Id && n.Signed, ct)) return (null, "Only a signed note in this episode can be amended.");
        var n = new ClinicalNote { EpisodeId = e.Id, Kind = r.Kind, Text = r.Text.Trim().Length > 20000 ? r.Text.Trim()[..20000] : r.Text.Trim(), Author = user, At = clock.GetUtcNow(), Signed = r.Sign, AmendsNoteId = r.AmendsNoteId };
        db.Notes.Add(n);
        await db.SaveChangesAsync(ct);
        await Audit(user, r.AmendsNoteId is null ? "Note" : "AmendNote", "Episode", e, $"{r.Kind}{(r.Sign ? " signed" : " draft")}", ct);
        return (n, null);
    }

    public async Task<string?> UpdateDraftNoteAsync(Guid noteId, string text, bool sign, string user, CancellationToken ct = default)
    {
        var n = await db.Notes.SingleOrDefaultAsync(x => x.Id == noteId, ct);
        if (n is null) return "Note not found.";
        if (n.Signed) return "Signed notes cannot be changed – add an amendment.";
        if (!string.Equals(n.Author, user, StringComparison.OrdinalIgnoreCase)) return "Only the author can change a draft note.";
        if (string.IsNullOrWhiteSpace(text)) return "Write the note.";
        n.Text = text.Trim(); n.Signed = sign; n.At = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, sign ? "SignNote" : "Note", "Episode", n.EpisodeId.ToString(), reason: n.Kind.ToString(), ct: ct);
        return null;
    }

    public async Task<IReadOnlyList<NoteTemplate>> TemplatesAsync(CancellationToken ct = default)
    {
        if (!await db.Templates.AnyAsync(ct))
        {
            db.Templates.AddRange(
                new NoteTemplate { Name = "Operation note", Kind = NoteKind.OperationNote, Body = "Procedure: {procedure}\nSurgeon: {surgeon}  Assistant:  Anaesthetist: {anaesthetist}\nAnaesthetic:\nIndication:\nFindings:\nProcedure details:\nSpecimens:\nImplants:\nEstimated blood loss:\nClosure:\nPost-operative instructions:\nFollow-up:" },
                new NoteTemplate { Name = "Discharge summary", Kind = NoteKind.Discharge, Body = "Procedure: {procedure} on {date}\nSurgeon: {surgeon}\nFindings and outcome:\nMedicines given / to continue:\nWound care:\nFollow-up: \nGP actions:" },
                new NoteTemplate { Name = "Patient discharge instructions", Kind = NoteKind.PatientInstructions, Body = "You had {procedure} today.\nFor 24 hours after sedation or anaesthetic: do not drive, drink alcohol, sign legal papers or be alone overnight.\nPain relief:\nWound care:\nCall us on {clinic_phone} or your GP if you have fever, bleeding, increasing pain or redness. In an emergency call 111." });
            await db.SaveChangesAsync(ct);
        }
        return await db.Templates.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);
    }

    // ---------------------------------------------------------------- discharge
    /// <summary>
    /// Discharge with the safety hard stop: after sedation or anaesthesia a responsible adult escort and an overnight carer must be confirmed,
    /// or an override reason recorded. The discharge criteria must be met; the latest early warning score must be 0–2 (otherwise a medical review note is needed).
    /// </summary>
    public async Task<IReadOnlyList<string>> DischargeAsync(Guid episodeId, DischargeRequest r, string user, CancellationToken ct = default)
    {
        var e = await OpenAsync(episodeId, ct);
        var errors = new List<string>();
        if (e.Stage == EpisodeStage.PreAdmission) errors.Add("The patient was never admitted.");
        if (!r.Transfer)
        {
            if (!r.CriteriaMet) errors.Add("Confirm the discharge criteria are met (observations stable, pain and nausea controlled, wound checked, voided if required, eating/drinking).");
            if (e.SedationOrAnaesthesia && !(r.EscortConfirmed && !string.IsNullOrWhiteSpace(r.EscortName) && !string.IsNullOrWhiteSpace(r.OvernightCarer)) && string.IsNullOrWhiteSpace(r.EscortOverrideReason))
                errors.Add("After sedation or anaesthesia a responsible adult must take the patient home and someone must stay overnight. Confirm both (names), or record why this patient may go without (override).");
            var last = await db.Observations.AsNoTracking().Where(o => o.EpisodeId == e.Id && o.Ews != null).OrderByDescending(o => o.At).FirstOrDefaultAsync(ct);
            if (last is null && e.SedationOrAnaesthesia) errors.Add("Record a set of observations before discharge.");
            if (last is { Ews: > 2 } && !await db.Notes.AnyAsync(n => n.EpisodeId == e.Id && n.Kind == NoteKind.Medical && n.At >= last.At, ct))
                errors.Add($"The latest early warning score is {last.Ews}. A doctor must review the patient (medical note) before discharge.");
            var openSignOut = await db.SafetyChecks.AnyAsync(c => c.EpisodeId == e.Id && c.Phase == CheckPhase.TimeOut, ct) && !await db.SafetyChecks.AnyAsync(c => c.EpisodeId == e.Id && c.Phase == CheckPhase.SignOut, ct);
            if (openSignOut) errors.Add("Sign Out was never completed for this procedure.");
        }
        else if (string.IsNullOrWhiteSpace(r.Destination)) errors.Add("Record the hospital the patient is being transferred to.");
        if (errors.Count > 0) return errors;
        e.EscortName = Clip(r.EscortName, 100) ?? e.EscortName; e.EscortPhone = Clip(r.EscortPhone, 40) ?? e.EscortPhone; e.OvernightCarer = Clip(r.OvernightCarer, 100);
        e.EscortConfirmed = r.EscortConfirmed; e.EscortOverrideReason = Clip(r.EscortOverrideReason, 300);
        e.Stage = r.Transfer ? EpisodeStage.Transferred : EpisodeStage.Discharged; e.DischargedAt = clock.GetUtcNow(); e.DischargedBy = user;
        e.DischargeDestination = Clip(r.Destination, 100) ?? (r.Transfer ? null : "Home"); e.FollowUpCallOn = r.FollowUpCallOn;
        await db.SaveChangesAsync(ct);
        await Audit(user, r.Transfer ? "Transfer" : "Discharge", "Episode", e, r.EscortOverrideReason is null ? $"escort {e.EscortName}, carer {e.OvernightCarer}" : $"ESCORT OVERRIDE: {r.EscortOverrideReason}", ct);
        return [];
    }

    internal static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length <= max ? s.Trim() : s.Trim()[..max]);
}
