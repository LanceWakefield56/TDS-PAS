using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Medicines;

public sealed record DoseRuleEdit(string Code, string Ingredient, string? Route, int? MinAgeYears, int? MaxAgeYears, string Unit, decimal? MaxSingle, decimal? MaxDaily,
    decimal? MaxSinglePerKg, decimal? MaxDailyPerKg, decimal? MinIntervalHours, CheckSeverity Severity, string? Reference, string? Note, decimal? MinWeightKg = null, decimal? MaxWeightKg = null);
public sealed record GroupEdit(string Code, string Name, bool AllergyClass, CheckSeverity SameGroupSeverity, IReadOnlyList<string> Members, IReadOnlyList<string> Keywords);
public sealed record CrossReactionEdit(string FromGroup, string ToGroup, CheckSeverity Severity, string? Message);
public sealed record InteractionEdit(string Code, string TermA, string TermB, CheckSeverity Severity, string Effect, string? Advice, string? Reference);
public sealed record MedRuleSetEdit(string Name, string? Notes, IReadOnlyList<DoseRuleEdit> DoseRules, IReadOnlyList<GroupEdit> Groups, IReadOnlyList<CrossReactionEdit> CrossReactions, IReadOnlyList<InteractionEdit> Interactions);

/// <summary>
/// Versioned medicine rules (same pattern as the triage rules). A draft can be edited; approving it (a clinician with the approval permission,
/// with a note) makes it the set used for patients and retires the previous one. An approved set never changes – edits start a new draft copy.
/// </summary>
public sealed class MedicineRulesService(MedicinesDbContext db, AuditWriter audit, TimeProvider clock)
{
    private static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    public async Task<List<MedRuleSet>> ListAsync(CancellationToken ct = default) => await db.RuleSets.AsNoTracking().OrderByDescending(r => r.Version).ToListAsync(ct);

    private IQueryable<MedRuleSet> Full() => db.RuleSets.AsNoTracking().Include(x => x.DoseRules).Include(x => x.Groups).ThenInclude(g => g.Members).Include(x => x.CrossReactions).Include(x => x.Interactions).AsSplitQuery();

    public async Task<MedRuleSet?> GetAsync(Guid id, CancellationToken ct = default) => Sort(await Full().SingleOrDefaultAsync(x => x.Id == id, ct));

    public async Task<MedRuleSet?> ApprovedAsync(CancellationToken ct = default) => Sort(await Full().SingleOrDefaultAsync(x => x.Status == MedRuleSetStatus.Approved, ct));

    private static MedRuleSet? Sort(MedRuleSet? r)
    {
        if (r is null) return null;
        r.DoseRules = [.. r.DoseRules.OrderBy(x => x.Ingredient, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal)];
        r.Groups = [.. r.Groups.OrderBy(x => x.Code, StringComparer.Ordinal)];
        foreach (var g in r.Groups) g.Members = [.. g.Members.OrderBy(m => m.IsKeyword).ThenBy(m => m.Term, StringComparer.Ordinal)];
        r.CrossReactions = [.. r.CrossReactions.OrderBy(x => x.FromGroup, StringComparer.Ordinal).ThenBy(x => x.ToGroup, StringComparer.Ordinal)];
        r.Interactions = [.. r.Interactions.OrderBy(x => x.Code, StringComparer.Ordinal)];
        return r;
    }

    private async Task<int> NextVersionAsync(CancellationToken ct) => (await db.RuleSets.MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;

    /// <summary>Adds the example rule set (once, when there are no rule sets) – a draft that must be reviewed and approved.</summary>
    public async Task<MedRuleSet?> SeedExampleAsync(string user, CancellationToken ct = default)
    {
        if (await db.RuleSets.AnyAsync(ct)) return null;
        var rs = MedicineExamples.ExampleRuleSet(1, user, clock.GetUtcNow());
        db.RuleSets.Add(rs);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "MedicineRuleSet", rs.Id.ToString(), reason: $"v{rs.Version} example rules (draft)", ct: ct);
        return rs;
    }

    public async Task<(Guid? Id, string? Error)> CopyAsync(Guid fromId, string user, CancellationToken ct = default)
    {
        var src = await GetAsync(fromId, ct);
        if (src is null) return (null, "Rule set not found.");
        if (await db.RuleSets.AnyAsync(r => r.Status == MedRuleSetStatus.Draft, ct)) return (null, "There is already a draft – edit or delete it first.");
        var rs = new MedRuleSet { Version = await NextVersionAsync(ct), Name = src.Name.Replace(" (not yet clinically approved)", "", StringComparison.Ordinal), IsExample = src.IsExample, Notes = src.Notes, CreatedBy = user, CreatedAt = clock.GetUtcNow() };
        Fill(rs, ToEdit(src));
        db.RuleSets.Add(rs);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "MedicineRuleSet", rs.Id.ToString(), reason: $"v{rs.Version} draft copied from v{src.Version}", ct: ct);
        return (rs.Id, null);
    }

    public static MedRuleSetEdit ToEdit(MedRuleSet s) => new(s.Name, s.Notes,
        [.. s.DoseRules.Select(r => new DoseRuleEdit(r.Code, r.Ingredient, r.Route, r.MinAgeYears, r.MaxAgeYears, r.Unit, r.MaxSingle, r.MaxDaily, r.MaxSinglePerKg, r.MaxDailyPerKg, r.MinIntervalHours, r.Severity, r.Reference, r.Note, r.MinWeightKg, r.MaxWeightKg))],
        [.. s.Groups.Select(g => new GroupEdit(g.Code, g.Name, g.AllergyClass, g.SameGroupSeverity, [.. g.Members.Where(m => !m.IsKeyword).Select(m => m.Term)], [.. g.Members.Where(m => m.IsKeyword).Select(m => m.Term)]))],
        [.. s.CrossReactions.Select(x => new CrossReactionEdit(x.FromGroup, x.ToGroup, x.Severity, x.Message))],
        [.. s.Interactions.Select(x => new InteractionEdit(x.Code, x.TermA, x.TermB, x.Severity, x.Effect, x.Advice, x.Reference))]);

    private static string Term(string t)
    {
        var s = t.Trim();
        return s.StartsWith("group:", StringComparison.OrdinalIgnoreCase) ? "group:" + s[6..].Trim().ToUpperInvariant() : MedicineCatalogue.IngredientKey(s);
    }

    private static void Fill(MedRuleSet rs, MedRuleSetEdit e)
    {
        rs.DoseRules.AddRange(e.DoseRules.Select(r => new DoseRule
        {
            RuleSetId = rs.Id,
            Code = Clip(r.Code, 20)!.ToUpperInvariant(),
            Ingredient = MedicineCatalogue.IngredientKey(r.Ingredient),
            Route = string.IsNullOrWhiteSpace(r.Route) ? null : Routes.Normal(r.Route),
            MinAgeYears = r.MinAgeYears,
            MaxAgeYears = r.MaxAgeYears,
            MinWeightKg = r.MinWeightKg,
            MaxWeightKg = r.MaxWeightKg,
            Unit = DoseParser.NormaliseUnit(r.Unit) ?? r.Unit.Trim(),
            MaxSingle = r.MaxSingle,
            MaxDaily = r.MaxDaily,
            MaxSinglePerKg = r.MaxSinglePerKg,
            MaxDailyPerKg = r.MaxDailyPerKg,
            MinIntervalHours = r.MinIntervalHours,
            Severity = r.Severity,
            Reference = Clip(r.Reference, 300),
            Note = Clip(r.Note, 500),
        }));
        foreach (var g in e.Groups)
        {
            var grp = new MedicineGroup { RuleSetId = rs.Id, Code = Clip(g.Code, 20)!.ToUpperInvariant(), Name = Clip(g.Name, 100)!, AllergyClass = g.AllergyClass, SameGroupSeverity = g.SameGroupSeverity };
            foreach (var m in g.Members.Select(MedicineCatalogue.IngredientKey).Where(m => m.Length > 0).Distinct(StringComparer.Ordinal))
                grp.Members.Add(new MedicineGroupMember { GroupId = grp.Id, Term = Clip(m, 150)! });
            foreach (var k in g.Keywords.Select(MedicineCatalogue.IngredientKey).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal))
                if (!grp.Members.Any(x => x.Term == k)) grp.Members.Add(new MedicineGroupMember { GroupId = grp.Id, Term = Clip(k, 150)!, IsKeyword = true });
            rs.Groups.Add(grp);
        }
        rs.CrossReactions.AddRange(e.CrossReactions.Select(x => new CrossReaction { RuleSetId = rs.Id, FromGroup = x.FromGroup.Trim().ToUpperInvariant(), ToGroup = x.ToGroup.Trim().ToUpperInvariant(), Severity = x.Severity, Message = Clip(x.Message, 500) ?? "" }));
        rs.Interactions.AddRange(e.Interactions.Select(x => new InteractionRule { RuleSetId = rs.Id, Code = Clip(x.Code, 20)!.ToUpperInvariant(), TermA = Term(x.TermA), TermB = Term(x.TermB), Severity = x.Severity, Effect = Clip(x.Effect, 300)!, Advice = Clip(x.Advice, 500), Reference = Clip(x.Reference, 300) }));
    }

    public static List<string> Validate(MedRuleSetEdit e)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.Name)) errors.Add("Give the rule set a name.");
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in e.DoseRules)
        {
            var label = string.IsNullOrWhiteSpace(r.Code) ? r.Ingredient : r.Code;
            if (string.IsNullOrWhiteSpace(r.Code)) errors.Add($"Dose rule for {r.Ingredient}: give it a short code.");
            else if (!codes.Add(r.Code.Trim())) errors.Add($"Dose rule code {r.Code} is used twice.");
            if (string.IsNullOrWhiteSpace(r.Ingredient)) errors.Add($"Dose rule {label}: choose the ingredient.");
            if (DoseParser.ToBase(1, DoseParser.NormaliseUnit(r.Unit)) is null) errors.Add($"Dose rule {label}: the unit must be mg, microgram, g, unit or mmol.");
            if (r.MaxSingle is null && r.MaxDaily is null && r.MaxSinglePerKg is null && r.MaxDailyPerKg is null && r.MinIntervalHours is null) errors.Add($"Dose rule {label}: give at least one limit.");
            foreach (var v in new[] { r.MaxSingle, r.MaxDaily, r.MaxSinglePerKg, r.MaxDailyPerKg, r.MinIntervalHours })
                if (v is { } d && d <= 0) { errors.Add($"Dose rule {label}: limits must be more than 0."); break; }
            if (r.MaxSingle is { } s && r.MaxDaily is { } dd && dd < s) errors.Add($"Dose rule {label}: the daily maximum is less than the single maximum.");
            if (r.MinAgeYears is { } lo && r.MaxAgeYears is { } hi && hi <= lo) errors.Add($"Dose rule {label}: the age band is empty (from {lo} to under {hi}).");
            if (r.MinAgeYears is < 0 or > 120 || r.MaxAgeYears is < 1 or > 130) errors.Add($"Dose rule {label}: check the ages.");
            if (r.MinWeightKg is { } wl && r.MaxWeightKg is { } wh && wh <= wl) errors.Add($"Dose rule {label}: the weight band is empty (from {wl} kg to under {wh} kg).");
            if (r.MinWeightKg is < 0 or > 400 || r.MaxWeightKg is <= 0 or > 400) errors.Add($"Dose rule {label}: check the weights.");
            if (r.Severity == CheckSeverity.Info) errors.Add($"Dose rule {label}: a dose limit must be Stop or Warn.");
        }
        var groupCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in e.Groups)
        {
            if (string.IsNullOrWhiteSpace(g.Code) || string.IsNullOrWhiteSpace(g.Name)) errors.Add("Every group needs a code and a name.");
            else if (!groupCodes.Add(g.Code.Trim())) errors.Add($"Group code {g.Code} is used twice.");
            if (g.Members.All(string.IsNullOrWhiteSpace)) errors.Add($"Group {g.Code}: list at least one ingredient.");
        }
        foreach (var x in e.CrossReactions)
        {
            if (!groupCodes.Contains(x.FromGroup.Trim()) || !groupCodes.Contains(x.ToGroup.Trim())) errors.Add($"Cross-reaction {x.FromGroup} → {x.ToGroup}: both groups must be in this rule set.");
            if (string.Equals(x.FromGroup.Trim(), x.ToGroup.Trim(), StringComparison.OrdinalIgnoreCase)) errors.Add($"Cross-reaction {x.FromGroup}: the groups must be different (the same group is covered by the group itself).");
        }
        if (e.CrossReactions.GroupBy(x => (x.FromGroup.Trim().ToUpperInvariant(), x.ToGroup.Trim().ToUpperInvariant())).Any(g => g.Count() > 1)) errors.Add("A cross-reaction is listed twice.");
        var icodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in e.Interactions)
        {
            var label = string.IsNullOrWhiteSpace(x.Code) ? $"{x.TermA} + {x.TermB}" : x.Code;
            if (string.IsNullOrWhiteSpace(x.Code)) errors.Add($"Interaction {label}: give it a short code.");
            else if (!icodes.Add(x.Code.Trim())) errors.Add($"Interaction code {x.Code} is used twice.");
            if (string.IsNullOrWhiteSpace(x.TermA) || string.IsNullOrWhiteSpace(x.TermB)) errors.Add($"Interaction {label}: name both medicines (an ingredient or group:CODE).");
            if (string.IsNullOrWhiteSpace(x.Effect)) errors.Add($"Interaction {label}: say what the effect is.");
            foreach (var t in new[] { x.TermA ?? "", x.TermB ?? "" }.Where(t => t.Trim().StartsWith("group:", StringComparison.OrdinalIgnoreCase)))
                if (!groupCodes.Contains(t.Trim()[6..].Trim())) errors.Add($"Interaction {label}: there is no group {t.Trim()[6..].Trim()} in this rule set.");
            if (!string.IsNullOrWhiteSpace(x.TermA) && string.Equals(x.TermA.Trim(), x.TermB?.Trim(), StringComparison.OrdinalIgnoreCase) && !x.TermA.Trim().StartsWith("group:", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Interaction {label}: a medicine cannot interact with itself – the dose rules cover repeat doses.");
        }
        return errors;
    }

    public async Task<IReadOnlyList<string>> SaveDraftAsync(Guid id, MedRuleSetEdit e, string user, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear(); // the old rows are deleted in the database below – nothing stale may stay tracked
        var rs = await db.RuleSets.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (rs is null) return ["Rule set not found."];
        if (rs.Status != MedRuleSetStatus.Draft) return ["Only a draft can be changed – make a new draft from it."];
        var errors = Validate(e);
        if (errors.Count > 0) return errors;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var groupIds = db.Groups.Where(g => g.RuleSetId == id).Select(g => g.Id);
            await db.GroupMembers.Where(m => groupIds.Contains(m.GroupId)).ExecuteDeleteAsync(ct);
            await db.Groups.Where(g => g.RuleSetId == id).ExecuteDeleteAsync(ct);
            await db.DoseRules.Where(r => r.RuleSetId == id).ExecuteDeleteAsync(ct);
            await db.CrossReactions.Where(r => r.RuleSetId == id).ExecuteDeleteAsync(ct);
            await db.Interactions.Where(r => r.RuleSetId == id).ExecuteDeleteAsync(ct);
            rs.Name = Clip(e.Name, 150)!; rs.Notes = Clip(e.Notes, 2000);
            rs.DoseRules = []; rs.Groups = []; rs.CrossReactions = []; rs.Interactions = [];
            Fill(rs, e);
            db.DoseRules.AddRange(rs.DoseRules); db.Groups.AddRange(rs.Groups); db.CrossReactions.AddRange(rs.CrossReactions); db.Interactions.AddRange(rs.Interactions);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        db.ChangeTracker.Clear();
        await audit.WriteAsync(user, "Update", "MedicineRuleSet", rs.Id.ToString(), reason: $"v{rs.Version} draft saved: {e.DoseRules.Count} dose rules, {e.Groups.Count} groups, {e.Interactions.Count} interactions", ct: ct);
        return [];
    }

    public async Task<string?> ApproveAsync(Guid id, string approver, string? role, string? note, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note)) return "Add an approval note (e.g. the clinical governance meeting and the references checked).";
        var rs = await GetAsync(id, ct);
        if (rs is null) return "Rule set not found.";
        if (rs.Status != MedRuleSetStatus.Draft) return "Only a draft can be approved.";
        var problems = Validate(ToEdit(rs));
        if (problems.Count > 0) return "Fix the rule set first: " + problems[0];
        string? error = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = clock.GetUtcNow();
            await db.RuleSets.Where(r => r.Status == MedRuleSetStatus.Approved).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, MedRuleSetStatus.Retired), ct);
            var n = await db.RuleSets.Where(r => r.Id == id && r.Status == MedRuleSetStatus.Draft).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, MedRuleSetStatus.Approved).SetProperty(r => r.ApprovedBy, approver).SetProperty(r => r.ApprovedRole, Clip(role, 100))
                .SetProperty(r => r.ApprovedAt, now).SetProperty(r => r.ApprovalNote, Clip(note, 1000))
                .SetProperty(r => r.Name, r => r.Name.Replace(" (not yet clinically approved)", "")), ct);
            if (n != 1) { error = "The draft was changed by someone else – reload it."; await tx.RollbackAsync(ct); return; }
            await tx.CommitAsync(ct);
        });
        if (error is not null) return error;
        await audit.WriteAsync(approver, "Approve", "MedicineRuleSet", id.ToString(), reason: $"v{rs.Version} approved: {note.Trim()}", ct: ct);
        return null;
    }

    public async Task<string?> DeleteDraftAsync(Guid id, string user, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        var rs = await db.RuleSets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (rs is null) return "Rule set not found.";
        if (rs.Status != MedRuleSetStatus.Draft) return "Only a draft can be deleted – approved and retired rules are kept for the record.";
        if (await db.Checks.AnyAsync(c => c.RuleSetVersion == rs.Version, ct)) return "Patients were checked against this version – it cannot be deleted.";
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var groupIds = db.Groups.Where(g => g.RuleSetId == id).Select(g => g.Id);
            await db.GroupMembers.Where(m => groupIds.Contains(m.GroupId)).ExecuteDeleteAsync(ct);
            await db.RuleSets.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        });
        await audit.WriteAsync(user, "Delete", "MedicineRuleSet", id.ToString(), reason: $"v{rs.Version} draft deleted", ct: ct);
        return null;
    }
}
