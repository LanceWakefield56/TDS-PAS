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

    public sealed record ExportRequest(string? View, BrightPAS.Web.Security.ExportOptions? Export);
    public sealed record NbrsRequest(DateOnly From, DateOnly To, BrightPAS.Web.Security.ExportOptions? Export);

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
        w.MapGet("/", async (string? view, Guid? specialist, Priority? priority, Guid? patientId, ReferralService svc) =>
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
        w.MapPost("/{id:guid}/book", async (Guid id, BookRequest r, ClaimsPrincipal u, ReferralService svc, LicenceCache licence, HttpContext http) =>
        {
            if (!u.HasClaim(Permissions.ClaimType, Permissions.TheatreBook)) return Results.Json(new { errors = new[] { "You need theatre booking rights to book from the waitlist." } }, statusCode: 403);
            if (licence.Status.AccessTo(TheatreModule.Id) != BrightPAS.Licensing.ModuleAccess.Full)
                return Results.Json(new { error = "module_not_licensed", module = TheatreModule.Id, errors = new[] { "The Theatre bookings module must be licensed to book from the waitlist." } }, statusCode: 403);
            var res = await svc.BookAsync(id, r.SessionId, r.AcceptWarnings, Me(u));
            if (res.Booking is not null) await PortalEndpoints.AutoSendAfterBookingAsync(http.RequestServices, http, res.Booking.PatientId, Me(u));
            return res.Booking is not null ? Results.Ok(new { bookingId = res.Booking.Id, res.Warnings }) : Results.BadRequest(new { errors = res.Errors, warnings = res.Warnings });
        }).RequireAuthorization(Permissions.ReferralsManage);
        w.MapPost("/export", async (ExportRequest r, ClaimsPrincipal u, ReferralService svc, BrightPAS.Web.Security.SecureExportService export) =>
        {
            if (r.Export?.WantsPdf == true)
            {
                var rows = await svc.WaitlistAsync(r.View ?? "all-open", null, null, null);
                (string, double)[] cols = [("Patient", 4.2), ("NHI / MRN", 2.6), ("Procedure", 4.6), ("Specialist", 3.2), ("Priority", 1.9), ("Funder", 1.9), ("Added", 2.0), ("Target", 2.0), ("Days", 1.1), ("Status", 2.2)];
                var data = rows.Select(w => new[] { w.PatientName, $"{w.Nhi ?? "—"} {w.Mrn}", w.Procedure + (w.Laterality == "NotApplicable" ? "" : $" ({w.Laterality})"), w.Specialist, w.Priority, w.Funder,
                    w.AddedOn.ToString("dd/MM/yy"), w.TargetDate.ToString("dd/MM/yy") + (w.Overdue ? " OVERDUE" : ""), w.DaysWaiting.ToString(), w.Status }).ToList();
                return await export.ExportPdfAsync(r.Export, "BrightPAS-waitlist", pw => BrightPAS.Web.Pdf.PdfReports.Table("Waitlist", $"{rows.Count} entries · {r.View ?? "all-open"} · {DateTime.Now:d MMMM yyyy}", cols, data, pw),
                    Me(u), "WaitlistPdf");
            }
            var csv = await svc.ExportCsvAsync(r.View ?? "all-open", Me(u));
            return await export.ExportAsync(r.Export, "BrightPAS-waitlist", [new("waitlist.csv", BrightPAS.Web.Security.SecureExportService.Utf8Bom(csv))], Me(u), "Waitlist");
        }).RequireAuthorization(Permissions.ReferralsManage);
        // NBRS extract (Health NZ, publicly funded patients).
        w.MapGet("/nbrs/codes", async (ReferralService svc) =>
        {
            var s = await svc.SettingsAsync();
            return Results.Ok(new { agencyCode = s.NbrsAgencyCode, facilityCode = s.NbrsFacilityCode, specialtySurgical = s.NbrsSpecialtySurgical, specialtyEndoscopy = s.NbrsSpecialtyEndoscopy });
        }).RequireAuthorization(Permissions.ReferralsManage);
        w.MapPut("/nbrs/codes", async (ReferralService.NbrsCodes c, ClaimsPrincipal u, ReferralService svc) =>
        {
            var e = await svc.UpdateNbrsCodesAsync(c, Me(u));
            return e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });
        }).RequireAuthorization(Permissions.ReferralsTriage);
        w.MapPost("/nbrs/check", async (NbrsRequest r, ClaimsPrincipal u, ReferralService svc) =>
        {
            var (_, problems, rows) = await svc.NbrsExtractAsync(r.From, r.To, Me(u));
            return Results.Ok(new { rows, problems });
        }).RequireAuthorization(Permissions.ReferralsManage);
        w.MapPost("/nbrs/export", async (NbrsRequest r, ClaimsPrincipal u, ReferralService svc, BrightPAS.Web.Security.SecureExportService export) =>
        {
            if (r.To < r.From) return Results.BadRequest(new { errors = new[] { "The period must end after it starts." } });
            var (csv, problems, _) = await svc.NbrsExtractAsync(r.From, r.To, Me(u));
            var files = new List<BrightPAS.Web.Security.SecureExportService.ExportFile> { new("nbrs-extract.csv", System.Text.Encoding.UTF8.GetBytes(csv)) };
            if (problems.Count > 0) files.Add(new("nbrs-problems.txt", System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, problems))));
            return await export.ExportAsync(r.Export, $"BrightPAS-NBRS-{r.From:yyyyMMdd}-{r.To:yyyyMMdd}", files, Me(u), "NbrsExtract");
        }).RequireAuthorization(Permissions.ReferralsManage);
    }
}
