using System.Security.Claims;
using System.Text.Json;
using BrightPAS.Identity;
using BrightPAS.Modules.Triage;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>Patient triage and suitability (0.22.0, spec 7.25, ADR 0035).</summary>
public static class TriageEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static string Role(ClaimsPrincipal u) => string.Join(", ", u.FindAll(ClaimTypes.Role).Select(c => c.Value)) is { Length: > 0 } r ? r : "user";
    private static bool Has(ClaimsPrincipal u, string p) => u.HasClaim(Permissions.ClaimType, p);
    private static IResult Err(string? e) => e is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { e } });

    private static async Task<IResult> Guard(Func<Task<IResult>> f)
    {
        try { return await f(); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { errors = new[] { ex.Message } }); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { errors = new[] { ex.Message } }); }
    }

    public sealed record RunRequest(Guid PatientId, Guid? BookingId, IReadOnlyList<StaffInput>? Inputs);
    public sealed record InputsRequest(IReadOnlyList<StaffInput> Inputs);
    public sealed record ApproveRequest(string? Note);

    private static object View(Assessment a) => new
    {
        a.Id,
        a.PatientId,
        a.BookingId,
        a.FormRequestId,
        a.PreviousId,
        trigger = a.Trigger.ToString(),
        a.TriggerDetail,
        status = a.Status.ToString(),
        a.Procedure,
        a.Anaesthetic,
        a.ProcedureDate,
        a.RuleSetVersion,
        a.RulesApproved,
        a.Bmi,
        a.StopBang,
        a.StopBangKnown,
        a.Frailty,
        a.Vte,
        a.Rcri,
        a.SuggestedAsa,
        a.ConfirmedAsa,
        suggested = a.SuggestedOutcome.ToString(),
        suggestedLabel = Outcomes.Labels[a.SuggestedOutcome],
        final = a.FinalOutcome?.ToString(),
        finalLabel = a.FinalOutcome is { } f ? Outcomes.Labels[f] : null,
        a.Precautions,
        a.Missing,
        a.ChangeSummary,
        a.Worse,
        a.Notes,
        a.OverrideReason,
        a.SignedOffBy,
        a.SignedOffRole,
        a.SignedOffAt,
        a.CreatedBy,
        a.CreatedAt,
        findings = a.Findings.Select(x => new { x.Source, outcome = x.Outcome.ToString(), label = Outcomes.Labels[x.Outcome], x.Text, x.Precaution }),
        scores = TriageService.ScoresOf(a),
        staff = string.IsNullOrWhiteSpace(a.StaffInputsJson) ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(a.StaffInputsJson, JsonSerializerOptions.Web),
        facts = JsonSerializer.Deserialize<Dictionary<string, string>>(a.FactsJson, JsonSerializerOptions.Web)
    };

    public static void MapTriageEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/triage").RequireModule(TriageModule.Id);

        g.MapGet("/meta", (ClaimsPrincipal u) => Results.Ok(new
        {
            outcomes = Outcomes.Labels.Select(kv => new { value = kv.Key.ToString(), label = kv.Value }),
            staffFacts = TriageService.StaffFacts.Select(kv => new { fact = kv.Key, label = kv.Value }),
            ruleFacts = TriageScoring.RuleFacts.Select(kv => new { fact = kv.Key, label = kv.Value }),
            canAssess = Has(u, Permissions.TriageAssess),
            canReview = Has(u, Permissions.TriageReview),
            canRules = Has(u, Permissions.TriageRules),
            canApprove = Has(u, Permissions.TriageApprove)
        })).RequireAuthorization(Permissions.TriageAny);

        g.MapGet("/worklist", async (string? view, TriageService svc) => Results.Ok(await svc.WorklistAsync(view ?? "open"))).RequireAuthorization(Permissions.TriageAny);

        g.MapGet("/patients/{patientId:guid}", async (Guid patientId, TriageService svc) => Results.Ok((await svc.HistoryAsync(patientId)).Select(a => new
        {
            a.Id,
            a.Procedure,
            a.ProcedureDate,
            status = a.Status.ToString(),
            suggested = Outcomes.Labels[a.SuggestedOutcome],
            final = a.FinalOutcome is { } f ? Outcomes.Labels[f] : null,
            trigger = a.Trigger.ToString(),
            a.CreatedAt,
            a.SignedOffBy,
            a.SignedOffAt,
            a.Worse,
            a.Precautions
        }))).RequireAuthorization(Permissions.TriageAny);

        g.MapGet("/assessments/{id:guid}", (Guid id, HttpContext http, ClaimsPrincipal u, TriageService svc) => Guard(async () =>
        {
            var a = await svc.GetAsync(id, Me(u), AuthEndpoints.Workstation(http));
            return a is null ? Results.NotFound() : Results.Ok(View(a));
        })).RequireAuthorization(Permissions.TriageAny);

        g.MapPost("/run", (RunRequest r, ClaimsPrincipal u, TriageService svc) => Guard(async () =>
        {
            var a = await svc.RunAsync(r.PatientId, r.BookingId, TriageTrigger.Manual, "Run by " + Me(u), Me(u), r.Inputs);
            return Results.Ok(new { a.Id });
        })).RequireAuthorization(Permissions.TriageAssess);

        g.MapPost("/assessments/{id:guid}/inputs", (Guid id, InputsRequest r, ClaimsPrincipal u, TriageService svc) => Guard(async () =>
        {
            var cur = await svc.GetAsync(id, Me(u), null) ?? throw new KeyNotFoundException("Assessment not found.");
            if (r.Inputs.Any(i => i.Fact == "asa_confirmed") && !Has(u, Permissions.TriageReview)) return Err("Only an anaesthetist can confirm the ASA grade.");
            var a = await svc.RunAsync(cur.PatientId, cur.BookingId, TriageTrigger.Manual, "Findings updated", Me(u), r.Inputs, cur.FormRequestId);
            return Results.Ok(new { a.Id });
        })).RequireAuthorization(Permissions.TriageAssess);

        g.MapPost("/assessments/{id:guid}/sign-off", async (Guid id, SignOffRequest r, ClaimsPrincipal u, TriageService svc) =>
            Err(await svc.SignOffAsync(id, r, Has(u, Permissions.TriageReview), Me(u), Role(u)))).RequireAuthorization(Permissions.TriageAssess);

        g.MapGet("/bookings", async (string ids, TriageService svc) =>
        {
            var list = ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).Take(200).ToList();
            return Results.Ok((await svc.ForBookingsAsync(list)).ToDictionary(kv => kv.Key.ToString(), kv => (object)new
            {
                assessmentId = kv.Value.Id,
                status = kv.Value.Status.ToString(),
                outcome = (kv.Value.FinalOutcome ?? kv.Value.SuggestedOutcome).ToString(),
                label = Outcomes.Labels[kv.Value.FinalOutcome ?? kv.Value.SuggestedOutcome],
                kv.Value.Precautions
            }));
        }).RequireAuthorization(Permissions.TriageAny);

        g.MapGet("/report", async (DateOnly? from, DateOnly? to, TriageService svc) =>
        {
            var t = to ?? BrightPAS.Core.NzTime.Today(TimeProvider.System);
            return Results.Ok(await svc.ReportAsync(from ?? t.AddMonths(-3), t));
        }).RequireAuthorization(Permissions.TriageAny);

        // ------------------------------------------------------------ rules and settings
        g.MapGet("/rulesets", async (TriageRulesService rules) => Results.Ok((await rules.ListAsync()).Select(r => new
        {
            r.Id,
            r.Version,
            r.Name,
            status = r.Status.ToString(),
            r.IsExample,
            r.CreatedBy,
            r.CreatedAt,
            r.ApprovedBy,
            r.ApprovedRole,
            r.ApprovedAt,
            r.ApprovalNote,
            r.RetiredAt
        }))).RequireAuthorization(Permissions.TriageAny);
        g.MapGet("/rulesets/{id:guid}", async (Guid id, TriageRulesService rules) => await rules.GetAsync(id) is { } r ? Results.Ok(new
        {
            r.Id,
            r.Version,
            r.Name,
            r.Notes,
            status = r.Status.ToString(),
            r.IsExample,
            r.ApprovedBy,
            r.ApprovedAt,
            r.ApprovalNote,
            rules = r.Rules.Select(x => new { x.Code, x.Name, x.Fact, op = x.Operator.ToString(), x.Threshold, x.TextValue, x.ProcedureContains, x.Anaesthetic, outcome = x.Outcome.ToString(), x.Precaution, x.Explanation, x.SortOrder, x.IsActive }),
            medicines = r.Medicines.Select(x => new { x.Name, x.Keywords, x.Instruction, outcome = x.Outcome.ToString(), x.IsActive })
        }) : Results.NotFound()).RequireAuthorization(Permissions.TriageAny);
        g.MapPost("/rulesets/{id:guid}/copy", async (Guid id, ClaimsPrincipal u, TriageRulesService rules) => { var (nid, err) = await rules.CopyAsync(id, Me(u)); return err is null ? Results.Ok(new { id = nid }) : Err(err); })
            .RequireAuthorization(Permissions.TriageRules);
        g.MapPost("/rulesets/example", async (ClaimsPrincipal u, TriageRulesService rules) => await rules.SeedExampleAsync(Me(u)) is { } r ? Results.Ok(new { r.Id }) : Err("Rule sets already exist – copy one to make a new draft."))
            .RequireAuthorization(Permissions.TriageRules);
        g.MapPut("/rulesets/{id:guid}", async (Guid id, RuleSetEdit e, ClaimsPrincipal u, TriageRulesService rules) =>
            await rules.SaveDraftAsync(id, e, Me(u)) is { Count: > 0 } errs ? Results.BadRequest(new { errors = errs }) : Results.Ok()).RequireAuthorization(Permissions.TriageRules);
        g.MapPost("/rulesets/{id:guid}/approve", async (Guid id, ApproveRequest r, ClaimsPrincipal u, TriageRulesService rules) => Err(await rules.ApproveAsync(id, r.Note, Me(u), Role(u))))
            .RequireAuthorization(Permissions.TriageApprove);
        g.MapDelete("/rulesets/{id:guid}", async (Guid id, ClaimsPrincipal u, TriageRulesService rules) => Err(await rules.DeleteDraftAsync(id, Me(u)))).RequireAuthorization(Permissions.TriageRules);

        // 0.36.0 anaesthetist reviews (video, phone or in person).
        g.MapGet("/anaesthetists", async (ReviewService rv) => Results.Ok((await rv.AnaesthetistsAsync()).Select(c => new { c.Id, c.Name }))).RequireAuthorization(Permissions.TriageAny);
        g.MapGet("/reviews", async (DateOnly? from, int? days, Guid? anaesthetistId, Guid? assessmentId, ReviewService rv) =>
            Results.Ok(await rv.ListAsync(from, days ?? 14, anaesthetistId, assessmentId))).RequireAuthorization(Permissions.TriageAny);
        g.MapPost("/reviews", async (ReviewEdit e, ClaimsPrincipal u, ReviewService rv) =>
        {
            var (r, errors) = await rv.BookAsync(e, Me(u));
            return r is null ? Results.BadRequest(new { errors }) : Results.Ok(new { r.Id, r.JoinLink });
        }).RequireAuthorization(Permissions.TriageAssess);
        g.MapPost("/reviews/{id:guid}/finish", async (Guid id, ReviewFinish f, ClaimsPrincipal u, ReviewService rv) =>
            f.Status == ReviewStatus.Done && !Has(u, Permissions.TriageReview) ? Err("Only an anaesthetist can record the review.") : Err(await rv.FinishAsync(id, f, Me(u))))
            .RequireAuthorization(Permissions.TriageAssess);

        g.MapGet("/settings", async (TriageService svc) => Results.Ok(await svc.SettingsAsync())).RequireAuthorization(Permissions.TriageAny);
        g.MapPut("/settings", async (TriageSettings s, ClaimsPrincipal u, TriageService svc) =>
            await svc.SaveSettingsAsync(s, Me(u)) is { Count: > 0 } errs ? Results.BadRequest(new { errors = errs }) : Results.Ok()).RequireAuthorization(Permissions.TriageRules);
    }
}
