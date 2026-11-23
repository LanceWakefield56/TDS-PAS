using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Licensing;
using BrightPAS.Modules.Letters;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Quality;
using BrightPAS.Modules.Reminders;
using BrightPAS.Web.Licensing;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Web.Api;

/// <summary>
/// Outbox and send safety (spec 7.19, 7.20, ADR 0038): messages held before release (undo), second-person release, send now, and "sent to the wrong
/// person" – recall or correction notice, a privacy incident in Quality for the Privacy Officer's notifiable-breach assessment.
/// </summary>
public static class OutboxEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    public sealed record ReasonRequest(string? Reason);
    public sealed record SafetyRequest(int HoldSeconds, int BulkHoldSeconds, string? ReviewKinds);

    private static async Task<Guid?> PrivacyIncidentAsync(IServiceProvider sp, LicenceCache licence, Guid? patientId, Guid? bookingId, string what, string reason, string action, string user)
    {
        if (licence.Status.AccessTo("quality") != ModuleAccess.Full) return null;
        var (i, _) = await sp.GetRequiredService<QualityService>().ReportAsync(new IncidentReport(sp.GetRequiredService<TimeProvider>().GetUtcNow(), "Communications (BrightPAS)", IncidentCategory.InformationPrivacy,
            $"{what} was sent to the wrong person. {reason} Privacy Officer: complete the Privacy Act notifiable breach assessment.", action, patientId, bookingId, null, null, Harm.Minor, false), user);
        return i?.Id;
    }

    public static void MapOutboxEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/outbox").RequireAuthorization();

        g.MapGet("/", async (LicenceCache licence, RemindersDbContext rem, LettersDbContext letters, PatientsDbContext patients, TimeProvider clock, ClaimsPrincipal u) =>
        {
            var now = clock.GetUtcNow();
            var since = now.AddDays(-7);
            var msgs = licence.Status.AccessTo("reminders") == ModuleAccess.None || !u.HasClaim(Permissions.ClaimType, Permissions.RemindersView) ? [] :
                await rem.Messages.AsNoTracking().Where(m => m.Status == MessageStatus.Queued && (m.NextAttemptAt > now || m.RequiresRelease && m.ReleasedAt == null)
                    || (m.Status == MessageStatus.Sent || m.Status == MessageStatus.Delivered) && m.SentAt >= since && m.PatientId != null || m.WrongRecipientAt >= since)
                    .OrderByDescending(m => m.CreatedAt).Take(300).ToListAsync();
            var lets = licence.Status.AccessTo("letters") == ModuleAccess.None || !u.HasClaim(Permissions.ClaimType, Permissions.LettersView) ? [] :
                await letters.Letters.AsNoTracking().Where(l => l.Status == LetterStatus.Final || (l.Status == LetterStatus.Sent || l.Status == LetterStatus.Acknowledged) && l.SentAt >= since).OrderByDescending(l => l.CreatedAt).Take(200).ToListAsync();
            var ids = msgs.Where(m => m.PatientId != null).Select(m => m.PatientId!.Value).Concat(lets.Select(l => l.PatientId)).Distinct().ToList();
            var names = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}");
            return Results.Ok(new
            {
                messages = msgs.Select(m => new
                {
                    m.Id,
                    m.PatientId,
                    patient = m.PatientId is { } p ? names.GetValueOrDefault(p) : null,
                    kind = m.Kind.ToString(),
                    channel = m.Channel.ToString(),
                    m.Address,
                    m.Subject,
                    m.Body,
                    status = m.Status.ToString(),
                    m.NextAttemptAt,
                    held = m.Status == MessageStatus.Queued && m.NextAttemptAt > now,
                    needsRelease = m.RequiresRelease && m.ReleasedAt == null && m.Status == MessageStatus.Queued,
                    mine = string.Equals(m.CreatedBy, Me(u), StringComparison.OrdinalIgnoreCase),
                    m.CreatedBy,
                    m.CreatedAt,
                    m.SentAt,
                    m.RecalledBy,
                    m.RecalledAt,
                    m.WrongRecipientAt
                }),
                letters = lets.Select(l => new { l.Id, l.Number, l.PatientId, patient = names.GetValueOrDefault(l.PatientId), l.Subject, l.RecipientName, recipient = l.Recipient.ToString(), status = l.Status.ToString(), channel = l.Channel.ToString(), l.SentAt, l.WrongRecipientAt, l.CreatedBy })
            });
        }).RequireAuthorization(Permissions.AnyOf(Permissions.RemindersView, Permissions.LettersView));

        g.MapPost("/messages/{id:guid}/recall", async (Guid id, ReasonRequest r, ClaimsPrincipal u, ReminderService svc) =>
            await svc.RecallAsync(id, r.Reason ?? "", Me(u)) is { } e ? Results.BadRequest(new { errors = new[] { e } }) : Results.Ok()).RequireAuthorization(Permissions.RemindersView);
        g.MapPost("/messages/{id:guid}/release", async (Guid id, ClaimsPrincipal u, ReminderService svc) =>
            await svc.ReleaseAsync(id, Me(u)) is { } e ? Results.BadRequest(new { errors = new[] { e } }) : Results.Ok()).RequireAuthorization(Permissions.RemindersView);
        g.MapPost("/messages/{id:guid}/send-now", async (Guid id, ReasonRequest r, ClaimsPrincipal u, ReminderService svc) =>
            await svc.SendNowAsync(id, r.Reason ?? "", Me(u)) is { } e ? Results.BadRequest(new { errors = new[] { e } }) : Results.Ok()).RequireAuthorization(Permissions.RemindersManage);
        g.MapPost("/messages/{id:guid}/wrong-recipient", async (Guid id, ReasonRequest r, ClaimsPrincipal u, ReminderService svc, RemindersDbContext db, LicenceCache licence, IServiceProvider sp) =>
        {
            var (recalled, correction, error) = await svc.WrongRecipientAsync(id, r.Reason ?? "", Me(u));
            if (error is not null) return Results.BadRequest(new { errors = new[] { error } });
            var m = await db.Messages.AsNoTracking().SingleAsync(x => x.Id == id);
            var incident = await PrivacyIncidentAsync(sp, licence, m.PatientId, m.BookingId, $"A {(m.Channel == Channel.Sms ? "text message" : "email")} ({m.Kind}) to {m.Address}", r.Reason!,
                recalled ? "Recalled from the outbox before it was sent." : "Correction notice sent to the same address asking them to delete it.", Me(u));
            return Results.Ok(new { recalled, correctionId = correction, incidentId = incident });
        }).RequireAuthorization(Permissions.RemindersView);
        g.MapPost("/letters/{id:guid}/wrong-recipient", async (Guid id, ReasonRequest r, ClaimsPrincipal u, LetterService svc, LicenceCache licence, IServiceProvider sp) =>
        {
            var (retraction, error) = await svc.WrongRecipientAsync(id, r.Reason ?? "", Me(u));
            if (error is not null) return Results.BadRequest(new { errors = new[] { error } });
            var incident = await PrivacyIncidentAsync(sp, licence, retraction!.PatientId, null, $"Letter to {retraction.RecipientName}", r.Reason!, $"Retraction letter {retraction.Number} drafted – check and send it.", Me(u));
            return Results.Ok(new { retractionId = retraction.Id, retraction.Number, incidentId = incident });
        }).RequireAuthorization(Permissions.LettersWrite);

        g.MapGet("/settings", async (ReminderService svc) => { var s = await svc.SettingsAsync(); return Results.Ok(new { s.HoldSeconds, s.BulkHoldSeconds, s.ReviewKinds, kinds = Enum.GetNames<MessageKind>() }); })
            .RequireAuthorization(Permissions.RemindersView);
        g.MapPut("/settings", async (SafetyRequest r, ClaimsPrincipal u, ReminderService svc) =>
            await svc.SaveSafetyAsync(r.HoldSeconds, r.BulkHoldSeconds, r.ReviewKinds, Me(u)) is { Count: > 0 } errors ? Results.BadRequest(new { errors }) : Results.Ok()).RequireAuthorization(Permissions.RemindersManage);
    }
}
