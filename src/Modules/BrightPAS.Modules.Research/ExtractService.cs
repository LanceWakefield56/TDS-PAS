using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Patients;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Modules.Research;

/// <summary>A dataset that can be extracted (implemented in the web app, which can read every module).</summary>
public interface IDatasetSource
{
    string Key { get; }
    string Label { get; }
    IReadOnlyList<ColumnDef> Columns { get; }
    Task<IReadOnlyList<DataRow>> RowsAsync(DateOnly @from, DateOnly to, CancellationToken ct);
}

public sealed record ExtractEdit(string Title, string Dataset, DeidLevel Level, DateOnly FromDate, DateOnly ToDate, ExtractPurpose Purpose, string Recipient,
    string? EthicsReference, string? MaoriDataGovernance, int SmallCell, bool RemoveSmallGroups, IReadOnlyList<string> Columns);

/// <summary>
/// Extract requests (spec 7.21): build → (patient-level) Privacy Officer approval → release as an encrypted, watermarked, logged file.
/// Aggregate extracts need no approval but are still suppressed and logged.
/// </summary>
public sealed class ExtractService(ResearchDbContext db, PatientsDbContext patients, IEnumerable<IDatasetSource> sources, AuditWriter audit, TimeProvider clock)
{
    public IReadOnlyList<IDatasetSource> Datasets => [.. sources.OrderBy(s => s.Label, StringComparer.Ordinal)];
    private IDatasetSource? Source(string key) => sources.FirstOrDefault(s => s.Key == key);

    public async Task<(Extract? Extract, IReadOnlyList<string> Errors)> CreateAsync(ExtractEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var src = Source(e.Dataset);
        if (src is null) errors.Add("Choose a dataset.");
        if (string.IsNullOrWhiteSpace(e.Title)) errors.Add("Give the extract a title.");
        if (string.IsNullOrWhiteSpace(e.Recipient)) errors.Add("Say who will receive it.");
        if (e.ToDate < e.FromDate || e.ToDate.DayNumber - e.FromDate.DayNumber > 3700) errors.Add("Choose a period of up to ten years.");
        if (!Enum.IsDefined(e.Level) || !Enum.IsDefined(e.Purpose)) errors.Add("Choose the level and purpose.");
        if (e.SmallCell is < 3 or > 20) errors.Add("The small-number threshold must be 3 to 20.");
        if (e.Columns is null || e.Columns.Count == 0) errors.Add("Choose at least one column.");
        if (src is not null && e.Columns is not null)
            foreach (var c in e.Columns.Where(c => src.Columns.All(d => d.Key != c))) errors.Add($"Unknown column: {c}.");
        if (e.Purpose == ExtractPurpose.Research && e.Level != DeidLevel.Aggregate && string.IsNullOrWhiteSpace(e.EthicsReference)) errors.Add("Research extracts need the ethics approval reference (HDEC or institutional).");
        if (src is not null && e.Columns is not null && e.Columns.Any(c => src.Columns.Any(d => d.Key == c && d.Kind == ColumnKind.Ethnicity)) && string.IsNullOrWhiteSpace(e.MaoriDataGovernance))
            errors.Add("Ethnicity can only be released with a note of how it meets TDS's Māori data governance arrangements (Te Mana Raraunga principles).");
        if (errors.Count > 0) return (null, errors);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var s = await db.Settings.SingleAsync(ct);
            var x = new Extract
            {
                Number = $"DX-{NzTime.Today(clock).Year}-{s.NextNumber:D3}",
                Title = e.Title.Trim(),
                Dataset = e.Dataset,
                Level = e.Level,
                FromDate = e.FromDate,
                ToDate = e.ToDate,
                Purpose = e.Purpose,
                Recipient = e.Recipient.Trim(),
                EthicsReference = e.EthicsReference?.Trim(),
                MaoriDataGovernance = e.MaoriDataGovernance?.Trim(),
                SmallCell = e.SmallCell,
                RemoveSmallGroups = e.RemoveSmallGroups,
                Status = e.Level == DeidLevel.Aggregate ? ExtractStatus.Approved : ExtractStatus.Submitted,
                RequestedBy = user,
                RequestedAt = clock.GetUtcNow(),
                Columns = [.. e.Columns!.Distinct(StringComparer.Ordinal).Select((c, i) => new ExtractColumn { Column = c, Position = i })]
            };
            if (x.Level == DeidLevel.Aggregate) { x.DecidedBy = "automatic"; x.DecidedAt = clock.GetUtcNow(); x.DecisionNote = "Aggregate only, small cells suppressed."; }
            s.NextNumber++;
            db.Extracts.Add(x);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await audit.WriteAsync(user, "Create", "Extract", x.Id.ToString(), reason: $"{x.Number} {x.Level} {x.Dataset} {x.FromDate:yyyy-MM-dd}–{x.ToDate:yyyy-MM-dd} for {x.Purpose} to {x.Recipient}", ct: ct);
            return ((Extract?)x, (IReadOnlyList<string>)[]);
        });
    }

    public async Task<IReadOnlyList<Extract>> ListAsync(CancellationToken ct = default) =>
        await db.Extracts.AsNoTracking().Include(x => x.Columns).OrderByDescending(x => x.RequestedAt).Take(300).ToListAsync(ct);

    public async Task<Extract?> GetAsync(Guid id, CancellationToken ct = default) => await db.Extracts.AsNoTracking().Include(x => x.Columns).SingleOrDefaultAsync(x => x.Id == id, ct);

    private async Task<DeidResult> RunAsync(Extract x, bool preview, CancellationToken ct)
    {
        var src = Source(x.Dataset) ?? throw new InvalidOperationException("Dataset not available.");
        var rows = await src.RowsAsync(x.FromDate, x.ToDate, ct);
        var codes = preview ? new Dictionary<Guid, string>() : await db.StudyCodes.Where(c => c.ExtractId == x.Id).ToDictionaryAsync(c => c.PatientId, c => c.Code, ct);
        var used = codes.Values.ToHashSet(StringComparer.Ordinal);
        string Code(Guid pid)
        {
            if (codes.TryGetValue(pid, out var c)) return c;
            do c = DeidEngine.NewStudyCode(); while (!used.Add(c));
            codes[pid] = c;
            if (!preview) db.StudyCodes.Add(new StudyCode { ExtractId = x.Id, PatientId = pid, Code = c });
            return preview ? "S•••••••" : c;
        }
        var r = DeidEngine.Apply(x.Level, src.Columns, [.. x.Columns.OrderBy(c => c.Position).Select(c => c.Column)], rows, NzTime.Today(clock), Code, x.SmallCell, x.RemoveSmallGroups, !string.IsNullOrWhiteSpace(x.MaoriDataGovernance));
        return r;
    }

    /// <summary>Runs the extract without keeping anything: counts, smallest group, rows at risk, notes and the first rows.</summary>
    public async Task<DeidResult?> PreviewAsync(Guid id, string user, CancellationToken ct = default)
    {
        var x = await GetAsync(id, ct);
        if (x is null) return null;
        var r = await RunAsync(x, true, ct);
        var t = await db.Extracts.SingleAsync(e => e.Id == id, ct);
        t.Rows = r.Rows.Count; t.MinGroupSize = r.MinGroupSize; t.AtRiskRows = r.AtRiskRows; t.SuppressedCells = r.SuppressedCells;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Preview", "Extract", id.ToString(), reason: $"{x.Number}: {r.Rows.Count} rows, k={r.MinGroupSize}, at risk {r.AtRiskRows}", ct: ct);
        return r with { Rows = [.. r.Rows.Take(25)] };
    }

    /// <summary>The Privacy Officer approves or declines. The requester cannot decide their own request; known re-identification risk needs a note.</summary>
    public async Task<string?> DecideAsync(Guid id, bool approve, string? note, string user, CancellationToken ct = default)
    {
        var x = await db.Extracts.Include(e => e.Columns).SingleOrDefaultAsync(e => e.Id == id, ct);
        if (x is null) return "Extract not found.";
        if (x.Status != ExtractStatus.Submitted) return "This extract is not waiting for a decision.";
        if (string.Equals(x.RequestedBy, user, StringComparison.OrdinalIgnoreCase)) return "Someone other than the requester must decide.";
        if (approve)
        {
            var r = await RunAsync(x, true, ct);
            x.Rows = r.Rows.Count; x.MinGroupSize = r.MinGroupSize; x.AtRiskRows = r.AtRiskRows;
            if (r.AtRiskRows > 0 && string.IsNullOrWhiteSpace(note)) return $"{r.AtRiskRows} row(s) could be re-identified (groups smaller than {x.SmallCell}). Note why release is acceptable, or decline and ask for small groups to be removed.";
        }
        else if (string.IsNullOrWhiteSpace(note)) return "Say why it is declined.";
        x.Status = approve ? ExtractStatus.Approved : ExtractStatus.Declined; x.DecidedBy = user; x.DecidedAt = clock.GetUtcNow(); x.DecisionNote = note?.Trim();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, approve ? "Approve" : "Decline", "Extract", id.ToString(), reason: $"{x.Number}: {note}", ct: ct);
        return null;
    }

    /// <summary>Produces the file (approved extracts only). Study codes are kept so a pseudonymised extract re-runs with the same codes.</summary>
    public async Task<(byte[]? Csv, string? Name, string? Error)> ReleaseAsync(Guid id, string user, CancellationToken ct = default)
    {
        var x = await db.Extracts.Include(e => e.Columns).SingleOrDefaultAsync(e => e.Id == id, ct);
        if (x is null) return (null, null, "Extract not found.");
        if (x.Status is not (ExtractStatus.Approved or ExtractStatus.Released)) return (null, null, x.Status == ExtractStatus.Submitted ? "Waiting for the Privacy Officer's approval." : "This extract was declined.");
        var r = await RunAsync(x, false, ct);
        if (x.Level == DeidLevel.DeIdentified && r.AtRiskRows > 0 && string.IsNullOrWhiteSpace(x.DecisionNote)) return (null, null, "Re-identification risk found since approval – ask the Privacy Officer to review.");
        x.Status = ExtractStatus.Released; x.ReleasedBy ??= user; x.ReleasedAt ??= clock.GetUtcNow(); x.Downloads++; x.Rows = r.Rows.Count;
        await db.SaveChangesAsync(ct);
        var mark = $"Tasman Day Surgery {x.Number} · {x.Level} · {x.Title} · for {x.Purpose} by {x.Recipient} · released by {user} {clock.GetUtcNow().ToNz():yyyy-MM-dd HH:mm} · do not attempt to identify anyone or pass this file on.";
        await audit.WriteAsync(user, "Export", "Extract", id.ToString(), reason: $"{x.Number} ({x.Level}, {r.Rows.Count} rows) to {x.Recipient} for {x.Purpose}", ct: ct);
        return (DeidEngine.Csv(r, mark), $"TDS-{x.Number}", null);
    }

    /// <summary>The pseudonym linking key (study code ↔ NHI) – Privacy Officer only; every look is audited.</summary>
    public async Task<byte[]?> StudyKeyAsync(Guid id, string user, CancellationToken ct = default)
    {
        var x = await db.Extracts.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct);
        if (x is null || x.Level != DeidLevel.Pseudonymised) return null;
        var codes = await db.StudyCodes.AsNoTracking().Where(c => c.ExtractId == id).ToListAsync(ct);
        var ids = codes.Select(c => c.PatientId).ToList();
        var nhi = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nhi ?? p.Mrn, ct);
        await audit.WriteAsync(user, "ViewStudyKey", "Extract", id.ToString(), reason: $"{x.Number}: {codes.Count} codes", ct: ct);
        var sb = new System.Text.StringBuilder($"# {x.Number} linking key – keep separate from the extract\r\nStudy ID,NHI or MRN\r\n");
        foreach (var c in codes.OrderBy(c => c.Code, StringComparer.Ordinal)) sb.Append(c.Code).Append(',').Append(nhi.GetValueOrDefault(c.PatientId)).Append("\r\n");
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }
}
