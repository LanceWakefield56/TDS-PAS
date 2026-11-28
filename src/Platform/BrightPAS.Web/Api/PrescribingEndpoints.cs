using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Modules.Prescribing;
using BrightPAS.Web.Licensing;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Web.Api;

/// <summary>Prescribing and barcode medicine checks (0.31.0, ADR 0044): medicine orders, the scan lookup, prescriptions, prescribers, settings and the barcode report.</summary>
public static class PrescribingEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Err(string? e) => e is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { e } });
    private static IResult Errs(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record StopRequest(string? Reason);
    public sealed record ScanRequest(Guid PatientId, string? Wristband, string? Pack);

    private static object SettingsView(RxSettings s) => new
    {
        bcmaMode = s.BcmaMode.ToString(),
        s.WarnWithoutOrder,
        provider = s.Provider.ToString(),
        s.NzepsUrl,
        s.NzepsClientId,
        nzepsKeySet = s.NzepsKeyProtected is not null,
        s.FacilityHpi,
        s.ExportFolder,
        s.UpdatedAt,
        s.UpdatedBy,
    };

    public static void MapPrescribingEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/prescribing").RequireModule(PrescribingModule.Id);
        g.MapGet("/settings", async (BcmaService svc) => Results.Ok(SettingsView(await svc.SettingsAsync()))).RequireAuthorization(Permissions.RxAny);
        g.MapPut("/settings", async (RxSettingsInput r, ClaimsPrincipal u, PrescriptionService svc) => Errs(await svc.SaveSettingsAsync(r, Me(u)))).RequireAuthorization(Permissions.RxManage);

        g.MapGet("/prescribers", async (PrescriptionService svc) => Results.Ok(await svc.PrescribersAsync())).RequireAuthorization(Permissions.RxAny);
        g.MapGet("/prescribers/me", async (ClaimsPrincipal u, PrescriptionService svc) => Results.Ok(new { prescriber = await svc.PrescriberForAsync(Me(u)) })).RequireAuthorization(Permissions.RxAny);
        g.MapPost("/prescribers", async (PrescriberInput r, ClaimsPrincipal u, PrescriptionService svc) =>
        {
            var (row, errors) = await svc.SavePrescriberAsync(null, r, Me(u));
            return row is null ? Errs(errors) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.RxManage);
        g.MapPut("/prescribers/{id:guid}", async (Guid id, PrescriberInput r, ClaimsPrincipal u, PrescriptionService svc) =>
        {
            var (row, errors) = await svc.SavePrescriberAsync(id, r, Me(u));
            return row is null ? Errs(errors) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.RxManage);

        g.MapGet("/episodes/{id:guid}/orders", async (Guid id, OrderService svc) => Results.Ok(await svc.ForEpisodeAsync(id))).RequireAuthorization(Permissions.RxAny);
        g.MapPost("/episodes/{id:guid}/orders", async (Guid id, OrderInput r, ClaimsPrincipal u, OrderService svc) =>
        {
            var (o, errors) = await svc.PrescribeAsync(id, r, Me(u));
            return o is null ? Errs(errors) : Results.Ok(new { o.Id });
        }).RequireAuthorization(Permissions.RxPrescribe);
        g.MapPost("/orders/{id:guid}/stop", async (Guid id, StopRequest r, ClaimsPrincipal u, OrderService svc) => Err(await svc.StopAsync(id, r.Reason, Me(u)))).RequireAuthorization(Permissions.RxPrescribe);
        g.MapGet("/episodes/{id:guid}/checks", async (Guid id, RxDbContext db) =>
            Results.Ok((await db.Scans.AsNoTracking().Where(x => x.EpisodeId == id && x.MedicationId != null).Select(x => new { x.MedicationId, x.Result, x.PatientScanned, x.PackScanned, x.Batch, x.Expiry, x.OverrideReason }).ToListAsync())
                .Select(x => new { medicationId = x.MedicationId, result = x.Result.ToString(), x.PatientScanned, x.PackScanned, x.Batch, x.Expiry, x.OverrideReason })))
            .RequireAuthorization(Permissions.RxAny);
        g.MapPost("/scan", async (ScanRequest r, BcmaService svc) => Results.Ok(await svc.LookupAsync(r.PatientId, r.Wristband, r.Pack))).RequireAuthorization(Permissions.RxAny);
        g.MapGet("/report", async (DateOnly? from, DateOnly? to, BcmaService svc, TimeProvider clock) =>
        {
            var today = BrightPAS.Core.NzTime.Today(clock);
            var f = from ?? today.AddDays(-30); var t = to ?? today;
            return t < f || t.DayNumber - f.DayNumber > 366 ? Err("Choose a period of up to a year.") : Results.Ok(await svc.ReportAsync(f, t));
        }).RequireAuthorization(Permissions.RxManage);

        g.MapGet("/prescriptions", async (Guid? patientId, string? view, PrescriptionService svc) => Results.Ok(await svc.ListAsync(patientId, view))).RequireAuthorization(Permissions.RxAny);
        g.MapGet("/prescriptions/{id:guid}", async (Guid id, PrescriptionService svc) => await svc.GetAsync(id) is { } p ? Results.Ok(p) : Results.NotFound()).RequireAuthorization(Permissions.RxAny);
        g.MapPost("/prescriptions", async (PrescriptionInput r, ClaimsPrincipal u, PrescriptionService svc) =>
        {
            var (rx, errors) = await svc.CreateAsync(r, Me(u));
            return rx is null ? Errs(errors) : Results.Ok(new { rx.Id, rx.Number });
        }).RequireAuthorization(Permissions.RxPrescribe);
        g.MapPost("/prescriptions/{id:guid}/sign", async (Guid id, ClaimsPrincipal u, PrescriptionService svc) =>
        {
            var (status, error) = await svc.SignAsync(id, Me(u));
            return status is null ? Err(error) : Results.Ok(new { status, message = error });
        }).RequireAuthorization(Permissions.RxPrescribe);
        g.MapPost("/prescriptions/{id:guid}/resend", async (Guid id, ClaimsPrincipal u, PrescriptionService svc) =>
        {
            var (status, error) = await svc.ResendAsync(id, Me(u));
            return status is null ? Err(error) : Results.Ok(new { status, message = error });
        }).RequireAuthorization(Permissions.RxPrescribe);
        g.MapPost("/prescriptions/{id:guid}/cancel", async (Guid id, StopRequest r, ClaimsPrincipal u, PrescriptionService svc) => Err(await svc.CancelAsync(id, r.Reason, Me(u))))
            .RequireAuthorization(Permissions.RxPrescribe);
        g.MapGet("/prescriptions/{id:guid}/pdf", async (Guid id, ClaimsPrincipal u, PrescriptionService svc) =>
        {
            var (pdf, number, error) = await svc.PdfAsync(id, Me(u));
            return pdf is null ? Err(error) : Results.File(pdf, "application/pdf", $"prescription-{number}.pdf");
        }).RequireAuthorization(Permissions.RxAny);
    }
}
