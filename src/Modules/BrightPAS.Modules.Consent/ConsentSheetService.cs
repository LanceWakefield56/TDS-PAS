using System.Globalization;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Consent;

public sealed record RiskEdit(string Text, RiskFrequency Frequency, bool Serious);
public sealed record LinkEdit(string Title, string Url);
public sealed record SheetEdit(string Code, string Name, string? ProcedureWords, string About, string Benefits, string Alternatives, string NoTreatment,
    string? Anaesthetic, string? Preparation, string? Recovery, IReadOnlyList<RiskEdit> Risks, IReadOnlyList<LinkEdit> Links);

/// <summary>
/// Procedure information sheets: written as drafts, approved by a second clinician (consent.approve) and then published. Publishing a version retires the
/// previous published version of the same code. Published and retired sheets never change – a new version is made from them instead.
/// </summary>
public sealed class ConsentSheetService(ConsentDbContext db, AuditWriter audit, TimeProvider clock)
{
    public static readonly IReadOnlyDictionary<RiskFrequency, string> FrequencyWords = new Dictionary<RiskFrequency, string>
    {
        [RiskFrequency.Common] = "Common (more than 1 in 100)",
        [RiskFrequency.Uncommon] = "Uncommon (1 in 100 to 1 in 1,000)",
        [RiskFrequency.Rare] = "Rare (1 in 1,000 to 1 in 10,000)",
        [RiskFrequency.VeryRare] = "Very rare (less than 1 in 10,000)",
    };

    public async Task<IReadOnlyList<ConsentSheet>> ListAsync(bool includeRetired, CancellationToken ct = default) =>
        (await db.Sheets.AsNoTracking().Include(s => s.Risks).Include(s => s.Links).Where(s => includeRetired || s.Status != SheetStatus.Retired).ToListAsync(ct))
            .OrderBy(s => s.Name, StringComparer.Ordinal).ThenByDescending(s => s.Version).ToList();

    public async Task<ConsentSheet?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Sheets.AsNoTracking().Include(s => s.Risks).Include(s => s.Links).SingleOrDefaultAsync(s => s.Id == id, ct);

    /// <summary>The published sheet whose procedure words appear in the booking's procedure (the longest matching word wins).</summary>
    public async Task<ConsentSheet?> SuggestAsync(string procedure, CancellationToken ct = default)
    {
        var text = procedure.ToLowerInvariant();
        var sheets = await db.Sheets.AsNoTracking().Where(s => s.Status == SheetStatus.Published && s.ProcedureWords != null).ToListAsync(ct);
        return sheets.Select(s => (s, best: Words(s.ProcedureWords).Where(w => text.Contains(w, StringComparison.Ordinal)).Select(w => w.Length).DefaultIfEmpty(0).Max()))
            .Where(x => x.best > 0).OrderByDescending(x => x.best).ThenBy(x => x.s.Name, StringComparer.Ordinal).Select(x => x.s).FirstOrDefault();
    }

    public static IEnumerable<string> Words(string? list) =>
        (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(w => w.ToLowerInvariant()).Where(w => w.Length >= 3);

    public static IReadOnlyList<string> Validate(SheetEdit e)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.Code) || e.Code.Trim().Length > 30 || !e.Code.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            errors.Add("Enter a short code (letters, numbers and dashes, up to 30), e.g. LAPCHOLE.");
        if (string.IsNullOrWhiteSpace(e.Name)) errors.Add("Enter the sheet's name.");
        if (string.IsNullOrWhiteSpace(e.About)) errors.Add("Say what the procedure is.");
        if (string.IsNullOrWhiteSpace(e.Benefits)) errors.Add("Enter the benefits.");
        if (string.IsNullOrWhiteSpace(e.Alternatives)) errors.Add("Enter the alternatives (including not having the procedure).");
        if (string.IsNullOrWhiteSpace(e.NoTreatment)) errors.Add("Say what may happen without the procedure.");
        if (e.Risks is null || e.Risks.Count == 0) errors.Add("Add at least one risk.");
        foreach (var r in e.Risks ?? [])
        {
            if (string.IsNullOrWhiteSpace(r.Text)) errors.Add("A risk has no text.");
            else if (r.Text.Length > 500) errors.Add("Keep each risk under 500 characters.");
            if (!Enum.IsDefined(r.Frequency)) errors.Add($"Choose how often \"{r.Text}\" happens.");
        }
        foreach (var l in e.Links ?? [])
        {
            if (string.IsNullOrWhiteSpace(l.Title)) errors.Add("A video or web page has no title.");
            if (!Uri.TryCreate(l.Url?.Trim(), UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) errors.Add($"\"{l.Title}\": the address must start with https://.");
        }
        foreach (var (name, text) in new[] { ("What it is", e.About), ("Benefits", e.Benefits), ("Alternatives", e.Alternatives), ("Without the procedure", e.NoTreatment), ("Anaesthetic", e.Anaesthetic), ("Getting ready", e.Preparation), ("Recovery", e.Recovery) })
            if ((text ?? "").Length > 4000) errors.Add($"{name} is too long (4,000 characters at most).");
        if ((e.ProcedureWords ?? "").Length > 300) errors.Add("Procedure words are too long.");
        return errors;
    }

    /// <summary>Saves a draft (new, or an existing draft). A published sheet cannot be edited – make a new version.</summary>
    public async Task<(ConsentSheet? Sheet, IReadOnlyList<string> Errors)> SaveDraftAsync(Guid? id, SheetEdit e, string user, CancellationToken ct = default)
    {
        var errors = Validate(e).ToList();
        if (errors.Count > 0) return (null, errors);
        var code = e.Code.Trim().ToUpperInvariant();
        var now = clock.GetUtcNow();
        ConsentSheet s;
        if (id is { } sid)
        {
            s = await db.Sheets.Include(x => x.Risks).Include(x => x.Links).SingleOrDefaultAsync(x => x.Id == sid, ct) ?? throw new InvalidOperationException();
            if (s.Status != SheetStatus.Draft) return (null, ["Only a draft can be changed. Make a new version of a published sheet."]);
            if (s.Code != code && await db.Sheets.AnyAsync(x => x.Code == code, ct)) return (null, [$"The code {code} is already used by another sheet."]);
            db.Risks.RemoveRange(s.Risks); db.Links.RemoveRange(s.Links);
            s.Risks.Clear(); s.Links.Clear();
            if (s.Code != code) s.Version = 1;
        }
        else
        {
            if (await db.Sheets.AnyAsync(x => x.Code == code, ct)) return (null, [$"The code {code} is already used. Open that sheet and make a new version."]);
            s = new ConsentSheet { Code = code, CreatedAt = now, CreatedBy = user };
            db.Sheets.Add(s);
        }
        Apply(s, e, code);
        s.UpdatedAt = now; s.UpdatedBy = user; s.ApprovedAt = null; s.ApprovedBy = null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "SaveDraft", "ConsentSheet", s.Id.ToString(), reason: $"{s.Code} v{s.Version.ToString(CultureInfo.InvariantCulture)}", ct: ct);
        return (s, []);
    }

    private void Apply(ConsentSheet s, SheetEdit e, string code)
    {
        s.Code = code;
        s.Name = e.Name.Trim();
        s.ProcedureWords = string.Join(", ", Words(e.ProcedureWords).Distinct());
        if (s.ProcedureWords.Length == 0) s.ProcedureWords = null;
        s.About = e.About.Trim(); s.Benefits = e.Benefits.Trim(); s.Alternatives = e.Alternatives.Trim(); s.NoTreatment = e.NoTreatment.Trim();
        s.Anaesthetic = Blank(e.Anaesthetic); s.Preparation = Blank(e.Preparation); s.Recovery = Blank(e.Recovery);
        var i = 0;
        foreach (var r in e.Risks)
            db.Risks.Add(new SheetRisk { SheetId = s.Id, Order = ++i, Text = r.Text.Trim(), Frequency = r.Frequency, Serious = r.Serious });
        i = 0;
        foreach (var l in e.Links ?? [])
            db.Links.Add(new SheetLink { SheetId = s.Id, Order = ++i, Title = l.Title.Trim(), Url = l.Url.Trim() });
    }

    /// <summary>Adds the example sheets as drafts (codes already used are skipped). They must be reviewed and approved before patients see them.</summary>
    public async Task<int> SeedExamplesAsync(string user, CancellationToken ct = default)
    {
        var n = 0;
        foreach (var e in ConsentExamples.Sheets)
            if (!await db.Sheets.AnyAsync(x => x.Code == e.Code, ct) && (await SaveDraftAsync(null, e, user, ct)).Sheet is not null) n++;
        return n;
    }

    /// <summary>Copies a published or retired sheet into a new draft version.</summary>
    public async Task<(ConsentSheet? Sheet, string? Error)> NewVersionAsync(Guid id, string user, CancellationToken ct = default)
    {
        var src = await GetAsync(id, ct);
        if (src is null) return (null, "Sheet not found.");
        if (await db.Sheets.AnyAsync(x => x.Code == src.Code && x.Status == SheetStatus.Draft, ct)) return (null, "There is already a draft of this sheet – open it instead.");
        var next = await db.Sheets.Where(x => x.Code == src.Code).MaxAsync(x => x.Version, ct) + 1;
        var now = clock.GetUtcNow();
        var s = new ConsentSheet { Code = src.Code, Version = next, CreatedAt = now, CreatedBy = user, UpdatedAt = now, UpdatedBy = user };
        db.Sheets.Add(s);
        Apply(s, new SheetEdit(src.Code, src.Name, src.ProcedureWords, src.About, src.Benefits, src.Alternatives, src.NoTreatment, src.Anaesthetic, src.Preparation, src.Recovery,
            [.. src.Risks.OrderBy(r => r.Order).Select(r => new RiskEdit(r.Text, r.Frequency, r.Serious))], [.. src.Links.OrderBy(l => l.Order).Select(l => new LinkEdit(l.Title, l.Url))]), src.Code);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "NewVersion", "ConsentSheet", s.Id.ToString(), reason: $"{s.Code} v{next.ToString(CultureInfo.InvariantCulture)} from v{src.Version.ToString(CultureInfo.InvariantCulture)}", ct: ct);
        return (s, null);
    }

    /// <summary>A clinician other than the last editor approves and publishes the draft; the previous published version is retired.</summary>
    public async Task<string?> ApproveAsync(Guid id, string? note, string user, CancellationToken ct = default)
    {
        var s = await db.Sheets.Include(x => x.Risks).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return "Sheet not found.";
        if (s.Status != SheetStatus.Draft) return "Only a draft can be approved.";
        if (string.Equals(s.UpdatedBy, user, StringComparison.OrdinalIgnoreCase)) return "A different clinician must approve the sheet (you made the last change).";
        if (s.Risks.Count == 0) return "Add at least one risk before approving.";
        var now = clock.GetUtcNow();
        foreach (var old in await db.Sheets.Where(x => x.Code == s.Code && x.Status == SheetStatus.Published).ToListAsync(ct))
        {
            old.Status = SheetStatus.Retired; old.RetiredAt = now;
        }
        s.Status = SheetStatus.Published; s.ApprovedAt = now; s.ApprovedBy = user; s.ApprovalNote = Blank(note);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Approve", "ConsentSheet", s.Id.ToString(), reason: $"{s.Code} v{s.Version.ToString(CultureInfo.InvariantCulture)} published", ct: ct);
        return null;
    }

    public async Task<string?> RetireAsync(Guid id, string user, CancellationToken ct = default)
    {
        var s = await db.Sheets.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return "Sheet not found.";
        if (s.Status == SheetStatus.Retired) return "This sheet is already retired.";
        if (s.Status == SheetStatus.Draft)
        {
            if (await db.Consents.AnyAsync(c => c.SheetId == id, ct)) return "This draft is used by a consent and cannot be removed.";
            db.Sheets.Remove(s);
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(user, "DeleteDraft", "ConsentSheet", id.ToString(), reason: s.Code, ct: ct);
            return null;
        }
        s.Status = SheetStatus.Retired; s.RetiredAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Retire", "ConsentSheet", s.Id.ToString(), reason: $"{s.Code} v{s.Version.ToString(CultureInfo.InvariantCulture)}", ct: ct);
        return null;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
