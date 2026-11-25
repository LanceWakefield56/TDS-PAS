using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Medicines;

public sealed record SafetyRequest(Guid PatientId, Guid? EpisodeId, string Medicine, string Dose, string Route, DateTimeOffset At, string? Barcode, Guid? MedicineId,
    decimal? WeightKg, string? OverrideReason, string User, bool Record);

/// <summary>What the rest of BrightPAS knows about the patient (gathered by the web app from Patients, eMR and the questionnaire).</summary>
public sealed record PatientFacts(int? AgeYears, decimal? WeightKg, string? WeightSource, IReadOnlyList<(Guid AlertId, string Text)> Allergies,
    IReadOnlyList<(string Text, string Dose, string Route, DateTimeOffset At)> Given, string? RegularText);

public sealed record SafetyResult(bool Allowed, string? Error, Guid? CheckId, CheckMode Mode, CheckResult Result, IReadOnlyList<Finding> Findings,
    Guid? MedicineId, string? MedicineName, decimal? WeightKg, string? WeightSource, int? RuleSetVersion, IReadOnlyList<string>? Ingredients = null);

public sealed record CheckRow(Guid Id, Guid PatientId, Guid? EpisodeId, string Medicine, string Dose, string Route, CheckResult Result, string? OverrideReason,
    int? RuleSetVersion, DateTimeOffset CheckedAt, string CheckedBy, IReadOnlyList<MedicineCheckFinding> Findings);

public sealed record MedSettingsEdit(CheckMode Mode, bool CheckRegularMedicines, int DuplicateWithinMinutes);

/// <summary>
/// Runs the medicine checks for a patient and keeps a record of each check, its findings and any override reason. The web app supplies the
/// patient facts (<see cref="PatientFacts"/>); this service resolves medicines to ingredients and amounts and applies the approved rules.
/// </summary>
public sealed class MedicineSafetyService(MedicinesDbContext db, MedicineCatalogue catalogue, MedicineRulesService rules, NzfService nzf, AuditWriter audit, TimeProvider clock)
{
    private static readonly TimeZoneInfo Nz = TryZone("Pacific/Auckland");
    private static TimeZoneInfo TryZone(string id) { try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; } }
    private static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    public async Task<MedSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is not null) return s;
        s = new MedSettings();
        db.Settings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<IReadOnlyList<string>> SaveSettingsAsync(MedSettingsEdit e, string user, CancellationToken ct = default)
    {
        if (e.DuplicateWithinMinutes is < 0 or > 1440) return ["The double-dose window must be between 0 and 1440 minutes."];
        if (e.Mode == CheckMode.Enforce && await rules.ApprovedAsync(ct) is null) return ["Approve a medicine rule set before enforcing the checks."];
        var s = await SettingsAsync(ct);
        var before = s.Mode;
        s.Mode = e.Mode; s.CheckRegularMedicines = e.CheckRegularMedicines; s.DuplicateWithinMinutes = e.DuplicateWithinMinutes;
        s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "MedicineSettings", "1", reason: $"Checks {before} → {e.Mode}; regular medicines {(e.CheckRegularMedicines ? "on" : "off")}; double dose {e.DuplicateWithinMinutes} min", ct: ct);
        return [];
    }

    // ------------------------------------------------------------------ allergy links
    public async Task<List<AllergyLink>> LinksAsync(Guid patientId, CancellationToken ct = default) =>
        await db.AllergyLinks.AsNoTracking().Where(l => l.PatientId == patientId).ToListAsync(ct);

    /// <summary>Things an allergy can be linked to: ingredients (by name or other name) and the allergy groups of the newest rule set.</summary>
    public async Task<List<(string Term, string Label)>> AllergenOptionsAsync(string? q, CancellationToken ct = default)
    {
        var list = new List<(string, string)>();
        var rs = await rules.ApprovedAsync(ct);
        if (rs is null && (await rules.ListAsync(ct)).FirstOrDefault() is { } newest) rs = await rules.GetAsync(newest.Id, ct);
        var t = (q ?? "").Trim().ToLowerInvariant();
        if (rs is not null)
            list.AddRange(rs.Groups.Where(g => g.AllergyClass && (t.Length == 0 || g.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || g.Members.Any(m => m.Term.Contains(t, StringComparison.Ordinal))))
                .Select(g => ("group:" + g.Code, g.Name + " (group)")));
        if (t.Length >= 2)
        {
            var ings = await db.Ingredients.AsNoTracking().Where(i => i.Name.Contains(t) || i.Aliases.Any(a => a.Alias.Contains(t))).OrderBy(i => i.Name).Take(20).Select(i => i.Name).ToListAsync(ct);
            list.AddRange(ings.Select(i => (i, i)));
        }
        return list;
    }

    public async Task<string?> LinkAsync(Guid alertId, Guid patientId, string term, string user, CancellationToken ct = default)
    {
        var t = (term ?? "").Trim();
        if (t.Length == 0) return "Choose the medicine or group the patient is allergic to.";
        if (t.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
        {
            var code = t[6..].Trim().ToUpperInvariant();
            if (!await db.Groups.AnyAsync(g => g.Code == code && g.AllergyClass, ct)) return $"There is no allergy group {code}.";
            t = "group:" + code;
        }
        else
        {
            var k = MedicineCatalogue.IngredientKey(t);
            var name = await db.Ingredients.Where(i => i.Name == k).Select(i => i.Name).SingleOrDefaultAsync(ct)
                       ?? await db.Aliases.Where(a => a.Alias == k).Join(db.Ingredients, a => a.IngredientId, i => i.Id, (a, i) => i.Name).SingleOrDefaultAsync(ct);
            if (name is null) return $"\"{t}\" is not an ingredient in the medicines list.";
            t = name;
        }
        var row = await db.AllergyLinks.SingleOrDefaultAsync(l => l.AlertId == alertId, ct);
        if (row is null) { row = new AllergyLink { AlertId = alertId }; db.AllergyLinks.Add(row); }
        row.PatientId = patientId; row.Term = t; row.LinkedBy = user; row.LinkedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "AllergyLink", alertId.ToString(), patientId.ToString(), reason: $"Allergy linked to {t}", ct: ct);
        return null;
    }

    public async Task<bool> UnlinkAsync(Guid alertId, Guid patientId, string user, CancellationToken ct = default)
    {
        var n = await db.AllergyLinks.Where(l => l.AlertId == alertId && l.PatientId == patientId).ExecuteDeleteAsync(ct);
        if (n > 0) await audit.WriteAsync(user, "Delete", "AllergyLink", alertId.ToString(), patientId.ToString(), reason: "Allergy link removed", ct: ct);
        return n > 0;
    }

    // ------------------------------------------------------------------ checks
    private async Task<List<GivenBefore>> ResolveGivenAsync(IReadOnlyList<(string Text, string Dose, string Route, DateTimeOffset At)> given, TermIndex index, CancellationToken ct)
    {
        var list = new List<GivenBefore>();
        foreach (var g in given)
        {
            var (_, ings) = await catalogue.ResolveAsync(g.Text, null, null, ct);
            if (ings.Count > 0)
            {
                var (amts, _) = MedicineChecks.Amounts(ings, g.Dose);
                list.AddRange(amts.Select(a => new GivenBefore($"{g.Text} {g.Dose}".Trim(), a.Ingredient, a.Amount, a.Unit, g.At)));
            }
            else
            {
                var found = index.Find(g.Text);
                var (n, u) = DoseParser.Parse(g.Dose);
                var b = found.Count == 1 && n is { } q ? DoseParser.ToBase(q, u) : null;
                list.AddRange(found.Select(i => new GivenBefore($"{g.Text} {g.Dose}".Trim(), i, b?.Amount, b?.Unit, g.At)));
            }
        }
        return list;
    }

    public async Task<SafetyResult> CheckAsync(SafetyRequest r, PatientFacts facts, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(ct);
        var weight = r.WeightKg is > 0 and < 400 ? r.WeightKg : facts.WeightKg;
        var weightSource = r.WeightKg is > 0 and < 400 ? "entered" : facts.WeightSource;
        if (settings.Mode == CheckMode.Off) return new(true, null, null, CheckMode.Off, CheckResult.NotChecked, [], null, null, weight, weightSource, null, []);

        var index = await catalogue.TermIndexAsync(ct);
        var approved = await rules.ApprovedAsync(ct);
        var (med, ings) = await catalogue.ResolveAsync(r.Medicine, r.Barcode, r.MedicineId, ct);
        List<IngredientAmount> amounts;
        string? doseProblem = null;
        if (ings.Count > 0) (amounts, doseProblem) = MedicineChecks.Amounts(ings, r.Dose);
        else
        {
            var found = index.Find(r.Medicine);
            var (n, u) = DoseParser.Parse(r.Dose);
            var b = found.Count == 1 && n is { } q ? DoseParser.ToBase(q, u) : null;
            if (found.Count > 1 && n is not null) doseProblem = "it is not in the medicines list and has more than one ingredient";
            else if (found.Count == 1 && b is null) doseProblem = u == "mL" || u == "each" ? "it is not in the medicines list, so its strength is not known" : "no unit (mg, microgram, g…)";
            amounts = [.. found.Select(i => new IngredientAmount(i, b?.Amount, b?.Unit))];
        }
        var links = facts.Allergies.Count == 0 ? [] : await LinksAsync(r.PatientId, ct);
        var allergies = facts.Allergies.Select(a => new PatientAllergy(a.Text, links.FirstOrDefault(l => l.AlertId == a.AlertId)?.Term)).ToList();
        var given = await ResolveGivenAsync(facts.Given, index, ct);
        var regular = settings.CheckRegularMedicines ? index.Find(facts.RegularText).Select(i => new RegularMedicine(i, "regular medicine in the questionnaire")).ToList() : [];
        var ctx = new CheckContext(facts.AgeYears, weight, allergies, given, regular, settings.DuplicateWithinMinutes, Nz);
        var proposed = new ProposedMedicine(med?.Name ?? r.Medicine, r.Dose, r.Route, amounts, r.At, doseProblem);
        var findings = MedicineChecks.Check(proposed, ctx, approved, index);
        // 0.27.0: where TDS has no dose limit for an ingredient on this route, show what the NZ Formulary says (information only).
        foreach (var ing in amounts.Select(a => a.Ingredient).Distinct(StringComparer.Ordinal))
        {
            if (approved?.DoseRules.Any(d => string.Equals(d.Ingredient, ing, StringComparison.OrdinalIgnoreCase) && Routes.Matches(d.Route, r.Route)) == true) continue;
            var hints = await nzf.HintsAsync(ing, r.Route, facts.AgeYears, weight, 2, ct);
            if (hints.Count == 0) continue;
            var hintText = $"No TDS dose limit for {ing} by {(string.IsNullOrWhiteSpace(r.Route) ? "this route" : Routes.Normal(r.Route))}. {hints[0].Edition} (release {hints[0].Release}): " + string.Join(" · ", hints.Select(h => h.Text));
            findings.Add(new Finding(FindingKind.NotChecked, CheckSeverity.Info, hintText.Length > 480 ? hintText[..477] + "…" : hintText, "nzf:" + ing));
        }

        var worst = MedicineChecks.Worst(findings);
        var reason = Clip(r.OverrideReason, 500);
        var blocked = settings.Mode == CheckMode.Enforce && worst == CheckSeverity.Stop && reason is null;
        var result = worst switch
        {
            CheckSeverity.Stop when blocked => CheckResult.Stopped,
            CheckSeverity.Stop when reason is not null => CheckResult.Overridden,
            CheckSeverity.Stop or CheckSeverity.Warn => CheckResult.Warnings,
            _ => CheckResult.Clear,
        };
        Guid? checkId = null;
        if (r.Record)
        {
            var row = new MedicineCheck
            {
                PatientId = r.PatientId,
                EpisodeId = r.EpisodeId,
                MedicineId = med?.Id,
                MedicineText = Clip(r.Medicine, 150) ?? "",
                Dose = Clip(r.Dose, 60) ?? "",
                Route = Clip(r.Route, 30) ?? "",
                Result = result,
                OverrideReason = worst == CheckSeverity.Stop ? reason : null,
                RuleSetVersion = approved?.Version,
                Mode = settings.Mode,
                CheckedAt = clock.GetUtcNow(),
                CheckedBy = r.User,
            };
            var seq = 0;
            foreach (var f in findings) row.Findings.Add(new MedicineCheckFinding { CheckId = row.Id, Sequence = ++seq, Kind = f.Kind, Severity = f.Severity, Text = f.Text.Length > 500 ? f.Text[..500] : f.Text, RuleRef = Clip(f.RuleRef, 60) });
            db.Checks.Add(row);
            await db.SaveChangesAsync(ct);
            checkId = row.Id;
            if (result is CheckResult.Overridden or CheckResult.Stopped)
                await audit.WriteAsync(r.User, result == CheckResult.Overridden ? "Override" : "Stop", "MedicineCheck", row.Id.ToString(), r.PatientId.ToString(),
                    reason: $"{row.MedicineText} {row.Dose} {row.Route}: {findings.First(f => f.Severity == CheckSeverity.Stop).Text}{(reason is null ? "" : " – reason: " + reason)}", ct: ct);
        }
        var error = blocked ? "Stop – " + findings.First(f => f.Severity == CheckSeverity.Stop).Text + " To give it anyway, give your reason." : null;
        return new(!blocked, error, checkId, settings.Mode, result, findings, med?.Id, med?.Name, weight, weightSource, approved?.Version, [.. amounts.Select(a => a.Ingredient).Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>Links a recorded check to the eMR medicine row it was given as.</summary>
    public async Task LinkCheckAsync(Guid checkId, Guid medicationId, CancellationToken ct = default) =>
        await db.Checks.Where(c => c.Id == checkId && c.MedicationId == null).ExecuteUpdateAsync(s => s.SetProperty(c => c.MedicationId, medicationId), ct);

    public async Task<List<CheckRow>> ChecksAsync(Guid? patientId, Guid? episodeId, DateTimeOffset? from, DateTimeOffset? to, CheckResult? result, int take, CancellationToken ct = default)
    {
        var q = db.Checks.AsNoTracking().Include(c => c.Findings).AsQueryable();
        if (patientId is { } p) q = q.Where(c => c.PatientId == p);
        if (episodeId is { } e) q = q.Where(c => c.EpisodeId == e);
        if (from is { } f) q = q.Where(c => c.CheckedAt >= f);
        if (to is { } t) q = q.Where(c => c.CheckedAt < t);
        if (result is { } res) q = q.Where(c => c.Result == res);
        var rows = await q.OrderByDescending(c => c.CheckedAt).Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
        return [.. rows.Select(c => new CheckRow(c.Id, c.PatientId, c.EpisodeId, c.MedicineText, c.Dose, c.Route, c.Result, c.OverrideReason, c.RuleSetVersion, c.CheckedAt, c.CheckedBy,
            [.. c.Findings.OrderBy(x => x.Sequence)]))];
    }

    /// <summary>Counts for the report and the Quality indicator: checks by result, and findings by kind for warnings and stops.</summary>
    public async Task<(Dictionary<CheckResult, int> ByResult, Dictionary<FindingKind, int> ByKind)> SummaryAsync(DateTimeOffset @from, DateTimeOffset to, CancellationToken ct = default)
    {
        var byResult = await db.Checks.AsNoTracking().Where(c => c.CheckedAt >= @from && c.CheckedAt < to).GroupBy(c => c.Result).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        var byKind = await (from f in db.CheckFindings.AsNoTracking()
                            join c in db.Checks.AsNoTracking() on f.CheckId equals c.Id
                            where c.CheckedAt >= @from && c.CheckedAt < to && f.Severity != CheckSeverity.Info
                            group f by f.Kind into g
                            select new { g.Key, N = g.Count() }).ToListAsync(ct);
        return (byResult.ToDictionary(x => x.Key, x => x.N), byKind.ToDictionary(x => x.Key, x => x.N));
    }

    /// <summary>Patient merge: moves checks and allergy links to the surviving patient.</summary>
    public async Task<int> MergeAsync(Guid fromPatient, Guid toPatient, CancellationToken ct = default)
    {
        var n = await db.Checks.Where(c => c.PatientId == fromPatient).ExecuteUpdateAsync(s => s.SetProperty(c => c.PatientId, toPatient), ct);
        n += await db.AllergyLinks.Where(c => c.PatientId == fromPatient).ExecuteUpdateAsync(s => s.SetProperty(c => c.PatientId, toPatient), ct);
        return n;
    }
}
