using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Identity;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Medicines;
using BrightPAS.Modules.Patients;
using BrightPAS.Web.Integration;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>Medicines and medicine safety (0.27.0, ADR 0040): medicines list, rules, checks, allergy links, report.</summary>
public static class MedicinesEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static string Role(ClaimsPrincipal u) => string.Join(", ", u.FindAll(ClaimTypes.Role).Select(c => c.Value)) is { Length: > 0 } r ? r : "user";
    private static IResult Err(string? e) => e is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { e } });
    private static IResult Errs(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record CheckRequest(Guid PatientId, Guid? EpisodeId, string Medicine, string Dose, string Route, DateTimeOffset? At, string? Barcode, Guid? MedicineId, decimal? WeightKg);
    public sealed record LinkRequest(string Term);
    public sealed record AliasRequest(string Ingredient, string Alias);
    public sealed record PeekRequest(string Text);
    public sealed record ImportRequest(string FileName, string Text, ImportMapping Mapping);
    public sealed record ApproveRequest(string? Note);
    public sealed record SuggestRequest(IReadOnlyList<string>? Ingredients, bool Children);

    private const int MaxImportChars = 60_000_000;

    public static object FindingView(Finding f) => new { kind = f.Kind.ToString(), severity = f.Severity.ToString(), f.Text, f.RuleRef };

    private static object RuleSetView(MedRuleSet r) => new
    {
        r.Id,
        r.Version,
        r.Name,
        r.Notes,
        status = r.Status.ToString(),
        r.IsExample,
        r.CreatedBy,
        r.CreatedAt,
        r.ApprovedBy,
        r.ApprovedRole,
        r.ApprovedAt,
        r.ApprovalNote,
        doseRules = r.DoseRules.Select(x => new { x.Code, x.Ingredient, x.Route, x.MinAgeYears, x.MaxAgeYears, x.MinWeightKg, x.MaxWeightKg, x.Unit, x.MaxSingle, x.MaxDaily, x.MaxSinglePerKg, x.MaxDailyPerKg, x.MinIntervalHours, severity = x.Severity.ToString(), x.Reference, x.Note }),
        groups = r.Groups.Select(g => new { g.Code, g.Name, g.AllergyClass, sameGroupSeverity = g.SameGroupSeverity.ToString(), members = g.Members.Where(m => !m.IsKeyword).Select(m => m.Term), keywords = g.Members.Where(m => m.IsKeyword).Select(m => m.Term) }),
        crossReactions = r.CrossReactions.Select(x => new { x.FromGroup, x.ToGroup, severity = x.Severity.ToString(), x.Message }),
        interactions = r.Interactions.Select(x => new { x.Code, x.TermA, x.TermB, severity = x.Severity.ToString(), x.Effect, x.Advice, x.Reference }),
    };

    public static void MapMedicinesEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/medicines").RequireModule(MedicinesModule.Id);

        // ---------------------------------------------------------------- medicines list
        g.MapGet("/search", async (string? q, bool? formulary, bool? inactive, MedicineCatalogue cat) => Results.Ok(await cat.SearchAsync(q, formulary == true, inactive == true, string.IsNullOrWhiteSpace(q) ? 100 : 50)))
            .RequireAuthorization(Permissions.MedicinesAny);
        g.MapGet("/barcode/{code}", async (string code, MedicineCatalogue cat) =>
            await cat.ByBarcodeAsync(code) is { } m ? Results.Ok(new { m.Id, m.Name, m.DefaultRoute }) : Results.NotFound(new { errors = new[] { "No medicine in the list has this barcode." } }))
            .RequireAuthorization(Permissions.MedicinesAny);
        g.MapGet("/catalogue/{id:guid}", async (Guid id, MedicineCatalogue cat) =>
        {
            if (await cat.GetAsync(id) is not { } m) return Results.NotFound();
            var names = await cat.IngredientNamesAsync(m.Ingredients.Select(i => i.IngredientId));
            return Results.Ok(new
            {
                m.Id,
                m.Name,
                m.NzmtId,
                m.Form,
                m.DefaultRoute,
                m.Source,
                m.OnFormulary,
                m.IsActive,
                m.UpdatedAt,
                m.UpdatedBy,
                ingredients = m.Ingredients.Where(i => names.ContainsKey(i.IngredientId)).Select(i => new { ingredient = names[i.IngredientId], i.Amount, i.Unit, i.PerMl }).OrderBy(i => i.ingredient, StringComparer.Ordinal),
                gtins = m.Gtins.Select(x => x.Gtin).Order(StringComparer.Ordinal),
            });
        }).RequireAuthorization(Permissions.MedicinesAny);
        g.MapPost("/catalogue", async (MedicineEdit e, ClaimsPrincipal u, MedicineCatalogue cat) =>
        {
            var (id, errors) = await cat.SaveAsync(null, e, Me(u));
            return id is null ? Errs(errors) : Results.Ok(new { id });
        }).RequireAuthorization(Permissions.MedicinesCatalogue);
        g.MapPut("/catalogue/{id:guid}", async (Guid id, MedicineEdit e, ClaimsPrincipal u, MedicineCatalogue cat) =>
        {
            try { var (_, errors) = await cat.SaveAsync(id, e, Me(u)); return Errs(errors); }
            catch (KeyNotFoundException ex) { return Results.NotFound(new { errors = new[] { ex.Message } }); }
        }).RequireAuthorization(Permissions.MedicinesCatalogue);
        g.MapPost("/catalogue/starter", async (ClaimsPrincipal u, MedicineCatalogue cat) => Results.Ok(new { added = await cat.LoadStarterAsync(Me(u)) }))
            .RequireAuthorization(Permissions.MedicinesCatalogue);
        g.MapGet("/aliases", async (string? ingredient, MedicineCatalogue cat) => Results.Ok((await cat.AliasesAsync(ingredient)).Select(a => new { a.Ingredient, a.Alias })))
            .RequireAuthorization(Permissions.MedicinesAny);
        g.MapPost("/aliases", async (AliasRequest r, ClaimsPrincipal u, MedicineCatalogue cat) => Err(await cat.AddAliasAsync(r.Ingredient, r.Alias, Me(u))))
            .RequireAuthorization(Permissions.MedicinesCatalogue);
        g.MapDelete("/aliases/{alias}", async (string alias, ClaimsPrincipal u, MedicineCatalogue cat) => await cat.RemoveAliasAsync(alias, Me(u)) ? Results.Ok() : Results.NotFound())
            .RequireAuthorization(Permissions.MedicinesCatalogue);
        g.MapPost("/import/peek", (PeekRequest r) =>
        {
            if (r.Text.Length > MaxImportChars) return Err("The file is too large (60 MB).");
            var (headers, sample, d) = MedicineCatalogue.Peek(r.Text);
            return headers.Count == 0 ? Err("The file is empty.") : Results.Ok(new { headers, sample, delimiter = d.ToString() });
        }).RequireAuthorization(Permissions.MedicinesCatalogue);
        g.MapPost("/import", async (ImportRequest r, ClaimsPrincipal u, MedicineCatalogue cat) =>
        {
            if (r.Text.Length > MaxImportChars) return Err("The file is too large (60 MB).");
            var (run, errors) = await cat.ImportAsync(r.FileName, r.Text, r.Mapping, Me(u));
            return run is null ? Errs(errors) : Results.Ok(run);
        }).RequireAuthorization(Permissions.MedicinesCatalogue).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(130_000_000));
        g.MapGet("/import/profiles", async (MedicineCatalogue cat) => Results.Ok(await cat.ProfilesAsync())).RequireAuthorization(Permissions.MedicinesCatalogue);
        g.MapGet("/import/runs", async (MedicineCatalogue cat) => Results.Ok(await cat.RunsAsync())).RequireAuthorization(Permissions.MedicinesAny);

        // ---------------------------------------------------------------- checks
        g.MapPost("/check", async (CheckRequest r, ClaimsPrincipal u, MedicineSafetyService safety, MedicineFactsProvider facts) =>
        {
            if (string.IsNullOrWhiteSpace(r.Medicine)) return Err("Enter the medicine.");
            var at = r.At ?? facts.Now;
            try
            {
                var f = await facts.ForAsync(r.PatientId, at);
                var res = await safety.CheckAsync(new SafetyRequest(r.PatientId, r.EpisodeId, r.Medicine, r.Dose ?? "", r.Route ?? "", at, r.Barcode, r.MedicineId, r.WeightKg, null, Me(u), Record: false), f);
                return Results.Ok(new { mode = res.Mode.ToString(), result = res.Result.ToString(), findings = res.Findings.Select(FindingView), res.MedicineId, res.MedicineName, res.WeightKg, res.WeightSource, res.RuleSetVersion, ingredients = res.Ingredients ?? [] });
            }
            catch (KeyNotFoundException ex) { return Results.NotFound(new { errors = new[] { ex.Message } }); }
        }).RequireAuthorization(Permissions.EmrWrite);
        g.MapGet("/patients/{id:guid}/checks", async (Guid id, MedicineSafetyService safety) => Results.Ok(await safety.ChecksAsync(id, null, null, null, null, 200)))
            .RequireAuthorization(Permissions.MedicinesAny);
        g.MapGet("/episodes/{id:guid}/checks", async (Guid id, MedicineSafetyService safety) => Results.Ok(await safety.ChecksAsync(null, id, null, null, null, 200)))
            .RequireAuthorization(Permissions.MedicinesAny);

        // ---------------------------------------------------------------- allergy links
        g.MapGet("/patients/{id:guid}/allergies", async (Guid id, PatientsDbContext patients, MedicineSafetyService safety) =>
        {
            var alerts = await patients.Alerts.AsNoTracking().Where(a => a.PatientId == id && a.IsActive && a.Type == AlertType.Allergy).OrderBy(a => a.RecordedAt).ToListAsync();
            var links = await safety.LinksAsync(id);
            return Results.Ok(alerts.Select(a => new { alertId = a.Id, a.Description, severity = a.Severity.ToString(), term = links.FirstOrDefault(l => l.AlertId == a.Id)?.Term, linkedBy = links.FirstOrDefault(l => l.AlertId == a.Id)?.LinkedBy }));
        }).RequireAuthorization(Permissions.MedicinesAny);
        g.MapGet("/allergens", async (string? q, MedicineSafetyService safety) => Results.Ok((await safety.AllergenOptionsAsync(q)).Select(o => new { o.Term, o.Label })))
            .RequireAuthorization(Permissions.MedicinesAny);
        g.MapPut("/patients/{pid:guid}/allergies/{alertId:guid}", async (Guid pid, Guid alertId, LinkRequest r, ClaimsPrincipal u, PatientsDbContext patients, MedicineSafetyService safety) =>
        {
            if (!await patients.Alerts.AnyAsync(a => a.Id == alertId && a.PatientId == pid && a.Type == AlertType.Allergy)) return Results.NotFound(new { errors = new[] { "Allergy alert not found." } });
            return Err(await safety.LinkAsync(alertId, pid, r.Term, Me(u)));
        }).RequireAuthorization(Permissions.PatientAlertsManage);
        g.MapDelete("/patients/{pid:guid}/allergies/{alertId:guid}", async (Guid pid, Guid alertId, ClaimsPrincipal u, MedicineSafetyService safety) =>
            await safety.UnlinkAsync(alertId, pid, Me(u)) ? Results.Ok() : Results.NotFound()).RequireAuthorization(Permissions.PatientAlertsManage);

        // ---------------------------------------------------------------- rules
        g.MapGet("/rules", async (MedicineRulesService rules) => Results.Ok((await rules.ListAsync()).Select(r => new
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
        }))).RequireAuthorization(Permissions.MedicinesAny);
        g.MapGet("/rules/{id:guid}", async (Guid id, MedicineRulesService rules) => await rules.GetAsync(id) is { } r ? Results.Ok(RuleSetView(r)) : Results.NotFound())
            .RequireAuthorization(Permissions.MedicinesAny);
        g.MapPost("/rules/example", async (ClaimsPrincipal u, MedicineRulesService rules) => await rules.SeedExampleAsync(Me(u)) is { } r ? Results.Ok(new { r.Id }) : Err("Rule sets already exist – copy one to make a new draft."))
            .RequireAuthorization(Permissions.MedicinesRules);
        g.MapPost("/rules/{id:guid}/copy", async (Guid id, ClaimsPrincipal u, MedicineRulesService rules) => { var (nid, err) = await rules.CopyAsync(id, Me(u)); return err is null ? Results.Ok(new { id = nid }) : Err(err); })
            .RequireAuthorization(Permissions.MedicinesRules);
        g.MapPut("/rules/{id:guid}", async (Guid id, MedRuleSetEdit e, ClaimsPrincipal u, MedicineRulesService rules) => Errs(await rules.SaveDraftAsync(id, e, Me(u))))
            .RequireAuthorization(Permissions.MedicinesRules);
        g.MapPost("/rules/{id:guid}/approve", async (Guid id, ApproveRequest r, ClaimsPrincipal u, MedicineRulesService rules) => Err(await rules.ApproveAsync(id, Me(u), Role(u), r.Note)))
            .RequireAuthorization(Permissions.MedicinesApprove);
        g.MapDelete("/rules/{id:guid}", async (Guid id, ClaimsPrincipal u, MedicineRulesService rules) => Err(await rules.DeleteDraftAsync(id, Me(u))))
            .RequireAuthorization(Permissions.MedicinesRules);

        // ---------------------------------------------------------------- NZ Formulary (NZF and NZFC PDFs)
        g.MapGet("/nzf", async (NzfService nzf) =>
        {
            var s = await nzf.SettingsAsync();
            var releases = await nzf.ReleasesAsync();
            return Results.Ok(new
            {
                settings = new { s.NzfUrl, s.NzfcUrl, s.AutoDownload, s.WatchFolder, s.LastCheckedAt, s.LastProblem, s.UpdatedAt, s.UpdatedBy },
                releases = releases.Select(r => new { r.Id, r.Edition, r.ReleaseNumber, r.ReleaseDate, r.FileName, r.Monographs, r.Doses, r.IsCurrent, r.Problems, r.ImportedAt, r.ImportedBy }),
                nextDue = NzfService.FirstWorkingDay(DateTime.UtcNow.AddMonths(1).Year, DateTime.UtcNow.AddMonths(1).Month),
            });
        }).RequireAuthorization(Permissions.MedicinesAny);
        g.MapPut("/nzf/settings", async (NzfSettingsEdit e, ClaimsPrincipal u, NzfService nzf) => Errs(await nzf.SaveSettingsAsync(e, Me(u)))).RequireAuthorization(Permissions.MedicinesCatalogue);
        g.MapPost("/nzf/import", async (HttpRequest req, ClaimsPrincipal u, NzfService nzf) =>
        {
            try
            {
                using var ms = new MemoryStream();
                var buf = new byte[81920];
                int n;
                while ((n = await req.Body.ReadAsync(buf)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > NzfPdf.MaxBytes) return Err("The file is larger than 120 MB.");
                }
                if (ms.Length == 0) return Err("The file is empty.");
                var r = await nzf.ImportAsync(Uri.UnescapeDataString(req.Headers["X-File-Name"].ToString()), ms.ToArray(), Me(u));
                return Results.Ok(r);
            }
            catch (InvalidOperationException ex) { return Err(ex.Message); }
        }).RequireAuthorization(Permissions.MedicinesCatalogue).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(NzfPdf.MaxBytes + 1024));
        g.MapGet("/nzf/monographs", async (string? q, NzfService nzf) => Results.Ok((await nzf.SearchAsync(q)).Select(m => new { m.Id, m.Name, m.Edition })))
            .RequireAuthorization(Permissions.MedicinesAny);
        g.MapGet("/nzf/lookup", async (string ingredient, NzfService nzf) => Results.Ok((await nzf.ForIngredientAsync(ingredient)).Select(m => new { m.Id, m.Name, m.Edition })))
            .RequireAuthorization(Permissions.MedicinesAny);
        g.MapGet("/nzf/monographs/{id:guid}", async (Guid id, NzfService nzf) =>
        {
            if (await nzf.MonographAsync(id) is not { } x) return Results.NotFound();
            var (m, r) = x;
            return Results.Ok(new
            {
                m.Id,
                m.Name,
                m.Synonyms,
                m.Chapter,
                edition = r.Edition,
                release = r.ReleaseNumber,
                r.ReleaseDate,
                sections = new[] { ("Indications", m.Indications), ("Contra-indications", m.ContraIndications), ("Cautions", m.Cautions), ("Hepatic impairment", m.HepaticImpairment),
                    ("Renal impairment", m.RenalImpairment), ("Pregnancy", m.Pregnancy), ("Breast-feeding", m.BreastFeeding), ("Adverse effects", m.AdverseEffects), ("Patient advice", m.PatientAdvice), ("Other", m.OtherSections) }
                    .Where(t => !string.IsNullOrWhiteSpace(t.Item2)).Select(t => new { title = t.Item1, text = t.Item2 }),
                doses = m.Doses.Select(d => new { d.Indication, d.Route, d.RouteCodes, d.Population, d.Dose, d.MaxSingle, d.MaxSinglePerKg, d.MaxDaily, d.MaxDailyPerKg, d.IntervalHours, d.Unit, d.Neonate }),
                preparations = m.Preparations.Select(p => new { p.Form, p.Strength, p.Subsidy }),
            });
        }).RequireAuthorization(Permissions.MedicinesAny);
        g.MapPost("/rules/{id:guid}/suggest-nzf", async (Guid id, SuggestRequest r, MedicineRulesService rules, MedicineCatalogue cat, MedicinesDbContext db, NzfService nzf) =>
        {
            if (await rules.GetAsync(id) is not { } rs) return Results.NotFound();
            if (rs.Status != MedRuleSetStatus.Draft) return Err("Suggestions go into a draft – make a new draft first.");
            var ingredients = r.Ingredients is { Count: > 0 } list ? list : await (from mi in db.MedicineIngredients.AsNoTracking()
                                                                                   join m in db.Medicines.AsNoTracking() on mi.MedicineId equals m.Id
                                                                                   join i in db.Ingredients.AsNoTracking() on mi.IngredientId equals i.Id
                                                                                   where m.OnFormulary && m.IsActive
                                                                                   select i.Name).Distinct().ToListAsync();
            var s = await nzf.SuggestAsync(ingredients, r.Children);
            return Results.Ok(new { ingredients = ingredients.Count, suggestions = s.Select(x => new { rule = x.Rule, x.Edition, x.Source, existing = rs.DoseRules.Any(d => d.Ingredient == x.Rule.Ingredient) }) });
        }).RequireAuthorization(Permissions.MedicinesRules);

        // ---------------------------------------------------------------- settings and report
        g.MapGet("/settings", async (MedicineSafetyService safety, MedicineRulesService rules) =>
        {
            var s = await safety.SettingsAsync();
            var approved = await rules.ApprovedAsync();
            return Results.Ok(new { mode = s.Mode.ToString(), s.CheckRegularMedicines, s.DuplicateWithinMinutes, s.UpdatedAt, s.UpdatedBy, approvedVersion = approved?.Version, approvedAt = approved?.ApprovedAt });
        }).RequireAuthorization(Permissions.MedicinesAny);
        g.MapPut("/settings", async (MedSettingsEdit e, ClaimsPrincipal u, MedicineSafetyService safety) => Errs(await safety.SaveSettingsAsync(e, Me(u))))
            .RequireAuthorization(Permissions.MedicinesApprove);
        g.MapGet("/report", async (DateOnly? from, DateOnly? to, MedicineSafetyService safety, PatientsDbContext patients, TimeProvider clock) =>
        {
            var t = (to ?? BrightPAS.Core.NzTime.Today(clock)).AddDays(1);
            var f = from ?? t.AddDays(-31);
            var start = BrightPAS.Core.NzTime.StartOfDay(f);
            var end = BrightPAS.Core.NzTime.StartOfDay(t);
            var (byResult, byKind) = await safety.SummaryAsync(start, end);
            var notable = (await safety.ChecksAsync(null, null, start, end, null, 1000)).Where(c => c.Result is CheckResult.Overridden or CheckResult.Stopped).Take(200).ToList();
            var pids = notable.Select(c => c.PatientId).Distinct().ToList();
            var names = await patients.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.Nhi, name = p.FamilyName.ToUpperInvariant() + ", " + p.GivenNames });
            return Results.Ok(new
            {
                from = f,
                to = t.AddDays(-1),
                byResult = byResult.ToDictionary(x => x.Key.ToString(), x => x.Value),
                byKind = byKind.ToDictionary(x => x.Key.ToString(), x => x.Value),
                notable = notable.Select(c => new
                {
                    c.Id,
                    c.PatientId,
                    nhi = names.GetValueOrDefault(c.PatientId)?.Nhi,
                    patient = names.GetValueOrDefault(c.PatientId)?.name,
                    c.EpisodeId,
                    c.Medicine,
                    c.Dose,
                    c.Route,
                    result = c.Result.ToString(),
                    c.OverrideReason,
                    c.CheckedAt,
                    c.CheckedBy,
                    findings = c.Findings.Where(x => x.Severity == CheckSeverity.Stop).Select(x => x.Text),
                }),
            });
        }).RequireAuthorization(Permissions.MedicinesGovern);
    }

    /// <summary>The finding notes returned with a medicine given in the clinical record.</summary>
    public static object NoteView(BrightPAS.Core.Modules.MedicineCheckNote n) => new { kind = n.Kind, severity = n.Severity, text = n.Text };

    /// <summary>Medicines charted in the clinical record with the check that went with each (for the episode's medicines list).</summary>
    public static async Task<Dictionary<Guid, (string Result, string? Reason)>> ChecksByMedicationAsync(MedicinesDbContext db, IReadOnlyCollection<Guid> medicationIds) =>
        medicationIds.Count == 0 ? [] : (await db.Checks.AsNoTracking().Where(c => c.MedicationId != null && medicationIds.Contains(c.MedicationId.Value))
            .Select(c => new { c.MedicationId, c.Result, c.OverrideReason, c.CheckedAt }).ToListAsync())
            .GroupBy(c => c.MedicationId!.Value).ToDictionary(x => x.Key, x => x.OrderByDescending(c => c.CheckedAt).Select(c => (c.Result.ToString(), c.OverrideReason)).First());
}
