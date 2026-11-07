using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Referrals;

public sealed record NewReferral(Guid PatientId, DateOnly ReceivedOn, ReferralSource Source, string ReferrerName, string? ReferrerOrganisation, string? ReferrerHpi,
    string Specialist, ServiceType Service, Funder Funder, string? FunderReference, string Reason);

/// <summary>Accept (add to the waitlist) or decline a referral.</summary>
public sealed record TriageDecision(bool Accept, string? Notes, string? DeclineReason, string? Procedure, Laterality Laterality, AnaestheticType Anaesthetic,
    int DurationMinutes, Priority Priority, DateOnly? DecisionDate);

public sealed record NewWaitlistEntry(Guid PatientId, string Procedure, Laterality Laterality, AnaestheticType Anaesthetic, int DurationMinutes, ServiceType Service,
    string Specialist, Funder Funder, Priority Priority, DateOnly? AddedOn, string? Notes);

public sealed record WaitlistUpdate(string Procedure, Laterality Laterality, AnaestheticType Anaesthetic, int DurationMinutes, string Specialist, Priority Priority, string? Notes);

public sealed record ReferralRow(Guid Id, string Number, Guid PatientId, string PatientName, string Mrn, string? Nhi, DateOnly DateOfBirth, DateOnly ReceivedOn,
    string Source, string ReferrerName, string? ReferrerOrganisation, string? ReferrerHpi, string Specialist, string Service, string Funder, string? FunderReference,
    string Reason, string Status, DateTimeOffset? TriagedAt, string? TriagedBy, string? TriageNotes, string? DeclineReason, int DaysSinceReceived, Guid? WaitlistEntryId);

public sealed record WaitlistRow(Guid Id, Guid? ReferralId, string? ReferralNumber, Guid PatientId, string PatientName, string Mrn, string? Nhi, DateOnly DateOfBirth,
    string Procedure, string Laterality, string Anaesthetic, int DurationMinutes, string Service, string Specialist, string Funder, string Priority,
    DateOnly AddedOn, DateOnly TargetDate, int DaysWaiting, int DaysToTarget, bool Overdue, string Status, string? HoldReason, DateOnly? HoldUntil,
    Guid? BookingId, DateOnly? BookedFor, string? RemovedReason, string? Notes, bool PubliclyFunded);

public sealed class ReferralService(ReferralsDbContext db, PatientsDbContext patients, TheatreService theatre, TheatreDbContext theatreDb, AuditWriter audit, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<ReferralSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is not null) return s;
        s = new ReferralSettings();
        db.Settings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<IReadOnlyList<string>> UpdateSettingsAsync(int urgentDays, int semiUrgentDays, int routineDays, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (urgentDays is < 1 or > 365 || semiUrgentDays is < 1 or > 365 || routineDays is < 1 or > 730) errors.Add("Target days must be between 1 and 365 (routine up to 730).");
        else if (!(urgentDays <= semiUrgentDays && semiUrgentDays <= routineDays)) errors.Add("Targets must get longer from urgent to semi-urgent to routine.");
        if (errors.Count > 0) return errors;
        var s = await SettingsAsync(ct);
        s.UrgentDays = urgentDays; s.SemiUrgentDays = semiUrgentDays; s.RoutineDays = routineDays;
        s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "ReferralSettings", "1", reason: $"Targets: urgent {urgentDays}, semi-urgent {semiUrgentDays}, routine {routineDays} days", ct: ct);
        return errors;
    }

    public static int TargetDays(ReferralSettings s, Priority p) => p switch { Priority.Urgent => s.UrgentDays, Priority.SemiUrgent => s.SemiUrgentDays, _ => s.RoutineDays };

    // ---------------------------------------------------------------- referrals

    public async Task<(Referral? Referral, IReadOnlyList<string> Errors)> CreateReferralAsync(NewReferral r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!await patients.Patients.AnyAsync(p => p.Id == r.PatientId, ct)) errors.Add("Choose the patient this referral is for.");
        if (string.IsNullOrWhiteSpace(r.ReferrerName)) errors.Add("Enter who referred the patient.");
        if (string.IsNullOrWhiteSpace(r.Specialist)) errors.Add("Enter the specialist the patient is referred to.");
        if (string.IsNullOrWhiteSpace(r.Reason)) errors.Add("Enter the reason for referral.");
        if (r.ReceivedOn > Today) errors.Add("The date received cannot be in the future.");
        if (r.ReceivedOn < Today.AddYears(-2)) errors.Add("The date received is more than two years ago – check the date.");
        if (r.ReferrerHpi is { Length: > 0 } hpi && !System.Text.RegularExpressions.Regex.IsMatch(hpi.Trim().ToUpperInvariant(), "^[0-9]{2}[A-Z]{3}[0-9]{3}[A-Z]?$|^F[0-9A-Z]{5}-[A-Z]$|^[A-Z0-9]{6,8}$"))
            errors.Add("The referrer's HPI number does not look right (e.g. 12ABC345 for a practitioner).");
        if (errors.Count > 0) return (null, errors);

        var settings = await SettingsAsync(ct);
        var referral = new Referral
        {
            Number = $"R{settings.NextReferralNumber:000000}", PatientId = r.PatientId, ReceivedOn = r.ReceivedOn, Source = r.Source, ReferrerName = r.ReferrerName.Trim(),
            ReferrerOrganisation = Blank(r.ReferrerOrganisation), ReferrerHpi = Blank(r.ReferrerHpi)?.ToUpperInvariant(), Specialist = r.Specialist.Trim(), Service = r.Service,
            Funder = r.Funder, FunderReference = Blank(r.FunderReference), Reason = r.Reason.Trim(), CreatedAt = clock.GetUtcNow(), CreatedBy = user
        };
        settings.NextReferralNumber++;
        db.Referrals.Add(referral);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "Referral", referral.Id.ToString(), r.PatientId.ToString(),
            reason: $"{referral.Number} from {referral.ReferrerName} to {referral.Specialist} ({referral.Funder})", ct: ct);
        return (referral, errors);
    }

    public async Task<(WaitlistEntry? Entry, IReadOnlyList<string> Errors)> TriageAsync(Guid id, TriageDecision d, string user, CancellationToken ct = default)
    {
        var r = await db.Referrals.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return (null, ["Referral not found."]);
        if (r.Status != ReferralStatus.Received) return (null, [$"This referral has already been {r.Status.ToString().ToLowerInvariant()}."]);
        var now = clock.GetUtcNow();
        if (!d.Accept)
        {
            if (string.IsNullOrWhiteSpace(d.DeclineReason)) return (null, ["Enter the reason for declining, so the referrer can be told."]);
            r.Status = ReferralStatus.Declined; r.DeclineReason = d.DeclineReason.Trim(); r.TriageNotes = Blank(d.Notes); r.TriagedAt = now; r.TriagedBy = user;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(user, "Decline", "Referral", r.Id.ToString(), r.PatientId.ToString(), reason: r.DeclineReason, ct: ct);
            return (null, []);
        }
        var (entry, errors) = await AddToWaitlistAsync(new NewWaitlistEntry(r.PatientId, d.Procedure ?? "", d.Laterality, d.Anaesthetic, d.DurationMinutes, r.Service,
            r.Specialist, r.Funder, d.Priority, d.DecisionDate, Blank(d.Notes)), user, r.Id, ct);
        if (entry is null) return (null, errors);
        r.Status = ReferralStatus.Accepted; r.TriageNotes = Blank(d.Notes); r.TriagedAt = now; r.TriagedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Accept", "Referral", r.Id.ToString(), r.PatientId.ToString(), reason: $"Waitlisted: {entry.Procedure} ({entry.Priority}, target {entry.TargetDate:yyyy-MM-dd})", ct: ct);
        return (entry, []);
    }

    public async Task<string?> WithdrawReferralAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var r = await db.Referrals.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return "Referral not found.";
        if (r.Status != ReferralStatus.Received) return "Only referrals waiting for triage can be withdrawn. Remove the waitlist entry instead.";
        if (string.IsNullOrWhiteSpace(reason)) return "Enter the reason for withdrawing.";
        r.Status = ReferralStatus.Withdrawn; r.DeclineReason = reason.Trim();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Withdraw", "Referral", r.Id.ToString(), r.PatientId.ToString(), reason: reason, ct: ct);
        return null;
    }

    public async Task<IReadOnlyList<ReferralRow>> ReferralsAsync(string view, Guid? patientId, CancellationToken ct = default)
    {
        var q = db.Referrals.AsNoTracking().AsQueryable();
        if (patientId is { } pid) q = q.Where(r => r.PatientId == pid);
        else if (view == "to-triage") q = q.Where(r => r.Status == ReferralStatus.Received);
        else if (view == "recent") q = q.Where(r => r.ReceivedOn >= Today.AddDays(-90));
        var list = await q.OrderBy(r => r.Status == ReferralStatus.Received ? 0 : 1).ThenBy(r => r.ReceivedOn).Take(500).ToListAsync(ct);
        var ids = list.Select(r => r.Id).ToList();
        var entries = await db.Waitlist.AsNoTracking().Where(w => w.ReferralId != null && ids.Contains(w.ReferralId.Value)).Select(w => new { w.ReferralId, w.Id }).ToListAsync(ct);
        var people = await People(list.Select(r => r.PatientId), ct);
        return list.Select(r =>
        {
            var p = people.GetValueOrDefault(r.PatientId);
            return new ReferralRow(r.Id, r.Number, r.PatientId, Name(p), p?.Mrn ?? "", p?.Nhi, p?.DateOfBirth ?? default, r.ReceivedOn, r.Source.ToString(), r.ReferrerName,
                r.ReferrerOrganisation, r.ReferrerHpi, r.Specialist, r.Service.ToString(), r.Funder.ToString(), r.FunderReference, r.Reason, r.Status.ToString(), r.TriagedAt,
                r.TriagedBy, r.TriageNotes, r.DeclineReason, Today.DayNumber - r.ReceivedOn.DayNumber, entries.FirstOrDefault(e => e.ReferralId == r.Id)?.Id);
        }).ToList();
    }

    public async Task<int> ToTriageCountAsync(CancellationToken ct = default) => await db.Referrals.CountAsync(r => r.Status == ReferralStatus.Received, ct);

    // ---------------------------------------------------------------- waitlist

    public async Task<(WaitlistEntry? Entry, IReadOnlyList<string> Errors)> AddToWaitlistAsync(NewWaitlistEntry n, string user, Guid? referralId = null, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!await patients.Patients.AnyAsync(p => p.Id == n.PatientId, ct)) errors.Add("Choose the patient.");
        if (string.IsNullOrWhiteSpace(n.Procedure)) errors.Add("Enter the planned procedure.");
        if (string.IsNullOrWhiteSpace(n.Specialist)) errors.Add("Enter the specialist.");
        if (n.DurationMinutes is < 5 or > 720) errors.Add("Enter an expected duration between 5 and 720 minutes.");
        var added = n.AddedOn ?? Today;
        if (added > Today) errors.Add("The decision-to-treat date cannot be in the future.");
        if (errors.Count > 0) return (null, errors);
        if (await db.Waitlist.AnyAsync(w => w.PatientId == n.PatientId && w.Procedure == n.Procedure.Trim() && w.Laterality == n.Laterality
                && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.OnHold || w.Status == WaitlistStatus.Booked), ct))
            return (null, ["This patient is already on the waitlist for this procedure."]);

        var settings = await SettingsAsync(ct);
        var now = clock.GetUtcNow();
        var e = new WaitlistEntry
        {
            ReferralId = referralId, PatientId = n.PatientId, Procedure = n.Procedure.Trim(), Laterality = n.Laterality, Anaesthetic = n.Anaesthetic, DurationMinutes = n.DurationMinutes,
            Service = n.Service, Specialist = n.Specialist.Trim(), Funder = n.Funder, Priority = n.Priority, AddedOn = added, TargetDate = added.AddDays(TargetDays(settings, n.Priority)),
            Notes = Blank(n.Notes), StatusAt = now, CreatedAt = now, CreatedBy = user
        };
        db.Waitlist.Add(e);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "WaitlistEntry", e.Id.ToString(), e.PatientId.ToString(),
            reason: $"{e.Procedure} with {e.Specialist}; {e.Priority}; target {e.TargetDate:yyyy-MM-dd}", ct: ct);
        return (e, []);
    }

    public async Task<string?> UpdateEntryAsync(Guid id, WaitlistUpdate u, string user, CancellationToken ct = default)
    {
        var e = await db.Waitlist.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return "Waitlist entry not found.";
        if (e.Status is WaitlistStatus.Completed or WaitlistStatus.Removed) return "This entry is closed.";
        if (string.IsNullOrWhiteSpace(u.Procedure) || string.IsNullOrWhiteSpace(u.Specialist)) return "Enter the procedure and specialist.";
        if (u.DurationMinutes is < 5 or > 720) return "Enter an expected duration between 5 and 720 minutes.";
        var changes = new List<string>();
        if (e.Priority != u.Priority)
        {
            var s = await SettingsAsync(ct);
            changes.Add($"priority {e.Priority} -> {u.Priority}");
            e.Priority = u.Priority;
            e.TargetDate = e.AddedOn.AddDays(TargetDays(s, u.Priority));
        }
        if (e.Procedure != u.Procedure.Trim()) changes.Add($"procedure -> {u.Procedure.Trim()}");
        if (e.Specialist != u.Specialist.Trim()) changes.Add($"specialist -> {u.Specialist.Trim()}");
        e.Procedure = u.Procedure.Trim(); e.Laterality = u.Laterality; e.Anaesthetic = u.Anaesthetic; e.DurationMinutes = u.DurationMinutes;
        e.Specialist = u.Specialist.Trim(); e.Notes = Blank(u.Notes);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "WaitlistEntry", e.Id.ToString(), e.PatientId.ToString(), reason: changes.Count > 0 ? string.Join("; ", changes) : "details", ct: ct);
        return null;
    }

    public async Task<string?> HoldAsync(Guid id, bool hold, string? reason, DateOnly? until, string user, CancellationToken ct = default)
    {
        var e = await db.Waitlist.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return "Waitlist entry not found.";
        if (hold)
        {
            if (e.Status != WaitlistStatus.Waiting) return "Only waiting patients can be put on hold.";
            if (string.IsNullOrWhiteSpace(reason)) return "Enter why the patient is on hold (e.g. not ready for surgery, unavailable).";
            if (until is { } d && d <= Today) return "The 'on hold until' date must be in the future.";
            e.Status = WaitlistStatus.OnHold; e.HoldReason = reason.Trim(); e.HoldUntil = until;
        }
        else
        {
            if (e.Status != WaitlistStatus.OnHold) return "This patient is not on hold.";
            e.Status = WaitlistStatus.Waiting; e.HoldReason = null; e.HoldUntil = null;
        }
        e.StatusAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, hold ? "Hold" : "Resume", "WaitlistEntry", e.Id.ToString(), e.PatientId.ToString(), reason: hold ? $"{reason}{(until is null ? "" : $" until {until:yyyy-MM-dd}")}" : null, ct: ct);
        return null;
    }

    public async Task<string?> RemoveAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var e = await db.Waitlist.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return "Waitlist entry not found.";
        if (e.Status is WaitlistStatus.Completed or WaitlistStatus.Removed) return "This entry is already closed.";
        if (e.Status == WaitlistStatus.Booked) return "Cancel the theatre booking first (Theatre lists), then remove the patient from the waitlist.";
        if (string.IsNullOrWhiteSpace(reason)) return "Enter the reason for removing the patient (e.g. treated elsewhere, no longer wants surgery).";
        e.Status = WaitlistStatus.Removed; e.RemovedReason = reason.Trim(); e.StatusAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Remove", "WaitlistEntry", e.Id.ToString(), e.PatientId.ToString(), reason: reason, ct: ct);
        return null;
    }

    /// <summary>Books the waitlisted procedure onto a theatre list. Theatre's own checks (overrun, same-day bookings) apply; warnings must be accepted.</summary>
    public async Task<BookingResult> BookAsync(Guid id, Guid sessionId, bool acceptWarnings, string user, CancellationToken ct = default)
    {
        var e = await db.Waitlist.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return new(null, ["Waitlist entry not found."], []);
        if (e.Status != WaitlistStatus.Waiting)
            return new(null, [e.Status == WaitlistStatus.OnHold ? "This patient is on hold. Resume them first." : $"This entry is {e.Status.ToString().ToLowerInvariant()}, not waiting."], []);
        var warnings = new List<string>();
        var session = await theatreDb.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not null && !session.Surgeon.Contains(e.Specialist, StringComparison.OrdinalIgnoreCase) && !e.Specialist.Contains(session.Surgeon, StringComparison.OrdinalIgnoreCase))
            warnings.Add($"This list is for {session.Surgeon}, but the patient is waiting for {e.Specialist}.");
        if (session is not null && session.Date > e.TargetDate) warnings.Add($"This date is after the patient's target date ({e.TargetDate:d MMM yyyy}).");
        if (warnings.Count > 0 && !acceptWarnings)
        {
            // Also show Theatre's own warnings in the same round trip.
            var dry = await theatre.BookAsync(new NewBooking(sessionId, e.PatientId, e.Procedure, e.Laterality, e.DurationMinutes, e.Anaesthetic, e.Notes, AcceptWarnings: false), user, ct, validateOnly: true);
            return new(null, dry.Errors, [.. warnings, .. dry.Warnings]);
        }
        var r = await theatre.BookAsync(new NewBooking(sessionId, e.PatientId, e.Procedure, e.Laterality, e.DurationMinutes, e.Anaesthetic,
            $"From waitlist{(e.Notes is null ? "" : ": " + e.Notes)}", acceptWarnings), user, ct);
        if (r.Booking is null) return new(null, r.Errors, [.. warnings, .. r.Warnings]);
        e.Status = WaitlistStatus.Booked; e.BookingId = r.Booking.Id; e.BookedFor = session?.Date; e.StatusAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Book", "WaitlistEntry", e.Id.ToString(), e.PatientId.ToString(),
            reason: $"Booked {session?.LocationName} {session?.Date:yyyy-MM-dd} (booking {r.Booking.Id}){(warnings.Count > 0 ? "; warnings accepted: " + string.Join("; ", warnings) : "")}", ct: ct);
        return new(r.Booking, [], [.. warnings, .. r.Warnings]);
    }

    /// <summary>
    /// Keeps the waitlist in step with Theatre: a cancelled or did-not-attend booking puts the patient back on the waitlist;
    /// a discharged booking completes the entry. Safe to run often.
    /// </summary>
    public async Task<int> SyncWithTheatreAsync(string user = "system", CancellationToken ct = default)
    {
        var booked = await db.Waitlist.Where(w => w.Status == WaitlistStatus.Booked && w.BookingId != null).ToListAsync(ct);
        var ids = booked.Select(b => b.BookingId!.Value).ToList();
        var statuses = await theatreDb.Bookings.AsNoTracking().Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Status, ct);
        var changed = 0;
        foreach (var e in booked)
        {
            if (!statuses.TryGetValue(e.BookingId!.Value, out var st)) continue;
            if (st is BookingStatus.Cancelled or BookingStatus.DidNotAttend)
            {
                e.Status = WaitlistStatus.Waiting; e.Notes = Trunc($"Previous booking {(st == BookingStatus.Cancelled ? "cancelled" : "not attended")} ({e.BookedFor:d MMM yyyy}). {e.Notes}", 1000);
                e.BookingId = null; e.BookedFor = null; e.StatusAt = clock.GetUtcNow(); changed++;
                await audit.WriteAsync(user, "ReturnToWaitlist", "WaitlistEntry", e.Id.ToString(), e.PatientId.ToString(), reason: $"Theatre booking {st}", ct: ct);
            }
            else if (st == BookingStatus.Discharged)
            {
                e.Status = WaitlistStatus.Completed; e.StatusAt = clock.GetUtcNow(); changed++;
                await audit.WriteAsync(user, "Complete", "WaitlistEntry", e.Id.ToString(), e.PatientId.ToString(), reason: "Procedure done (patient discharged)", ct: ct);
            }
        }
        // Holds that have reached their end date go back to waiting.
        foreach (var e in await db.Waitlist.Where(w => w.Status == WaitlistStatus.OnHold && w.HoldUntil != null && w.HoldUntil <= Today).ToListAsync(ct))
        {
            e.Status = WaitlistStatus.Waiting; e.Notes = Trunc($"Hold ended {e.HoldUntil:d MMM yyyy} ({e.HoldReason}). {e.Notes}", 1000);
            e.HoldReason = null; e.HoldUntil = null; e.StatusAt = clock.GetUtcNow(); changed++;
            await audit.WriteAsync(user, "Resume", "WaitlistEntry", e.Id.ToString(), e.PatientId.ToString(), reason: "Hold end date reached", ct: ct);
        }
        await db.SaveChangesAsync(ct);
        return changed;
    }

    public async Task<IReadOnlyList<WaitlistRow>> WaitlistAsync(string view, string? specialist, Priority? priority, Guid? patientId, CancellationToken ct = default)
    {
        var q = db.Waitlist.AsNoTracking().AsQueryable();
        if (patientId is { } pid) q = q.Where(w => w.PatientId == pid);
        else q = view switch
        {
            "on-hold" => q.Where(w => w.Status == WaitlistStatus.OnHold),
            "booked" => q.Where(w => w.Status == WaitlistStatus.Booked),
            "closed" => q.Where(w => w.Status == WaitlistStatus.Completed || w.Status == WaitlistStatus.Removed),
            "all-open" => q.Where(w => w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.OnHold || w.Status == WaitlistStatus.Booked),
            _ => q.Where(w => w.Status == WaitlistStatus.Waiting),
        };
        if (!string.IsNullOrWhiteSpace(specialist)) q = q.Where(w => w.Specialist == specialist);
        if (priority is { } pr) q = q.Where(w => w.Priority == pr);
        var list = await q.OrderBy(w => w.TargetDate).ThenBy(w => w.AddedOn).Take(1000).ToListAsync(ct);
        var refIds = list.Where(w => w.ReferralId != null).Select(w => w.ReferralId!.Value).ToList();
        var numbers = await db.Referrals.AsNoTracking().Where(r => refIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, ct);
        var people = await People(list.Select(w => w.PatientId), ct);
        var today = Today;
        return list.Select(w =>
        {
            var p = people.GetValueOrDefault(w.PatientId);
            var open = w.Status is WaitlistStatus.Waiting or WaitlistStatus.OnHold;
            return new WaitlistRow(w.Id, w.ReferralId, w.ReferralId is { } rid ? numbers.GetValueOrDefault(rid) : null, w.PatientId, Name(p), p?.Mrn ?? "", p?.Nhi,
                p?.DateOfBirth ?? default, w.Procedure, w.Laterality.ToString(), w.Anaesthetic.ToString(), w.DurationMinutes, w.Service.ToString(), w.Specialist,
                w.Funder.ToString(), w.Priority.ToString(), w.AddedOn, w.TargetDate, today.DayNumber - w.AddedOn.DayNumber, w.TargetDate.DayNumber - today.DayNumber,
                open && w.TargetDate < today, w.Status.ToString(), w.HoldReason, w.HoldUntil, w.BookingId, w.BookedFor, w.RemovedReason, w.Notes, w.Funder == Funder.HealthNz);
        }).ToList();
    }

    public async Task<object> SummaryAsync(CancellationToken ct = default)
    {
        var today = Today;
        var open = await db.Waitlist.AsNoTracking().Where(w => w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.OnHold)
            .Select(w => new { w.Priority, w.TargetDate, w.Status, w.Specialist }).ToListAsync(ct);
        return new
        {
            toTriage = await ToTriageCountAsync(ct),
            waiting = open.Count(w => w.Status == WaitlistStatus.Waiting),
            onHold = open.Count(w => w.Status == WaitlistStatus.OnHold),
            overdue = open.Count(w => w.Status == WaitlistStatus.Waiting && w.TargetDate < today),
            dueIn14Days = open.Count(w => w.Status == WaitlistStatus.Waiting && w.TargetDate >= today && w.TargetDate <= today.AddDays(14)),
            byPriority = Enum.GetValues<Priority>().Select(p => new { priority = p.ToString(), count = open.Count(w => w.Priority == p && w.Status == WaitlistStatus.Waiting) }),
            specialists = open.Select(w => w.Specialist).Distinct().OrderBy(s => s).ToList()
        };
    }

    /// <summary>Waitlist as CSV (for the specialist, management reporting, or Health NZ NBRS preparation). Contains health information: audited.</summary>
    public async Task<string> ExportCsvAsync(string view, string user, CancellationToken ct = default)
    {
        var rows = await WaitlistAsync(view, null, null, null, ct);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("NHI,MRN,Family name,Given names,Date of birth,Procedure,Side,Specialist,Priority,Funder,Publicly funded,Decision to treat,Target date,Days waiting,Status,On hold reason,Booked for,Referral");
        foreach (var r in rows)
        {
            var names = r.PatientName.Split(", ", 2);
            sb.AppendLine(string.Join(",", new[] { r.Nhi ?? "", r.Mrn, names[0], names.Length > 1 ? names[1] : "", r.DateOfBirth.ToString("yyyy-MM-dd"), r.Procedure, r.Laterality,
                r.Specialist, r.Priority, r.Funder, r.PubliclyFunded ? "Yes" : "No", r.AddedOn.ToString("yyyy-MM-dd"), r.TargetDate.ToString("yyyy-MM-dd"), r.DaysWaiting.ToString(),
                r.Status, r.HoldReason ?? "", r.BookedFor?.ToString("yyyy-MM-dd") ?? "", r.ReferralNumber ?? "" }.Select(Csv)));
        }
        await audit.WriteAsync(user, "Export", "Waitlist", view, reason: $"{rows.Count} rows exported to CSV", ct: ct);
        return sb.ToString();
    }

    /// <summary>CSV cell: quoted, with spreadsheet formula injection neutralised.</summary>
    public static string Csv(string v)
    {
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }

    private async Task<Dictionary<Guid, Patient>> People(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await patients.Patients.AsNoTracking().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }

    private static string Name(Patient? p) => p is null ? "(unknown)" : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}";
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}
