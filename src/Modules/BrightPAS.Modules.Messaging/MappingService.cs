using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Messaging;

public sealed record EntryEdit(string From, string To, string? Description);
public sealed record TableEdit(Guid? Id, string Name, string? Description, MissingCode WhenMissing, string? DefaultValue, IReadOnlyList<EntryEdit>? Entries);
public sealed record TableRow(Guid Id, string Name, string? Description, string WhenMissing, string? DefaultValue, int Entries, int UsedByRules, DateTimeOffset UpdatedAt, string UpdatedBy);
public sealed record TableView(TableRow Table, IReadOnlyList<EntryEdit> Entries);

public sealed record NewMappingEdit(string Name, MessageFormat SourceFormat, MessageFormat TargetFormat, string? Description);
public sealed record RuleEdit(string Target, string? Source, RuleFunction Function, string? Argument, Guid? TranslationTableId,
    string? ConditionSource, ConditionOperator? ConditionOperator, string? ConditionValue);
public sealed record FilterEdit(string Source, ConditionOperator Operator, string? Value);
public sealed record VersionEdit(StartFrom StartFrom, string? Template, string? SampleInput, string? ChangeNote, IReadOnlyList<RuleEdit>? Rules, IReadOnlyList<FilterEdit>? Filters);

public sealed record MappingRow(Guid Id, string Name, string SourceFormat, string TargetFormat, string? Description, int? Live, int? Draft, int? Awaiting, int UsedInFlows);
public sealed record VersionHeader(Guid Id, int Number, string Status, string EditedBy, DateTimeOffset EditedAt, string? ApprovedBy, DateTimeOffset? ApprovedAt, string? ChangeNote);
public sealed record VersionView(Guid Id, int Number, string Status, string StartFrom, string? Template, string? SampleInput, string? ChangeNote, string EditedBy, DateTimeOffset EditedAt,
    string? SubmittedBy, DateTimeOffset? SubmittedAt, string? ApprovedBy, DateTimeOffset? ApprovedAt, IReadOnlyList<RuleEdit> Rules, IReadOnlyList<FilterEdit> Filters);
public sealed record MappingView(MappingRow Mapping, IReadOnlyList<VersionHeader> Versions, VersionView Selected);

public sealed record PreviewResult(string? Output, IReadOnlyList<DocNode> OutputTree, bool FilteredOut, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Steps, IReadOnlyList<DocNode> SourceTree, IReadOnlyList<string> SourceProblems);

public sealed record LiveMapping(Mapping Mapping, MappingVersion Version, IReadOnlyList<FilterRule> Filters, IReadOnlyList<MappingRule> Rules, IReadOnlyDictionary<Guid, Translation> Tables);

/// <summary>Translation tables and versioned mappings (draft → submitted → approved by a second person → live; instant rollback).</summary>
public sealed class MappingService(MsgDbContext db, AuditWriter audit, TimeProvider clock)
{
    // ------------------------------------------------------------------ translation tables
    public async Task<IReadOnlyList<TableRow>> TablesAsync(CancellationToken ct = default)
    {
        var counts = await db.Entries.AsNoTracking().GroupBy(e => e.TableId).Select(g => new { g.Key, n = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.n, ct);
        var used = await db.Rules.AsNoTracking().Where(r => r.TranslationTableId != null).GroupBy(r => r.TranslationTableId!.Value).Select(g => new { g.Key, n = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.n, ct);
        return (await db.Tables.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct))
            .Select(t => new TableRow(t.Id, t.Name, t.Description, t.WhenMissing.ToString(), t.DefaultValue, counts.GetValueOrDefault(t.Id), used.GetValueOrDefault(t.Id), t.UpdatedAt, t.UpdatedBy)).ToList();
    }

    public async Task<TableView?> TableAsync(Guid id, CancellationToken ct = default)
    {
        var row = (await TablesAsync(ct)).FirstOrDefault(t => t.Id == id);
        if (row is null) return null;
        var entries = await db.Entries.AsNoTracking().Where(e => e.TableId == id).OrderBy(e => e.FromCode).Select(e => new EntryEdit(e.FromCode, e.ToCode, e.Description)).ToListAsync(ct);
        return new(row, entries);
    }

    public async Task<(TranslationTable? Table, IReadOnlyList<string> Errors)> SaveTableAsync(TableEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var name = (e.Name ?? "").Trim();
        if (name.Length is < 2 or > 80) errors.Add("Enter a table name (2–80 characters).");
        if (await db.Tables.AnyAsync(t => t.Name == name && t.Id != (e.Id ?? Guid.Empty), ct)) errors.Add($"There is already a table called {name}.");
        if (e.WhenMissing == MissingCode.UseDefault && string.IsNullOrWhiteSpace(e.DefaultValue)) errors.Add("Enter the default value to use for codes not in the table.");
        var entries = (e.Entries ?? []).Where(x => !string.IsNullOrWhiteSpace(x.From)).Select(x => new EntryEdit(x.From.Trim(), (x.To ?? "").Trim(), Blank(x.Description))).ToList();
        foreach (var dup in entries.GroupBy(x => x.From, StringComparer.Ordinal).Where(g => g.Count() > 1)) errors.Add($"\"{dup.Key}\" appears more than once.");
        if (entries.Any(x => x.From.Length > 100 || x.To.Length > 200)) errors.Add("Codes can be up to 100 characters (from) and 200 (to).");
        var t = e.Id is { } id ? await db.Tables.SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        if (e.Id is not null && t is null) errors.Add("Table not found.");
        if (errors.Count > 0) return (null, errors);
        t ??= db.Tables.Add(new TranslationTable()).Entity;
        t.Name = name; t.Description = Blank(e.Description); t.WhenMissing = e.WhenMissing; t.DefaultValue = Blank(e.DefaultValue);
        t.UpdatedAt = clock.GetUtcNow(); t.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        if (e.Entries is not null)
        {
            await db.Entries.Where(x => x.TableId == t.Id).ExecuteDeleteAsync(ct);
            db.Entries.AddRange(entries.Select(x => new TranslationEntry { TableId = t.Id, FromCode = x.From, ToCode = x.To, Description = x.Description }));
            await db.SaveChangesAsync(ct);
        }
        await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "TranslationTable", t.Id.ToString(), reason: $"{t.Name}: {entries.Count} code(s)", ct: ct);
        return (t, []);
    }

    public async Task<string?> DeleteTableAsync(Guid id, string user, CancellationToken ct = default)
    {
        var t = await db.Tables.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return "Table not found.";
        if (await db.Rules.AnyAsync(r => r.TranslationTableId == id, ct)) return "This table is used by a mapping. Remove it from the mapping first.";
        db.Tables.Remove(t);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "TranslationTable", id.ToString(), reason: t.Name, ct: ct);
        return null;
    }

    /// <summary>CSV with columns From,To,Description (header optional). Quotes allowed.</summary>
    public static IReadOnlyList<EntryEdit> ParseCsv(string csv)
    {
        var list = new List<EntryEdit>();
        foreach (var line in csv.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = SplitCsv(line);
            if (list.Count == 0 && cells.Count > 0 && cells[0].Trim().Equals("from", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new(cells.ElementAtOrDefault(0)?.Trim() ?? "", cells.ElementAtOrDefault(1)?.Trim() ?? "", cells.ElementAtOrDefault(2)?.Trim()));
        }
        return list;
    }

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted) { if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else if (c == '"') quoted = false; else sb.Append(c); }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    public static string ToCsv(IEnumerable<EntryEdit> entries)
    {
        static string Q(string? s) => s is null ? "" : s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        // A leading =, +, - or @ is prefixed so spreadsheets do not run it as a formula.
        static string Safe(string? s) => s is { Length: > 0 } && "=+-@".Contains(s[0]) ? "'" + s : s ?? "";
        return "From,To,Description\r\n" + string.Concat(entries.Select(e => $"{Q(Safe(e.From))},{Q(Safe(e.To))},{Q(Safe(e.Description))}\r\n"));
    }

    // ------------------------------------------------------------------ mappings
    public async Task<IReadOnlyList<MappingRow>> MappingsAsync(CancellationToken ct = default)
    {
        var versions = await db.Versions.AsNoTracking().Select(v => new { v.MappingId, v.Number, v.Status }).ToListAsync(ct);
        var used = await db.Nodes.AsNoTracking().Where(n => n.MappingId != null).GroupBy(n => n.MappingId!.Value).Select(g => new { g.Key, n = g.Select(x => x.FlowId).Distinct().Count() }).ToDictionaryAsync(x => x.Key, x => x.n, ct);
        return (await db.Mappings.AsNoTracking().OrderBy(m => m.Name).ToListAsync(ct)).Select(m =>
        {
            var vs = versions.Where(v => v.MappingId == m.Id).ToList();
            return new MappingRow(m.Id, m.Name, m.SourceFormat.ToString(), m.TargetFormat.ToString(), m.Description,
                vs.FirstOrDefault(v => v.Status == VersionStatus.Live)?.Number, vs.FirstOrDefault(v => v.Status == VersionStatus.Draft)?.Number,
                vs.FirstOrDefault(v => v.Status == VersionStatus.AwaitingApproval)?.Number, used.GetValueOrDefault(m.Id));
        }).ToList();
    }

    public async Task<(Mapping? Mapping, IReadOnlyList<string> Errors)> CreateMappingAsync(NewMappingEdit e, string user, CancellationToken ct = default)
    {
        var name = (e.Name ?? "").Trim();
        if (name.Length is < 2 or > 100) return (null, ["Enter a mapping name (2–100 characters)."]);
        if (await db.Mappings.AnyAsync(m => m.Name == name, ct)) return (null, [$"There is already a mapping called {name}."]);
        var now = clock.GetUtcNow();
        var m = db.Mappings.Add(new Mapping { Name = name, SourceFormat = e.SourceFormat, TargetFormat = e.TargetFormat, Description = Blank(e.Description), CreatedAt = now, CreatedBy = user }).Entity;
        db.Versions.Add(new MappingVersion
        {
            MappingId = m.Id, Number = 1, StartFrom = e.SourceFormat == e.TargetFormat ? StartFrom.CopySource : StartFrom.Template,
            SampleInput = SampleFor(e.SourceFormat), Template = e.SourceFormat == e.TargetFormat ? null : TemplateFor(e.TargetFormat), EditedBy = user, EditedAt = now, ChangeNote = "First version",
        });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "Mapping", m.Id.ToString(), reason: $"{m.Name} ({m.SourceFormat} → {m.TargetFormat})", ct: ct);
        return (m, []);
    }

    public async Task<MappingView?> MappingAsync(Guid id, Guid? versionId = null, CancellationToken ct = default)
    {
        var row = (await MappingsAsync(ct)).FirstOrDefault(m => m.Id == id);
        if (row is null) return null;
        var versions = await db.Versions.AsNoTracking().Where(v => v.MappingId == id).OrderByDescending(v => v.Number).ToListAsync(ct);
        var sel = versionId is { } vid ? versions.FirstOrDefault(v => v.Id == vid)
            : versions.FirstOrDefault(v => v.Status == VersionStatus.Draft) ?? versions.FirstOrDefault(v => v.Status == VersionStatus.AwaitingApproval)
              ?? versions.FirstOrDefault(v => v.Status == VersionStatus.Live) ?? versions.First();
        if (sel is null) return null;
        return new(row, versions.Select(v => new VersionHeader(v.Id, v.Number, v.Status.ToString(), v.EditedBy, v.EditedAt, v.ApprovedBy, v.ApprovedAt, v.ChangeNote)).ToList(), await ViewAsync(sel, ct));
    }

    private async Task<VersionView> ViewAsync(MappingVersion v, CancellationToken ct)
    {
        var rules = await db.Rules.AsNoTracking().Where(r => r.VersionId == v.Id).OrderBy(r => r.Sequence)
            .Select(r => new RuleEdit(r.Target, r.Source, r.Function, r.Argument, r.TranslationTableId, r.ConditionSource, r.ConditionOperator, r.ConditionValue)).ToListAsync(ct);
        var filters = await db.Filters.AsNoTracking().Where(f => f.VersionId == v.Id).OrderBy(f => f.Sequence).Select(f => new FilterEdit(f.Source, f.Operator, f.Value)).ToListAsync(ct);
        return new(v.Id, v.Number, v.Status.ToString(), v.StartFrom.ToString(), v.Template, v.SampleInput, v.ChangeNote, v.EditedBy, v.EditedAt, v.SubmittedBy, v.SubmittedAt,
            v.ApprovedBy, v.ApprovedAt, rules, filters);
    }

    private static List<string> Check(Mapping m, VersionEdit e, IReadOnlySet<Guid> tables)
    {
        var errors = new List<string>();
        var n = 0;
        foreach (var r in e.Rules ?? [])
        {
            n++;
            if (string.IsNullOrWhiteSpace(r.Target)) { errors.Add($"Rule {n}: choose the field to set."); continue; }
            if (MappingEngine.PathProblem(m.TargetFormat, r.Target.Trim()) is { } tp) errors.Add($"Rule {n}: {tp}");
            var needsSource = r.Function is not (RuleFunction.Constant or RuleFunction.Clear);
            if (needsSource && string.IsNullOrWhiteSpace(r.Source)) errors.Add($"Rule {n} ({r.Target}): choose where the value comes from.");
            foreach (var s in (r.Source ?? "").Split(" + ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (needsSource && MappingEngine.PathProblem(m.SourceFormat, s) is { } sp) errors.Add($"Rule {n}: {sp}");
            if (r.Function == RuleFunction.Translate && (r.TranslationTableId is null || !tables.Contains(r.TranslationTableId.Value))) errors.Add($"Rule {n} ({r.Target}): choose a translation table.");
            if (r.Function == RuleFunction.Date && (r.Argument ?? "").Split('>').Length != 2) errors.Add($"Rule {n} ({r.Target}): date needs \"from>to\" formats, e.g. yyyyMMdd>yyyy-MM-dd.");
            if (r.ConditionSource is { Length: > 0 } cs && MappingEngine.PathProblem(m.SourceFormat, cs.Trim()) is { } cp) errors.Add($"Rule {n} condition: {cp}");
            if (r.ConditionSource is { Length: > 0 } && r.ConditionOperator is null) errors.Add($"Rule {n} ({r.Target}): choose how the condition compares.");
        }
        n = 0;
        foreach (var f in e.Filters ?? [])
        {
            n++;
            if (string.IsNullOrWhiteSpace(f.Source) || MappingEngine.PathProblem(m.SourceFormat, f.Source.Trim()) is not null) errors.Add($"Filter {n}: choose a valid field to check.");
        }
        if (e.StartFrom == StartFrom.Template && !string.IsNullOrWhiteSpace(e.Template))
            try { MessageDocs.Parse(m.TargetFormat, e.Template); } catch (MappingException ex) { errors.Add("Template: " + ex.Message); }
        return errors;
    }

    public async Task<IReadOnlyList<string>> SaveDraftAsync(Guid versionId, VersionEdit e, string user, CancellationToken ct = default)
    {
        var v = await db.Versions.SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null) return ["Version not found."];
        if (v.Status != VersionStatus.Draft) return ["Only a draft can be changed. Make a new draft from this version first."];
        var m = await db.Mappings.AsNoTracking().SingleAsync(x => x.Id == v.MappingId, ct);
        var tables = (await db.Tables.AsNoTracking().Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        var errors = Check(m, e, tables);
        if (errors.Count > 0) return errors;
        v.StartFrom = m.SourceFormat == m.TargetFormat ? e.StartFrom : StartFrom.Template;
        v.Template = Blank(e.Template); v.SampleInput = Blank(e.SampleInput); v.ChangeNote = Blank(e.ChangeNote) is { } note ? Trunc(note, 300) : null;
        v.EditedBy = user; v.EditedAt = clock.GetUtcNow();
        await db.Rules.Where(r => r.VersionId == v.Id).ExecuteDeleteAsync(ct);
        await db.Filters.Where(f => f.VersionId == v.Id).ExecuteDeleteAsync(ct);
        var seq = 0;
        db.Rules.AddRange((e.Rules ?? []).Select(r => new MappingRule
        {
            VersionId = v.Id, Sequence = ++seq, Target = r.Target.Trim(), Source = Blank(r.Source), Function = r.Function, Argument = r.Argument,
            TranslationTableId = r.Function == RuleFunction.Translate ? r.TranslationTableId : null, ConditionSource = Blank(r.ConditionSource),
            ConditionOperator = Blank(r.ConditionSource) is null ? null : r.ConditionOperator, ConditionValue = Blank(r.ConditionSource) is null ? null : r.ConditionValue,
        }));
        seq = 0;
        db.Filters.AddRange((e.Filters ?? []).Select(f => new FilterRule { VersionId = v.Id, Sequence = ++seq, Source = f.Source.Trim(), Operator = f.Operator, Value = f.Value }));
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "MappingVersion", v.Id.ToString(), reason: $"{m.Name} v{v.Number} draft: {seq} filter(s), {(e.Rules ?? []).Count} rule(s)", ct: ct);
        return [];
    }

    public async Task<(MappingVersion? Version, string? Error)> NewDraftAsync(Guid mappingId, Guid fromVersionId, string user, CancellationToken ct = default)
    {
        if (await db.Versions.AnyAsync(v => v.MappingId == mappingId && (v.Status == VersionStatus.Draft || v.Status == VersionStatus.AwaitingApproval), ct))
            return (null, "There is already a draft or a version waiting for approval. Finish that first.");
        var from = await db.Versions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == fromVersionId && v.MappingId == mappingId, ct);
        if (from is null) return (null, "Version not found.");
        var number = await db.Versions.Where(v => v.MappingId == mappingId).MaxAsync(v => v.Number, ct) + 1;
        var nv = db.Versions.Add(new MappingVersion
        {
            MappingId = mappingId, Number = number, StartFrom = from.StartFrom, Template = from.Template, SampleInput = from.SampleInput,
            ChangeNote = $"Copied from v{from.Number}", EditedBy = user, EditedAt = clock.GetUtcNow(),
        }).Entity;
        db.Rules.AddRange((await db.Rules.AsNoTracking().Where(r => r.VersionId == from.Id).ToListAsync(ct)).Select(r => new MappingRule
        {
            VersionId = nv.Id, Sequence = r.Sequence, Target = r.Target, Source = r.Source, Function = r.Function, Argument = r.Argument, TranslationTableId = r.TranslationTableId,
            ConditionSource = r.ConditionSource, ConditionOperator = r.ConditionOperator, ConditionValue = r.ConditionValue,
        }));
        db.Filters.AddRange((await db.Filters.AsNoTracking().Where(f => f.VersionId == from.Id).ToListAsync(ct)).Select(f => new FilterRule { VersionId = nv.Id, Sequence = f.Sequence, Source = f.Source, Operator = f.Operator, Value = f.Value }));
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "MappingVersion", nv.Id.ToString(), reason: $"v{number} draft from v{from.Number}", ct: ct);
        return (nv, null);
    }

    /// <summary>Sends a draft for approval. The sample must run through without errors.</summary>
    public async Task<string?> SubmitAsync(Guid versionId, string user, CancellationToken ct = default)
    {
        var v = await db.Versions.SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null) return "Version not found.";
        if (v.Status != VersionStatus.Draft) return "Only a draft can be sent for approval.";
        if (string.IsNullOrWhiteSpace(v.SampleInput)) return "Add a sample message and check the preview before sending for approval.";
        var m = await db.Mappings.AsNoTracking().SingleAsync(x => x.Id == v.MappingId, ct);
        var test = MappingEngine.Run(m.SourceFormat, m.TargetFormat, v.SampleInput, v.StartFrom, v.Template, await FiltersOf(v.Id, ct), await RulesOf(v.Id, ct), await TablesForAsync(v.Id, ct));
        if (test.Errors.Count > 0) return "The sample message does not map cleanly: " + string.Join(" ", test.Errors);
        v.Status = VersionStatus.AwaitingApproval; v.SubmittedBy = user; v.SubmittedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Submit", "MappingVersion", v.Id.ToString(), reason: $"{m.Name} v{v.Number} for approval", ct: ct);
        return null;
    }

    public async Task<string?> ApproveAsync(Guid versionId, string user, bool requireSecondPerson, CancellationToken ct = default)
    {
        var v = await db.Versions.SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null) return "Version not found.";
        if (v.Status != VersionStatus.AwaitingApproval) return "This version is not waiting for approval.";
        if (requireSecondPerson && (string.Equals(v.EditedBy, user, StringComparison.OrdinalIgnoreCase) || string.Equals(v.SubmittedBy, user, StringComparison.OrdinalIgnoreCase)))
            return "A second person must approve a mapping change – you edited or submitted this version.";
        var m = await db.Mappings.AsNoTracking().SingleAsync(x => x.Id == v.MappingId, ct);
        foreach (var live in await db.Versions.Where(x => x.MappingId == v.MappingId && x.Status == VersionStatus.Live).ToListAsync(ct)) live.Status = VersionStatus.Retired;
        v.Status = VersionStatus.Live; v.ApprovedBy = user; v.ApprovedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Approve", "MappingVersion", v.Id.ToString(), reason: $"{m.Name} v{v.Number} is now live", ct: ct);
        return null;
    }

    public async Task<string?> RejectAsync(Guid versionId, string user, string? note, CancellationToken ct = default)
    {
        var v = await db.Versions.SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null || v.Status != VersionStatus.AwaitingApproval) return "This version is not waiting for approval.";
        v.Status = VersionStatus.Draft; v.ChangeNote = Trunc($"Returned by {user}: {note}".Trim(), 300); v.SubmittedBy = null; v.SubmittedAt = null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Reject", "MappingVersion", v.Id.ToString(), reason: note, ct: ct);
        return null;
    }

    /// <summary>Puts a previous (retired) version back live immediately – for when a new version causes problems. Audited with the reason.</summary>
    public async Task<string?> RollbackAsync(Guid versionId, string user, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5) return "Give a reason for rolling back.";
        var v = await db.Versions.SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null || v.Status != VersionStatus.Retired) return "Only a previous (retired) version can be put back live.";
        foreach (var live in await db.Versions.Where(x => x.MappingId == v.MappingId && x.Status == VersionStatus.Live).ToListAsync(ct)) live.Status = VersionStatus.Retired;
        v.Status = VersionStatus.Live;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Rollback", "MappingVersion", v.Id.ToString(), reason: $"v{v.Number} back live: {reason.Trim()}", ct: ct);
        return null;
    }

    public async Task<LiveMapping?> LiveAsync(Guid mappingId, CancellationToken ct = default)
    {
        var m = await db.Mappings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mappingId, ct);
        var v = await db.Versions.AsNoTracking().SingleOrDefaultAsync(x => x.MappingId == mappingId && x.Status == VersionStatus.Live, ct);
        if (m is null || v is null) return null;
        return new(m, v, await FiltersOf(v.Id, ct), await RulesOf(v.Id, ct), await TablesForAsync(v.Id, ct));
    }

    private async Task<IReadOnlyList<MappingRule>> RulesOf(Guid versionId, CancellationToken ct) => await db.Rules.AsNoTracking().Where(r => r.VersionId == versionId).OrderBy(r => r.Sequence).ToListAsync(ct);
    private async Task<IReadOnlyList<FilterRule>> FiltersOf(Guid versionId, CancellationToken ct) => await db.Filters.AsNoTracking().Where(f => f.VersionId == versionId).OrderBy(f => f.Sequence).ToListAsync(ct);

    private async Task<IReadOnlyDictionary<Guid, Translation>> TablesForAsync(Guid versionId, CancellationToken ct)
    {
        var ids = await db.Rules.AsNoTracking().Where(r => r.VersionId == versionId && r.TranslationTableId != null).Select(r => r.TranslationTableId!.Value).Distinct().ToListAsync(ct);
        return await TablesAsync(ids, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, Translation>> TablesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        var tables = await db.Tables.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var entries = (await db.Entries.AsNoTracking().Where(e => ids.Contains(e.TableId)).ToListAsync(ct)).ToLookup(e => e.TableId);
        return tables.ToDictionary(t => t.Id, t => new Translation(t.Name, t.WhenMissing, t.DefaultValue,
            entries[t.Id].ToDictionary(e => e.FromCode, e => e.ToCode, StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>Live preview for the designer: nothing is saved or sent.</summary>
    public async Task<PreviewResult> PreviewAsync(MessageFormat source, MessageFormat target, VersionEdit e, CancellationToken ct = default)
    {
        var sample = e.SampleInput ?? "";
        IReadOnlyList<DocNode> srcTree = [];
        var srcProblems = MessageDocs.Validate(source, sample);
        try { srcTree = MessageDocs.Tree(source, sample); } catch (MappingException ex) { srcProblems = [ex.Message]; }
        var fake = new Mapping { SourceFormat = source, TargetFormat = target };
        var errors = Check(fake, e, (await db.Tables.AsNoTracking().Select(t => t.Id).ToListAsync(ct)).ToHashSet());
        if (errors.Count > 0) return new(null, [], false, errors, [], [], srcTree, srcProblems);
        var tables = await TablesAsync((e.Rules ?? []).Where(r => r.TranslationTableId != null).Select(r => r.TranslationTableId!.Value).Distinct().ToList(), ct);
        var seq = 0;
        var rules = (e.Rules ?? []).Select(r => new MappingRule
        {
            Sequence = ++seq, Target = r.Target.Trim(), Source = Blank(r.Source), Function = r.Function, Argument = r.Argument, TranslationTableId = r.TranslationTableId,
            ConditionSource = Blank(r.ConditionSource), ConditionOperator = r.ConditionOperator, ConditionValue = r.ConditionValue,
        }).ToList();
        seq = 0;
        var filters = (e.Filters ?? []).Select(f => new FilterRule { Sequence = ++seq, Source = f.Source.Trim(), Operator = f.Operator, Value = f.Value }).ToList();
        var res = MappingEngine.Run(source, target, sample, source == target ? e.StartFrom : StartFrom.Template, e.Template, filters, rules, tables);
        IReadOnlyList<DocNode> outTree = [];
        if (res.Output is not null) try { outTree = MessageDocs.Tree(target, res.Output); } catch (MappingException) { }
        return new(res.Output, outTree, res.FilteredOut, res.Errors, res.Warnings, res.Steps, srcTree, srcProblems);
    }

    /// <summary>Synthetic starter samples (fictional test patient ZZZ0016 – never real data).</summary>
    public static string SampleFor(MessageFormat f) => f switch
    {
        MessageFormat.Hl7v2 => "MSH|^~\\&|LAB|AWANUI|BrightPAS|TDS|202611101030||ORU^R01|MSG0001|P|2.4\r" +
                               "PID|1||ZZZ0016^^^NHI^NHI||TEST^Aroha^Mere||19800517|F|||1 Test Street^^Nelson^^7010^NZ\r" +
                               "OBR|1|ORD123|ACC456|HB^Haemoglobin^L|||202611100900|||||||||12345^SURGEON^Ben\r" +
                               "OBX|1|NM|HB^Haemoglobin^L||132|g/L|115-155|N|||F\r" +
                               "OBX|2|NM|PLT^Platelets^L||250|10*9/L|150-400|N|||F\r",
        MessageFormat.Fhir => "{\n  \"resourceType\": \"Patient\",\n  \"id\": \"example\",\n  \"identifier\": [{ \"system\": \"https://standards.digital.health.nz/ns/nhi-id\", \"value\": \"ZZZ0016\" }],\n  \"name\": [{ \"family\": \"Test\", \"given\": [\"Aroha\", \"Mere\"] }],\n  \"gender\": \"female\",\n  \"birthDate\": \"1980-05-17\"\n}",
        MessageFormat.Json => "{\n  \"patient\": { \"nhi\": \"ZZZ0016\", \"family\": \"Test\", \"given\": \"Aroha\", \"dob\": \"1980-05-17\" }\n}",
        MessageFormat.Dicom => "{\n  \"00080020\": { \"vr\": \"DA\", \"Value\": [\"20261110\"] },\n  \"00080050\": { \"vr\": \"SH\", \"Value\": [\"ACC456\"] },\n  \"00080060\": { \"vr\": \"CS\", \"Value\": [\"ES\"] },\n" +
                               "  \"00081030\": { \"vr\": \"LO\", \"Value\": [\"Colonoscopy\"] },\n  \"00100010\": { \"vr\": \"PN\", \"Value\": [{ \"Alphabetic\": \"TEST^Aroha^Mere\" }] },\n" +
                               "  \"00100020\": { \"vr\": \"LO\", \"Value\": [\"ZZZ0016\"] },\n  \"00100030\": { \"vr\": \"DA\", \"Value\": [\"19800517\"] },\n  \"00100040\": { \"vr\": \"CS\", \"Value\": [\"F\"] },\n" +
                               "  \"0020000D\": { \"vr\": \"UI\", \"Value\": [\"1.2.826.0.1.3680043.10.1.1\"] }\n}",
        MessageFormat.Xml => "<ClinicalDocument xmlns=\"urn:hl7-org:v3\">\n  <title>Discharge summary</title>\n  <recordTarget>\n    <patientRole>\n      <id root=\"2.16.840.1.113883.2.18.2\" extension=\"ZZZ0016\"/>\n" +
                             "      <patient>\n        <name><given>Aroha</given><family>Test</family></name>\n        <administrativeGenderCode code=\"F\"/>\n        <birthTime value=\"19800517\"/>\n      </patient>\n    </patientRole>\n  </recordTarget>\n</ClinicalDocument>",
        MessageFormat.X12 => "ISA*00*          *00*          *ZZ*TDS            *ZZ*INSURER        *261110*1030*^*00501*000000001*0*T*:~\nGS*HC*TDS*INSURER*20261110*1030*1*X*005010X222A1~\nST*837*0001*005010X222A1~\n" +
                             "NM1*IL*1*TEST*AROHA****MI*ZZZ0016~\nCLM*ACC456*4800***24:B:1~\nSE*4*0001~\nGE*1*1~\nIEA*1*000000001~",
        _ => "Sample text",
    };

    public static string TemplateFor(MessageFormat f) => f switch
    {
        MessageFormat.Hl7v2 => "MSH|^~\\&|BrightPAS|TDS|||||ORU^R01||P|2.4\rPID|1\rOBR|1\r",
        MessageFormat.Fhir => "{ \"resourceType\": \"Patient\" }",
        MessageFormat.Dicom => "{}",
        MessageFormat.Json => "{}",
        MessageFormat.Xml => "<ClinicalDocument xmlns=\"urn:hl7-org:v3\"><recordTarget><patientRole/></recordTarget></ClinicalDocument>",
        MessageFormat.X12 => SampleFor(MessageFormat.X12),
        _ => "",
    };

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
