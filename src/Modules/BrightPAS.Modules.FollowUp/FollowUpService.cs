using System.Security.Cryptography;
using System.Text;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Quality;
using BrightPAS.Modules.Reminders;
using BrightPAS.Modules.Theatre;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Modules.FollowUp;

/// <summary>What BrightPAS knows about the operation, supplied by the clinical record, stock (implants) and triage. Implemented in the web app.</summary>
public sealed record OperationFacts(bool Implant, bool HighRisk, string? Surgeon, string? WoundClass, int? Asa, IReadOnlyDictionary<string, string> Risk);

public interface IOperationFacts
{
    Task<OperationFacts> ForBookingAsync(Guid bookingId, CancellationToken ct);
}

public sealed record StepEdit(ContactKind Kind, ContactMethod Method, int Days, int? DaysWithImplant, bool WorkingDays, bool OnlyIfHighRisk, string? AssignTo, string Label);
public sealed record PlanEdit(string Name, string? ProcedureContains, bool IsDefault, bool IsActive, int MaxAttempts, int SortOrder, string? Notes, IReadOnlyList<StepEdit> Steps);
public sealed record RecordContact(ContactStatus Outcome, string? How, IReadOnlyDictionary<string, string>? Answers, Escalation Escalation, string? EscalationNote, string? Notes);
public sealed record ContactRow(Guid Id, Guid PatientId, string Patient, string? Nhi, string? Phone, Guid BookingId, string Procedure, DateOnly OperationDate, string Kind, string Method, string Label,
    DateOnly DueOn, string Status, int Attempts, int MaxAttempts, string? AssignedTo, bool Concern, string? ConcernText, bool SuspectedSsi, string Escalation, bool EscalationOpen,
    string? DoneBy, DateTimeOffset? DoneAt, DateTimeOffset? LinkSentAt, DateTimeOffset? AnsweredAt);
public sealed record PublicCheckIn(string Status, string? GivenName, string Clinic, IReadOnlyList<Question> Questions);

/// <summary>
/// Post-operative follow-up (spec 7.26, ADR 0037): plans → contacts at discharge (NZ working days, DST-safe dates), call script, patient check-in links,
/// warning rules that alert the duty nurse, escalations tracked to closure, worklists and calendar.
/// </summary>
public sealed class FollowUpService(FollowUpDbContext db, TheatreDbContext theatre, PatientsDbContext patients, ReminderService reminders, QualityService quality,
    IEnumerable<IOperationFacts> facts, AuditWriter audit, TimeProvider clock)
{
    public static readonly TimeSpan LinkLifetimeMin = TimeSpan.FromDays(2);
    public const int MaxDobAttempts = 5;
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private DateOnly Today => NzTime.Today(clock);

    private async Task<OperationFacts> FactsAsync(Guid bookingId, CancellationToken ct)
    {
        foreach (var f in facts) return await f.ForBookingAsync(bookingId, ct);
        return new OperationFacts(false, false, null, null, null, new Dictionary<string, string>());
    }

    // ------------------------------------------------------------------ settings & plans
    public async Task<FollowUpSettings> SettingsAsync(CancellationToken ct = default) => await db.Settings.SingleOrDefaultAsync(ct) ?? new FollowUpSettings();

    public async Task<IReadOnlyList<string>> SaveSettingsAsync(FollowUpSettings input, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (input.LinkDays is < 2 or > 30) errors.Add("The check-in link must work for 2 to 30 days.");
        if (!string.IsNullOrWhiteSpace(input.DutyMobile) && (Addresses.ToE164(input.DutyMobile) is not { } m || !Addresses.IsNzMobile(m))) errors.Add("The duty nurse mobile must be an NZ mobile number.");
        if (!string.IsNullOrWhiteSpace(input.PublicUrl) && (!Uri.TryCreate(input.PublicUrl.Trim(), UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(u.Query)))
            errors.Add("The public web address must start with https:// (for example https://brightpas.tasmandaysurgery.co.nz).");
        if (errors.Count > 0) return errors;
        var s = await db.Settings.SingleAsync(ct);
        s.PublicUrl = string.IsNullOrWhiteSpace(input.PublicUrl) ? null : input.PublicUrl.Trim().TrimEnd('/');
        s.DutyMobile = string.IsNullOrWhiteSpace(input.DutyMobile) ? null : input.DutyMobile.Trim();
        s.SendCheckInLinks = input.SendCheckInLinks; s.LinkDays = input.LinkDays; s.GpLetter = input.GpLetter; s.StartFrom = input.StartFrom;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "FollowUpSettings", "1", reason: $"links {s.SendCheckInLinks} ({s.LinkDays} d), GP letter {s.GpLetter}, duty mobile {(s.DutyMobile is null ? "none" : "set")}, from {s.StartFrom}", ct: ct);
        return [];
    }

    public async Task<IReadOnlyList<Plan>> PlansAsync(CancellationToken ct = default) =>
        await db.Plans.AsNoTracking().Include(p => p.Steps).OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync(ct);

    /// <summary>Adds the example plans once (when there are none).</summary>
    public async Task<int> SeedExamplesAsync(string user, CancellationToken ct = default)
    {
        if (await db.Plans.AnyAsync(ct)) return 0;
        db.Plans.Add(FollowUpScript.ExamplePlan(user, clock.GetUtcNow()));
        db.Plans.Add(FollowUpScript.EndoscopyPlan(user, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "FollowUpPlan", "examples", reason: "Example follow-up plans added", ct: ct);
        return 2;
    }

    public async Task<(Plan? Plan, IReadOnlyList<string> Errors)> SavePlanAsync(Guid? id, PlanEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var name = (e.Name ?? "").Trim();
        if (name.Length is 0 or > 150) errors.Add("Give the plan a name (up to 150 characters).");
        if (e.MaxAttempts is < 1 or > 6) errors.Add("Attempts must be 1 to 6.");
        if (e.Steps is null || e.Steps.Count == 0) errors.Add("Add at least one contact.");
        if (e.Steps is { Count: > 12 }) errors.Add("A plan can have at most 12 contacts.");
        foreach (var (s, i) in (e.Steps ?? []).Select((s, i) => (s, i + 1)))
        {
            if (s.Days is < 0 or > 400 || s.DaysWithImplant is < 0 or > 400) errors.Add($"Contact {i}: days must be 0 to 400.");
            if (string.IsNullOrWhiteSpace(s.Label)) errors.Add($"Contact {i}: give it a label.");
            if (!Enum.IsDefined(s.Kind) || !Enum.IsDefined(s.Method)) errors.Add($"Contact {i}: choose the type and method.");
        }
        if (!e.IsDefault && string.IsNullOrWhiteSpace(e.ProcedureContains)) errors.Add("Say which procedures this plan is for, or make it the default plan.");
        if (await db.Plans.AnyAsync(p => p.Name == name && p.Id != id, ct)) errors.Add("Another plan has that name.");
        if (errors.Count > 0) return (null, errors);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (e.IsDefault) foreach (var other in await db.Plans.Where(p => p.IsDefault && p.Id != id).ToListAsync(ct)) other.IsDefault = false;
            await db.SaveChangesAsync(ct);
            Plan p;
            if (id is { } pid)
            {
                p = await db.Plans.Include(x => x.Steps).SingleOrDefaultAsync(x => x.Id == pid, ct) ?? throw new KeyNotFoundException("Plan not found.");
                db.Steps.RemoveRange(p.Steps);
                await db.SaveChangesAsync(ct);
                p.Steps = [];
            }
            else { p = new Plan(); db.Plans.Add(p); }
            (p.Name, p.ProcedureContains, p.IsDefault, p.IsActive, p.MaxAttempts, p.SortOrder, p.Notes, p.UpdatedBy, p.UpdatedAt) =
                (name, string.IsNullOrWhiteSpace(e.ProcedureContains) ? null : e.ProcedureContains.Trim(), e.IsDefault, e.IsActive, e.MaxAttempts, e.SortOrder, e.Notes?.Trim(), user, clock.GetUtcNow());
            var seq = 1;
            foreach (var s in e.Steps!)
                db.Steps.Add(new PlanStep { PlanId = p.Id, Sequence = seq++, Kind = s.Kind, Method = s.Method, Days = s.Days, DaysWithImplant = s.DaysWithImplant, WorkingDays = s.WorkingDays, OnlyIfHighRisk = s.OnlyIfHighRisk, AssignTo = s.AssignTo?.Trim(), Label = s.Label.Trim() });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await audit.WriteAsync(user, id is null ? "Create" : "Update", "FollowUpPlan", p.Id.ToString(), reason: $"{p.Name}: {string.Join("; ", e.Steps!.Select(x => $"{x.Label} d{x.Days}"))}", ct: ct);
            return ((Plan?)p, (IReadOnlyList<string>)[]);
        });
    }

    /// <summary>The plan for a procedure: the first active plan (by order) whose words appear in it, else the default plan.</summary>
    public static Plan? PlanFor(IReadOnlyList<Plan> plans, string procedure) =>
        plans.Where(p => p.IsActive && !string.IsNullOrWhiteSpace(p.ProcedureContains)).OrderBy(p => p.SortOrder)
            .FirstOrDefault(p => p.ProcedureContains!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(w => procedure.Contains(w, StringComparison.OrdinalIgnoreCase)))
        ?? plans.FirstOrDefault(p => p.IsActive && p.IsDefault);

    // ------------------------------------------------------------------ creating contacts
    /// <summary>Creates the follow-up contacts for a discharged booking (once). Returns how many were made.</summary>
    public async Task<int> CreateForBookingAsync(Guid bookingId, string user, CancellationToken ct = default)
    {
        if (await db.Contacts.AnyAsync(c => c.BookingId == bookingId, ct)) return 0;
        var b = await (from x in theatre.Bookings.AsNoTracking() join s in theatre.Sessions.AsNoTracking() on x.SessionId equals s.Id where x.Id == bookingId select new { x.PatientId, x.Procedure, s.Date }).SingleOrDefaultAsync(ct);
        if (b is null) return 0;
        var plan = PlanFor(await PlansAsync(ct), b.Procedure);
        if (plan is null) return 0;
        var f = await FactsAsync(bookingId, ct);
        var closed = await quality.ExtraDaysAsync(ct);
        var now = clock.GetUtcNow();
        var made = 0;
        foreach (var step in plan.Steps.OrderBy(s => s.Sequence).Where(s => !s.OnlyIfHighRisk || f.HighRisk))
        {
            db.Contacts.Add(new Contact
            {
                PatientId = b.PatientId,
                BookingId = bookingId,
                PlanId = plan.Id,
                Procedure = b.Procedure,
                OperationDate = b.Date,
                Kind = step.Kind,
                Method = step.Method,
                Label = step.Kind == ContactKind.SsiCheck && f.Implant && step.DaysWithImplant is not null ? step.Label + " – implant" : step.Label,
                DueOn = FollowUpScript.DueOn(b.Date, step, f.Implant, closed),
                MaxAttempts = plan.MaxAttempts,
                AssignedTo = step.AssignTo,
                CreatedAt = now,
                CreatedBy = user
            });
            made++;
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "FollowUpContacts", bookingId.ToString(), b.PatientId.ToString(), reason: $"{made} contact(s) from plan {plan.Name}{(f.Implant ? " (implant)" : "")}{(f.HighRisk ? " (high risk)" : "")}", ct: ct);
        return made;
    }

    /// <summary>Background job: contacts for newly discharged patients; check-in links for due patient-form contacts; unanswered links become phone calls.</summary>
    public async Task<(int Created, int Linked, int Converted, List<Guid> NewBookings)> ScanAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleAsync(ct);
        var today = Today;
        var since = s.StartFrom is { } sf && sf > today.AddDays(-30) ? sf : today.AddDays(-30);
        var discharged = await (from x in theatre.Bookings.AsNoTracking()
                                join ss in theatre.Sessions.AsNoTracking() on x.SessionId equals ss.Id
                                where x.Status == BookingStatus.Discharged && ss.Date >= since && ss.Date <= today
                                select x.Id).ToListAsync(ct);
        var have = await db.Contacts.Where(c => discharged.Contains(c.BookingId)).Select(c => c.BookingId).Distinct().ToListAsync(ct);
        var created = 0; var newBookings = new List<Guid>();
        foreach (var id in discharged.Except(have))
            if (await CreateForBookingAsync(id, "system", ct) > 0) { created++; newBookings.Add(id); }

        var linked = 0;
        if (s.SendCheckInLinks && s.PublicUrl is { } linkBase)
            foreach (var c in await db.Contacts.Where(c => c.Method == ContactMethod.PatientForm && c.Status == ContactStatus.Due && c.DueOn <= today && c.LinkSentAt == null).Take(200).ToListAsync(ct))
                if (await SendLinkAsync(c, s, linkBase, "system", ct)) linked++;

        // A link not answered in time becomes a phone call (the patient may not use text messages).
        var converted = 0;
        foreach (var c in await db.Contacts.Where(c => c.Method == ContactMethod.PatientForm && c.Status == ContactStatus.Due && c.LinkExpiresAt != null && c.LinkExpiresAt < clock.GetUtcNow() && c.AnsweredAt == null).Take(200).ToListAsync(ct))
        {
            c.Method = ContactMethod.Phone; c.TokenHash = null; c.DueOn = today; c.Label += " – no reply to the text, please phone";
            converted++;
        }
        s.LastScanAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (created, linked, converted, newBookings);
    }

    private async Task<bool> SendLinkAsync(Contact c, FollowUpSettings s, string linkBase, string user, CancellationToken ct)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        c.TokenHash = Hash(token); c.LinkSentAt = clock.GetUtcNow(); c.LinkExpiresAt = clock.GetUtcNow().AddDays(Math.Max(2, s.LinkDays)); c.FailedAttempts = 0;
        await db.SaveChangesAsync(ct);
        var sent = await reminders.QueueFollowUpLinkAsync(c.PatientId, c.BookingId, $"{linkBase.TrimEnd('/')}/c/{token}", $"followup:{c.Id}:{c.LinkSentAt:yyyyMMddHHmm}", user, ct);
        if (sent.All(m => m.Status == MessageStatus.Suppressed))
        {
            // No way to reach the patient by text or email: make it a phone call now.
            c.Method = ContactMethod.Phone; c.TokenHash = null; c.LinkExpiresAt = null; c.Label += " – no mobile or email, please phone";
            await db.SaveChangesAsync(ct);
            return false;
        }
        return true;
    }

    /// <summary>Staff send (or re-send) the check-in link now.</summary>
    public async Task<string?> SendLinkNowAsync(Guid id, string user, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(ct);
        if (settings.PublicUrl is not { } linkBase) return "Set the public web address in Follow-up settings first.";
        var c = await db.Contacts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "Contact not found.";
        if (c.Status is not (ContactStatus.Due or ContactStatus.NoAnswer)) return "This contact is finished.";
        c.Method = ContactMethod.PatientForm;
        return await SendLinkAsync(c, settings, linkBase, user, ct) ? null : "The patient has no mobile or email that can be used – phone them instead.";
    }

    // ------------------------------------------------------------------ worklists
    public async Task<IReadOnlyList<ContactRow>> WorklistAsync(string view, DateOnly? @from = null, DateOnly? to = null, Guid? patientId = null, CancellationToken ct = default)
    {
        var today = Today;
        IQueryable<Contact> q = db.Contacts.AsNoTracking();
        q = view switch
        {
            "today" => q.Where(c => (c.Status == ContactStatus.Due || c.Status == ContactStatus.NoAnswer) && c.DueOn == today),
            "overdue" => q.Where(c => (c.Status == ContactStatus.Due || c.Status == ContactStatus.NoAnswer) && c.DueOn < today),
            "upcoming" => q.Where(c => c.Status == ContactStatus.Due && c.DueOn > today && c.DueOn <= today.AddDays(14)),
            "concerns" => q.Where(c => c.Concern && c.EscalationClosedAt == null),
            "escalations" => q.Where(c => c.Escalation != Escalation.None && c.Escalation != Escalation.Advice && c.EscalationClosedAt == null),
            "unreachable" => q.Where(c => c.Status == ContactStatus.Unreachable && c.DoneAt >= clock.GetUtcNow().AddDays(-30)),
            "done" => q.Where(c => c.Status == ContactStatus.Done && c.DoneAt >= clock.GetUtcNow().AddDays(-14)),
            "calendar" => q.Where(c => c.DueOn >= (@from ?? today) && c.DueOn <= (to ?? today.AddDays(6)) && c.Status != ContactStatus.Cancelled),
            "patient" => q.Where(c => c.PatientId == patientId),
            _ => q.Where(c => false)
        };
        var rows = await q.OrderBy(c => c.DueOn).ThenBy(c => c.Kind).Take(500).ToListAsync(ct);
        var ids = rows.Select(r => r.PatientId).Distinct().ToList();
        var pts = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.FamilyName, p.GivenNames, p.Nhi, p.MobilePhone, p.HomePhone }).ToDictionaryAsync(p => p.Id, ct);
        return [.. rows.Select(c =>
        {
            var p = pts.GetValueOrDefault(c.PatientId);
            return new ContactRow(c.Id, c.PatientId, p is null ? "?" : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p?.Nhi, p?.MobilePhone ?? p?.HomePhone, c.BookingId, c.Procedure, c.OperationDate,
                c.Kind.ToString(), c.Method.ToString(), c.Label, c.DueOn, c.Status.ToString(), c.Attempts, c.MaxAttempts, c.AssignedTo, c.Concern, c.ConcernText, c.SuspectedSsi,
                c.Escalation.ToString(), c.Escalation is not (Escalation.None or Escalation.Advice) && c.EscalationClosedAt is null, c.DoneBy, c.DoneAt, c.LinkSentAt, c.AnsweredAt);
        })];
    }

    public async Task<Contact?> GetAsync(Guid id, string user, string? workstation, CancellationToken ct = default)
    {
        var c = await db.Contacts.AsNoTracking().Include(x => x.Answers).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is not null) await audit.WriteAsync(user, "View", "FollowUpContact", id.ToString(), c.PatientId.ToString(), workstation: workstation, ct: ct);
        return c;
    }

    // ------------------------------------------------------------------ recording
    /// <summary>Records a contact: done (with script answers and escalation), no answer (try again, up to the plan's attempts), or unreachable.</summary>
    public async Task<(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, bool SuspectedSsi, string Signs)> RecordAsync(Guid id, RecordContact r, string user, CancellationToken ct = default)
    {
        var c = await db.Contacts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return (["Contact not found."], [], false, "");
        if (c.Status is ContactStatus.Done or ContactStatus.Cancelled) return (["This contact is already finished."], [], false, "");
        if (!Enum.IsDefined(r.Escalation)) return (["Choose an escalation."], [], false, "");
        var now = clock.GetUtcNow();
        if (r.Outcome == ContactStatus.NoAnswer)
        {
            c.Attempts++;
            if (c.Attempts >= c.MaxAttempts)
            {
                c.Status = ContactStatus.Unreachable; c.DoneAt = now; c.DoneBy = user; c.Notes = Clip(r.Notes, 4000);
                await db.SaveChangesAsync(ct);
                await audit.WriteAsync(user, "Record", "FollowUpContact", id.ToString(), c.PatientId.ToString(), reason: $"Unreachable after {c.Attempts} attempts", ct: ct);
                return ([], [$"No answer after {c.Attempts} attempts – marked unreachable. Send the check-in text or a letter."], false, "");
            }
            c.Status = ContactStatus.NoAnswer; c.DueOn = FollowUpScript.AddClinicDays(Today, 1, await quality.ExtraDaysAsync(ct));
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(user, "Record", "FollowUpContact", id.ToString(), c.PatientId.ToString(), reason: $"No answer (attempt {c.Attempts} of {c.MaxAttempts}); next try {c.DueOn:yyyy-MM-dd}", ct: ct);
            return ([], [$"No answer – try again on {c.DueOn:d MMM}."], false, "");
        }
        if (r.Outcome != ContactStatus.Done) return (["Choose done or no answer."], [], false, "");
        if (r.Escalation is not (Escalation.None or Escalation.Advice) && string.IsNullOrWhiteSpace(r.EscalationNote)) return (["Say what was arranged for the escalation."], [], false, "");
        var answers = FollowUpScript.Clean(r.Answers);
        var warnings = FollowUpScript.Warnings(answers);
        var (ssi, signs) = FollowUpScript.Wound(answers);
        SetAnswers(c, answers);
        c.Status = ContactStatus.Done; c.DoneAt = now; c.DoneBy = user; c.DoneHow = Clip(r.How, 40) ?? c.Method.ToString(); c.Attempts++;
        c.Escalation = r.Escalation; c.EscalationNote = Clip(r.EscalationNote, 2000); c.Notes = Clip(r.Notes, 4000);
        c.Concern = warnings.Count > 0; c.ConcernText = warnings.Count > 0 ? Clip(string.Join("; ", warnings), 1000) : null;
        c.SuspectedSsi = ssi;
        // Staff answered the concern on this call: the concern is dealt with unless it was escalated.
        if (c.Concern && r.Escalation is Escalation.None or Escalation.Advice) { c.EscalationClosedAt = now; c.EscalationClosedBy = user; c.EscalationOutcome = "Dealt with on the call"; }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Record", "FollowUpContact", id.ToString(), c.PatientId.ToString(), reason: $"Done{(c.Concern ? " – concern: " + c.ConcernText : "")}{(r.Escalation != Escalation.None ? " – " + r.Escalation : "")}{(ssi ? " – suspected SSI" : "")}", ct: ct);
        return ([], warnings, ssi, signs);
    }

    public async Task<string?> RescheduleAsync(Guid id, DateOnly due, string? reason, string user, CancellationToken ct = default)
    {
        var c = await db.Contacts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "Contact not found.";
        if (c.Status is not (ContactStatus.Due or ContactStatus.NoAnswer)) return "Only contacts still to do can be moved.";
        if (due < Today || due > Today.AddDays(400)) return "Choose a date from today.";
        var old = c.DueOn; c.DueOn = due;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Reschedule", "FollowUpContact", id.ToString(), c.PatientId.ToString(), reason: $"{old:yyyy-MM-dd} → {due:yyyy-MM-dd} {reason}", ct: ct);
        return null;
    }

    public async Task<string?> AssignAsync(Guid id, string? to, string user, CancellationToken ct = default)
    {
        var c = await db.Contacts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "Contact not found.";
        c.AssignedTo = string.IsNullOrWhiteSpace(to) ? null : Clip(to.Trim(), 100);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Assign", "FollowUpContact", id.ToString(), c.PatientId.ToString(), reason: c.AssignedTo ?? "unassigned", ct: ct);
        return null;
    }

    public async Task<string?> CancelAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var c = await db.Contacts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "Contact not found.";
        if (string.IsNullOrWhiteSpace(reason)) return "Give a reason.";
        if (c.Status is not (ContactStatus.Due or ContactStatus.NoAnswer)) return "Only contacts still to do can be cancelled.";
        c.Status = ContactStatus.Cancelled; c.Notes = Clip(reason, 4000); c.TokenHash = null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Cancel", "FollowUpContact", id.ToString(), c.PatientId.ToString(), reason: reason, ct: ct);
        return null;
    }

    /// <summary>Closes an escalation or a patient-reported concern with what happened.</summary>
    public async Task<string?> CloseEscalationAsync(Guid id, string outcome, string user, CancellationToken ct = default)
    {
        var c = await db.Contacts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "Contact not found.";
        if (string.IsNullOrWhiteSpace(outcome)) return "Say how it ended.";
        if (c.EscalationClosedAt is not null) return "Already closed.";
        c.EscalationClosedAt = clock.GetUtcNow(); c.EscalationClosedBy = user; c.EscalationOutcome = Clip(outcome, 2000);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "CloseEscalation", "FollowUpContact", id.ToString(), c.PatientId.ToString(), reason: outcome, ct: ct);
        return null;
    }

    /// <summary>Raises a Quality incident from a follow-up (unplanned return, ED, complication). The incident is then handled in Quality.</summary>
    public async Task<(Guid? IncidentId, IReadOnlyList<string> Errors)> RaiseIncidentAsync(Guid id, string description, Harm harm, string user, CancellationToken ct = default)
    {
        var c = await db.Contacts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return (null, ["Contact not found."]);
        if (c.IncidentId is not null) return (c.IncidentId, ["An incident has already been raised."]);
        var (i, errors) = await quality.ReportAsync(new IncidentReport(c.DoneAt ?? clock.GetUtcNow(), "Post-operative follow-up", IncidentCategory.ClinicalCare,
            description, c.EscalationNote, c.PatientId, c.BookingId, null, null, harm, false), user, ct);
        if (i is null) return (null, errors);
        c.IncidentId = i.Id;
        await db.SaveChangesAsync(ct);
        return (i.Id, []);
    }

    // ------------------------------------------------------------------ patient check-in (public link)
    private async Task<Contact?> ByToken(string token, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(token) || token.Length > 100 ? null : await db.Contacts.SingleOrDefaultAsync(c => c.TokenHash == Hash(token), ct);

    public async Task<PublicCheckIn?> PublicInfoAsync(string token, CancellationToken ct = default)
    {
        var c = await ByToken(token, ct);
        if (c is null) return null;
        var status = c.AnsweredAt is not null ? "submitted" : c.Status is ContactStatus.Due or ContactStatus.NoAnswer && c.LinkExpiresAt > clock.GetUtcNow() ? "open" : "closed";
        return new PublicCheckIn(status, null, "Tasman Day Surgery", status == "open" ? [.. FollowUpScript.Questions.Where(q => q.PatientForm)] : []);
    }

    public async Task<(bool Ok, string? Error, string? GivenName)> VerifyAsync(string token, DateOnly dob, CancellationToken ct = default)
    {
        var c = await ByToken(token, ct);
        var now = clock.GetUtcNow();
        if (c is null || c.AnsweredAt is not null || c.Status is not (ContactStatus.Due or ContactStatus.NoAnswer) || c.LinkExpiresAt < now) return (false, "This link is no longer active. Please phone us if you have any concerns.", null);
        if (c.LockedUntil > now) return (false, "Too many attempts. Please try again in 30 minutes or phone us.", null);
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == c.PatientId, ct);
        if (p.DateOfBirth != dob)
        {
            c.FailedAttempts++;
            if (c.FailedAttempts >= MaxDobAttempts) { c.LockedUntil = now.AddMinutes(30); c.FailedAttempts = 0; }
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync("patient-link", "CheckInVerifyFailed", "FollowUpContact", c.Id.ToString(), c.PatientId.ToString(), ct: ct);
            return (false, "That date of birth does not match our records.", null);
        }
        c.FailedAttempts = 0;
        await db.SaveChangesAsync(ct);
        return (true, null, p.PreferredName ?? p.GivenNames.Split(' ')[0]);
    }

    /// <summary>The patient's own answers. A warning answer creates a call-back today and alerts the duty nurse.</summary>
    public async Task<(bool Ok, IReadOnlyList<string> Errors, bool Concern)> SubmitAsync(string token, DateOnly dob, IReadOnlyDictionary<string, string> answers, CancellationToken ct = default)
    {
        var (ok, error, _) = await VerifyAsync(token, dob, ct);
        if (!ok) return (false, [error!], false);
        var c = (await ByToken(token, ct))!;
        var clean = FollowUpScript.Clean(answers);
        if (clean.Count < 3) return (false, ["Please answer the questions."], false);
        var warnings = FollowUpScript.Warnings(clean);
        var (ssi, _) = FollowUpScript.Wound(clean);
        var now = clock.GetUtcNow();
        SetAnswers(c, clean); c.AnsweredAt = now; c.Status = ContactStatus.Done; c.DoneAt = now; c.DoneBy = "patient"; c.DoneHow = "Patient check-in";
        c.Concern = warnings.Count > 0; c.ConcernText = warnings.Count > 0 ? Clip(string.Join("; ", warnings), 1000) : null; c.SuspectedSsi = ssi; c.TokenHash = null;
        if (c.Concern)
            db.Contacts.Add(new Contact
            {
                PatientId = c.PatientId,
                BookingId = c.BookingId,
                PlanId = c.PlanId,
                Procedure = c.Procedure,
                OperationDate = c.OperationDate,
                Kind = ContactKind.Call,
                Method = ContactMethod.Phone,
                Label = "Call back – the patient reported a concern",
                DueOn = Today,
                MaxAttempts = c.MaxAttempts,
                Concern = true,
                ConcernText = c.ConcernText,
                SuspectedSsi = ssi,
                CreatedAt = now,
                CreatedBy = "patient"
            });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("patient-link", "CheckInSubmitted", "FollowUpContact", c.Id.ToString(), c.PatientId.ToString(), reason: c.Concern ? "Concern: " + c.ConcernText : "No concerns", ct: ct);
        if (c.Concern && (await SettingsAsync(ct)).DutyMobile is { } duty)
            await reminders.QueueStaffAlertAsync(duty, "BrightPAS: a patient's follow-up check-in needs a call back today. Open Follow-up › Concerns.", "system", ct);
        return (true, [], c.Concern);
    }

    // ------------------------------------------------------------------ report
    public sealed record FollowUpReport(int Operations, int WithFollowUp, int Contacts, int Done, int Unreachable, int Concerns, int Escalations, IReadOnlyDictionary<string, int> EscalationTypes,
        double CompletionPercent, int Ssi, int SsiConfirmed, IReadOnlyList<SsiRate> Rates, int Returns, double? MeanSatisfaction);
    public sealed record SsiRate(string Group, string Name, int Operations, int Ssi, double Percent);

    public async Task<FollowUpReport> ReportAsync(DateOnly @from, DateOnly to, CancellationToken ct = default)
    {
        var contacts = await db.Contacts.AsNoTracking().Include(c => c.Answers).Where(c => c.OperationDate >= @from && c.OperationDate <= to).ToListAsync(ct);
        var ops = await (from x in theatre.Bookings.AsNoTracking()
                         join s in theatre.Sessions.AsNoTracking() on x.SessionId equals s.Id
                         where x.Status == BookingStatus.Discharged && s.Date >= @from && s.Date <= to
                         select new { x.Id, x.Procedure }).ToListAsync(ct);
        var ssi = await db.SsiCases.AsNoTracking().Where(c => c.OperationDate >= @from && c.OperationDate <= to && c.Class != SsiClass.NotSsi).ToListAsync(ct);
        var due = contacts.Where(c => c.DueOn <= Today && c.Status != ContactStatus.Cancelled).ToList();
        var done = due.Count(c => c.Status == ContactStatus.Done);
        // SSI rate denominator: operations under surveillance (an SSI check was planned).
        var surveyed = contacts.Where(c => c.Kind == ContactKind.SsiCheck).Select(c => c.BookingId).ToHashSet();
        var opsSurveyed = ops.Where(o => surveyed.Contains(o.Id)).ToList();
        var rates = opsSurveyed.GroupBy(o => o.Procedure).Select(g => { var total = g.Count(); var n = ssi.Count(s => g.Any(o => o.Id == s.BookingId) && s.Class != SsiClass.Suspected); return new SsiRate("Procedure", g.Key, total, n, Math.Round(100.0 * n / total, 1)); })
            .Concat(ssi.Where(s => s.Surgeon is not null).GroupBy(s => s.Surgeon!).Select(g => new SsiRate("Surgeon", g.Key, 0, g.Count(s => s.Class != SsiClass.Suspected), 0)))
            .OrderBy(r => r.Group, StringComparer.Ordinal).ThenByDescending(r => r.Ssi).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
        var sat = contacts.Select(c => c.Answers.FirstOrDefault(x => x.QuestionKey == "satisfaction")?.Value)
            .Select(v => int.TryParse(v, out var n) ? n : (int?)null).Where(v => v is not null).Select(v => v!.Value).ToList();
        return new FollowUpReport(ops.Count, contacts.Select(c => c.BookingId).Distinct().Count(), due.Count, done, due.Count(c => c.Status == ContactStatus.Unreachable),
            contacts.Count(c => c.Concern), contacts.Count(c => c.Escalation is not (Escalation.None or Escalation.Advice)),
            contacts.Where(c => c.Escalation != Escalation.None).GroupBy(c => FollowUpScript.EscalationLabels[c.Escalation]).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            due.Count == 0 ? 0 : Math.Round(100.0 * done / due.Count, 1), ssi.Count, ssi.Count(s => s.Class != SsiClass.Suspected), rates,
            contacts.Count(c => c.Escalation is Escalation.ReturnToTds or Escalation.EmergencyDepartment), sat.Count == 0 ? null : Math.Round(sat.Average(), 1));
    }

    private void SetAnswers(Contact c, Dictionary<string, string> answers)
    {
        db.Answers.RemoveRange(db.Answers.Where(a => a.ContactId == c.Id));
        foreach (var (k, v) in answers) db.Answers.Add(new ContactAnswer { ContactId = c.Id, QuestionKey = k, Value = v });
    }

    private static string? Clip(string? s, int n) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > n ? s.Trim()[..n] : s.Trim();
}
