using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrightPAS.Core.Audit;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Modules.Forms;

public sealed record OptionEdit(string Value, int? Score);
public sealed record FieldEdit(string? Key, string? Section, string Label, FieldType Type, bool Required, decimal? Min, decimal? Max, string? ShowIfKey, string? ShowIfValue,
    string? Calculation, string? MapsTo, string? Help, IReadOnlyList<OptionEdit>? Options);
public sealed record VersionEdit(string Name, string? Header, string? Footer, bool ThirdParty, string? LicenceNote, IReadOnlyList<FieldEdit> Fields);

/// <summary>Where mapped answers go when an entry is completed (implemented in the web app: allergies → patient alerts, measurements → triage).</summary>
public interface IFormDataSink
{
    Task<IReadOnlyList<string>> ApplyAsync(FormEntry entry, IReadOnlyDictionary<string, string> mapped, string user, CancellationToken ct);
}

/// <summary>Form designer and form import (spec 7.23, ADR 0038).</summary>
public sealed class FormDesignerService(FormsDbContext db, IEnumerable<IFormDataSink> sinks, AuditWriter audit, TimeProvider clock)
{
    public const int MaxFields = 200;

    public async Task<IReadOnlyList<FormDesign>> DesignsAsync(CancellationToken ct = default) =>
        await db.Designs.AsNoTracking().Include(d => d.Versions).OrderBy(d => d.Name).ToListAsync(ct);

    public async Task<FormDesignVersion?> VersionAsync(Guid id, CancellationToken ct = default) =>
        await db.DesignVersions.AsNoTracking().Include(v => v.Fields.OrderBy(f => f.Sequence)).ThenInclude(f => f.Options.OrderBy(o => o.Sequence)).SingleOrDefaultAsync(v => v.Id == id, ct);

    public async Task<(FormDesignVersion? Version, IReadOnlyList<string> Errors)> CreateAsync(string code, string name, FormCategory category, DesignSource source, IReadOnlyList<FieldEdit> fields, string user, CancellationToken ct = default)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        var errors = new List<string>();
        if (code.Length is < 2 or > 30 || code.Any(c => !char.IsLetterOrDigit(c) && c != '-')) errors.Add("Give the form a short code (letters, numbers and dashes).");
        if (string.IsNullOrWhiteSpace(name)) errors.Add("Give the form a name.");
        if (!Enum.IsDefined(category)) errors.Add("Choose a category.");
        if (await db.Designs.AnyAsync(d => d.Code == code, ct)) errors.Add("Another form has that code.");
        errors.AddRange(Check(fields));
        if (errors.Count > 0) return (null, errors);
        var d = new FormDesign { Code = code, Name = name.Trim(), Category = category, CreatedAt = clock.GetUtcNow(), CreatedBy = user };
        var v = new FormDesignVersion { DesignId = d.Id, Version = 1, Source = source, UpdatedAt = clock.GetUtcNow(), UpdatedBy = user };
        v.Fields = Build(v.Id, fields);
        d.Versions.Add(v);
        db.Designs.Add(d);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "FormDesign", d.Id.ToString(), reason: $"{code} {d.Name} v1 ({source}, {v.Fields.Count} fields)", ct: ct);
        return (v, []);
    }

    private static List<string> Check(IReadOnlyList<FieldEdit>? fields)
    {
        var errors = new List<string>();
        if (fields is null) return ["Add at least one field."];
        if (fields.Count > MaxFields) errors.Add($"A form can have at most {MaxFields} fields.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (f, i) in fields.Select((f, i) => (f, i + 1)))
        {
            if (string.IsNullOrWhiteSpace(f.Label)) errors.Add($"Field {i}: give it a label.");
            if (!Enum.IsDefined(f.Type)) errors.Add($"Field {i}: choose a type.");
            if (f.Type is FieldType.Choice or FieldType.MultiChoice && (f.Options is null || f.Options.Count(o => !string.IsNullOrWhiteSpace(o.Value)) < 2)) errors.Add($"Field {i} ({f.Label}): add at least two options.");
            if (f.Type == FieldType.Calculated && string.IsNullOrWhiteSpace(f.Calculation)) errors.Add($"Field {i} ({f.Label}): enter the calculation, e.g. bmi(weight,height) or sum(q1,q2).");
            if (f.MapsTo is { Length: > 0 } m && !FormLogic.DataElements.ContainsKey(m)) errors.Add($"Field {i}: unknown data element {m}.");
            if (f.Min > f.Max) errors.Add($"Field {i}: minimum is above maximum.");
            if (f.Key is { Length: > 0 } k && !keys.Add(k)) errors.Add($"Field {i}: the key {k} is used twice.");
        }
        foreach (var f in fields.Where(f => f.ShowIfKey is { Length: > 0 }))
            if (!fields.Any(x => x.Key == f.ShowIfKey)) errors.Add($"{f.Label}: shows only if “{f.ShowIfKey}”, which is not a field on this form.");
        return errors;
    }

    private static List<FormField> Build(Guid versionId, IReadOnlyList<FieldEdit> fields)
    {
        var used = new HashSet<string>(fields.Where(f => !string.IsNullOrWhiteSpace(f.Key)).Select(f => f.Key!.Trim()), StringComparer.Ordinal);
        var list = new List<FormField>();
        var seq = 1;
        foreach (var f in fields)
        {
            var key = string.IsNullOrWhiteSpace(f.Key) ? FormLogic.KeyFrom(f.Label, used) : f.Key.Trim();
            static string? T(string? s, int n) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > n ? s.Trim()[..n] : s.Trim();
            list.Add(new FormField
            {
                VersionId = versionId,
                Key = key.Length > 40 ? key[..40] : key,
                Sequence = seq++,
                Section = T(f.Section, 100),
                Label = T(f.Label, 300)!,
                Type = f.Type,
                Required = f.Required && f.Type is not (FieldType.Heading or FieldType.Calculated),
                Min = f.Min,
                Max = f.Max,
                ShowIfKey = T(f.ShowIfKey, 40),
                ShowIfValue = T(f.ShowIfValue, 100),
                Calculation = T(f.Calculation, 200),
                MapsTo = T(f.MapsTo, 30),
                Help = T(f.Help, 500),
                Options = [.. (f.Options ?? []).Where(o => !string.IsNullOrWhiteSpace(o.Value)).Select((o, i) => new FieldOption { VersionId = versionId, FieldKey = key, Sequence = i + 1, Value = o.Value.Trim().Length > 200 ? o.Value.Trim()[..200] : o.Value.Trim(), Score = o.Score })]
            });
        }
        return list;
    }

    /// <summary>Saves a draft (or a form being tested). Published and retired versions never change – start a new version.</summary>
    public async Task<IReadOnlyList<string>> SaveAsync(Guid versionId, VersionEdit e, string user, CancellationToken ct = default)
    {
        var v = await db.DesignVersions.Include(x => x.Fields).ThenInclude(f => f.Options).SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null) return ["Form not found."];
        if (v.Status is not (DesignStatus.Draft or DesignStatus.Testing)) return ["Published and retired versions can't be changed. Start a new version."];
        var errors = Check(e.Fields);
        if (string.IsNullOrWhiteSpace(e.Name)) errors.Add("Give the form a name.");
        if (errors.Count > 0) return errors;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.Options.RemoveRange(v.Fields.SelectMany(f => f.Options));
            db.Fields.RemoveRange(v.Fields);
            await db.SaveChangesAsync(ct);
            var d = await db.Designs.SingleAsync(x => x.Id == v.DesignId, ct);
            d.Name = e.Name.Trim();
            v.Header = e.Header?.Trim(); v.Footer = e.Footer?.Trim(); v.ThirdParty = e.ThirdParty; v.LicenceNote = e.LicenceNote?.Trim(); v.UpdatedAt = clock.GetUtcNow(); v.UpdatedBy = user;
            foreach (var f in Build(v.Id, e.Fields)) db.Fields.Add(f);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await audit.WriteAsync(user, "Update", "FormDesignVersion", v.Id.ToString(), reason: $"{d.Code} v{v.Version}: {e.Fields.Count} fields", ct: ct);
            return (IReadOnlyList<string>)[];
        });
    }

    /// <summary>A new draft copied from the latest version.</summary>
    public async Task<(FormDesignVersion? Version, string? Error)> NewVersionAsync(Guid designId, string user, CancellationToken ct = default)
    {
        var versions = await db.DesignVersions.AsNoTracking().Include(v => v.Fields).ThenInclude(f => f.Options).Where(v => v.DesignId == designId).ToListAsync(ct);
        if (versions.Count == 0) return (null, "Form not found.");
        if (versions.Any(v => v.Status is DesignStatus.Draft or DesignStatus.Testing)) return (null, "Finish or delete the draft first.");
        var last = versions.OrderByDescending(v => v.Version).First();
        var nv = new FormDesignVersion { DesignId = designId, Version = last.Version + 1, Source = DesignSource.Copy, Header = last.Header, Footer = last.Footer, ThirdParty = last.ThirdParty, LicenceNote = last.LicenceNote, UpdatedAt = clock.GetUtcNow(), UpdatedBy = user };
        nv.Fields = Build(nv.Id, [.. last.Fields.OrderBy(f => f.Sequence).Select(f => new FieldEdit(f.Key, f.Section, f.Label, f.Type, f.Required, f.Min, f.Max, f.ShowIfKey, f.ShowIfValue, f.Calculation, f.MapsTo, f.Help, [.. f.Options.OrderBy(o => o.Sequence).Select(o => new OptionEdit(o.Value, o.Score))]))]);
        db.DesignVersions.Add(nv);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "FormDesignVersion", nv.Id.ToString(), reason: $"v{nv.Version} from v{last.Version}", ct: ct);
        return (nv, null);
    }

    public async Task<string?> TestAsync(Guid versionId, string user, CancellationToken ct = default)
    {
        var v = await db.DesignVersions.SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null) return "Form not found.";
        if (v.Status != DesignStatus.Draft) return "Only a draft can go to testing.";
        v.Status = DesignStatus.Testing; v.UpdatedAt = clock.GetUtcNow(); v.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Test", "FormDesignVersion", v.Id.ToString(), ct: ct);
        return null;
    }

    /// <summary>
    /// Publishes a version and retires the one in use. Clinical, consent and patient forms need Clinical Governance approval (<paramref name="canApprove"/>
    /// and a note); third-party/licensed forms need the licence recorded. Entries already started stay on their version.
    /// </summary>
    public async Task<string?> PublishAsync(Guid versionId, bool canApprove, string? note, string user, CancellationToken ct = default)
    {
        var v = await db.DesignVersions.Include(x => x.Fields).SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null) return "Form not found.";
        if (v.Status is not (DesignStatus.Draft or DesignStatus.Testing)) return "This version is already published or retired.";
        if (!v.Fields.Any(f => f.Type != FieldType.Heading)) return "Add at least one question.";
        var d = await db.Designs.SingleAsync(x => x.Id == v.DesignId, ct);
        var governed = d.Category is FormCategory.Clinical or FormCategory.Consent or FormCategory.PatientQuestionnaire;
        if (governed && !canApprove) return "Clinical, consent and patient forms are published by someone with Clinical Governance approval.";
        if (governed && string.IsNullOrWhiteSpace(note)) return "Record the Clinical Governance approval (who approved it and where).";
        if (v.ThirdParty && string.IsNullOrWhiteSpace(v.LicenceNote)) return "This form is marked as licensed/third-party: record TDS's licence before publishing.";
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            foreach (var old in await db.DesignVersions.Where(x => x.DesignId == v.DesignId && x.Status == DesignStatus.Published).ToListAsync(ct)) { old.Status = DesignStatus.Retired; old.RetiredAt = clock.GetUtcNow(); }
            await db.SaveChangesAsync(ct);
            v.Status = DesignStatus.Published; v.PublishedAt = clock.GetUtcNow(); v.UpdatedBy = user;
            if (governed) { v.ApprovedBy = user; v.ApprovedAt = clock.GetUtcNow(); v.ApprovalNote = note!.Trim(); }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await audit.WriteAsync(user, "Publish", "FormDesignVersion", v.Id.ToString(), reason: $"{d.Code} v{v.Version}{(governed ? " – approved: " + note : "")}", ct: ct);
            return (string?)null;
        });
    }

    public async Task<string?> RetireAsync(Guid versionId, string user, CancellationToken ct = default)
    {
        var v = await db.DesignVersions.SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null) return "Form not found.";
        if (v.Status != DesignStatus.Published) return "Only the published version can be retired.";
        v.Status = DesignStatus.Retired; v.RetiredAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Retire", "FormDesignVersion", v.Id.ToString(), ct: ct);
        return null;
    }

    public async Task<string?> DeleteDraftAsync(Guid versionId, string user, CancellationToken ct = default)
    {
        var v = await db.DesignVersions.SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null) return "Form not found.";
        if (v.Status is not (DesignStatus.Draft or DesignStatus.Testing)) return "Only drafts can be deleted.";
        if (await db.Entries.AnyAsync(e => e.VersionId == versionId, ct)) return "Test entries exist for this draft – it can't be deleted.";
        var others = await db.DesignVersions.CountAsync(x => x.DesignId == v.DesignId && x.Id != v.Id, ct);
        if (others == 0) db.Designs.Remove(await db.Designs.SingleAsync(d => d.Id == v.DesignId, ct)); else db.DesignVersions.Remove(v);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "FormDesignVersion", versionId.ToString(), ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ import / export
    /// <summary>
    /// CSV question list (spec 7.23.2): columns question, type, options (separated by |, a score as "Yes=1"), required (yes/no), section, help.
    /// Types: text, longtext, number, date, yesno, choice, multichoice, signature, heading, nhi.
    /// </summary>
    public static (List<FieldEdit> Fields, List<string> Errors) ParseCsv(string csv)
    {
        var errors = new List<string>(); var fields = new List<FieldEdit>();
        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count < 2) return (fields, ["The file needs a heading row and at least one question."]);
        var head = SplitCsv(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => head.FindIndex(h => names.Contains(h));
        int q = Col("question", "label", "text"), t = Col("type"), o = Col("options", "choices", "answers"), r = Col("required", "mandatory"), s = Col("section"), hp = Col("help", "hint");
        if (q < 0) return (fields, ["No 'question' column found."]);
        foreach (var (line, n) in lines.Skip(1).Select((l, i) => (l, i + 2)))
        {
            var c = SplitCsv(line);
            string Get(int i) => i >= 0 && i < c.Count ? c[i].Trim() : "";
            var type = Get(t).ToLowerInvariant() switch
            {
                "" or "text" or "string" => FieldType.Text,
                "longtext" or "memo" or "textarea" => FieldType.LongText,
                "number" or "integer" or "decimal" => FieldType.Number,
                "date" => FieldType.Date,
                "yesno" or "boolean" or "y/n" => FieldType.YesNo,
                "choice" or "select" or "radio" => FieldType.Choice,
                "multichoice" or "checkbox" or "multi" => FieldType.MultiChoice,
                "signature" => FieldType.Signature,
                "heading" or "section" or "display" => FieldType.Heading,
                "nhi" => FieldType.Nhi,
                var x => (FieldType)(-1)
            };
            if ((int)type == -1) { errors.Add($"Row {n}: unknown type “{Get(t)}”."); continue; }
            var opts = Get(o).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.Contains('=') && int.TryParse(x[(x.LastIndexOf('=') + 1)..], out var sc) ? new OptionEdit(x[..x.LastIndexOf('=')].Trim(), sc) : new OptionEdit(x, null)).ToList();
            if (type == FieldType.Text && opts.Count >= 2) type = FieldType.Choice;
            fields.Add(new FieldEdit(null, Get(s) is { Length: > 0 } sec ? sec : null, Get(q), type, Get(r).ToLowerInvariant() is "yes" or "y" or "true" or "1", null, null, null, null, null, null, Get(hp) is { Length: > 0 } h ? h : null, opts));
        }
        return (fields, errors);
    }

    private static List<string> SplitCsv(string line)
    {
        var list = new List<string>(); var sb = new StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted) { if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else if (ch == '"') quoted = false; else sb.Append(ch); }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { list.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        list.Add(sb.ToString());
        return list;
    }

    /// <summary>FHIR R4 Questionnaire import (items, nested groups as sections, answerOption, required, enableWhen equals).</summary>
    public static (string Name, List<FieldEdit> Fields, List<string> Errors) ParseFhir(string json)
    {
        var errors = new List<string>(); var fields = new List<FieldEdit>();
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException) { return ("", fields, ["This is not valid JSON."]); }
        if (root?["resourceType"]?.GetValue<string>() != "Questionnaire") return ("", fields, ["This is not a FHIR Questionnaire."]);
        var name = root["title"]?.GetValue<string>() ?? root["name"]?.GetValue<string>() ?? "Imported questionnaire";
        void Walk(JsonArray? items, string? section)
        {
            foreach (var it in items ?? [])
            {
                if (it is null) continue;
                var type = it["type"]?.GetValue<string>() ?? "string";
                var text = it["text"]?.GetValue<string>() ?? it["linkId"]?.GetValue<string>() ?? "";
                var key = it["linkId"]?.GetValue<string>();
                if (type == "group") { fields.Add(new FieldEdit(key, text, text, FieldType.Heading, false, null, null, null, null, null, null, null, null)); Walk(it["item"] as JsonArray, text); continue; }
                var ft = type switch
                {
                    "string" or "url" => FieldType.Text,
                    "text" => FieldType.LongText,
                    "integer" or "decimal" or "quantity" => FieldType.Number,
                    "date" or "dateTime" => FieldType.Date,
                    "boolean" => FieldType.YesNo,
                    "choice" or "open-choice" or "coding" => it["repeats"]?.GetValue<bool>() == true ? FieldType.MultiChoice : FieldType.Choice,
                    "display" => FieldType.Heading,
                    "attachment" => FieldType.Signature,
                    _ => FieldType.Text
                };
                var opts = (it["answerOption"] as JsonArray ?? []).Select(o => o?["valueCoding"]?["display"]?.GetValue<string>() ?? o?["valueCoding"]?["code"]?.GetValue<string>() ?? o?["valueString"]?.GetValue<string>() ?? o?["valueInteger"]?.ToString())
                    .Where(v => v is not null).Select(v => new OptionEdit(v!, null)).ToList();
                string? showKey = null, showVal = null;
                if (it["enableWhen"] is JsonArray ew && ew.Count > 0 && ew[0] is { } w && w["operator"]?.GetValue<string>() == "=")
                {
                    showKey = w["question"]?.GetValue<string>();
                    showVal = w["answerBoolean"] is { } ab ? (ab.GetValue<bool>() ? "yes" : "no") : w["answerCoding"]?["display"]?.GetValue<string>() ?? w["answerString"]?.GetValue<string>();
                }
                fields.Add(new FieldEdit(key, section, text, ft, it["required"]?.GetValue<bool>() == true, null, null, showKey, showVal, null, null, null, opts));
            }
        }
        Walk(root["item"] as JsonArray, null);
        if (fields.Count == 0) errors.Add("The questionnaire has no items.");
        return (name, fields, errors);
    }

    public static string ToFhir(FormDesign d, FormDesignVersion v)
    {
        string FType(FieldType t) => t switch { FieldType.LongText => "text", FieldType.Number => "decimal", FieldType.Date => "date", FieldType.YesNo => "boolean", FieldType.Choice or FieldType.MultiChoice => "choice", FieldType.Heading => "display", FieldType.Signature => "attachment", _ => "string" };
        var items = new JsonArray();
        foreach (var f in v.Fields.OrderBy(f => f.Sequence))
        {
            var o = new JsonObject { ["linkId"] = f.Key, ["text"] = f.Label, ["type"] = FType(f.Type) };
            if (f.Required) o["required"] = true;
            if (f.Type == FieldType.MultiChoice) o["repeats"] = true;
            if (f.Type == FieldType.Calculated) o["readOnly"] = true;
            if (f.Options.Count > 0) o["answerOption"] = new JsonArray([.. f.Options.OrderBy(x => x.Sequence).Select(x => (JsonNode)new JsonObject { ["valueString"] = x.Value })]);
            if (f.ShowIfKey is not null) o["enableWhen"] = new JsonArray(new JsonObject { ["question"] = f.ShowIfKey, ["operator"] = "=", ["answerString"] = f.ShowIfValue });
            items.Add(o);
        }
        var q = new JsonObject
        {
            ["resourceType"] = "Questionnaire",
            ["id"] = $"{d.Code.ToLowerInvariant()}-v{v.Version}",
            ["name"] = d.Code,
            ["title"] = d.Name,
            ["version"] = v.Version.ToString(CultureInfo.InvariantCulture),
            ["status"] = v.Status switch { DesignStatus.Published => "active", DesignStatus.Retired => "retired", _ => "draft" },
            ["publisher"] = "Tasman Day Surgery",
            ["item"] = items
        };
        return q.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // ------------------------------------------------------------------ entries
    public async Task<(FormEntry? Entry, string? Error)> StartAsync(Guid versionId, Guid? patientId, Guid? bookingId, string user, CancellationToken ct = default)
    {
        var v = await db.DesignVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null) return (null, "Form not found.");
        if (v.Status is not (DesignStatus.Published or DesignStatus.Testing)) return (null, v.Status == DesignStatus.Draft ? "Put the form into testing or publish it first." : "This version is retired – use the current one.");
        var d = await db.Designs.AsNoTracking().SingleAsync(x => x.Id == v.DesignId, ct);
        if (d.Category is FormCategory.Clinical or FormCategory.Consent or FormCategory.PatientQuestionnaire && patientId is null) return (null, "Choose the patient.");
        var e = new FormEntry { VersionId = versionId, PatientId = patientId, BookingId = bookingId, StartedBy = user, StartedAt = clock.GetUtcNow() };
        db.Entries.Add(e);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Start", "FormEntry", e.Id.ToString(), patientId?.ToString(), reason: $"{d.Code} v{v.Version}{(v.Status == DesignStatus.Testing ? " (testing)" : "")}", ct: ct);
        return (e, null);
    }

    public async Task<FormEntry?> EntryAsync(Guid id, string user, CancellationToken ct = default)
    {
        var e = await db.Entries.AsNoTracking().Include(x => x.Values).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is not null) await audit.WriteAsync(user, "View", "FormEntry", id.ToString(), e.PatientId?.ToString(), ct: ct);
        return e;
    }

    public async Task<IReadOnlyList<(FormEntry Entry, string Code, string Name, int Version)>> EntriesForAsync(Guid? patientId, Guid? versionId, CancellationToken ct = default)
    {
        var rows = await (from e in db.Entries.AsNoTracking()
                          join v in db.DesignVersions.AsNoTracking() on e.VersionId equals v.Id
                          join d in db.Designs.AsNoTracking() on v.DesignId equals d.Id
                          where (patientId == null || e.PatientId == patientId) && (versionId == null || e.VersionId == versionId)
                          orderby e.StartedAt descending
                          select new { e, d.Code, d.Name, v.Version }).Take(300).ToListAsync(ct);
        return [.. rows.Select(r => (r.e, r.Code, r.Name, r.Version))];
    }

    /// <summary>Saves answers (validated, hidden answers dropped, calculations worked out). Completing applies mapped answers to the record.</summary>
    public async Task<(IReadOnlyList<string> Errors, IReadOnlyList<string> Applied)> SaveEntryAsync(Guid id, IReadOnlyDictionary<string, string> answers, bool complete, string user, CancellationToken ct = default)
    {
        var e = await db.Entries.Include(x => x.Values).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return (["Form not found."], []);
        if (e.Status != EntryStatus.InProgress) return (["This form has been completed – it can't be changed."], []);
        var v = (await VersionAsync(e.VersionId, ct))!;
        var (clean, errors) = FormLogic.Validate(v.Fields, answers ?? new Dictionary<string, string>(), complete);
        if (errors.Count > 0) return (errors, []);
        var all = FormLogic.Calculate(v.Fields, clean);
        db.EntryValues.RemoveRange(e.Values);
        foreach (var (k, val) in all) db.EntryValues.Add(new EntryValue { EntryId = e.Id, FieldKey = k, Value = val });
        var applied = new List<string>();
        if (complete)
        {
            e.Status = EntryStatus.Completed; e.CompletedBy = user; e.CompletedAt = clock.GetUtcNow();
            var mapped = v.Fields.Where(f => f.MapsTo is not null && all.ContainsKey(f.Key)).ToDictionary(f => f.MapsTo!, f => all[f.Key], StringComparer.Ordinal);
            await db.SaveChangesAsync(ct);
            if (mapped.Count > 0 && e.PatientId is not null) foreach (var s in sinks) applied.AddRange(await s.ApplyAsync(e, mapped, user, ct));
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, complete ? "Complete" : "Save", "FormEntry", e.Id.ToString(), e.PatientId?.ToString(), reason: $"{all.Count} answer(s){(applied.Count > 0 ? "; " + string.Join("; ", applied) : "")}", ct: ct);
        return ([], applied);
    }

    public async Task<string?> VoidEntryAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var e = await db.Entries.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return "Form not found.";
        if (string.IsNullOrWhiteSpace(reason)) return "Give a reason.";
        if (e.Status == EntryStatus.Voided) return "Already voided.";
        e.Status = EntryStatus.Voided; e.VoidReason = reason.Trim();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Void", "FormEntry", e.Id.ToString(), e.PatientId?.ToString(), reason: reason, ct: ct);
        return null;
    }
}
