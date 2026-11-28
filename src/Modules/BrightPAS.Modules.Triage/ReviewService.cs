using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Organisation;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Triage;

/// <summary>Booking an anaesthetist review. StartsAt is NZ local time.</summary>
public sealed record ReviewEdit(Guid AssessmentId, Guid AnaesthetistId, ReviewMode Mode, DateTime StartsAt, int? Minutes, string? Phone, string? Reason);

public sealed record ReviewFinish(ReviewStatus Status, string? Outcome);

public sealed record ReviewRow(Guid Id, Guid AssessmentId, Guid PatientId, string Patient, string? Nhi, Guid AnaesthetistId, string Anaesthetist, ReviewMode Mode, DateTimeOffset StartsAt,
    int Minutes, ReviewStatus Status, string? JoinLink, string? Phone, string? Reason, string? Outcome, string? Procedure, DateOnly? ProcedureDate, string CreatedBy, string? DoneBy, DateTimeOffset? DoneAt);

/// <summary>
/// Anaesthetist reviews (0.36.0, ADR 0049): when triage suggests an anaesthetist review, staff book a video, phone or in-person appointment with an
/// anaesthetist before the day of surgery. Video reviews get a join link from the meeting link set in Triage settings. The anaesthetist records the
/// outcome; signing off triage stays a separate clinical step.
/// </summary>
public sealed class ReviewService(TriageDbContext db, PasDbContext core, PatientsDbContext patients, TriageService triage, AuditWriter audit, TimeProvider clock)
{
    public async Task<List<Clinician>> AnaesthetistsAsync(CancellationToken ct = default) =>
        await core.Clinicians.AsNoTracking().Where(c => c.Type == ClinicianType.Anaesthetist).OrderBy(c => c.Name).ToListAsync(ct);

    public async Task<(Review? Review, IReadOnlyList<string> Errors)> BookAsync(ReviewEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var a = await db.Assessments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == e.AssessmentId, ct);
        if (a is null) return (null, ["Triage assessment not found."]);
        if (a.Status is AssessmentStatus.Superseded or AssessmentStatus.Cancelled) errors.Add("This assessment has been replaced – open the latest one.");
        var doc = await core.Clinicians.AsNoTracking().SingleOrDefaultAsync(c => c.Id == e.AnaesthetistId, ct);
        if (doc is null || doc.Type != ClinicianType.Anaesthetist) errors.Add("Choose an anaesthetist.");
        if (!Enum.IsDefined(e.Mode)) errors.Add("Choose video, phone or in person.");
        var s = await triage.SettingsAsync(ct);
        var minutes = e.Minutes ?? s.ReviewMinutes;
        if (minutes is < 5 or > 120) errors.Add("A review is 5–120 minutes.");
        var start = NzTime.FromLocal(e.StartsAt);
        if (start <= clock.GetUtcNow()) errors.Add("Choose a time in the future.");
        if (a.ProcedureDate is { } day && start.NzDate() > day) errors.Add($"The review must be before surgery on {day:d MMM yyyy}.");
        if (e.Mode == ReviewMode.Video && s.VideoLinkTemplate is null) errors.Add("Set the video meeting link in Triage settings first.");
        string? phone = null;
        if (e.Mode == ReviewMode.Phone)
        {
            var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == a.PatientId, ct);
            phone = string.IsNullOrWhiteSpace(e.Phone) ? p?.MobilePhone ?? p?.HomePhone : e.Phone.Trim();
            if (string.IsNullOrWhiteSpace(phone) || phone.Length > 40) errors.Add("Give the number to ring.");
        }
        if (errors.Count == 0)
        {
            var end = start.AddMinutes(minutes);
            var clash = await db.Reviews.AsNoTracking().Where(r => r.AnaesthetistId == e.AnaesthetistId && r.Status == ReviewStatus.Booked && r.StartsAt < end)
                .Select(r => new { r.StartsAt, r.Minutes }).ToListAsync(ct);
            if (clash.Exists(r => r.StartsAt.AddMinutes(r.Minutes) > start)) errors.Add($"{doc!.Name} already has a review at that time.");
            if (await db.Reviews.AnyAsync(r => r.AssessmentId == a.Id && r.Status == ReviewStatus.Booked, ct)) errors.Add("A review is already booked for this assessment – cancel it first.");
        }
        if (errors.Count > 0) return (null, errors);
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
        var r = new Review
        {
            AssessmentId = a.Id, PatientId = a.PatientId, BookingId = a.BookingId, AnaesthetistId = doc!.Id, AnaesthetistName = doc.Name, Mode = e.Mode, StartsAt = start, Minutes = minutes,
            JoinLink = e.Mode == ReviewMode.Video ? s.VideoLinkTemplate!.Replace("{code}", code, StringComparison.Ordinal) : null, Phone = phone,
            Reason = Cut(e.Reason, 500), CreatedAt = clock.GetUtcNow(), CreatedBy = user,
        };
        db.Reviews.Add(r);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Book", "AnaesthetistReview", r.Id.ToString(), r.PatientId.ToString(), reason: $"{r.Mode} with {r.AnaesthetistName} {start.ToNz():d MMM yyyy HH:mm}", ct: ct);
        return (r, []);
    }

    public async Task<string?> FinishAsync(Guid id, ReviewFinish f, string user, CancellationToken ct = default)
    {
        var r = await db.Reviews.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return "Review not found.";
        if (r.Status != ReviewStatus.Booked) return "This review is already finished.";
        if (f.Status == ReviewStatus.Booked || !Enum.IsDefined(f.Status)) return "Choose done, did not attend or cancelled.";
        if (f.Status == ReviewStatus.Done && string.IsNullOrWhiteSpace(f.Outcome)) return "Write what the review found.";
        if (f.Status == ReviewStatus.Cancelled && string.IsNullOrWhiteSpace(f.Outcome)) return "Say why the review is cancelled.";
        (r.Status, r.Outcome, r.DoneAt, r.DoneBy) = (f.Status, Cut(f.Outcome, 2000), clock.GetUtcNow(), user);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, f.Status.ToString(), "AnaesthetistReview", r.Id.ToString(), r.PatientId.ToString(), reason: Cut(r.Outcome, 200), ct: ct);
        return null;
    }

    /// <summary>Reviews from a day (NZ) for the number of days given; optionally one anaesthetist; or one assessment's reviews.</summary>
    public async Task<List<ReviewRow>> ListAsync(DateOnly? from, int days, Guid? anaesthetistId, Guid? assessmentId, CancellationToken ct = default)
    {
        var q = db.Reviews.AsNoTracking();
        if (assessmentId is { } aid) q = q.Where(r => r.AssessmentId == aid);
        else
        {
            var start = NzTime.StartOfDay(from ?? NzTime.Today(clock));
            var end = NzTime.StartOfDay((from ?? NzTime.Today(clock)).AddDays(Math.Clamp(days, 1, 92)));
            q = q.Where(r => r.StartsAt >= start && r.StartsAt < end);
        }
        if (anaesthetistId is { } d) q = q.Where(r => r.AnaesthetistId == d);
        var list = await q.OrderBy(r => r.StartsAt).Take(500).ToListAsync(ct);
        var ids = list.Select(r => r.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.GivenNames, p.FamilyName, p.Nhi }).ToDictionaryAsync(p => p.Id, ct);
        var aids = list.Select(r => r.AssessmentId).Distinct().ToList();
        var asm = await db.Assessments.AsNoTracking().Where(a => aids.Contains(a.Id)).Select(a => new { a.Id, a.Procedure, a.ProcedureDate }).ToDictionaryAsync(a => a.Id, ct);
        return [.. list.Select(r =>
        {
            var p = people.GetValueOrDefault(r.PatientId); var a = asm.GetValueOrDefault(r.AssessmentId);
            return new ReviewRow(r.Id, r.AssessmentId, r.PatientId, p is null ? "?" : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p?.Nhi, r.AnaesthetistId, r.AnaesthetistName, r.Mode, r.StartsAt,
                r.Minutes, r.Status, r.JoinLink, r.Phone, r.Reason, r.Outcome, a?.Procedure, a?.ProcedureDate, r.CreatedBy, r.DoneBy, r.DoneAt);
        })];
    }

    private static string? Cut(string? s, int n) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > n ? s.Trim()[..n] : s.Trim();
}
