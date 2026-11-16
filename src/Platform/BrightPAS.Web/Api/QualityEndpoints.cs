using BrightPAS.Core;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Data;
using BrightPAS.Identity;
using BrightPAS.Modules.Assets;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Quality;
using BrightPAS.Web.Licensing;
using BrightPAS.Web.Security;

namespace BrightPAS.Web.Api;

/// <summary>Quality management (0.19.0, ADR 0031): incidents, complaints, compliments, risks and hazards, corrective actions, policies, indicators and the governance pack.</summary>
public static class QualityEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Errors(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });
    private static IResult Saved<T>((T? Row, IReadOnlyList<string> Errors) r, Func<T, object>? shape = null) where T : class =>
        r.Errors.Count > 0 ? Results.BadRequest(new { errors = r.Errors }) : Results.Ok(shape is null ? r.Row : shape(r.Row!));

    private static async Task<IResult> Guard(Func<Task<IResult>> f)
    {
        try { return await f(); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { errors = new[] { ex.Message } }); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { errors = new[] { ex.Message } }); }
    }

    public sealed record TriageRequest(int Sac, Harm Harm, bool AlwaysReport, string? Investigator, Agency[]? Notify);
    public sealed record NotifyRequest(string? Reference);
    public sealed record EvidenceRequest(string Evidence);
    public sealed record ValueRequest(DateOnly Month, decimal Numerator, decimal? Denominator);
    public sealed record SettingsRequest(string EscalateTo, string? ExtraNonWorkingDays);
    public sealed record PackRequest(string? Month, ExportOptions? Export);

    private static async Task<Dictionary<Guid, string>> NamesAsync(PatientsDbContext patients, IEnumerable<Guid?> ids)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        return list.Count == 0 ? [] : await patients.Patients.AsNoTracking().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}{(p.Nhi is null ? "" : $" ({p.Nhi})")}");
    }

    public static void MapQualityEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/quality").RequireModule(QualityModule.Id);

        // ------------------------------------------------------------ every staff member: report, my reports, policies to read
        g.MapPost("/incidents", (IncidentReport r, ClaimsPrincipal u, QualityService svc) => Guard(async () =>
            Saved(await svc.ReportAsync(r, Me(u)), i => new { i.Id, i.Number, escalated = i.EscalatedAt is not null }))).RequireAuthorization(Permissions.QualityReport);
        g.MapGet("/my", async (ClaimsPrincipal u, QualityDbContext db, QualityService svc) =>
        {
            var me = Me(u);
            var mine = await db.Incidents.AsNoTracking().Where(i => i.ReportedBy == me).OrderByDescending(i => i.ReportedAt).Take(30)
                .Select(i => new { i.Id, i.Number, i.ReportedAt, category = i.Category.ToString(), status = i.Status.ToString(), i.LearningSummary }).ToListAsync();
            var acks = (await svc.AcknowledgementsAsync(user: me)).Select(a => (a.DocumentId, a.Version)).ToHashSet();
            var toRead = (await svc.PoliciesAsync()).Where(p => p.IsActive && p.RequiresAcknowledgement && !acks.Contains((p.Id, p.Version)))
                .Select(p => new { p.Id, p.Code, p.Title, p.Version, p.Link });
            return Results.Ok(new
            {
                incidents = mine,
                toRead,
                learning = await db.Incidents.AsNoTracking().Where(i => i.Status == IncidentStatus.Closed && i.LearningSummary != null)
                .OrderByDescending(i => i.ClosedAt).Take(10).Select(i => new { i.Number, category = i.Category.ToString(), i.LearningSummary, i.ClosedAt }).ToListAsync()
            });
        }).RequireAuthorization(Permissions.QualityReport);
        g.MapGet("/policies", async (ClaimsPrincipal u, QualityService svc) =>
        {
            var acks = (await svc.AcknowledgementsAsync(user: Me(u))).Select(a => (a.DocumentId, a.Version)).ToHashSet();
            return Results.Ok((await svc.PoliciesAsync()).Where(p => p.IsActive || u.HasClaim(Permissions.ClaimType, Permissions.QualityManage))
                .Select(p => new { p.Id, p.Code, p.Title, p.Version, p.Owner, p.Link, p.ReviewBy, p.RequiresAcknowledgement, p.IsActive, acknowledged = acks.Contains((p.Id, p.Version)) }));
        }).RequireAuthorization(Permissions.QualityReport);
        g.MapPost("/policies/{id:guid}/acknowledge", (Guid id, ClaimsPrincipal u, QualityService svc) => Guard(async () =>
            await svc.AcknowledgeAsync(id, Me(u)) is { } e ? Results.BadRequest(new { errors = new[] { e } }) : Results.Ok())).RequireAuthorization(Permissions.QualityReport);
        g.MapGet("/lookups", async (PasDbContext core, AssetsDbContext assets, BrightPAS.Web.Licensing.LicenceCache licence) => Results.Ok(new
        {
            locations = await core.Locations.AsNoTracking().Where(l => l.IsActive).OrderBy(l => l.Name).Select(l => l.Name).ToListAsync(),
            equipment = licence.Status.AccessTo(AssetsModule.Id) == BrightPAS.Licensing.ModuleAccess.None ? []
                : await assets.Equipment.AsNoTracking().OrderBy(e => e.AssetNumber).Select(e => new { e.Id, label = e.AssetNumber + " " + e.Name }).Take(500).ToListAsync(),
            alwaysReport = QualityService.AlwaysReportExamples,
        })).RequireAuthorization(Permissions.QualityReport);

        // ------------------------------------------------------------ dashboard and registers (quality managers and governance)
        g.MapGet("/summary", async (QualityDbContext db, QualityService svc, IndicatorService ind, TimeProvider clock) =>
        {
            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var complaints = await svc.ComplaintsAsync(true);
            var indicators = new List<object>();
            foreach (var i in await ind.ListAsync(true))
            {
                var s = await ind.SeriesAsync(i.Id, 12);
                indicators.Add(new { i.Id, i.Code, i.Name, i.Scale, i.Target, s.Rag, s.Latest, signal = s.Points.Any(p => p.OutsideLimits || p.Shift || p.Trend) });
            }
            return Results.Ok(new
            {
                incidentsOpen = await db.Incidents.CountAsync(i => i.Status != IncidentStatus.Closed),
                incidentsUntriaged = await db.Incidents.CountAsync(i => i.Status == IncidentStatus.Reported),
                sac12Open = await db.Incidents.CountAsync(i => i.Status != IncidentStatus.Closed && (i.Sac <= 2 || i.EscalatedAt != null)),
                complaintsOpen = complaints.Count,
                complaintsOverdue = complaints.Count(c => svc.Overdue(c) is not null),
                actionsOverdue = await db.Actions.CountAsync(a => a.Status == ActionStatus.Open && a.DueOn < today),
                actionsToVerify = await db.Actions.CountAsync(a => a.Status == ActionStatus.Done),
                risksHigh = await db.Risks.CountAsync(r => r.Status != RiskStatus.Closed && !r.IsHazard && r.Likelihood * r.Consequence >= 10),
                risksReviewDue = await db.Risks.CountAsync(r => r.Status != RiskStatus.Closed && r.ReviewBy < today),
                policiesReviewDue = await db.Policies.CountAsync(p => p.IsActive && p.ReviewBy < today),
                indicators,
            });
        }).RequireAuthorization(Permissions.QualityAny);

        g.MapGet("/incidents", async (bool? open, QualityService svc, PatientsDbContext patients) =>
        {
            var list = await svc.IncidentsAsync(open ?? true);
            var names = await NamesAsync(patients, list.Select(i => i.PatientId));
            return Results.Ok(list.Select(i => new
            {
                i.Id,
                i.Number,
                i.ReportedAt,
                i.OccurredAt,
                i.Location,
                category = i.Category.ToString(),
                harm = i.Harm.ToString(),
                i.Sac,
                i.AlwaysReport,
                status = i.Status.ToString(),
                escalated = i.EscalatedAt is not null,
                patient = i.PatientId is { } p ? names.GetValueOrDefault(p) : null,
                reportedBy = i.ReportedBy ?? "Anonymous"
            }));
        }).RequireAuthorization(Permissions.QualityAny);
        g.MapGet("/incidents/{id:guid}", async (Guid id, QualityService svc, PatientsDbContext patients, AssetsDbContext assets) =>
        {
            var i = await svc.IncidentAsync(id);
            if (i is null) return Results.NotFound();
            var names = await NamesAsync(patients, [i.PatientId]);
            var eq = i.EquipmentId is { } e ? await assets.Equipment.AsNoTracking().Where(x => x.Id == e).Select(x => x.AssetNumber + " " + x.Name).FirstOrDefaultAsync() : null;
            var actions = await svc.ActionsAsync(false, i.Id);
            var settings = await svc.SettingsAsync();
            return Results.Ok(new
            {
                i.Id,
                i.Number,
                i.ReportedAt,
                reportedBy = i.ReportedBy ?? "Anonymous",
                i.OccurredAt,
                i.Location,
                category = i.Category.ToString(),
                i.Description,
                i.ImmediateAction,
                i.PatientId,
                patient = i.PatientId is { } p ? names.GetValueOrDefault(p) : null,
                i.BookingId,
                equipment = eq,
                i.StaffInvolved,
                harm = i.Harm.ToString(),
                i.Sac,
                i.AlwaysReport,
                status = i.Status.ToString(),
                i.Investigator,
                i.TriagedBy,
                i.TriagedAt,
                i.EscalatedAt,
                escalateTo = settings.EscalateTo,
                i.Findings,
                i.ContributingFactors,
                i.LearningSummary,
                i.ClosedBy,
                i.ClosedAt,
                disclosures = i.Disclosures.OrderBy(d => d.At).Select(d => new { d.At, d.By, d.PersonTold, d.Method, d.Notes }),
                notifications = i.Notifications.OrderBy(n => n.Agency).Select(n => new { agency = n.Agency.ToString(), n.NotifiedAt, n.NotifiedBy, n.Reference }),
                actions = actions.Select(a => new { a.Id, a.Number, a.Title, a.Owner, a.DueOn, status = a.Status.ToString() }),
            });
        }).RequireAuthorization(Permissions.QualityAny);
        g.MapPost("/incidents/{id:guid}/triage", (Guid id, TriageRequest r, ClaimsPrincipal u, QualityService svc) => Guard(async () =>
            Errors(await svc.TriageAsync(id, new IncidentTriage(r.Sac, r.Harm, r.AlwaysReport, r.Investigator, r.Notify ?? []), Me(u))))).RequireAuthorization(Permissions.QualityManage);
        g.MapPost("/incidents/{id:guid}/findings", (Guid id, IncidentFindings r, ClaimsPrincipal u, QualityService svc) => Guard(async () => Errors(await svc.FindingsAsync(id, r, Me(u))))).RequireAuthorization(Permissions.QualityManage);
        g.MapPost("/incidents/{id:guid}/disclosures", (Guid id, DisclosureEdit r, ClaimsPrincipal u, QualityService svc) => Guard(async () => Errors(await svc.DiscloseAsync(id, r, Me(u))))).RequireAuthorization(Permissions.QualityManage);
        g.MapPost("/incidents/{id:guid}/notifications/{agency}", (Guid id, Agency agency, NotifyRequest r, ClaimsPrincipal u, QualityService svc) => Guard(async () => Errors(await svc.NotifiedAsync(id, agency, r.Reference, Me(u))))).RequireAuthorization(Permissions.QualityManage);
        g.MapPost("/incidents/{id:guid}/close", (Guid id, ClaimsPrincipal u, QualityService svc) => Guard(async () => Errors(await svc.CloseIncidentAsync(id, Me(u))))).RequireAuthorization(Permissions.QualityManage);

        g.MapGet("/complaints", async (bool? open, QualityService svc, PatientsDbContext patients) =>
        {
            var list = await svc.ComplaintsAsync(open ?? true);
            var names = await NamesAsync(patients, list.Select(c => c.PatientId));
            return Results.Ok(list.Select(c => new
            {
                c.Id,
                c.Number,
                c.ReceivedOn,
                c.Complainant,
                c.Category,
                status = c.Status.ToString(),
                c.AcknowledgeBy,
                c.AcknowledgedOn,
                c.DecideBy,
                c.RespondedOn,
                outcome = c.Outcome?.ToString(),
                c.Owner,
                overdue = svc.Overdue(c),
                patient = c.PatientId is { } p ? names.GetValueOrDefault(p) : null
            }));
        }).RequireAuthorization(Permissions.QualityAny);
        g.MapGet("/complaints/{id:guid}", async (Guid id, QualityDbContext db, QualityService svc, PatientsDbContext patients) =>
        {
            var c = await db.Complaints.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            var names = await NamesAsync(patients, [c.PatientId]);
            return Results.Ok(new
            {
                complaint = c,
                status = c.Status.ToString(),
                outcome = c.Outcome?.ToString(),
                overdue = svc.Overdue(c),
                patient = c.PatientId is { } p ? names.GetValueOrDefault(p) : null,
                incident = c.IncidentId is { } iid ? await db.Incidents.AsNoTracking().Where(i => i.Id == iid).Select(i => i.Number).FirstOrDefaultAsync() : null,
                actions = (await svc.ActionsAsync(false, c.Id)).Select(a => new { a.Id, a.Number, a.Title, a.Owner, a.DueOn, status = a.Status.ToString() })
            });
        }).RequireAuthorization(Permissions.QualityAny);
        g.MapPost("/complaints", (ComplaintEdit r, ClaimsPrincipal u, QualityService svc) => Guard(async () => Saved(await svc.NewComplaintAsync(r, Me(u)), c => new { c.Id, c.Number, c.AcknowledgeBy }))).RequireAuthorization(Permissions.QualityManage);
        g.MapPost("/complaints/{id:guid}/step", (Guid id, ComplaintStep r, ClaimsPrincipal u, QualityService svc) => Guard(async () => Errors(await svc.ComplaintStepAsync(id, r, Me(u))))).RequireAuthorization(Permissions.QualityManage);
        g.MapGet("/compliments", async (QualityService svc) => Results.Ok(await svc.ComplimentsAsync())).RequireAuthorization(Permissions.QualityAny);
        g.MapPost("/compliments", (ComplimentEdit r, ClaimsPrincipal u, QualityService svc) => Guard(async () => Saved(await svc.ComplimentAsync(r, Me(u)), c => new { c.Id }))).RequireAuthorization(Permissions.QualityManage);
        g.MapPost("/compliments/{id:guid}/share", (Guid id, ClaimsPrincipal u, QualityService svc) => Guard(async () => { await svc.ShareComplimentAsync(id, Me(u)); return Results.Ok(); })).RequireAuthorization(Permissions.QualityManage);

        g.MapGet("/risks", async (bool? hazards, QualityService svc) => Results.Ok((await svc.RisksAsync(hazards ?? false)).Select(r => new
        {
            r.Id,
            r.Number,
            r.IsHazard,
            r.Title,
            category = r.Category.ToString(),
            r.Description,
            r.Owner,
            r.Likelihood,
            r.Consequence,
            score = QualityService.Score(r.Likelihood, r.Consequence),
            band = QualityService.Band(QualityService.Score(r.Likelihood, r.Consequence)),
            r.Controls,
            r.ResidualLikelihood,
            r.ResidualConsequence,
            residualBand = r.ResidualLikelihood is { } rl && r.ResidualConsequence is { } rc ? QualityService.Band(rl * rc) : null,
            r.Location,
            r.EquipmentId,
            r.ReviewBy,
            status = r.Status.ToString(),
        }))).RequireAuthorization(Permissions.QualityAny);
        g.MapPost("/risks", (RiskEdit r, ClaimsPrincipal u, QualityService svc) => Guard(async () => Saved(await svc.SaveRiskAsync(null, r, Me(u)), x => new { x.Id, x.Number }))).RequireAuthorization(Permissions.QualityManage);
        g.MapPut("/risks/{id:guid}", (Guid id, RiskEdit r, ClaimsPrincipal u, QualityService svc) => Guard(async () => Saved(await svc.SaveRiskAsync(id, r, Me(u)), x => new { x.Id, x.Number }))).RequireAuthorization(Permissions.QualityManage);

        g.MapGet("/actions", async (bool? open, QualityService svc, TimeProvider clock) =>
        {
            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            return Results.Ok((await svc.ActionsAsync(open ?? true)).Select(a => new
            {
                a.Id,
                a.Number,
                source = a.Source.ToString(),
                a.SourceId,
                a.SourceRef,
                a.Title,
                a.Detail,
                a.Owner,
                a.DueOn,
                status = a.Status.ToString(),
                overdue = a.Status == ActionStatus.Open && a.DueOn < today,
                a.Evidence,
                a.DoneBy,
                a.DoneAt,
                a.VerifiedBy,
                a.VerifiedAt
            }));
        }).RequireAuthorization(Permissions.QualityAny);
        g.MapPost("/actions", (ActionEdit r, ClaimsPrincipal u, QualityService svc) => Guard(async () => Saved(await svc.NewActionAsync(r, Me(u)), a => new { a.Id, a.Number }))).RequireAuthorization(Permissions.QualityManage);
        g.MapPost("/actions/{id:guid}/done", (Guid id, EvidenceRequest r, ClaimsPrincipal u, QualityService svc) => Guard(async () => Errors(await svc.ActionDoneAsync(id, r.Evidence, Me(u))))).RequireAuthorization(Permissions.QualityManage);
        g.MapPost("/actions/{id:guid}/verify", (Guid id, ClaimsPrincipal u, QualityService svc) => Guard(async () => Errors(await svc.ActionVerifyAsync(id, Me(u))))).RequireAuthorization(Permissions.QualityManage);

        g.MapPost("/policies", (PolicyEdit r, ClaimsPrincipal u, QualityService svc) => Guard(async () => Saved(await svc.SavePolicyAsync(null, r, Me(u)), p => new { p.Id }))).RequireAuthorization(Permissions.QualityManage);
        g.MapPut("/policies/{id:guid}", (Guid id, PolicyEdit r, ClaimsPrincipal u, QualityService svc) => Guard(async () => Saved(await svc.SavePolicyAsync(id, r, Me(u)), p => new { p.Id }))).RequireAuthorization(Permissions.QualityManage);
        g.MapGet("/policies/{id:guid}/acknowledgements", async (Guid id, QualityService svc) => Results.Ok(await svc.AcknowledgementsAsync(id))).RequireAuthorization(Permissions.QualityManage);

        // ------------------------------------------------------------ indicators
        g.MapGet("/indicators", async (IndicatorService svc) => Results.Ok(new { indicators = await svc.ListAsync(false), calculators = svc.AvailableCalculators.Select(c => new { key = c.Key, name = c.Name }) })).RequireAuthorization(Permissions.QualityAny);
        g.MapGet("/indicators/{id:guid}/series", (Guid id, int? months, IndicatorService svc) => Guard(async () =>
        {
            var s = await svc.SeriesAsync(id, Math.Clamp(months ?? 12, 3, 36));
            return Results.Ok(new { indicator = s.Indicator, s.Points, s.Centre, s.Chart, s.Rag, s.Latest, equity = s.Equity.Select(e => new { group = e.Group, numerator = e.Numerator, denominator = e.Denominator, rate = e.Rate }) });
        })).RequireAuthorization(Permissions.QualityAny);
        g.MapGet("/indicators/{id:guid}/cases", (Guid id, DateOnly month, IndicatorService svc) => Guard(async () => Results.Ok(await svc.CasesAsync(id, month)))).RequireAuthorization(Permissions.QualityManage);
        g.MapPost("/indicators", (IndicatorEdit r, ClaimsPrincipal u, IndicatorService svc) => Guard(async () => Saved(await svc.SaveAsync(null, r, Me(u)), i => new { i.Id }))).RequireAuthorization(Permissions.QualityManage);
        g.MapPut("/indicators/{id:guid}", (Guid id, IndicatorEdit r, ClaimsPrincipal u, IndicatorService svc) => Guard(async () => Saved(await svc.SaveAsync(id, r, Me(u)), i => new { i.Id }))).RequireAuthorization(Permissions.QualityManage);
        g.MapPost("/indicators/{id:guid}/values", (Guid id, ValueRequest r, ClaimsPrincipal u, IndicatorService svc) => Guard(async () => Errors(await svc.EnterValueAsync(id, r.Month, r.Numerator, r.Denominator, Me(u))))).RequireAuthorization(Permissions.QualityManage);

        g.MapGet("/settings", async (QualityService svc) => Results.Ok(await svc.SettingsAsync())).RequireAuthorization(Permissions.QualityManage);
        g.MapPut("/settings", async (SettingsRequest r, ClaimsPrincipal u, QualityService svc) => Errors(await svc.SaveSettingsAsync(r.EscalateTo, r.ExtraNonWorkingDays, Me(u)))).RequireAuthorization(Permissions.QualityManage);

        // ------------------------------------------------------------ governance pack (encrypted PDF like every export)
        g.MapPost("/pack", async (PackRequest r, ClaimsPrincipal u, QualityService svc, IndicatorService ind, QualityDbContext db, PasDbContext core, SecureExportService export, TimeProvider clock) =>
        {
            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var month = r.Month is { Length: 7 } m && DateOnly.TryParseExact(m + "-01", "yyyy-MM-dd", out var d) ? d : new DateOnly(today.Year, today.Month, 1);
            var series = new List<IndicatorSeries>();
            foreach (var i in await ind.ListAsync(true)) series.Add(await ind.SeriesAsync(i.Id, 12));
            var org = await core.Organisations.AsNoTracking().Select(o => o.Name).FirstOrDefaultAsync() ?? "Tasman Day Surgery";
            var since = NzTime.StartOfDay(month.AddMonths(-12));
            var data = new BrightPAS.Web.Pdf.QualityPackData(org, month, series,
                await db.Incidents.AsNoTracking().Where(i => i.Status != IncidentStatus.Closed || i.ReportedAt >= since || i.ClosedAt >= since).ToListAsync(),
                await db.Complaints.AsNoTracking().Where(c => c.Status != ComplaintStatus.Closed || c.ReceivedOn >= month).ToListAsync(),
                await db.Compliments.CountAsync(c => c.ReceivedOn >= month && c.ReceivedOn < month.AddMonths(1)),
                await db.Risks.AsNoTracking().Where(x => !x.IsHazard).ToListAsync(), await db.Actions.AsNoTracking().ToListAsync(),
                await db.Policies.AsNoTracking().Where(p => p.IsActive && p.ReviewBy < today.AddDays(30)).OrderBy(p => p.ReviewBy).ToListAsync(), svc.Overdue, today);
            return await export.ExportPdfAsync((r.Export ?? new(true, null)) with { Format = "pdf" }, $"BrightPAS-quality-pack-{month:yyyy-MM}", pw => BrightPAS.Web.Pdf.QualityPdf.Pack(data, pw), Me(u), "QualityPack");
        }).RequireAuthorization(Permissions.QualityAny);
    }
}
