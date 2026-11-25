using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Medicines;

public sealed record IngredientEdit(string Ingredient, decimal? Amount, string? Unit, decimal? PerMl);
public sealed record MedicineEdit(string Name, string? NzmtId, string? Form, string? DefaultRoute, bool OnFormulary, bool IsActive, IReadOnlyList<IngredientEdit> Ingredients, IReadOnlyList<string> Gtins);
public sealed record MedicineRow(Guid Id, string Name, string? Form, string? DefaultRoute, string Source, bool OnFormulary, bool IsActive, string? NzmtId, string Ingredients);
public sealed record ImportMapping(string Name, string Source, string NameColumn, string? NzmtColumn, string? FormColumn, string? RouteColumn, string? IngredientColumn,
    string? AmountColumn, string? UnitColumn, string? PerMlColumn, string? GtinColumn, bool DeactivateMissing, bool SaveProfile);

/// <summary>
/// The medicines list: search, TDS's own entries, the starter list, ingredient aliases, barcodes and file imports (NZULM monthly files or
/// TDS's own list) with a saved column layout. Rules refer to ingredients by name, so re-importing never breaks the rules.
/// </summary>
public sealed partial class MedicineCatalogue(MedicinesDbContext db, AuditWriter audit, TimeProvider clock)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static readonly string[] Units = ["mg", "microgram", "g", "unit", "mmol"];

    private static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());
    public static string IngredientKey(string name) => TermIndex.Normalise(name);

    // ------------------------------------------------------------------ search and read
    public async Task<List<MedicineRow>> SearchAsync(string? q, bool formularyOnly, bool includeInactive, int take, CancellationToken ct = default)
    {
        var query = db.Medicines.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(m => m.IsActive);
        if (formularyOnly) query = query.Where(m => m.OnFormulary);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            var ingIds = db.Ingredients.Where(i => i.Name.Contains(t) || i.Aliases.Any(a => a.Alias.Contains(t))).Select(i => i.Id);
            query = query.Where(m => m.Name.Contains(t) || m.NzmtId == t || m.Gtins.Any(g => g.Gtin == t) || m.Ingredients.Any(mi => ingIds.Contains(mi.IngredientId)));
        }
        var rows = await query.OrderByDescending(m => m.OnFormulary).ThenBy(m => m.Name).Take(Math.Clamp(take, 1, 500))
            .Select(m => new { m.Id, m.Name, m.Form, m.DefaultRoute, m.Source, m.OnFormulary, m.IsActive, m.NzmtId }).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var ings = await (from mi in db.MedicineIngredients.AsNoTracking()
                          join i in db.Ingredients.AsNoTracking() on mi.IngredientId equals i.Id
                          where ids.Contains(mi.MedicineId)
                          select new { mi.MedicineId, i.Name, mi.Amount, mi.Unit, mi.PerMl }).ToListAsync(ct);
        return [.. rows.Select(r => new MedicineRow(r.Id, r.Name, r.Form, r.DefaultRoute, r.Source, r.OnFormulary, r.IsActive, r.NzmtId,
            string.Join(", ", ings.Where(i => i.MedicineId == r.Id).OrderBy(i => i.Name, StringComparer.Ordinal).Select(i => i.Name + Strength(i.Amount, i.Unit, i.PerMl)))))];
    }

    public static string Strength(decimal? amount, string? unit, decimal? perMl) =>
        amount is null ? "" : $" {amount.Value.ToString("0.####", Inv)} {unit}{(perMl is { } p ? "/" + (p == 1 ? "" : p.ToString("0.###", Inv) + " ") + "mL" : "")}";

    public async Task<Medicine?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Medicines.AsNoTracking().Include(m => m.Ingredients).Include(m => m.Gtins).SingleOrDefaultAsync(m => m.Id == id, ct);

    public async Task<Dictionary<Guid, string>> IngredientNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        return await db.Ingredients.AsNoTracking().Where(i => list.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
    }

    public async Task<Medicine?> ByBarcodeAsync(string gtin, CancellationToken ct = default)
    {
        var g = NormaliseGtin(gtin);
        if (g is null) return null;
        var id = await db.Gtins.AsNoTracking().Where(x => x.Gtin == g).Select(x => (Guid?)x.MedicineId).SingleOrDefaultAsync(ct);
        return id is null ? null : await GetAsync(id.Value, ct);
    }

    /// <summary>GTIN-8/12/13/14 digits, padded to 14 (GS1). A GS1 DataMatrix "(01)" string gives its GTIN. Null if not a GTIN.</summary>
    public static string? NormaliseGtin(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t.StartsWith("(01)", StringComparison.Ordinal)) t = t[4..];
        else if (t.StartsWith("01", StringComparison.Ordinal) && t.Length > 16 && t[2..16].All(char.IsAsciiDigit)) t = t[2..16];
        if (t.Length > 14 && t[..14].All(char.IsAsciiDigit)) t = t[..14];
        if (!t.All(char.IsAsciiDigit) || t.Length is not (8 or 12 or 13 or 14)) return null;
        t = t.PadLeft(14, '0');
        var sum = 0;
        for (var i = 0; i < 13; i++) sum += (t[i] - '0') * (i % 2 == 0 ? 3 : 1);
        return (10 - sum % 10) % 10 == t[13] - '0' ? t : null;
    }

    /// <summary>All ingredient names and aliases – the index used to read free text.</summary>
    public async Task<TermIndex> TermIndexAsync(CancellationToken ct = default)
    {
        var names = await db.Ingredients.AsNoTracking().Select(i => new { i.Name, i.Id }).ToListAsync(ct);
        var byId = names.ToDictionary(n => n.Id, n => n.Name);
        var aliases = await db.Aliases.AsNoTracking().Select(a => new { a.Alias, a.IngredientId }).ToListAsync(ct);
        return new TermIndex(names.Select(n => (n.Name, n.Name)).Concat(aliases.Where(a => byId.ContainsKey(a.IngredientId)).Select(a => (a.Alias, byId[a.IngredientId]))));
    }

    /// <summary>A medicine from the list by barcode or exact name (active first), with each ingredient's strength.</summary>
    public async Task<(Medicine? Medicine, List<(string Ingredient, decimal? Amount, string? Unit, decimal? PerMl)> Ingredients)> ResolveAsync(string text, string? barcode, Guid? medicineId, CancellationToken ct = default)
    {
        Medicine? m = null;
        if (medicineId is { } id) m = await GetAsync(id, ct);
        if (m is null && !string.IsNullOrWhiteSpace(barcode)) m = await ByBarcodeAsync(barcode, ct);
        if (m is null && !string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim();
            var mid = await db.Medicines.AsNoTracking().Where(x => x.Name == t).OrderByDescending(x => x.IsActive).ThenByDescending(x => x.OnFormulary).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (mid is not null) m = await GetAsync(mid.Value, ct);
        }
        if (m is null) return (null, []);
        var names = await IngredientNamesAsync(m.Ingredients.Select(i => i.IngredientId), ct);
        return (m, [.. m.Ingredients.Where(i => names.ContainsKey(i.IngredientId)).Select(i => (names[i.IngredientId], i.Amount, i.Unit, i.PerMl))]);
    }

    // ------------------------------------------------------------------ edit
    private async Task<Ingredient> EnsureIngredientAsync(string name, string? nzmt, Dictionary<string, Ingredient>? cache, CancellationToken ct)
    {
        var key = IngredientKey(name);
        if (cache is not null && cache.TryGetValue(key, out var hit)) return hit;
        var ing = db.Ingredients.Local.FirstOrDefault(i => i.Name == key) ?? await db.Ingredients.SingleOrDefaultAsync(i => i.Name == key, ct);
        if (ing is null)
        {
            ing = new Ingredient { Name = key, NzmtId = Clip(nzmt, 20) };
            db.Ingredients.Add(ing);
        }
        if (cache is not null) cache[key] = ing;
        return ing;
    }

    public async Task<(Guid? Id, IReadOnlyList<string> Errors)> SaveAsync(Guid? id, MedicineEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.Name)) errors.Add("Enter the medicine's name, strength and form (e.g. \"Paracetamol 500 mg tablet\").");
        if (e.Ingredients.Count == 0 || e.Ingredients.Any(i => string.IsNullOrWhiteSpace(i.Ingredient))) errors.Add("List at least one ingredient (e.g. paracetamol).");
        foreach (var i in e.Ingredients)
        {
            if (i.Amount is { } a && a <= 0) errors.Add($"{i.Ingredient}: the strength must be more than 0.");
            if (i.Amount is not null && (i.Unit is null || !Units.Contains(i.Unit))) errors.Add($"{i.Ingredient}: choose the unit (mg, microgram, g, unit or mmol).");
            if (i.PerMl is { } p && p <= 0) errors.Add($"{i.Ingredient}: \"per mL\" must be more than 0.");
        }
        var gtins = new List<string>();
        foreach (var g in e.Gtins.Where(g => !string.IsNullOrWhiteSpace(g)))
        {
            var n = NormaliseGtin(g);
            if (n is null) errors.Add($"Barcode {g.Trim()} is not a valid GTIN (8, 12, 13 or 14 digits with a correct check digit).");
            else gtins.Add(n);
        }
        if (e.Ingredients.Select(i => IngredientKey(i.Ingredient)).Distinct(StringComparer.Ordinal).Count() != e.Ingredients.Count) errors.Add("An ingredient is listed twice.");
        var nzmt = Clip(e.NzmtId, 20);
        if (nzmt is not null && await db.Medicines.AnyAsync(m => m.NzmtId == nzmt && m.Id != id, ct)) errors.Add($"Another medicine already has NZMT id {nzmt}.");
        foreach (var g in gtins.Distinct(StringComparer.Ordinal))
            if (await db.Gtins.AnyAsync(x => x.Gtin == g && x.MedicineId != id, ct)) errors.Add($"Barcode {g} already belongs to another medicine.");
        if (errors.Count > 0) return (null, errors);

        Medicine m;
        if (id is { } mid)
        {
            m = await db.Medicines.Include(x => x.Ingredients).Include(x => x.Gtins).SingleOrDefaultAsync(x => x.Id == mid, ct) ?? throw new KeyNotFoundException("Medicine not found.");
            db.MedicineIngredients.RemoveRange(m.Ingredients); m.Ingredients.Clear();
            db.Gtins.RemoveRange(m.Gtins); m.Gtins.Clear();
            await db.SaveChangesAsync(ct);
        }
        else
        {
            m = new Medicine { Source = "TDS" };
            db.Medicines.Add(m);
        }
        m.Name = Clip(e.Name, 250)!; m.NzmtId = nzmt; m.Form = Clip(e.Form, 60); m.DefaultRoute = Clip(e.DefaultRoute, 30);
        m.OnFormulary = e.OnFormulary; m.IsActive = e.IsActive; m.UpdatedAt = clock.GetUtcNow(); m.UpdatedBy = user;
        foreach (var i in e.Ingredients)
        {
            var ing = await EnsureIngredientAsync(i.Ingredient, null, null, ct);
            db.MedicineIngredients.Add(new MedicineIngredient { MedicineId = m.Id, IngredientId = ing.Id, Amount = i.Amount, Unit = i.Amount is null ? null : i.Unit, PerMl = i.PerMl });
        }
        foreach (var g in gtins.Distinct(StringComparer.Ordinal)) db.Gtins.Add(new MedicineGtin { Gtin = g, MedicineId = m.Id });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, id is null ? "Create" : "Update", "Medicine", m.Id.ToString(), reason: m.Name, ct: ct);
        return (m.Id, []);
    }

    // ------------------------------------------------------------------ aliases
    public async Task<List<(string Ingredient, string Alias)>> AliasesAsync(string? ingredient, CancellationToken ct = default)
    {
        var q = from a in db.Aliases.AsNoTracking() join i in db.Ingredients.AsNoTracking() on a.IngredientId equals i.Id select new { i.Name, a.Alias };
        if (!string.IsNullOrWhiteSpace(ingredient)) { var k = IngredientKey(ingredient); q = q.Where(x => x.Name == k); }
        return [.. (await q.OrderBy(x => x.Name).ThenBy(x => x.Alias).Take(2000).ToListAsync(ct)).Select(x => (x.Name, x.Alias))];
    }

    public async Task<string?> AddAliasAsync(string ingredient, string alias, string user, CancellationToken ct = default)
    {
        var a = IngredientKey(alias);
        if (a.Length < 3) return "The other name must be at least 3 letters.";
        if (await db.Aliases.AnyAsync(x => x.Alias == a, ct) || await db.Ingredients.AnyAsync(x => x.Name == a, ct)) return $"\"{a}\" is already an ingredient name or another name.";
        var ing = await EnsureIngredientAsync(ingredient, null, null, ct);
        db.Aliases.Add(new IngredientAlias { IngredientId = ing.Id, Alias = a });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "IngredientAlias", ing.Id.ToString(), reason: $"{a} = {ing.Name}", ct: ct);
        return null;
    }

    public async Task<bool> RemoveAliasAsync(string alias, string user, CancellationToken ct = default)
    {
        var a = IngredientKey(alias);
        var row = await db.Aliases.SingleOrDefaultAsync(x => x.Alias == a, ct);
        if (row is null) return false;
        db.Aliases.Remove(row);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "IngredientAlias", row.IngredientId.ToString(), reason: a, ct: ct);
        return true;
    }

    // ------------------------------------------------------------------ starter list
    /// <summary>Adds BrightPAS's starter list (common day-surgery medicines, on the formulary) and ingredient aliases. Skips anything already there.</summary>
    public async Task<int> LoadStarterAsync(string user, CancellationToken ct = default)
    {
        var cache = await db.Ingredients.ToDictionaryAsync(i => i.Name, StringComparer.Ordinal, ct);
        var existing = (await db.Medicines.Select(m => m.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var x in MedicineExamples.Medicines.Where(x => !existing.Contains(x.Name)))
        {
            var m = new Medicine { Name = x.Name, Form = x.Form, DefaultRoute = x.Route, Source = "Example", OnFormulary = true, UpdatedAt = clock.GetUtcNow(), UpdatedBy = user };
            db.Medicines.Add(m);
            foreach (var i in x.Ingredients)
            {
                var ing = await EnsureIngredientAsync(i.Ingredient, null, cache, ct);
                db.MedicineIngredients.Add(new MedicineIngredient { MedicineId = m.Id, IngredientId = ing.Id, Amount = i.Amount, Unit = i.Unit, PerMl = i.PerMl });
            }
            added++;
        }
        foreach (var e in MedicineExamples.ExtraIngredients) await EnsureIngredientAsync(e, null, cache, ct);
        foreach (var (k, v) in MedicineExamples.Aliases) await EnsureIngredientAsync(k, null, cache, ct);
        await db.SaveChangesAsync(ct);
        var taken = (await db.Aliases.Select(a => a.Alias).ToListAsync(ct)).Concat(cache.Keys).ToHashSet(StringComparer.Ordinal);
        foreach (var (k, v) in MedicineExamples.Aliases)
            foreach (var a in v.Select(IngredientKey).Where(a => taken.Add(a)))
                db.Aliases.Add(new IngredientAlias { IngredientId = cache[IngredientKey(k)].Id, Alias = a });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Import", "Medicine", "starter", reason: $"Starter medicines list: {added} added", ct: ct);
        return added;
    }

    // ------------------------------------------------------------------ file import
    public async Task<List<ImportProfile>> ProfilesAsync(CancellationToken ct = default) => await db.ImportProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
    public async Task<List<ImportRun>> RunsAsync(CancellationToken ct = default) => await db.ImportRuns.AsNoTracking().OrderByDescending(r => r.ImportedAt).Take(50).ToListAsync(ct);

    /// <summary>The column headings of a CSV/TSV/pipe-separated file and its first rows, to choose the column layout.</summary>
    public static (List<string> Headers, List<List<string>> Sample, char Delimiter) Peek(string text, int rows = 5)
    {
        var d = DetectDelimiter(text);
        var all = ReadDelimited(text, d).Take(rows + 1).ToList();
        return all.Count == 0 ? ([], [], d) : ([.. all[0].Select(h => h.Trim())], all.Skip(1).ToList(), d);
    }

    public static char DetectDelimiter(string text)
    {
        var nl = text.IndexOf('\n', StringComparison.Ordinal);
        var first = nl < 0 ? text : text[..nl];
        var counts = new[] { ',', '\t', '|', ';' }.Select(c => (c, n: first.Count(x => x == c))).OrderByDescending(x => x.n).ToList();
        return counts[0].n == 0 ? ',' : counts[0].c;
    }

    /// <summary>RFC 4180 reader (quoted fields, doubled quotes, line breaks inside quotes).</summary>
    public static IEnumerable<List<string>> ReadDelimited(string text, char delimiter)
    {
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(ch);
                continue;
            }
            if (ch == '"' && cell.Length == 0) quoted = true;
            else if (ch == delimiter) { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                if (row.Count > 1 || row[0].Length > 0) yield return row;
                row = [];
            }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); yield return row; }
    }

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(mg|g|gram|grams|microgram|micrograms|mcg|µg|ug|units?|iu|mmol)\s*(?:/\s*(\d+(?:[.,]\d+)?)?\s*(ml|mL|millilitres?)?)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex StrengthRx();

    /// <summary>"10 mg/mL" → (10, mg, 1); "1 g/100 mL" → (1, g, 100); "500 mg" → (500, mg, null).</summary>
    public static (decimal? Amount, string? Unit, decimal? PerMl) ParseStrength(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return (null, null, null);
        var m = StrengthRx().Match(s);
        if (!m.Success || !decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, Inv, out var a)) return (null, null, null);
        var unit = DoseParser.NormaliseUnit(m.Groups[2].Value);
        decimal? per = null;
        if (m.Groups[4].Success) per = m.Groups[3].Success && decimal.TryParse(m.Groups[3].Value.Replace(',', '.'), NumberStyles.Number, Inv, out var p) ? p : 1;
        return (a, unit, per);
    }

    public async Task<(ImportRun? Run, IReadOnlyList<string> Errors)> ImportAsync(string fileName, string text, ImportMapping map, string user, CancellationToken ct = default)
    {
        var source = map.Source is "NZULM" or "TDS" ? map.Source : "TDS";
        var (headers, _, delimiter) = Peek(text, 0);
        var errors = new List<string>();
        int Col(string? name, string label, bool required)
        {
            if (string.IsNullOrWhiteSpace(name)) { if (required) errors.Add($"Choose the column for {label}."); return -1; }
            var i = headers.FindIndex(h => string.Equals(h, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (i < 0) errors.Add($"The file has no column \"{name}\" ({label}).");
            return i;
        }
        var cName = Col(map.NameColumn, "the medicine name", true);
        var cNzmt = Col(map.NzmtColumn, "the NZMT id", false); var cForm = Col(map.FormColumn, "the form", false); var cRoute = Col(map.RouteColumn, "the route", false);
        var cIng = Col(map.IngredientColumn, "the ingredient", false); var cAmt = Col(map.AmountColumn, "the strength", false); var cUnit = Col(map.UnitColumn, "the unit", false);
        var cPer = Col(map.PerMlColumn, "per mL", false); var cGtin = Col(map.GtinColumn, "the barcode", false);
        if (errors.Count > 0) return (null, errors);

        if (map.SaveProfile && !string.IsNullOrWhiteSpace(map.Name))
        {
            var pname = Clip(map.Name, 100)!;
            var prof = await db.ImportProfiles.SingleOrDefaultAsync(p => p.Name == pname, ct);
            if (prof is null) { prof = new ImportProfile { Name = pname }; db.ImportProfiles.Add(prof); }
            prof.Source = source; prof.NameColumn = map.NameColumn.Trim(); prof.NzmtColumn = Clip(map.NzmtColumn, 100); prof.FormColumn = Clip(map.FormColumn, 100);
            prof.RouteColumn = Clip(map.RouteColumn, 100); prof.IngredientColumn = Clip(map.IngredientColumn, 100); prof.AmountColumn = Clip(map.AmountColumn, 100);
            prof.UnitColumn = Clip(map.UnitColumn, 100); prof.PerMlColumn = Clip(map.PerMlColumn, 100); prof.GtinColumn = Clip(map.GtinColumn, 100);
            prof.DeactivateMissing = map.DeactivateMissing; prof.UpdatedAt = clock.GetUtcNow(); prof.UpdatedBy = user;
        }

        string Cell(List<string> r, int i) => i >= 0 && i < r.Count ? r[i].Trim() : "";
        var problems = new List<string>();
        var groups = new Dictionary<string, (string Name, string? Nzmt, string? Form, string? Route, List<IngredientEdit> Ings, HashSet<string> Gtins)>(StringComparer.OrdinalIgnoreCase);
        var rows = 0; var skipped = 0;
        foreach (var r in ReadDelimited(text, delimiter).Skip(1))
        {
            rows++;
            var name = Cell(r, cName);
            if (name.Length == 0) { skipped++; continue; }
            var nz = Cell(r, cNzmt);
            var key = nz.Length > 0 ? "nz:" + nz : "n:" + name;
            if (!groups.TryGetValue(key, out var g)) { g = (name, nz.Length > 0 ? nz : null, Clip(Cell(r, cForm), 60), Clip(Cell(r, cRoute), 30), [], new(StringComparer.Ordinal)); groups[key] = g; }
            if (cIng >= 0 && Cell(r, cIng) is { Length: > 0 } ingCell)
            {
                var ings = ingCell.Split([';', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var amts = Cell(r, cAmt).Split(';', StringSplitOptions.TrimEntries);
                var units = Cell(r, cUnit).Split(';', StringSplitOptions.TrimEntries);
                var pers = Cell(r, cPer).Split(';', StringSplitOptions.TrimEntries);
                // "8;500 mg" – a bare number takes the unit written with the other strengths in the cell.
                var shared = amts.Select(ParseStrength).LastOrDefault(x => x.Unit is not null);
                for (var k = 0; k < ings.Length; k++)
                {
                    var amtText = k < amts.Length ? amts[k] : "";
                    var unitText = k < units.Length ? units[k] : "";
                    decimal? amount = null; string? unit = null; decimal? per = null;
                    if (amtText.Length > 0)
                    {
                        if (decimal.TryParse(amtText.Replace(',', '.'), NumberStyles.Number, Inv, out var a))
                        {
                            amount = a;
                            var su = ParseStrength("1 " + unitText);
                            unit = su.Unit ?? DoseParser.NormaliseUnit(unitText);
                            per = su.PerMl;
                            if (unit is null && unitText.Length == 0 && shared.Unit is not null) (unit, per) = (shared.Unit, shared.PerMl);
                        }
                        else (amount, unit, per) = ParseStrength(amtText);
                    }
                    if (k < pers.Length && decimal.TryParse(pers[k].Replace(',', '.'), NumberStyles.Number, Inv, out var pm) && pm > 0) per = pm;
                    if (amount is not null && (unit is null || !Units.Contains(unit))) { if (problems.Count < 30) problems.Add($"Row {rows + 1}: unit \"{unitText}\" not understood – strength left blank for {ings[k]}."); amount = null; unit = null; }
                    var ingName = ings[k];
                    if (!g.Ings.Any(x => IngredientKey(x.Ingredient) == IngredientKey(ingName))) g.Ings.Add(new(ingName, amount, unit, per));
                }
            }
            if (cGtin >= 0 && NormaliseGtin(Cell(r, cGtin)) is { } gt) g.Gtins.Add(gt);
        }

        var now = clock.GetUtcNow();
        var cache = await db.Ingredients.ToDictionaryAsync(i => i.Name, StringComparer.Ordinal, ct);
        var bySource = await db.Medicines.Where(m => m.Source == source).ToListAsync(ct);
        var byNz = (await db.Medicines.Where(m => m.NzmtId != null).ToListAsync(ct)).ToDictionary(m => m.NzmtId!, StringComparer.OrdinalIgnoreCase);
        var byName = bySource.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var gtinOwners = await db.Gtins.ToDictionaryAsync(x => x.Gtin, x => x.MedicineId, StringComparer.Ordinal, ct);
        var sourceIds = bySource.Select(m => m.Id).Concat(byNz.Values.Select(m => m.Id)).ToHashSet();
        var existingIngs = (await db.MedicineIngredients.ToListAsync(ct)).Where(x => sourceIds.Contains(x.MedicineId)).ToLookup(x => x.MedicineId);
        var seen = new HashSet<Guid>();
        int added = 0, updated = 0, n = 0;
        foreach (var g in groups.Values)
        {
            Medicine? m = null;
            if (g.Nzmt is not null) byNz.TryGetValue(g.Nzmt, out m);
            if (m is null) byName.TryGetValue(g.Name, out m);
            if (m is null)
            {
                m = new Medicine { Source = source, OnFormulary = source == "TDS" };
                db.Medicines.Add(m);
                added++;
            }
            else updated++;
            seen.Add(m.Id);
            m.Name = Clip(g.Name, 250)!; m.NzmtId = Clip(g.Nzmt, 20) ?? m.NzmtId; m.Form = g.Form ?? m.Form; m.DefaultRoute = g.Route ?? m.DefaultRoute;
            m.IsActive = true; m.UpdatedAt = now; m.UpdatedBy = user;
            if (m.NzmtId is not null) byNz[m.NzmtId] = m;
            if (g.Ings.Count > 0)
            {
                var current = existingIngs[m.Id].ToList();
                var keep = new HashSet<Guid>();
                foreach (var i in g.Ings)
                {
                    var ing = await EnsureIngredientAsync(i.Ingredient, null, cache, ct);
                    keep.Add(ing.Id);
                    if (current.FirstOrDefault(x => x.IngredientId == ing.Id) is { } row) { row.Amount = i.Amount; row.Unit = i.Unit; row.PerMl = i.PerMl; }
                    else db.MedicineIngredients.Add(new MedicineIngredient { MedicineId = m.Id, IngredientId = ing.Id, Amount = i.Amount, Unit = i.Unit, PerMl = i.PerMl });
                }
                db.MedicineIngredients.RemoveRange(current.Where(x => !keep.Contains(x.IngredientId)));
            }
            foreach (var gt in g.Gtins)
            {
                if (gtinOwners.TryGetValue(gt, out var owner))
                {
                    if (owner != m.Id && problems.Count < 30) problems.Add($"Barcode {gt} already belongs to another medicine – not moved to {g.Name}.");
                    continue;
                }
                db.Gtins.Add(new MedicineGtin { Gtin = gt, MedicineId = m.Id });
                gtinOwners[gt] = m.Id;
            }
            if (++n % 500 == 0) await db.SaveChangesAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        var deactivated = 0;
        if (map.DeactivateMissing && groups.Count > 0)
        {
            var ids = await db.Medicines.Where(m => m.Source == source && m.IsActive).Select(m => m.Id).ToListAsync(ct);
            var gone = ids.Where(i => !seen.Contains(i)).ToList();
            foreach (var chunk in gone.Chunk(500))
                deactivated += await db.Medicines.Where(m => chunk.Contains(m.Id)).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsActive, false).SetProperty(m => m.UpdatedAt, now).SetProperty(m => m.UpdatedBy, user), ct);
        }
        var run = new ImportRun
        {
            FileName = Clip(Path.GetFileName(fileName), 260) ?? "file",
            Source = source,
            Rows = rows,
            Added = added,
            Updated = updated,
            Deactivated = deactivated,
            Skipped = skipped,
            Problems = problems.Count == 0 ? null : Clip(string.Join("\n", problems), 4000),
            ImportedAt = now,
            ImportedBy = user,
        };
        db.ImportRuns.Add(run);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Import", "Medicine", run.Id.ToString(), reason: $"{source} file {run.FileName}: {rows} rows, {added} added, {updated} updated, {deactivated} made inactive", ct: ct);
        return (run, []);
    }
}
