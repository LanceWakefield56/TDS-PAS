using BrightPAS.Core;
using BrightPAS.Identity;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Coding;
using BrightPAS.Modules.Consent;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Estimates;
using BrightPAS.Modules.FollowUp;
using BrightPAS.Modules.Forms;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Reminders;
using BrightPAS.Modules.Theatre;
using BrightPAS.Modules.Triage;
using Assessment = BrightPAS.Modules.Triage.Assessment;
using Episode = BrightPAS.Modules.Emr.Episode;
using BrightPAS.Licensing;
using BrightPAS.Web.Licensing;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Web.Journey;

/// <summary>Where a stage is: done, the one to do now, waiting for someone outside TDS, stopped, still to come, or not part of this journey.</summary>
public enum StageState { Done, Now, Waiting, Blocked, Later, Skipped }

/// <summary>One thing to do. <c>To</c> is a screen path in the app; <c>Permission</c> is what the person needs to do it.</summary>
public sealed record JourneyAction(string Label, string To, string Permission);

public sealed record JourneyStage(string Key, string Label, StageState State, string? Detail, DateTimeOffset? When, JourneyAction? Action);

/// <summary>One episode of care: from referral (or estimate, or booking) to coding.</summary>
public sealed record JourneyCase(string Id, string Title, DateOnly? SurgeryDate, string Status, IReadOnlyList<JourneyStage> Stages, JourneyStage? Next, IReadOnlyList<string> Blockers);

public sealed record JourneyView(Guid PatientId, string Patient, string? Nhi, IReadOnlyList<JourneyCase> Cases, IReadOnlyList<JourneyAction> Starts);

/// <summary>An item on someone's Today list: a patient, what to do and why.</summary>
public sealed record TodayItem(string Group, Guid PatientId, string Patient, string? Nhi, string What, string? Why, DateOnly? Due, JourneyAction Action, int Urgency);

/// <summary>A theatre booking on the day board.</summary>
public sealed record DayCard(Guid BookingId, Guid PatientId, string Patient, string? Nhi, string Procedure, string Anaesthetic, string Session, TimeOnly Start, string Column, string Status,
    string? Triage, string? Precautions, string? Alert, Guid? EpisodeId);

/// <summary>
/// The patient journey (ADR 0036): reads every module (read only) and puts the patient's care in one order with one next step.
/// It never changes data – every action opens the screen that already does the job, so the rules stay in one place.
/// </summary>
public sealed class JourneyService(
    PatientsDbContext patients, TheatreDbContext theatre, ReferralsDbContext referrals, FormsDbContext forms, TriageDbContext triage,
    EstDbContext estimates, BillingDbContext billing, EmrDbContext emr, CodingDbContext coding, RemindersDbContext reminders, FollowUpDbContext followup,
    BrightPAS.Modules.Claims.ClaimsDbContext claimsDb, ConsentDbContext consentDb, LicenceCache licence, TimeProvider clock)
{
    private bool On(string module) => licence.Status.AccessTo(module) != ModuleAccess.None;

    private static readonly BookingStatus[] Gone = [BookingStatus.Cancelled, BookingStatus.DidNotAttend];

    public async Task<JourneyView?> ForPatientAsync(Guid patientId, CancellationToken ct = default)
    {
        var p = await patients.Patients.AsNoTracking().Where(x => x.Id == patientId).Select(x => new { x.Id, x.FamilyName, x.GivenNames, x.Nhi }).FirstOrDefaultAsync(ct);
        if (p is null) return null;
        var today = NzTime.Today(clock);

        // ---- read everything for this patient once
        var books = On("theatre") ? await (from b in theatre.Bookings.AsNoTracking()
                                           join s in theatre.Sessions.AsNoTracking() on b.SessionId equals s.Id
                                           where b.PatientId == patientId
                                           select new { b.Id, b.Procedure, b.Status, b.StatusAt, b.Anaesthetic, s.Date, s.StartTime, b.CreatedAt }).ToListAsync(ct) : [];
        var refs = On("referrals") ? await referrals.Referrals.AsNoTracking().Where(r => r.PatientId == patientId).ToListAsync(ct) : [];
        var waits = On("referrals") ? await referrals.Waitlist.AsNoTracking().Where(w => w.PatientId == patientId).ToListAsync(ct) : [];
        var frms = On("forms") ? await forms.Requests.AsNoTracking().Where(f => f.PatientId == patientId && f.Status != FormStatus.Cancelled).ToListAsync(ct) : [];
        var tri = On("triage") ? await triage.Assessments.AsNoTracking().Where(a => a.PatientId == patientId && a.Status != AssessmentStatus.Superseded).ToListAsync(ct) : [];
        var ests = On("estimates") ? await estimates.Estimates.AsNoTracking().Where(e => e.PatientId == patientId && e.Status != EstimateStatus.Cancelled).ToListAsync(ct) : [];
        var docs = On("billing") ? await billing.Documents.AsNoTracking().Include(d => d.Lines).Include(d => d.Payments).Where(d => d.PatientId == patientId && d.Status != DocumentStatus.Void).ToListAsync(ct) : [];
        var claimRows = On("claims") ? await claimsDb.Claims.AsNoTracking().Where(c => c.PatientId == patientId).ToListAsync(ct) : [];
        var eps = On("emr") ? await emr.Episodes.AsNoTracking().Where(e => e.PatientId == patientId).ToListAsync(ct) : [];
        var coded = On("reporting") || On("coding") ? await coding.Episodes.AsNoTracking().Where(c => c.PatientId == patientId).ToListAsync(ct) : [];
        var fus = On("followup") ? await followup.Contacts.AsNoTracking().Where(c => c.PatientId == patientId && c.Status != ContactStatus.Cancelled).ToListAsync(ct) : [];
        var cons = On("consent") ? await consentDb.Consents.AsNoTracking().Include(c => c.Questions).Where(c => c.PatientId == patientId).ToListAsync(ct) : [];
        var msgs = On("reminders") ? await reminders.Messages.AsNoTracking().Where(m => m.PatientId == patientId && (m.Kind == MessageKind.BookingConfirmation || m.Kind == MessageKind.DayBeforeReminder)).ToListAsync(ct) : [];

        var cases = new List<JourneyCase>();
        var usedWaits = new HashSet<Guid>();
        var usedRefs = new HashSet<Guid>();

        // ---- one case per live or recent booking (newest first; finished ones older than a year are history)
        // Current first: the soonest booking still to happen (or still in the building), then finished ones newest first.
        foreach (var b in books.Where(b => !Gone.Contains(b.Status) && b.Date >= today.AddDays(-365))
                     .OrderBy(b => b.Status == BookingStatus.Discharged || b.Date < today ? 1 : 0)
                     .ThenBy(b => b.Status == BookingStatus.Discharged || b.Date < today ? -b.Date.DayNumber : b.Date.DayNumber))
        {
            var w = waits.FirstOrDefault(x => x.BookingId == b.Id);
            if (w is not null) usedWaits.Add(w.Id);
            var r = w?.ReferralId is { } rid ? refs.FirstOrDefault(x => x.Id == rid) : null;
            if (r is not null) usedRefs.Add(r.Id);
            var stages = new List<JourneyStage>();
            stages.Add(ReferralStage(r));
            stages.Add(w is null ? Skip("waitlist", "Waitlist", "Booked directly") : new("waitlist", "Waitlist", StageState.Done, $"Added {NzDate(w.AddedOn)}", w.CreatedAt, null));
            stages.Add(EstimateStage(ests, docs, b.Date, patientId));
            stages.Add(QuestionnaireStage(frms, b.Id, patientId, b.Date, today));
            stages.Add(TriageStage(tri, b.Id, patientId));
            if (On("consent")) stages.Add(ConsentStage(cons, b.Id, b.Status, b.Date, today));
            stages.Add(BookingStage(b.Id, b.Status, b.Date));
            stages.Add(ReminderStage(msgs, b.Id, b.Date, today));
            var ep = eps.FirstOrDefault(e => e.BookingId == b.Id);
            stages.AddRange(DayStages(b.Id, b.Status, b.Date, ep, today));
            stages.Add(On("followup") ? FollowUpPlanStage(fus.Where(c => c.BookingId == b.Id).ToList(), b.Status, ep, today) : FollowUpStage(ep, b.Status, today));
            stages.Add(BillingStage(docs, b.Id, b.Status, patientId, claimRows));
            stages.Add(CodingStage(coded, b.Id, b.Status, ep));
            cases.Add(Build(b.Id.ToString(), b.Procedure, b.Date, StatusLabel(b.Status, b.Date, today), stages));
        }

        // ---- waiting for a date (waitlist without a booking)
        foreach (var w in waits.Where(w => !usedWaits.Contains(w.Id) && w.Status is WaitlistStatus.Waiting or WaitlistStatus.OnHold))
        {
            var r = w.ReferralId is { } rid ? refs.FirstOrDefault(x => x.Id == rid) : null;
            if (r is not null) usedRefs.Add(r.Id);
            var stages = new List<JourneyStage>
            {
                ReferralStage(r),
                w.Status == WaitlistStatus.OnHold
                    ? new("waitlist", "Waitlist", StageState.Blocked, $"On hold{(w.HoldReason is null ? "" : ": " + w.HoldReason)}{(w.HoldUntil is { } hu ? $" until {NzDate(hu)}" : "")}", w.StatusAt, new("Open the waitlist", $"/waitlist?patient={patientId}", Permissions.ReferralsView))
                    : new("waitlist", "Waitlist", StageState.Now, $"Waiting since {NzDate(w.AddedOn)} · target {NzDate(w.TargetDate)}{(w.TargetDate < today ? " (past target)" : "")}", w.CreatedAt,
                        new("Book a theatre date", $"/waitlist?patient={patientId}", Permissions.TheatreBook)),
                EstimateStage(ests, docs, null, patientId),
                QuestionnaireStage(frms, null, patientId, null, today),
                TriageStage(tri, null, patientId),
            };
            stages.AddRange(LaterStages());
            cases.Add(Build("w" + w.Id, w.Procedure, null, w.Status == WaitlistStatus.OnHold ? "On hold" : "Waiting for a date", stages));
        }

        // ---- referral not yet on the waitlist
        foreach (var r in refs.Where(r => !usedRefs.Contains(r.Id) && r.Status is ReferralStatus.Received or ReferralStatus.Accepted && r.ReceivedOn >= today.AddDays(-365)))
        {
            var stages = new List<JourneyStage>
            {
                ReferralStage(r),
                r.Status == ReferralStatus.Accepted
                    ? new("waitlist", "Waitlist", StageState.Now, "Accepted – not on the waitlist yet", null, new("Add to the waitlist", $"/referrals?patient={patientId}", Permissions.ReferralsManage))
                    : new("waitlist", "Waitlist", StageState.Later, null, null, null),
                EstimateStage(ests, docs, null, patientId),
            };
            stages.AddRange(LaterStages(fromQuestionnaire: true));
            cases.Add(Build("r" + r.Id, r.Reason, null, r.Status == ReferralStatus.Received ? "Referral to triage" : "Referral accepted", stages));
        }

        // ---- an estimate on its own (enquiry)
        if (cases.Count == 0 && ests.Any(e => e.EstimateDate >= today.AddDays(-180)))
        {
            var e = ests.OrderByDescending(x => x.EstimateDate).First();
            var stages = new List<JourneyStage> { Skip("referral", "Referral", "Self-referred or enquiry"), Skip("waitlist", "Waitlist", null), EstimateStage(ests, docs, null, patientId) };
            stages.AddRange(LaterStages(fromQuestionnaire: true));
            stages[3] = stages[3] with { State = StageState.Now, Detail = "When the patient goes ahead, book them or add them to the waitlist", Action = new("Book a theatre date", $"/theatre?patient={patientId}", Permissions.TheatreBook) };
            cases.Add(Build("e" + e.Id, e.ProcedureName ?? e.Title ?? "Estimate", e.ProcedureDate, "Estimate given", stages));
        }

        var starts = new List<JourneyAction>();
        if (On("referrals")) starts.Add(new("Record a referral", $"/referrals?new=1&patient={patientId}", Permissions.ReferralsManage));
        if (On("estimates")) starts.Add(new("Prepare an estimate", $"/estimates/new?patient={patientId}", Permissions.EstimatesBuild));
        if (On("theatre")) starts.Add(new("Book a theatre date", $"/theatre?patient={patientId}", Permissions.TheatreBook));
        if (On("theatre")) starts.Add(new("Book a clinic appointment", $"/calendar?patient={patientId}", Permissions.TheatreBook));
        return new JourneyView(p.Id, $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p.Nhi, cases, starts);
    }

    // ------------------------------------------------------------------ stages
    private static string NzDate(DateOnly d) => d.ToString("d MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-NZ"));
    private static JourneyStage Skip(string key, string label, string? why) => new(key, label, StageState.Skipped, why, null, null);

    private static IEnumerable<JourneyStage> LaterStages(bool fromQuestionnaire = false)
    {
        if (fromQuestionnaire) { yield return new("questionnaire", "Questionnaire", StageState.Later, null, null, null); yield return new("triage", "Triage", StageState.Later, null, null, null); }
        foreach (var (k, l) in new[] { ("booking", "Booked"), ("reminders", "Reminders"), ("admission", "Admitted"), ("theatre", "Theatre"), ("recovery", "Recovery"), ("discharge", "Discharged"), ("followup", "Follow-up"), ("billing", "Paid"), ("coding", "Coded") })
            yield return new(k, l, StageState.Later, null, null, null);
    }

    private JourneyStage ReferralStage(Referral? r)
    {
        if (!On("referrals")) return Skip("referral", "Referral", null);
        if (r is null) return Skip("referral", "Referral", "No referral recorded");
        return r.Status switch
        {
            ReferralStatus.Received => new("referral", "Referral", StageState.Now, $"{r.Number} from {r.ReferrerName} – to triage", r.CreatedAt, new("Triage the referral", "/referrals", Permissions.ReferralsTriage)),
            ReferralStatus.Accepted => new("referral", "Referral", StageState.Done, $"{r.Number} accepted", r.TriagedAt, null),
            ReferralStatus.Declined => new("referral", "Referral", StageState.Blocked, $"{r.Number} declined: {r.DeclineReason}", r.TriagedAt, null),
            _ => new("referral", "Referral", StageState.Skipped, $"{r.Number} withdrawn", r.TriagedAt, null)
        };
    }

    private JourneyStage EstimateStage(List<Estimate> ests, List<BillingDocument> docs, DateOnly? surgery, Guid patientId)
    {
        if (!On("estimates") && !On("billing")) return Skip("estimate", "Estimate", null);
        var from = (surgery ?? NzTime.Today(clock)).AddDays(-365);
        var e = ests.Where(x => x.EstimateDate >= from && (surgery is null || x.EstimateDate <= surgery)).OrderByDescending(x => x.EstimateDate).FirstOrDefault();
        if (e is not null)
            return e.Status == EstimateStatus.Issued
                ? new("estimate", "Estimate", StageState.Done, $"{e.Ref} · {e.Subtotal:C2}{(e.EmailedAt is null ? " · not emailed" : " · emailed")}", e.IssuedAt, new("Open the estimate", $"/estimates/{e.Id}", Permissions.EstimatesBuild))
                : new("estimate", "Estimate", StageState.Waiting, $"{e.Ref} is a draft", e.CreatedAt, new("Finish the estimate", $"/estimates/{e.Id}", Permissions.EstimatesBuild));
        var d = docs.Where(x => x.Kind == DocumentKind.Estimate).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        if (d is not null) return new("estimate", "Estimate", d.Status == DocumentStatus.Draft ? StageState.Now : StageState.Done, $"{d.Number ?? "Draft"} ({d.Status})", d.CreatedAt, new("Open the estimate", $"/billing/{d.Id}", Permissions.BillingView));
        // Optional: an estimate is offered, never required.
        return new("estimate", "Estimate", StageState.Later, "None yet (optional)", null,
            On("estimates") ? new("Prepare an estimate", $"/estimates/new?patient={patientId}", Permissions.EstimatesBuild) : new("Prepare an estimate", $"/billing/new?patient={patientId}", Permissions.BillingManage));
    }

    private JourneyStage QuestionnaireStage(List<FormRequest> frms, Guid? bookingId, Guid patientId, DateOnly? surgery, DateOnly today)
    {
        if (!On("forms")) return Skip("questionnaire", "Questionnaire", null);
        // The questionnaire for this booking, or any recent one (answers stay valid for about six months – triage uses the latest).
        var since = clock.GetUtcNow().AddDays(-180);
        var f = frms.Where(x => bookingId is null || x.BookingId == bookingId || x.BookingId == null || x.CreatedAt >= since).OrderByDescending(x => x.BookingId == bookingId).ThenByDescending(x => x.CreatedAt).FirstOrDefault();
        var send = new JourneyAction("Send the questionnaire", $"/patients/{patientId}#forms", Permissions.FormsSend);
        if (f is null)
            return new("questionnaire", "Questionnaire", surgery is { } s && s <= today.AddDays(21) ? StageState.Now : StageState.Later, "Not sent", null, send);
        return f.Status switch
        {
            FormStatus.Sent or FormStatus.Opened => new("questionnaire", "Questionnaire", StageState.Waiting, f.Status == FormStatus.Opened ? "Opened by the patient – not finished" : "Sent – waiting for the patient", f.CreatedAt,
                f.ExpiresAt < clock.GetUtcNow().AddDays(3) ? send with { Label = "Send a new link" } : null),
            FormStatus.Submitted => new("questionnaire", "Questionnaire", On("triage") ? StageState.Done : StageState.Now, "Answered", f.SubmittedAt, On("triage") ? null : new("Triage the answers", $"/forms/{f.Id}", Permissions.FormsTriage)),
            FormStatus.Triaged => new("questionnaire", "Questionnaire", StageState.Done, $"Answered · {f.TriageCategory}", f.SubmittedAt, null),
            _ => new("questionnaire", "Questionnaire", StageState.Now, "Link expired", f.ExpiresAt, send with { Label = "Send a new link" })
        };
    }

    private JourneyStage TriageStage(List<Assessment> tri, Guid? bookingId, Guid patientId)
    {
        if (!On("triage")) return Skip("triage", "Triage", null);
        var a = tri.Where(x => bookingId is null || x.BookingId == bookingId || x.BookingId == null).OrderByDescending(x => x.BookingId == bookingId).ThenByDescending(x => x.CreatedAt).FirstOrDefault();
        if (a is null) return new("triage", "Triage", StageState.Later, "Runs when the answers come in", null, new("Run triage now", $"/patients/{patientId}#triage", Permissions.TriageAssess));
        var open = new JourneyAction(a.SuggestedOutcome == TriageOutcome.AnaesthetistReview ? "Anaesthetist review" : "Sign off triage", $"/triage/{a.Id}", a.SuggestedOutcome == TriageOutcome.AnaesthetistReview ? Permissions.TriageReview : Permissions.TriageAssess);
        if (a.Status == AssessmentStatus.Open)
            return new("triage", "Triage", a.SuggestedOutcome == TriageOutcome.MoreInformation ? StageState.Waiting : StageState.Now, $"Suggested: {Outcomes.Labels[a.SuggestedOutcome]}{(a.Worse ? " · worse than last time" : "")}", a.CreatedAt, open);
        var final = a.FinalOutcome ?? a.SuggestedOutcome;
        return final switch
        {
            TriageOutcome.Suitable or TriageOutcome.SuitableWithPrecautions => new("triage", "Triage", StageState.Done, Outcomes.Labels[final] + (a.Precautions is null ? "" : " – " + a.Precautions.Replace('\n', ';')), a.SignedOffAt, open with { Label = "Open triage" }),
            TriageOutcome.NotSuitable => new("triage", "Triage", StageState.Blocked, "Not suitable for day surgery at TDS", a.SignedOffAt, open with { Label = "Open triage" }),
            _ => new("triage", "Triage", StageState.Waiting, Outcomes.Labels[final], a.SignedOffAt, open with { Label = "Open triage" })
        };
    }

    private static JourneyStage ConsentStage(List<ConsentRecord> cons, Guid bookingId, BookingStatus st, DateOnly date, DateOnly today)
    {
        var c = cons.Where(x => x.BookingId == bookingId).OrderBy(x => x.Status == ConsentStatus.Withdrawn ? 1 : 0).ThenByDescending(x => x.CreatedAt).FirstOrDefault();
        var finished = st is BookingStatus.InTheatre or BookingStatus.Recovery or BookingStatus.Discharged;
        if (c is null || c.Status == ConsentStatus.Withdrawn)
            return new("consent", "Consent", finished ? StageState.Skipped : date <= today.AddDays(21) ? StageState.Now : StageState.Later,
                c is null ? "Not prepared" : "Withdrawn – " + c.WithdrawnReason, c?.WithdrawnAt, finished ? null : new("Prepare the consent", $"/consent?booking={bookingId}", Permissions.ConsentManage));
        var open = new JourneyAction("Open the consent", $"/consent/{c.Id}", Permissions.ConsentView);
        if (c.Questions.Any(q => q.AnsweredAt is null)) return new("consent", "Consent", StageState.Now, "The patient asked a question", c.Questions.Max(q => q.AskedAt), open with { Label = "Answer the question", Permission = Permissions.ConsentManage });
        return c.Status switch
        {
            ConsentStatus.Draft => new("consent", "Consent", StageState.Now, $"{c.Number} prepared – not sent", c.CreatedAt, open with { Label = "Send to the patient", Permission = Permissions.ConsentManage }),
            ConsentStatus.Sent => new("consent", "Consent", StageState.Waiting, c.OpenedAt is null ? "Sent – waiting for the patient" : "Opened by the patient – not signed", c.LinkSentAt, open),
            ConsentStatus.Signed => new("consent", "Consent", date <= today && !finished ? StageState.Now : StageState.Done, $"Signed {(c.Method == ConsentMethod.Paper ? "on paper" : "online")} – confirm on the day", c.SignedAt,
                open with { Label = "Confirm with the patient", Permission = Permissions.ConsentConfirm }),
            _ => new("consent", "Consent", StageState.Done, $"Signed and confirmed by {c.ConfirmedBy}", c.ConfirmedAt, open),
        };
    }

    private static JourneyStage BookingStage(Guid bookingId, BookingStatus st, DateOnly date) => st == BookingStatus.Booked
        ? new("booking", "Booked", StageState.Now, $"{NzDate(date)} – not confirmed", null, new("Confirm the booking", $"/theatre?date={date:yyyy-MM-dd}#b{bookingId}", Permissions.TheatreBook))
        : new("booking", "Booked", StageState.Done, $"{NzDate(date)}{(st == BookingStatus.Confirmed ? " · confirmed" : "")}", null, new("Open the theatre list", $"/theatre?date={date:yyyy-MM-dd}", Permissions.TheatreView));

    private JourneyStage ReminderStage(List<OutboundMessage> msgs, Guid bookingId, DateOnly date, DateOnly today)
    {
        if (!On("reminders")) return Skip("reminders", "Reminders", null);
        var mine = msgs.Where(m => m.BookingId == bookingId).ToList();
        var failed = mine.FirstOrDefault(m => m.Status == MessageStatus.Failed);
        if (failed is not null) return new("reminders", "Reminders", StageState.Blocked, $"{failed.Channel} to {failed.Address} failed", failed.CreatedAt, new("Check the patient's contact details", "/reminders", Permissions.RemindersView));
        var sent = mine.Where(m => m.Status is MessageStatus.Sent or MessageStatus.Delivered).OrderByDescending(m => m.SentAt).FirstOrDefault();
        if (sent is not null) return new("reminders", "Reminders", StageState.Done, sent.Kind == MessageKind.DayBeforeReminder ? "Day-before reminder sent" : "Booking confirmation sent", sent.SentAt, null);
        return new("reminders", "Reminders", date <= today ? StageState.Skipped : StageState.Later, mine.Count > 0 ? "Queued" : "Sent automatically", null, null);
    }

    private IEnumerable<JourneyStage> DayStages(Guid bookingId, BookingStatus st, DateOnly date, Episode? ep, DateOnly today)
    {
        var emrOn = On("emr");
        var open = new JourneyAction("Open the clinical record", $"/emr/{bookingId}", Permissions.EmrView);
        var day = date == today;
        var rank = st switch { BookingStatus.Arrived => 1, BookingStatus.InTheatre => 2, BookingStatus.Recovery => 3, BookingStatus.Discharged => 4, _ => 0 };
        if (ep is not null) rank = Math.Max(rank, ep.Stage switch { EpisodeStage.Admitted => 1, EpisodeStage.InTheatre => 2, EpisodeStage.Recovery or EpisodeStage.ReadyForDischarge => 3, EpisodeStage.Discharged or EpisodeStage.Transferred => 4, _ => 0 });
        JourneyStage S(string k, string l, int at, string nowLabel, string doneDetail) =>
            rank > at ? new(k, l, StageState.Done, doneDetail, null, emrOn ? open : null)
            : rank == at && (day || rank > 0) ? new(k, l, StageState.Now, null, null, emrOn ? open with { Label = nowLabel } : new(nowLabel, "/day", Permissions.TheatreBook))
            : new(k, l, StageState.Later, null, null, null);
        yield return rank >= 1 ? new("admission", "Admitted", StageState.Done, ep?.AdmittedAt is { } at ? $"Admitted {NzTime.ToNz(at):HH:mm}" : "Arrived", ep?.AdmittedAt, emrOn ? open : null)
            : day ? new("admission", "Admitted", StageState.Now, "Due today", null, emrOn ? open with { Label = "Admit the patient" } : new("Mark arrived", "/day", Permissions.TheatreBook))
            : new("admission", "Admitted", StageState.Later, null, null, null);
        yield return S("theatre", "Theatre", 2, "Send to theatre", "Operation done");
        yield return S("recovery", "Recovery", 3, "Record recovery", "Recovered");
        yield return rank >= 4 ? new("discharge", "Discharged", StageState.Done, ep?.DischargedAt is { } d ? $"{NzTime.ToNz(d):d MMM HH:mm}{(ep.DischargeDestination is null ? "" : " · " + ep.DischargeDestination)}" : "Discharged", ep?.DischargedAt, emrOn ? open : null)
            : S("discharge", "Discharged", 4, "Discharge", "");
    }

    private static JourneyStage FollowUpStage(Episode? ep, BookingStatus st, DateOnly today)
    {
        if (st != BookingStatus.Discharged && ep?.Stage != EpisodeStage.Discharged) return new("followup", "Follow-up", StageState.Later, null, null, null);
        if (ep?.FollowUpCallOn is { } due)
            return new("followup", "Follow-up", due <= today ? StageState.Now : StageState.Later, $"Phone call {(due < today ? "overdue since" : "due")} {NzDate(due)}", null, new("Open the clinical record", $"/emr/{ep.BookingId}", Permissions.EmrView));
        return new("followup", "Follow-up", StageState.Done, "No call planned", null, null);
    }

    private static JourneyStage FollowUpPlanStage(List<Contact> mine, BookingStatus st, Episode? ep, DateOnly today)
    {
        var discharged = st == BookingStatus.Discharged || ep?.Stage == EpisodeStage.Discharged;
        if (!discharged) return new("followup", "Follow-up", StageState.Later, null, null, null);
        if (mine.Count == 0) return new("followup", "Follow-up", StageState.Waiting, "Plan starts within 5 minutes of discharge", null, null);
        var open = mine.Where(c => c.Status is ContactStatus.Due or ContactStatus.NoAnswer).OrderBy(c => c.DueOn).ToList();
        var concern = mine.FirstOrDefault(c => c.Concern && c.EscalationClosedAt == null);
        if (concern is not null) return new("followup", "Follow-up", StageState.Blocked, $"Concern: {concern.ConcernText}", concern.AnsweredAt ?? concern.DoneAt, new("Open the follow-up", $"/followup/{concern.Id}", Permissions.FollowUpRecord));
        var esc = mine.FirstOrDefault(c => c.Escalation is not (Escalation.None or Escalation.Advice) && c.EscalationClosedAt == null);
        if (esc is not null) return new("followup", "Follow-up", StageState.Blocked, $"{FollowUpScript.EscalationLabels[esc.Escalation]} – not closed", esc.DoneAt, new("Open the follow-up", $"/followup/{esc.Id}", Permissions.FollowUpRecord));
        var done = mine.Count(c => c.Status == ContactStatus.Done);
        if (open.Count == 0) return new("followup", "Follow-up", StageState.Done, $"{done} of {mine.Count} contact(s) done", mine.Max(c => c.DoneAt), null);
        var n = open[0];
        var label = n.Method == ContactMethod.PatientForm ? (n.LinkSentAt is null ? $"{n.Label} – text goes {n.DueOn:d MMM}" : $"{n.Label} – waiting for the patient") : $"{n.Label} – due {n.DueOn:d MMM}";
        var state = n.DueOn <= today && n.Method != ContactMethod.PatientForm ? StageState.Now : n.Method == ContactMethod.PatientForm && n.LinkSentAt is not null ? StageState.Waiting : StageState.Later;
        return new("followup", "Follow-up", state, $"{label} ({done} of {mine.Count} done)", null, new(n.Method == ContactMethod.PatientForm ? "Open the follow-up" : "Make the call", $"/followup/{n.Id}", Permissions.FollowUpRecord));
    }

    private JourneyStage BillingStage(List<BillingDocument> docs, Guid bookingId, BookingStatus st, Guid patientId, List<BrightPAS.Modules.Claims.Claim> claimRows)
    {
        if (!On("billing")) return Skip("billing", "Paid", null);
        var inv = docs.Where(d => d.Kind == DocumentKind.Invoice && d.BookingId == bookingId).OrderByDescending(d => d.CreatedAt).FirstOrDefault();
        if (inv is null)
            return st == BookingStatus.Discharged
                ? new("billing", "Paid", StageState.Now, "No invoice yet", null, new("Create the invoice", $"/billing/new?kind=Invoice&patient={patientId}&booking={bookingId}", Permissions.BillingManage))
                : new("billing", "Paid", StageState.Later, null, null, null);
        return inv.Status switch
        {
            DocumentStatus.Paid => new("billing", "Paid", StageState.Done, $"{inv.Number} paid", null, new("Open the invoice", $"/billing/{inv.Id}", Permissions.BillingView)),
            DocumentStatus.Draft => new("billing", "Paid", StageState.Now, "Invoice is a draft", inv.CreatedAt, new("Finish and issue the invoice", $"/billing/{inv.Id}", Permissions.BillingManage)),
            _ when On("claims") && inv.Payer is Payer.Acc or Payer.Insurer or Payer.HealthNz => ClaimStage(inv, claimRows),
            _ => new("billing", "Paid", inv.DueOn is { } due && due < NzTime.Today(clock) ? StageState.Blocked : StageState.Waiting, $"{inv.Number} {(inv.Status == DocumentStatus.PartPaid ? "part paid" : "issued")}{(inv.DueOn is { } d ? $" · due {NzDate(d)}" : "")}", inv.CreatedAt,
                new("Record a payment", $"/billing/{inv.Id}", Permissions.BillingManage))
        };
    }

    /// <summary>Owed by ACC or an insurer: the claim is the next step until the funder pays.</summary>
    private JourneyStage ClaimStage(BillingDocument inv, List<BrightPAS.Modules.Claims.Claim> claimRows)
    {
        var c = claimRows.Where(x => x.InvoiceId == inv.Id && x.Status != BrightPAS.Modules.Claims.ClaimStatus.WrittenOff).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        if (c is null) return new("billing", "Paid", StageState.Now, $"{inv.Number} – claim not made yet", inv.CreatedAt, new("Make the claim", $"/billing/{inv.Id}", Permissions.ClaimsManage));
        return c.Status switch
        {
            BrightPAS.Modules.Claims.ClaimStatus.Draft => new("billing", "Paid", StageState.Blocked, $"Claim {c.Number} needs fixing", c.CreatedAt, new("Fix the claim", $"/claims/{c.Id}", Permissions.ClaimsManage)),
            BrightPAS.Modules.Claims.ClaimStatus.Rejected => new("billing", "Paid", StageState.Blocked, $"Claim {c.Number} rejected{(c.ResponseText is null ? "" : ": " + c.ResponseText)}", c.ResponseAt, new("Open the claim", $"/claims/{c.Id}", Permissions.ClaimsManage)),
            BrightPAS.Modules.Claims.ClaimStatus.Ready => new("billing", "Paid", StageState.Now, $"Claim {c.Number} ready to send", c.CreatedAt, new("Send the claim", "/claims", Permissions.ClaimsManage)),
            _ => new("billing", "Paid", StageState.Waiting, $"Claim {c.Number} with the funder{(c.Paid > 0 ? $" · {c.Paid:0.00} paid" : "")}", c.SubmittedAt, new("Open the claim", $"/claims/{c.Id}", Permissions.ClaimsView)),
        };
    }

    private JourneyStage CodingStage(List<CodedEpisode> coded, Guid bookingId, BookingStatus st, Episode? ep)
    {
        if (!On("reporting") && !On("coding")) return Skip("coding", "Coded", null);
        var c = coded.Where(x => x.BookingId == bookingId).OrderByDescending(x => x.Version).FirstOrDefault();
        if (c is null) return new("coding", "Coded", StageState.Later, st == BookingStatus.Discharged || ep?.Stage == EpisodeStage.Discharged ? "Waiting to be coded" : null, null, null);
        var open = new JourneyAction("Open the coding", $"/coding/events/{c.Id}", Permissions.CodingCode);
        return c.Status switch
        {
            CodingStatus.Coded or CodingStatus.Submitted => new("coding", "Coded", StageState.Done, c.Status == CodingStatus.Submitted ? "Coded and reported" : "Coded", c.CodedAt, open),
            CodingStatus.NotReportable => new("coding", "Coded", StageState.Done, "Not reportable", c.CodedAt, open),
            CodingStatus.Rejected => new("coding", "Coded", StageState.Blocked, $"Health NZ error: {c.HealthNzError}", null, open with { Label = "Fix the coding" }),
            _ => new("coding", "Coded", StageState.Now, "To code", null, open with { Label = "Code the event" })
        };
    }

    private static string StatusLabel(BookingStatus st, DateOnly date, DateOnly today) => st switch
    {
        BookingStatus.Booked or BookingStatus.Confirmed => date == today ? "Surgery today" : date < today ? "Surgery date passed" : $"Surgery in {date.DayNumber - today.DayNumber} day{(date.DayNumber - today.DayNumber == 1 ? "" : "s")}",
        BookingStatus.Arrived => "Arrived",
        BookingStatus.InTheatre => "In theatre",
        BookingStatus.Recovery => "In recovery",
        BookingStatus.Discharged => "Discharged",
        _ => st.ToString()
    };

    private static JourneyCase Build(string id, string title, DateOnly? date, string status, List<JourneyStage> stages)
    {
        // The next step is the first blocked or "now" stage; if none, the first stage waiting on someone else.
        // An estimate is offered, never required, so it is never the next step of a case that has moved on.
        var required = stages.Where(s => s.Key != "estimate" || stages.All(x => x.Key == "estimate" || x.State is StageState.Later or StageState.Skipped)).ToList();
        var next = required.FirstOrDefault(s => s.State is StageState.Blocked or StageState.Now) ?? required.FirstOrDefault(s => s.State == StageState.Waiting);
        var blockers = stages.Where(s => s.State == StageState.Blocked).Select(s => $"{s.Label}: {s.Detail}").ToList();
        return new JourneyCase(id, title, date, status, stages, next, blockers);
    }

    // ------------------------------------------------------------------ Today
    /// <summary>What this person needs to do now, across every module they can use, most urgent first.</summary>
    public async Task<IReadOnlyList<TodayItem>> TodayAsync(Func<string, bool> can, CancellationToken ct = default, string me = "")
    {
        var today = NzTime.Today(clock);
        var items = new List<TodayItem>();
        var names = new Dictionary<Guid, (string Name, string? Nhi)>();
        async Task Names(IEnumerable<Guid> ids)
        {
            var need = ids.Where(i => !names.ContainsKey(i)).Distinct().ToList();
            if (need.Count == 0) return;
            foreach (var x in await patients.Patients.AsNoTracking().Where(p => need.Contains(p.Id)).Select(p => new { p.Id, p.FamilyName, p.GivenNames, p.Nhi }).ToListAsync(ct))
                names[x.Id] = ($"{x.FamilyName.ToUpperInvariant()}, {x.GivenNames}", x.Nhi);
        }
        void Add(string group, Guid pid, string what, string? why, DateOnly? due, JourneyAction a, int urgency)
        {
            if (!can(a.Permission)) return;
            var (n, nhi) = names.TryGetValue(pid, out var v) ? v : ("?", null);
            items.Add(new TodayItem(group, pid, n, nhi, what, why, due, a, urgency));
        }

        // Day of surgery: today's patients by where they are.
        if (On("theatre") && (can(Permissions.TheatreView) || can(Permissions.EmrView)))
        {
            var day = await (from b in theatre.Bookings.AsNoTracking()
                             join s in theatre.Sessions.AsNoTracking() on b.SessionId equals s.Id
                             where s.Date == today && !s.IsCancelled && !Gone.Contains(b.Status) && b.Status != BookingStatus.Discharged
                             select new { b.Id, b.PatientId, b.Procedure, b.Status, s.StartTime }).ToListAsync(ct);
            await Names(day.Select(d => d.PatientId));
            foreach (var d in day)
            {
                var (what, perm) = d.Status switch
                {
                    BookingStatus.Booked or BookingStatus.Confirmed => ("Admit", Permissions.EmrView),
                    BookingStatus.Arrived => ("To theatre", Permissions.EmrView),
                    BookingStatus.InTheatre => ("In theatre", Permissions.EmrView),
                    _ => ("In recovery – discharge when ready", Permissions.EmrView)
                };
                Add("Theatre today", d.PatientId, what, $"{d.StartTime:HH\\:mm} {d.Procedure}", today, On("emr") ? new("Open", $"/emr/{d.Id}", perm) : new("Open the day board", "/day", Permissions.TheatreView), 1);
            }
        }
        if (On("referrals") && can(Permissions.ReferralsTriage))
        {
            var r = await referrals.Referrals.AsNoTracking().Where(x => x.Status == ReferralStatus.Received).OrderBy(x => x.ReceivedOn).Take(50).ToListAsync(ct);
            await Names(r.Select(x => x.PatientId));
            foreach (var x in r) Add("Before surgery", x.PatientId, "Triage the referral", $"{x.Number} from {x.ReferrerName}", x.ReceivedOn, new("Open referrals", "/referrals", Permissions.ReferralsTriage), 3);
        }
        if (On("referrals") && can(Permissions.TheatreBook))
        {
            var w = await referrals.Waitlist.AsNoTracking().Where(x => x.Status == WaitlistStatus.Waiting && x.TargetDate <= today.AddDays(14)).OrderBy(x => x.TargetDate).Take(50).ToListAsync(ct);
            await Names(w.Select(x => x.PatientId));
            foreach (var x in w) Add("Before surgery", x.PatientId, "Book a theatre date", $"{x.Procedure}{(x.TargetDate < today ? " · past target" : " · target soon")}", x.TargetDate, new("Open the waitlist", $"/waitlist?patient={x.PatientId}", Permissions.TheatreBook), x.TargetDate < today ? 2 : 4);
        }
        if (On("consent") && (can(Permissions.ConsentManage) || can(Permissions.ConsentConfirm)))
        {
            var cq = await consentDb.Consents.AsNoTracking().Where(c => c.Status != ConsentStatus.Withdrawn && c.Questions.Any(q => q.AnsweredAt == null)).Take(50).ToListAsync(ct);
            var bookingIds = await (from b in theatre.Bookings.AsNoTracking()
                                    join x in theatre.Sessions.AsNoTracking() on b.SessionId equals x.Id
                                    where x.Date == today && !x.IsCancelled
                                    select b.Id).ToListAsync(ct);
            var toConfirm = await consentDb.Consents.AsNoTracking().Where(c => c.Status == ConsentStatus.Signed && bookingIds.Contains(c.BookingId)).ToListAsync(ct);
            await Names(cq.Concat(toConfirm).Select(c => c.PatientId));
            foreach (var c in cq) Add("Before surgery", c.PatientId, "Answer the patient's consent question", $"{c.Number} {c.Procedure}", null, new("Open the consent", $"/consent/{c.Id}", Permissions.ConsentManage), 2);
            foreach (var c in toConfirm) Add("Theatre today", c.PatientId, "Confirm consent with the patient", $"{c.Number} {c.Procedure}", today, new("Open the consent", $"/consent/{c.Id}", Permissions.ConsentConfirm), 1);
        }
        if (On("triage") && (can(Permissions.TriageAssess) || can(Permissions.TriageReview)))
        {
            var a = await triage.Assessments.AsNoTracking().Where(x => x.Status == AssessmentStatus.Open).OrderBy(x => x.ProcedureDate).Take(100).ToListAsync(ct);
            await Names(a.Select(x => x.PatientId));
            foreach (var x in a)
            {
                var review = x.SuggestedOutcome is TriageOutcome.AnaesthetistReview or TriageOutcome.NotSuitable;
                Add("Before surgery", x.PatientId, review ? "Anaesthetist review" : "Sign off triage", $"Suggested: {Outcomes.Labels[x.SuggestedOutcome]}{(x.Worse ? " · got worse" : "")}", x.ProcedureDate,
                    new("Open triage", $"/triage/{x.Id}", review ? Permissions.TriageReview : Permissions.TriageAssess), x.ProcedureDate is { } pd && pd <= today.AddDays(2) ? 2 : 3);
            }
        }
        if (On("forms") && can(Permissions.FormsTriage) && !On("triage"))
        {
            var f = await forms.Requests.AsNoTracking().Where(x => x.Status == FormStatus.Submitted).Take(50).ToListAsync(ct);
            await Names(f.Select(x => x.PatientId));
            foreach (var x in f) Add("Before surgery", x.PatientId, "Triage the questionnaire", null, null, new("Open", $"/forms/{x.Id}", Permissions.FormsTriage), 3);
        }
        if (On("theatre") && can(Permissions.TheatreBook))
        {
            var b = await (from x in theatre.Bookings.AsNoTracking()
                           join s in theatre.Sessions.AsNoTracking() on x.SessionId equals s.Id
                           where x.Status == BookingStatus.Booked && s.Date > today && s.Date <= today.AddDays(14) && !s.IsCancelled
                           select new { x.Id, x.PatientId, x.Procedure, s.Date }).Take(100).ToListAsync(ct);
            await Names(b.Select(x => x.PatientId));
            foreach (var x in b) Add("Before surgery", x.PatientId, "Confirm the booking", x.Procedure, x.Date, new("Open the journey", $"/patients/{x.PatientId}", Permissions.TheatreBook), x.Date <= today.AddDays(3) ? 2 : 4);
        }
        if (On("emr") && can(Permissions.EmrView))
        {
            var calls = await emr.Episodes.AsNoTracking().Where(e => e.Stage == EpisodeStage.Discharged && e.FollowUpCallOn != null && e.FollowUpCallOn <= today && e.FollowUpCallOn >= today.AddDays(-14)).Take(100).ToListAsync(ct);
            await Names(calls.Select(x => x.PatientId));
            foreach (var x in calls) Add("After surgery", x.PatientId, "Follow-up phone call", x.FollowUpCallOn < today ? "Overdue" : "Due today", x.FollowUpCallOn, new("Open the clinical record", $"/emr/{x.BookingId}", Permissions.EmrView), x.FollowUpCallOn < today ? 2 : 3);
        }
        if (On("reminders") && can(Permissions.RemindersView))
        {
            var held = await reminders.Messages.AsNoTracking().Where(m => m.Status == MessageStatus.Queued && m.RequiresRelease && m.ReleasedAt == null && m.PatientId != null).Take(50).ToListAsync(ct);
            await Names(held.Select(x => x.PatientId!.Value));
            foreach (var m in held.Where(m => !string.Equals(m.CreatedBy, me, StringComparison.OrdinalIgnoreCase)))
                Add("Before surgery", m.PatientId!.Value, "Release a message", $"{m.Kind} by {m.Channel} – written by {m.CreatedBy}, needs a second person", null, new("Open the outbox", "/outbox?tab=release", Permissions.RemindersView), 2);
        }
        if (On("followup") && (can(Permissions.FollowUpRecord) || can(Permissions.FollowUpSsi)))
        {
            var due = await followup.Contacts.AsNoTracking().Where(c => (c.Status == ContactStatus.Due || c.Status == ContactStatus.NoAnswer) && c.DueOn <= today && c.Method != ContactMethod.PatientForm
                || c.Concern && c.EscalationClosedAt == null
                || c.Escalation != Escalation.None && c.Escalation != Escalation.Advice && c.EscalationClosedAt == null).OrderBy(c => c.DueOn).Take(200).ToListAsync(ct);
            await Names(due.Select(x => x.PatientId));
            foreach (var x in due.GroupBy(c => c.Id).Select(g => g.First()))
            {
                if (x.Concern && x.EscalationClosedAt == null && x.Status != ContactStatus.Due)
                    Add("After surgery", x.PatientId, "Patient reported a concern", x.ConcernText, x.DueOn, new("Open the follow-up", $"/followup/{x.Id}", Permissions.FollowUpRecord), 0);
                else if (x.Status is ContactStatus.Due or ContactStatus.NoAnswer)
                    Add("After surgery", x.PatientId, x.Concern ? "Call back – patient concern" : x.Label, $"{x.Procedure}{(x.DueOn < today ? " · overdue" : "")}{(x.Attempts > 0 ? $" · attempt {x.Attempts + 1}" : "")}", x.DueOn, new("Make the call", $"/followup/{x.Id}", Permissions.FollowUpRecord), x.Concern ? 0 : x.DueOn < today ? 2 : 3);
                else
                    Add("After surgery", x.PatientId, "Close the escalation", $"{FollowUpScript.EscalationLabels[x.Escalation]}: {x.EscalationNote}", x.DueOn, new("Open the follow-up", $"/followup/{x.Id}", Permissions.FollowUpRecord), 2);
            }
            if (can(Permissions.FollowUpSsi))
            {
                var ssi = await followup.SsiCases.AsNoTracking().Where(c => c.Class == SsiClass.Suspected).Take(100).ToListAsync(ct);
                await Names(ssi.Select(x => x.PatientId));
                foreach (var x in ssi) Add("After surgery", x.PatientId, "Confirm the SSI classification", $"{x.Number} · {x.Procedure}", x.OnsetOn, new("Open the SSI record", $"/followup/ssi/{x.Id}", Permissions.FollowUpSsi), 2);
            }
        }
        if (On("billing") && can(Permissions.BillingManage) && On("theatre"))
        {
            var since = today.AddDays(-60);
            var done = await (from x in theatre.Bookings.AsNoTracking()
                              join s in theatre.Sessions.AsNoTracking() on x.SessionId equals s.Id
                              where x.Status == BookingStatus.Discharged && s.Date >= since && !x.BilledExternally
                              select new { x.Id, x.PatientId, x.Procedure, s.Date }).ToListAsync(ct);
            var ids = done.Select(d => d.Id).ToList();
            var billed = await billing.Documents.AsNoTracking().Where(d => d.Kind == DocumentKind.Invoice && d.BookingId != null && ids.Contains(d.BookingId.Value) && d.Status != DocumentStatus.Void).Select(d => d.BookingId!.Value).ToListAsync(ct);
            var todo = done.Where(d => !billed.Contains(d.Id)).Take(100).ToList();
            await Names(todo.Select(x => x.PatientId));
            foreach (var x in todo) Add("After surgery", x.PatientId, "Create the invoice", x.Procedure, x.Date, new("Create the invoice", $"/billing/new?kind=Invoice&patient={x.PatientId}&booking={x.Id}", Permissions.BillingManage), 4);
            var overdue = await billing.Documents.AsNoTracking().Where(d => d.Kind == DocumentKind.Invoice && (d.Status == DocumentStatus.Issued || d.Status == DocumentStatus.PartPaid) && d.DueOn != null && d.DueOn < today).Take(100).ToListAsync(ct);
            await Names(overdue.Select(x => x.PatientId));
            foreach (var x in overdue) Add("After surgery", x.PatientId, "Invoice overdue", x.Number, x.DueOn, new("Open the invoice", $"/billing/{x.Id}", Permissions.BillingManage), 3);
        }
        if (On("claims") && can(Permissions.ClaimsManage))
        {
            var fix = await claimsDb.Claims.AsNoTracking().Where(c => c.Status == BrightPAS.Modules.Claims.ClaimStatus.Draft || c.Status == BrightPAS.Modules.Claims.ClaimStatus.Rejected).Take(100).ToListAsync(ct);
            await Names(fix.Select(x => x.PatientId));
            foreach (var x in fix) Add("After surgery", x.PatientId, x.Status == BrightPAS.Modules.Claims.ClaimStatus.Rejected ? "Claim rejected" : "Fix the claim", x.Number, x.CreatedAt.NzDate(), new("Open the claim", $"/claims/{x.Id}", Permissions.ClaimsManage), 3);
            var send = await claimsDb.Approvals.AsNoTracking().Where(a => a.Status == BrightPAS.Modules.Claims.ApprovalStatus.Draft).Take(100).ToListAsync(ct);
            await Names(send.Select(x => x.PatientId));
            foreach (var x in send) Add("Before surgery", x.PatientId, "Send the prior approval request", $"{x.Number} · {x.Procedure}", x.CreatedAt.NzDate(), new("Open prior approvals", "/claims?tab=approvals", Permissions.ClaimsManage), 3);
        }
        if ((On("reporting") || On("coding")) && can(Permissions.CodingCode))
        {
            var c = await coding.Episodes.AsNoTracking().Where(x => x.Status == CodingStatus.ToCode || x.Status == CodingStatus.Rejected).OrderBy(x => x.DischargeDate).Take(100).ToListAsync(ct);
            await Names(c.Select(x => x.PatientId));
            foreach (var x in c) Add("After surgery", x.PatientId, x.Status == CodingStatus.Rejected ? "Fix the coding" : "Code the event", x.Status == CodingStatus.Rejected ? x.HealthNzError : null, x.DischargeDate, new("Open", $"/coding/events/{x.Id}", Permissions.CodingCode), x.Status == CodingStatus.Rejected ? 2 : 5);
        }
        return [.. items.OrderBy(i => i.Urgency).ThenBy(i => i.Due ?? DateOnly.MaxValue).ThenBy(i => i.Patient, StringComparer.Ordinal)];
    }

    // ------------------------------------------------------------------ day board
    public static readonly string[] Columns = ["Coming in", "Admitted", "In theatre", "Recovery", "Gone home"];

    public async Task<IReadOnlyList<DayCard>> DayAsync(DateOnly date, CancellationToken ct = default)
    {
        if (!On("theatre")) return [];
        var rows = await (from b in theatre.Bookings.AsNoTracking()
                          join s in theatre.Sessions.AsNoTracking() on b.SessionId equals s.Id
                          where s.Date == date && !s.IsCancelled && !Gone.Contains(b.Status)
                          orderby s.StartTime, b.Order
                          select new { b.Id, b.PatientId, b.Procedure, b.Anaesthetic, b.Status, b.Order, s.StartTime, s.LocationId, s.SurgeonId }).ToListAsync(ct);
        var pids = rows.Select(r => r.PatientId).Distinct().ToList();
        var bids = rows.Select(r => r.Id).ToList();
        var pts = await patients.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).Select(p => new { p.Id, p.FamilyName, p.GivenNames, p.Nhi }).ToDictionaryAsync(p => p.Id, ct);
        var alerts = await patients.Alerts.AsNoTracking().Where(a => pids.Contains(a.PatientId) && a.IsActive).Select(a => new { a.PatientId, Text = a.Description }).ToListAsync(ct);
        var tri = On("triage") ? await triage.Assessments.AsNoTracking().Where(a => a.BookingId != null && bids.Contains(a.BookingId.Value) && a.Status != AssessmentStatus.Superseded).ToListAsync(ct) : [];
        var eps = On("emr") ? await emr.Episodes.AsNoTracking().Where(e => bids.Contains(e.BookingId)).ToListAsync(ct) : [];
        var locs = await theatre.Sessions.AsNoTracking().Where(s => s.Date == date).Select(s => new { s.Id, s.LocationId }).ToListAsync(ct);
        return [.. rows.Select(r =>
        {
            var p = pts.GetValueOrDefault(r.PatientId);
            var t = tri.Where(a => a.BookingId == r.Id).OrderByDescending(a => a.CreatedAt).FirstOrDefault();
            var ep = eps.FirstOrDefault(e => e.BookingId == r.Id);
            var col = r.Status switch { BookingStatus.Arrived => 1, BookingStatus.InTheatre => 2, BookingStatus.Recovery => 3, BookingStatus.Discharged => 4, _ => 0 };
            if (ep is not null) col = Math.Max(col, ep.Stage switch { EpisodeStage.Admitted => 1, EpisodeStage.InTheatre => 2, EpisodeStage.Recovery or EpisodeStage.ReadyForDischarge => 3, EpisodeStage.Discharged or EpisodeStage.Transferred => 4, _ => 0 });
            return new DayCard(r.Id, r.PatientId, p is null ? "?" : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p?.Nhi, r.Procedure, r.Anaesthetic.ToString(), "", r.StartTime, Columns[col], r.Status.ToString(),
                t is null ? null : Outcomes.Labels[t.FinalOutcome ?? t.SuggestedOutcome] + (t.Status == AssessmentStatus.Open ? " (not signed off)" : ""), t?.Precautions,
                alerts.Where(a => a.PatientId == r.PatientId).Select(a => a.Text).FirstOrDefault(), ep?.Id);
        })];
    }
}
