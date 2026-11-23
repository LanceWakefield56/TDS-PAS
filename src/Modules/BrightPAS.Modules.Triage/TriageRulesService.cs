using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Triage;

public sealed record RuleEdit(string Code, string Name, string Fact, RuleOperator Operator, decimal? Threshold, string? TextValue, string? ProcedureContains, string? Anaesthetic,
    TriageOutcome Outcome, string? Precaution, string? Explanation, int SortOrder, bool IsActive);
public sealed record MedicineEdit(string Name, string Keywords, string Instruction, TriageOutcome Outcome, bool IsActive);
public sealed record RuleSetEdit(string Name, string? Notes, IReadOnlyList<RuleEdit> Rules, IReadOnlyList<MedicineEdit> Medicines);

/// <summary>
/// Versioned suitability rule sets. A draft can be edited; approving it (a clinician with rule-approval permission, with a note) makes it the
/// one used for patients and retires the previous one. An approved set never changes – edits start a new draft copy.
/// </summary>
public sealed class TriageRulesService(TriageDbContext db, AuditWriter audit, TimeProvider clock)
{
    public async Task<List<RuleSet>> ListAsync(CancellationToken ct = default) =>
        await db.RuleSets.AsNoTracking().OrderByDescending(r => r.Version).ToListAsync(ct);

    public async Task<RuleSet?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.RuleSets.AsNoTracking().Include(x => x.Rules).Include(x => x.Medicines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is not null) { r.Rules = [.. r.Rules.OrderBy(x => x.SortOrder).ThenBy(x => x.Code, StringComparer.Ordinal)]; r.Medicines = [.. r.Medicines.OrderBy(x => x.Name, StringComparer.Ordinal)]; }
        return r;
    }

    private async Task<int> NextVersionAsync(CancellationToken ct) => (await db.RuleSets.MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;

    /// <summary>Adds the example rule set (once, when there are no rule sets) – a draft that must be reviewed and approved.</summary>
    public async Task<RuleSet?> SeedExampleAsync(string user, CancellationToken ct = default)
    {
        if (await db.RuleSets.AnyAsync(ct)) return null;
        var rs = TriageScoring.ExampleRuleSet(1, user, clock.GetUtcNow());
        db.RuleSets.Add(rs);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "TriageRuleSet", rs.Id.ToString(), reason: $"v{rs.Version} example rules (draft)", ct: ct);
        return rs;
    }

    /// <summary>New draft copied from a rule set (usually the approved one).</summary>
    public async Task<(Guid? Id, string? Error)> CopyAsync(Guid fromId, string user, CancellationToken ct = default)
    {
        var src = await GetAsync(fromId, ct);
        if (src is null) return (null, "Rule set not found.");
        if (await db.RuleSets.AnyAsync(r => r.Status == RuleSetStatus.Draft, ct)) return (null, "There is already a draft – edit or delete it first.");
        var rs = new RuleSet { Version = await NextVersionAsync(ct), Name = src.Name.Replace(" (not yet clinically approved)", "", StringComparison.Ordinal), IsExample = src.IsExample, Notes = src.Notes, CreatedBy = user, CreatedAt = clock.GetUtcNow() };
        rs.Rules.AddRange(src.Rules.Select(x => new SuitabilityRule
        {
            RuleSetId = rs.Id,
            Code = x.Code,
            Name = x.Name,
            Fact = x.Fact,
            Operator = x.Operator,
            Threshold = x.Threshold,
            TextValue = x.TextValue,
            ProcedureContains = x.ProcedureContains,
            Anaesthetic = x.Anaesthetic,
            Outcome = x.Outcome,
            Precaution = x.Precaution,
            Explanation = x.Explanation,
            SortOrder = x.SortOrder,
            IsActive = x.IsActive
        }));
        rs.Medicines.AddRange(src.Medicines.Select(x => new MedicineRule { RuleSetId = rs.Id, Name = x.Name, Keywords = x.Keywords, Instruction = x.Instruction, Outcome = x.Outcome, IsActive = x.IsActive }));
        db.RuleSets.Add(rs);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "TriageRuleSet", rs.Id.ToString(), reason: $"v{rs.Version} draft copied from v{src.Version}", ct: ct);
        return (rs.Id, null);
    }

    public async Task<IReadOnlyList<string>> SaveDraftAsync(Guid id, RuleSetEdit e, string user, CancellationToken ct = default)
    {
        var rs = await db.RuleSets.Include(x => x.Rules).Include(x => x.Medicines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (rs is null) return ["Rule set not found."];
        if (rs.Status != RuleSetStatus.Draft) return ["Only a draft can be changed – make a new draft from it."];
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.Name)) errors.Add("Give the rule set a name.");
        foreach (var r in e.Rules)
        {
            var label = string.IsNullOrWhiteSpace(r.Code) ? r.Name : r.Code;
            if (string.IsNullOrWhiteSpace(r.Code) || r.Code.Length > 20) errors.Add($"Rule ‘{r.Name}’: enter a code of up to 20 characters.");
            if (string.IsNullOrWhiteSpace(r.Name)) errors.Add($"Rule {label}: enter a name.");
            if (!TriageScoring.RuleFacts.ContainsKey(r.Fact ?? "")) errors.Add($"Rule {label}: choose what it checks.");
            if (!Enum.IsDefined(r.Operator) || !Enum.IsDefined(r.Outcome)) errors.Add($"Rule {label}: choose the test and outcome.");
            if (r.Operator is RuleOperator.AtLeast or RuleOperator.AtMost or RuleOperator.Above or RuleOperator.Below && r.Threshold is null) errors.Add($"Rule {label}: enter the limit.");
            if (r.Operator == RuleOperator.Equals && string.IsNullOrWhiteSpace(r.TextValue)) errors.Add($"Rule {label}: enter the value to match.");
            if (r.Anaesthetic is { Length: > 0 } an && an is not ("General" or "Sedation" or "Regional" or "Local")) errors.Add($"Rule {label}: anaesthetic must be General, Sedation, Regional or Local.");
        }
        foreach (var d in e.Rules.GroupBy(r => r.Code?.Trim().ToUpperInvariant()).Where(g => g.Count() > 1)) errors.Add($"Rule code {d.Key} is used more than once.");
        foreach (var m in e.Medicines)
            if (string.IsNullOrWhiteSpace(m.Name) || string.IsNullOrWhiteSpace(m.Keywords) || string.IsNullOrWhiteSpace(m.Instruction)) errors.Add($"Medicine prompt ‘{m.Name}’: enter the name, the words to look for and the instruction.");
        if (errors.Count > 0) return errors;
        (rs.Name, rs.Notes) = (e.Name.Trim(), string.IsNullOrWhiteSpace(e.Notes) ? null : e.Notes.Trim());
        db.Rules.RemoveRange(rs.Rules); db.Medicines.RemoveRange(rs.Medicines);
        await db.SaveChangesAsync(ct);
        foreach (var r in e.Rules)
            db.Rules.Add(new SuitabilityRule
            {
                RuleSetId = rs.Id,
                Code = r.Code.Trim().ToUpperInvariant(),
                Name = r.Name.Trim(),
                Fact = r.Fact,
                Operator = r.Operator,
                Threshold = r.Threshold,
                TextValue = r.TextValue?.Trim(),
                ProcedureContains = string.IsNullOrWhiteSpace(r.ProcedureContains) ? null : r.ProcedureContains.Trim(),
                Anaesthetic = string.IsNullOrWhiteSpace(r.Anaesthetic) ? null : r.Anaesthetic,
                Outcome = r.Outcome,
                Precaution = string.IsNullOrWhiteSpace(r.Precaution) ? null : r.Precaution.Trim(),
                Explanation = string.IsNullOrWhiteSpace(r.Explanation) ? null : r.Explanation.Trim(),
                SortOrder = r.SortOrder,
                IsActive = r.IsActive
            });
        foreach (var m in e.Medicines)
            db.Medicines.Add(new MedicineRule { RuleSetId = rs.Id, Name = m.Name.Trim(), Keywords = m.Keywords.Trim(), Instruction = m.Instruction.Trim(), Outcome = m.Outcome, IsActive = m.IsActive });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "TriageRuleSet", rs.Id.ToString(), reason: $"v{rs.Version}: {e.Rules.Count} rules, {e.Medicines.Count} medicine prompts", ct: ct);
        return [];
    }

    /// <summary>Clinical approval: the draft becomes the rule set used for patients; the previous approved set is retired.</summary>
    public async Task<string?> ApproveAsync(Guid id, string? note, string user, string role, CancellationToken ct = default)
    {
        var rs = await db.RuleSets.Include(x => x.Rules).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (rs is null) return "Rule set not found.";
        if (rs.Status != RuleSetStatus.Draft) return "Only a draft can be approved.";
        if (string.IsNullOrWhiteSpace(note)) return "Record the approval (who reviewed the rules and where it was agreed, e.g. the anaesthetic meeting of …).";
        if (rs.Rules.Count == 0) return "The rule set has no rules.";
        var now = clock.GetUtcNow();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var current = await db.RuleSets.Where(x => x.Status == RuleSetStatus.Approved).ToListAsync(ct);
            foreach (var c in current) { c.Status = RuleSetStatus.Retired; c.RetiredAt = now; }
            await db.SaveChangesAsync(ct);
            (rs.Status, rs.ApprovedBy, rs.ApprovedRole, rs.ApprovedAt, rs.ApprovalNote) = (RuleSetStatus.Approved, user, role, now, note.Trim());
            rs.Name = rs.Name.Replace(" (not yet clinically approved)", "", StringComparison.Ordinal);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        await audit.WriteAsync(user, "Approve", "TriageRuleSet", rs.Id.ToString(), reason: $"v{rs.Version} approved by {user} ({role}): {note}", ct: ct);
        return null;
    }

    public async Task<string?> DeleteDraftAsync(Guid id, string user, CancellationToken ct = default)
    {
        var rs = await db.RuleSets.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (rs is null) return "Rule set not found.";
        if (rs.Status != RuleSetStatus.Draft) return "Only a draft can be deleted – approved and retired rule sets are kept because assessments refer to them.";
        if (await db.Assessments.AnyAsync(a => a.RuleSetId == id, ct)) return "Assessments refer to this rule set.";
        db.RuleSets.Remove(rs);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "TriageRuleSet", id.ToString(), reason: $"draft v{rs.Version}", ct: ct);
        return null;
    }
}
