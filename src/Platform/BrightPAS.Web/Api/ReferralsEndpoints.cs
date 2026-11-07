using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Theatre;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>Referrals & waitlist: referral intake and triage, the waitlist, and booking theatre from it.</summary>
public static class ReferralsEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Done(string? error) => error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });
    private static IResult Errors(IReadOnlyList<string> errors) => Results.BadRequest(new { errors });

    public sealed record ReasonRequest(string Reason);
    public sealed record HoldRequest(bool Hold, string? Reason, DateOnly? Until);
    public sealed record BookRequest(Guid SessionId, bool AcceptWarnings);
    public sealed record TargetsRequest(int UrgentDays, int SemiUrgentDays, int RoutineDays);

    public static void MapReferralsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/referrals").RequireModule(ReferralsModule.Id);

        g.MapGet("/", async (string? view, Guid? patientId, ReferralService svc) => Results.Ok(await svc.ReferralsAsync(view ?? "to-triage", patientId)))
            .RequireAuthorization(Permissions.ReferralsView);
        g.MapPost("/", async (NewReferral r, ClaimsPrincipal u, ReferralService svc) =>
        {
            var (referral, errors) = await svc.CreateReferralAsync(r, Me(u));
            return referral is null ? Errors(errors) : Results.Ok(new { referral.Id, referral.Number });
        }).RequireAuthorization(Permissions.ReferralsManage);
        g.MapPost("/{id:guid}/triage", async (Guid id, TriageDecision d, ClaimsPrincipal u, ReferralService svc) =>
        {
            var (entry, errors) = await svc.TriageAsync(id, d, Me(u));
            return errors.Count > 0 ? Errors(errors) : Results.Ok(new { waitlistEntryId = entry?.Id });
        }).RequireAuthorization(Permissions.ReferralsTriage);
        g.MapPost("/{id:guid}/withdraw", async (Guid id, ReasonRequest r, ClaimsPrincipal u, ReferralService svc) => Done(await svc.WithdrawReferralAsync(id, r.Reason, Me(u))))
            .RequireAuthorization(Permissions.ReferralsManage);

        g.MapGet("/summary", async (ReferralService svc) => { await svc.SyncWithTheatreAsync(); return Results.Ok(await svc.SummaryAsync()); })
            .RequireAuthorization(Permissions.ReferralsView);
        g.MapGet("/settings", async (ReferralService svc) =>
        {
            var s = await svc.SettingsAsync();
            return Results.Ok(new { s.UrgentDays, s.SemiUrgentDays, s.RoutineDays, s.UpdatedAt, s.UpdatedBy });
        }).RequireAuthorization(Permissions.ModulesManage);
        g.MapPut("/settings", async (TargetsRequest r, ClaimsPrincipal u, ReferralService svc) =>
        {
            var errors = await svc.UpdateSettingsAsync(r.UrgentDays, r.SemiUrgentDays, r.RoutineDays, Me(u));
            return errors.Count == 0 ? Results.Ok() : Errors(errors);
        }).RequireAuthorization(Permissions.ModulesManage);

        var w = app.MapGroup("/api/waitlist").RequireModule(ReferralsModule.Id);
        w.MapGet("/", async (string? view, string? specialist, Priority? priority, Guid? patientId, ReferralService svc) =>
        {
            await svc.SyncWithTheatreAsync();
            return Results.Ok(await svc.WaitlistAsync(view ?? "waiting", specialist, priority, patientId));
        }).RequireAuthorization(Permissions.ReferralsView);
        w.MapPost("/", async (NewWaitlistEntry n, ClaimsPrincipal u, ReferralService svc) =>
        {
            var (entry, errors) = await svc.AddToWaitlistAsync(n, Me(u));
            return entry is null ? Errors(errors) : Results.Ok(new { entry.Id, entry.TargetDate });
        }).RequireAuthorization(Permissions.ReferralsManage);
        w.MapPut("/{id:guid}", async (Guid id, WaitlistUpdate r, ClaimsPrincipal u, ReferralService svc) => Done(await svc.UpdateEntryAsync(id, r, Me(u))))
            .RequireAuthorization(Permissions.ReferralsManage);
        w.MapPost("/{id:guid}/hold", async (Guid id, HoldRequest r, ClaimsPrincipal u, ReferralService svc) => Done(await svc.HoldAsync(id, r.Hold, r.Reason, r.Until, Me(u))))
            .RequireAuthorization(Permissions.ReferralsManage);
        w.MapPost("/{id:guid}/remove", async (Guid id, ReasonRequest r, ClaimsPrincipal u, ReferralService svc) => Done(await svc.RemoveAsync(id, r.Reason, Me(u))))
            .RequireAuthorization(Permissions.ReferralsManage);
        // Booking needs both waitlist and theatre rights, and the Theatre module licensed for writing.
        w.MapPost("/{id:guid}/book", async (Guid id, BookRequest r, ClaimsPrincipal u, ReferralService svc, LicenceCache licence) =>
        {
            if (!u.HasClaim(Permissions.ClaimType, Permissions.TheatreBook)) return Results.Json(new { errors = new[] { "You need theatre booking rights to book from the waitlist." } }, statusCode: 403);
            if (licence.Status.AccessTo(TheatreModule.Id) != BrightPAS.Licensing.ModuleAccess.Full)
                return Results.Json(new { error = "module_not_licensed", module = TheatreModule.Id, errors = new[] { "The Theatre bookings module must be licensed to book from the waitlist." } }, statusCode: 403);
            var res = await svc.BookAsync(id, r.SessionId, r.AcceptWarnings, Me(u));
            return res.Booking is not null ? Results.Ok(new { bookingId = res.Booking.Id, res.Warnings }) : Results.BadRequest(new { errors = res.Errors, warnings = res.Warnings });
        }).RequireAuthorization(Permissions.ReferralsManage);
        w.MapGet("/export", async (string? view, ClaimsPrincipal u, ReferralService svc, TimeProvider clock) =>
        {
            var csv = await svc.ExportCsvAsync(view ?? "all-open", Me(u));
            return Results.File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv",
                $"BrightPAS-waitlist-{clock.GetLocalNow():yyyyMMdd-HHmm}.csv");
        }).RequireAuthorization(Permissions.ReferralsManage);
    }
}
