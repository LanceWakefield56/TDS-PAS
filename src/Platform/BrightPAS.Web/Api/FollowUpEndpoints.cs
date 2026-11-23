using System.Security.Claims;
using System.Text.Json;
using BrightPAS.Core;
using BrightPAS.Identity;
using BrightPAS.Modules.FollowUp;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Quality;
using BrightPAS.Web.Licensing;
using BrightPAS.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Web.Api;

/// <summary>Post-operative follow-up and SSI surveillance (spec 7.26, ADR 0037).</summary>
public static class FollowUpEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Err(string? e) => e is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { e } });

    public sealed record SettingsRequest(string? DutyMobile, string? PublicUrl, bool SendCheckInLinks, int LinkDays, bool GpLetter, DateOnly? StartFrom);
    public sealed record RescheduleRequest(DateOnly DueOn, string? Reason);
    public sealed record AssignRequest(string? To);
    public sealed record ReasonRequest(string? Reason);
    public sealed record IncidentRequest(string Description, Harm Harm);
    public sealed record ConfirmRequest(SsiClass Class, string? Note);
    public sealed record DobRequest(DateOnly DateOfBirth);
    public sealed record CheckInRequest(DateOnly DateOfBirth, Dictionary<string, string>? Answers);

    private static object View(Contact c, string patient, string? nhi, string? phone) => new
    {
        c.Id,
        c.PatientId,
        patient,
        nhi,
        phone,
        c.BookingId,
        c.Procedure,
        c.OperationDate,
        kind = c.Kind.ToString(),
        method = c.Method.ToString(),
        c.Label,
        c.DueOn,
        status = c.Status.ToString(),
        c.Attempts,
        c.MaxAttempts,
        c.AssignedTo,
        c.Concern,
        c.ConcernText,
        c.SuspectedSsi,
        escalation = c.Escalation.ToString(),
        escalationLabel = FollowUpScript.EscalationLabels[c.Escalation],
        c.EscalationNote,
        c.EscalationClosedAt,
        c.EscalationClosedBy,
        c.EscalationOutcome,
        c.IncidentId,
        c.DoneBy,
        c.DoneAt,
        c.DoneHow,
        c.Notes,
        c.LinkSentAt,
        c.LinkExpiresAt,
        c.AnsweredAt,
        answers = c.Answers.ToDictionary(a => a.QuestionKey, a => a.Value, StringComparer.Ordinal)
    };

    private static object SsiView(SsiCase c, string? patient) => new
    {
        c.Id,
        c.Number,
        c.PatientId,
        patient,
        c.BookingId,
        c.ContactId,
        c.Procedure,
        c.Surgeon,
        c.OperationDate,
        c.OnsetOn,
        onsetDay = c.OnsetOn.DayNumber - c.OperationDate.DayNumber,
        c.Signs,
        source = c.Source.ToString(),
        c.Antibiotics,
        c.ReturnToTheatre,
        c.Readmitted,
        c.Microbiology,
        @class = c.Class.ToString(),
        classLabel = FollowUpScript.ClassLabels[c.Class],
        c.ConfirmedBy,
        c.ConfirmedAt,
        c.ConfirmNote,
        c.SurveillanceDays,
        c.Implant,
        c.WoundClass,
        c.Asa,
        c.IncidentId,
        c.CreatedAt,
        c.CreatedBy,
        risk = c.Risk.ToDictionary(r => r.Name, r => r.Value, StringComparer.Ordinal)
    };

    public static void MapFollowUpEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/followup").RequireModule(FollowUpModule.Id);

        g.MapGet("/meta", (ClaimsPrincipal u) => Results.Ok(new
        {
            questions = FollowUpScript.Questions,
            escalations = FollowUpScript.EscalationLabels.Select(kv => new { value = kv.Key.ToString(), label = kv.Value }),
            classes = FollowUpScript.ClassLabels.Select(kv => new { value = kv.Key.ToString(), label = kv.Value }),
            canRecord = u.HasClaim(Permissions.ClaimType, Permissions.FollowUpRecord),
            canPlans = u.HasClaim(Permissions.ClaimType, Permissions.FollowUpPlans),
            canSsi = u.HasClaim(Permissions.ClaimType, Permissions.FollowUpSsi),
            canReport = u.HasClaim(Permissions.ClaimType, Permissions.FollowUpReport)
        })).RequireAuthorization(Permissions.FollowUpAny);

        g.MapGet("/worklist", async (string? view, DateOnly? from, DateOnly? to, Guid? patientId, FollowUpService svc) =>
            Results.Ok(await svc.WorklistAsync(view ?? "today", from, to, patientId))).RequireAuthorization(Permissions.FollowUpAny);

        g.MapGet("/contacts/{id:guid}", async (Guid id, ClaimsPrincipal u, HttpContext http, FollowUpService svc, PatientsDbContext patients, SsiService ssi) =>
        {
            var c = await svc.GetAsync(id, Me(u), AuthEndpoints.Workstation(http));
            if (c is null) return Results.NotFound();
            var p = await patients.Patients.AsNoTracking().Where(x => x.Id == c.PatientId).Select(x => new { x.FamilyName, x.GivenNames, x.Nhi, x.MobilePhone, x.HomePhone }).SingleAsync();
            var history = await svc.WorklistAsync("patient", patientId: c.PatientId);
            var ssiCase = (await ssi.ListAsync(false)).FirstOrDefault(s => s.BookingId == c.BookingId && s.Class != SsiClass.NotSsi);
            return Results.Ok(new { contact = View(c, $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p.Nhi, p.MobilePhone ?? p.HomePhone), history = history.Where(h => h.BookingId == c.BookingId), ssiCaseId = ssiCase?.Id });
        }).RequireAuthorization(Permissions.FollowUpAny);

        g.MapPost("/contacts/{id:guid}/record", async (Guid id, RecordContact r, ClaimsPrincipal u, FollowUpService svc) =>
        {
            var (errors, warnings, ssi, signs) = await svc.RecordAsync(id, r, Me(u));
            return errors.Count > 0 ? Results.BadRequest(new { errors }) : Results.Ok(new { warnings, suspectedSsi = ssi, signs });
        }).RequireAuthorization(Permissions.FollowUpRecord);
        g.MapPost("/contacts/{id:guid}/reschedule", async (Guid id, RescheduleRequest r, ClaimsPrincipal u, FollowUpService svc) => Err(await svc.RescheduleAsync(id, r.DueOn, r.Reason, Me(u)))).RequireAuthorization(Permissions.FollowUpRecord);
        g.MapPost("/contacts/{id:guid}/assign", async (Guid id, AssignRequest r, ClaimsPrincipal u, FollowUpService svc) => Err(await svc.AssignAsync(id, r.To, Me(u)))).RequireAuthorization(Permissions.FollowUpRecord);
        g.MapPost("/contacts/{id:guid}/cancel", async (Guid id, ReasonRequest r, ClaimsPrincipal u, FollowUpService svc) => Err(await svc.CancelAsync(id, r.Reason ?? "", Me(u)))).RequireAuthorization(Permissions.FollowUpRecord);
        g.MapPost("/contacts/{id:guid}/close-escalation", async (Guid id, ReasonRequest r, ClaimsPrincipal u, FollowUpService svc) => Err(await svc.CloseEscalationAsync(id, r.Reason ?? "", Me(u)))).RequireAuthorization(Permissions.FollowUpRecord);
        g.MapPost("/contacts/{id:guid}/send-link", async (Guid id, ClaimsPrincipal u, FollowUpService svc) => Err(await svc.SendLinkNowAsync(id, Me(u)))).RequireAuthorization(Permissions.FollowUpRecord);
        g.MapPost("/contacts/{id:guid}/incident", async (Guid id, IncidentRequest r, ClaimsPrincipal u, FollowUpService svc) =>
        {
            var (iid, errors) = await svc.RaiseIncidentAsync(id, r.Description, r.Harm, Me(u));
            return iid is null || errors.Count > 0 ? Results.BadRequest(new { errors }) : Results.Ok(new { incidentId = iid });
        }).RequireAuthorization(Permissions.FollowUpRecord);

        // ---- SSI
        g.MapGet("/ssi", async (bool? open, SsiService svc, PatientsDbContext patients) =>
        {
            var list = await svc.ListAsync(open ?? false);
            var ids = list.Select(x => x.PatientId).Distinct().ToList();
            var names = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}");
            return Results.Ok(list.Select(c => SsiView(c, names.GetValueOrDefault(c.PatientId))));
        }).RequireAuthorization(Permissions.FollowUpAny);
        g.MapGet("/ssi/{id:guid}", async (Guid id, ClaimsPrincipal u, HttpContext http, SsiService svc, PatientsDbContext patients) =>
        {
            var c = await svc.GetAsync(id, Me(u), AuthEndpoints.Workstation(http));
            if (c is null) return Results.NotFound();
            var p = await patients.Patients.AsNoTracking().Where(p => p.Id == c.PatientId).Select(p => new { p.FamilyName, p.GivenNames }).SingleOrDefaultAsync();
            return Results.Ok(SsiView(c, p is null ? null : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}"));
        }).RequireAuthorization(Permissions.FollowUpAny);
        g.MapPost("/ssi", async (SsiOpen r, ClaimsPrincipal u, SsiService svc) =>
        {
            var (c, errors) = await svc.OpenAsync(r, Me(u));
            return c is null ? Results.BadRequest(new { errors }) : Results.Ok(new { c.Id, c.Number });
        }).RequireAuthorization(Permissions.FollowUpRecord);
        g.MapPost("/ssi/{id:guid}/confirm", async (Guid id, ConfirmRequest r, ClaimsPrincipal u, SsiService svc) => Err(await svc.ConfirmAsync(id, r.Class, r.Note, Me(u)))).RequireAuthorization(Permissions.FollowUpSsi);
        g.MapPost("/ssi/{id:guid}/incident", async (Guid id, ClaimsPrincipal u, SsiService svc) =>
        {
            var (iid, errors) = await svc.RaiseIncidentAsync(id, Me(u));
            return iid is null || errors.Count > 0 ? Results.BadRequest(new { errors }) : Results.Ok(new { incidentId = iid });
        }).RequireAuthorization(Permissions.FollowUpSsi);
        g.MapPost("/ssi/export", async (DateOnly from, DateOnly to, CodingEndpoints.DownloadRequest r, ClaimsPrincipal u, SsiService svc, SecureExportService export) =>
        {
            if (to < from || to.DayNumber - from.DayNumber > 800) return Results.BadRequest(new { errors = new[] { "Choose a period of up to two years." } });
            var bytes = await svc.ExportCsvAsync(from, to);
            var name = $"TDS-SSI-surveillance-{from:yyyyMMdd}-{to:yyyyMMdd}";
            return await export.ExportAsync(r.Export, name, [new(name + ".csv", bytes)], Me(u), "SsiExport");
        }).RequireAuthorization(Permissions.FollowUpReport);

        g.MapGet("/report", async (DateOnly? from, DateOnly? to, FollowUpService svc, TimeProvider clock) =>
        {
            var t = to ?? NzTime.Today(clock);
            return Results.Ok(await svc.ReportAsync(from ?? t.AddMonths(-3), t));
        }).RequireAuthorization(Permissions.FollowUpReport);

        // ---- plans & settings
        g.MapGet("/plans", async (FollowUpService svc) => Results.Ok((await svc.PlansAsync()).Select(p => new
        {
            p.Id,
            p.Name,
            p.ProcedureContains,
            p.IsDefault,
            p.IsActive,
            p.MaxAttempts,
            p.SortOrder,
            p.Notes,
            p.UpdatedBy,
            p.UpdatedAt,
            steps = p.Steps.OrderBy(s => s.Sequence).Select(s => new { kind = s.Kind.ToString(), method = s.Method.ToString(), s.Days, s.DaysWithImplant, s.WorkingDays, s.OnlyIfHighRisk, s.AssignTo, s.Label })
        }))).RequireAuthorization(Permissions.FollowUpAny);
        g.MapPost("/plans/examples", async (ClaimsPrincipal u, FollowUpService svc) => Results.Ok(new { added = await svc.SeedExamplesAsync(Me(u)) })).RequireAuthorization(Permissions.FollowUpPlans);
        g.MapPost("/plans", async (PlanEdit e, ClaimsPrincipal u, FollowUpService svc) =>
        {
            var (p, errors) = await svc.SavePlanAsync(null, e, Me(u));
            return p is null ? Results.BadRequest(new { errors }) : Results.Ok(new { p.Id });
        }).RequireAuthorization(Permissions.FollowUpPlans);
        g.MapPut("/plans/{id:guid}", async (Guid id, PlanEdit e, ClaimsPrincipal u, FollowUpService svc) =>
        {
            try
            {
                var (p, errors) = await svc.SavePlanAsync(id, e, Me(u));
                return p is null ? Results.BadRequest(new { errors }) : Results.Ok(new { p.Id });
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        }).RequireAuthorization(Permissions.FollowUpPlans);
        g.MapGet("/settings", async (FollowUpService svc) =>
        {
            var s = await svc.SettingsAsync();
            return Results.Ok(new { s.DutyMobile, s.PublicUrl, s.SendCheckInLinks, s.LinkDays, s.GpLetter, s.StartFrom, s.LastScanAt });
        }).RequireAuthorization(Permissions.FollowUpAny);
        g.MapPut("/settings", async (SettingsRequest r, ClaimsPrincipal u, FollowUpService svc) =>
        {
            var errors = await svc.SaveSettingsAsync(new FollowUpSettings { DutyMobile = r.DutyMobile, PublicUrl = r.PublicUrl, SendCheckInLinks = r.SendCheckInLinks, LinkDays = r.LinkDays, GpLetter = r.GpLetter, StartFrom = r.StartFrom }, Me(u));
            return errors.Count > 0 ? Results.BadRequest(new { errors }) : Results.Ok();
        }).RequireAuthorization(Permissions.FollowUpPlans);

        // ---- patient check-in (no staff sign-in): secret link + date of birth; rate-limited per IP; the link locks after 5 wrong attempts.
        var c = app.MapGroup("/api/c/{token}").RequireModule(FollowUpModule.Id).RequireRateLimiting("patient-links").AllowAnonymous();
        c.MapGet("/", async (string token, FollowUpService svc) => await svc.PublicInfoAsync(token) is { } info ? Results.Ok(new { info.Status, info.Clinic }) : Results.NotFound());
        c.MapPost("/open", async (string token, DobRequest r, FollowUpService svc) =>
        {
            var (ok, error, name) = await svc.VerifyAsync(token, r.DateOfBirth);
            if (!ok) return Results.BadRequest(new { errors = new[] { error } });
            var info = await svc.PublicInfoAsync(token);
            return Results.Ok(new { givenName = name, questions = info!.Questions });
        });
        c.MapPost("/submit", async (string token, CheckInRequest r, FollowUpService svc) =>
        {
            var (ok, errors, concern) = await svc.SubmitAsync(token, r.DateOfBirth, r.Answers ?? []);
            return ok ? Results.Ok(new { concern }) : Results.BadRequest(new { errors });
        });
    }
}
