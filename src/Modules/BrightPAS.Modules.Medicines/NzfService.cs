using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Medicines;

public sealed record NzfImportResult(Guid ReleaseId, string Edition, int? Release, DateOnly? ReleaseDate, int Monographs, int Doses, int Preparations, int IngredientsAdded, int AliasesAdded, bool AlreadyImported, IReadOnlyList<string> Problems);
public sealed record NzfSettingsEdit(string? NzfUrl, string? NzfcUrl, bool AutoDownload, string? WatchFolder = null);
public sealed record NzfSuggestion(DoseRuleEdit Rule, string Edition, string Source);

/// <summary>
/// The NZ Formulary (NZF) and NZ Formulary for Children (NZFC) inside BrightPAS (0.27.0): imports the monthly PDFs (uploaded, or downloaded
/// on the first working day of the month), keeps the current release's monographs, dosing lines and preparations, adds ingredient names and
/// other names to the medicines list, gives dose information when TDS has no dose limit, and suggests dose limits for a draft rule set.
/// The NZF contains no interaction data (it links to an online checker), so interactions remain TDS's own rules.
/// </summary>
public sealed class NzfService(MedicinesDbContext db, AuditWriter audit, TimeProvider clock)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    public async Task<NzfSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.NzfSettings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is not null) return s;
        s = new NzfSettings();
        db.NzfSettings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<IReadOnlyList<string>> SaveSettingsAsync(NzfSettingsEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        foreach (var (u, label) in new[] { (e.NzfUrl, "NZF"), (e.NzfcUrl, "NZFC") })
            if (!string.IsNullOrWhiteSpace(u) && (!Uri.TryCreate(u.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
                errors.Add($"The {label} address must be a full https:// link to the PDF.");
        if (e.AutoDownload && string.IsNullOrWhiteSpace(e.NzfUrl) && string.IsNullOrWhiteSpace(e.NzfcUrl)) errors.Add("Give at least one PDF address to download automatically.");
        if (!string.IsNullOrWhiteSpace(e.WatchFolder) && !Path.IsPathFullyQualified(e.WatchFolder.Trim())) errors.Add("The folder to watch must be a full path, e.g. \\\\server\\share\\NZF or E:\\NZF.");
        if (errors.Count > 0) return errors;
        var s = await SettingsAsync(ct);
        s.NzfUrl = Clip(e.NzfUrl, 500); s.NzfcUrl = Clip(e.NzfcUrl, 500); s.AutoDownload = e.AutoDownload; s.WatchFolder = Clip(e.WatchFolder, 500); s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "NzfSettings", "1", reason: $"Automatic download {(e.AutoDownload ? "on" : "off")}", ct: ct);
        return [];
    }

    public async Task RecordCheckAsync(string? problem, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        s.LastCheckedAt = clock.GetUtcNow(); s.LastProblem = Clip(problem, 1000);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<NzfRelease>> ReleasesAsync(CancellationToken ct = default) => await db.NzfReleases.AsNoTracking().OrderByDescending(r => r.ImportedAt).Take(30).ToListAsync(ct);

    /// <summary>True when this exact file has been imported before (any release).</summary>
    public async Task<bool> HaveAsync(byte[] pdf, CancellationToken ct = default)
    {
        var sha = Convert.ToHexString(SHA256.HashData(pdf));
        return await db.NzfReleases.AnyAsync(r => r.Sha256 == sha, ct);
    }

    public async Task<NzfRelease?> CurrentAsync(string edition, CancellationToken ct = default) =>
        await db.NzfReleases.AsNoTracking().SingleOrDefaultAsync(r => r.Edition == edition && r.IsCurrent, ct);

    /// <summary>First working day of the month in NZ (Monday–Friday, not 1 or 2 January) – the NZF release day.</summary>
    public static DateOnly FirstWorkingDay(int year, int month)
    {
        var d = new DateOnly(year, month, 1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || (d.Month == 1 && d.Day <= 2) || (d.Month == 1 && d.Day <= 4 && d.DayOfWeek == DayOfWeek.Monday && new DateOnly(year, 1, 1).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday))
            d = d.AddDays(1);
        return d;
    }

    /// <summary>True when this month's release is due (on or after its first working day) and not yet imported for the edition.</summary>
    public async Task<bool> DueAsync(string edition, DateOnly today, CancellationToken ct = default)
    {
        if (today < FirstWorkingDay(today.Year, today.Month)) return false;
        var cur = await CurrentAsync(edition, ct);
        return cur?.ReleaseDate is not { } rd || rd.Year != today.Year || rd.Month != today.Month;
    }

    // ------------------------------------------------------------------ import
    public async Task<NzfImportResult> ImportAsync(string fileName, byte[] pdf, string user, CancellationToken ct = default)
    {
        if (pdf.Length < 5 || pdf[0] != '%' || pdf[1] != 'P' || pdf[2] != 'D' || pdf[3] != 'F') throw new InvalidOperationException("This is not a PDF file.");
        if (pdf.Length > NzfPdf.MaxBytes) throw new InvalidOperationException("The file is larger than 120 MB.");
        var sha = Convert.ToHexString(SHA256.HashData(pdf));
        var lines = await Task.Run(() => NzfPdf.ReadLines(pdf, ct), ct);
        var doc = NzfParser.Parse(lines);
        if (doc.Monographs.Count < 50) throw new InvalidOperationException("This PDF doesn't look like the NZ Formulary or NZ Formulary for Children (no drug monographs found). " + string.Join(" ", doc.Problems));
        var existing = await CurrentAsync(doc.Edition, ct);
        if (existing is not null && existing.Sha256 == sha)
            return new(existing.Id, existing.Edition, existing.ReleaseNumber, existing.ReleaseDate, existing.Monographs, existing.Doses, 0, 0, 0, true, []);

        var now = clock.GetUtcNow();
        var release = new NzfRelease
        {
            Edition = doc.Edition,
            ReleaseNumber = doc.Release,
            ReleaseDate = doc.ReleaseDate,
            FileName = Clip(Path.GetFileName(fileName), 260) ?? "nzf.pdf",
            Sha256 = sha,
            Monographs = doc.Monographs.Count,
            Doses = doc.Monographs.Sum(m => m.Doses.Count),
            IsCurrent = false,
            Problems = doc.Problems.Count == 0 ? null : Clip(string.Join("\n", doc.Problems), 2000),
            ImportedAt = now,
            ImportedBy = user,
        };
        var preps = 0;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            db.NzfReleases.Add(release);
            await db.SaveChangesAsync(ct);
            var n = 0;
            foreach (var m in doc.Monographs)
            {
                string? S(string k) => m.Sections.TryGetValue(k, out var v) && v.Length > 0 ? v : null;
                var known = new[] { "Indications", "Contra-indications", "Cautions", "Hepatic impairment", "Renal impairment", "Pregnancy", "Breast-feeding", "Adverse effects", "Patient advice", "Drug action", "Interactions" };
                var other = string.Join("\n\n", m.Sections.Where(x => !known.Contains(x.Key, StringComparer.Ordinal) && x.Value.Length > 0).Select(x => x.Key + ": " + x.Value));
                var mono = new NzfMonograph
                {
                    ReleaseId = release.Id,
                    Name = Clip(m.Name, 200)!,
                    BaseName = Clip(NzfParser.BaseName(m.Name), 200)!,
                    Synonyms = Clip(m.Synonyms, 300),
                    Chapter = Clip(m.Chapter, 200),
                    Indications = S("Indications"),
                    ContraIndications = S("Contra-indications"),
                    Cautions = S("Cautions"),
                    HepaticImpairment = S("Hepatic impairment"),
                    RenalImpairment = S("Renal impairment"),
                    Pregnancy = S("Pregnancy"),
                    BreastFeeding = S("Breast-feeding"),
                    AdverseEffects = S("Adverse effects"),
                    PatientAdvice = S("Patient advice"),
                    OtherSections = other.Length == 0 ? null : other,
                };
                db.NzfMonographs.Add(mono);
                var seq = 0;
                foreach (var d in m.Doses)
                    db.NzfDoses.Add(new NzfDose
                    {
                        MonographId = mono.Id,
                        Sequence = ++seq,
                        Indication = d.Indication,
                        Route = d.Route,
                        RouteCodes = Clip(d.RouteCodes, 60) ?? "Other",
                        Population = d.Population,
                        Dose = d.Dose,
                        MinAgeYears = d.MinAgeYears,
                        MaxAgeYears = d.MaxAgeYears,
                        MinWeightKg = d.MinWeightKg,
                        MaxWeightKg = d.MaxWeightKg,
                        Neonate = d.Neonate,
                        Unit = d.Unit,
                        MaxSingle = d.MaxSingle,
                        MaxSinglePerKg = d.MaxSinglePerKg,
                        MaxDaily = d.MaxDaily,
                        MaxDailyPerKg = d.MaxDailyPerKg,
                        IntervalHours = d.IntervalHours,
                        Titration = d.Titration,
                    });
                seq = 0;
                foreach (var p in m.Preparations) { db.NzfPreparations.Add(new NzfPreparation { MonographId = mono.Id, Sequence = ++seq, Form = p.Form, Strength = p.Strength, Subsidy = p.Subsidy }); preps++; }
                if (++n % 150 == 0) { db.ChangeTracker.DetectChanges(); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear(); }
            }
            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        finally { db.ChangeTracker.AutoDetectChangesEnabled = true; }

        // Switch over: the new release becomes current; the old one's monographs are removed (its release row is kept as history).
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var old = await db.NzfReleases.Where(r => r.Edition == release.Edition && r.IsCurrent).Select(r => r.Id).ToListAsync(ct);
            await db.NzfReleases.Where(r => old.Contains(r.Id)).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsCurrent, false), ct);
            await db.NzfReleases.Where(r => r.Id == release.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsCurrent, true), ct);
            await tx.CommitAsync(ct);
            foreach (var id in old) await DeleteContentAsync(id, ct);
        });
        var (ings, aliases) = await AddNamesAsync(doc, ct);
        await audit.WriteAsync(user, "Import", "NzfRelease", release.Id.ToString(), reason: $"{release.Edition} release {release.ReleaseNumber} ({release.ReleaseDate:d MMM yyyy}): {release.Monographs} monographs, {release.Doses} dosing lines, {ings} ingredients and {aliases} other names added", ct: ct);
        return new(release.Id, release.Edition, release.ReleaseNumber, release.ReleaseDate, release.Monographs, release.Doses, preps, ings, aliases, false, doc.Problems);
    }

    private async Task DeleteContentAsync(Guid releaseId, CancellationToken ct)
    {
        var monos = db.NzfMonographs.Where(m => m.ReleaseId == releaseId).Select(m => m.Id);
        await db.NzfDoses.Where(d => monos.Contains(d.MonographId)).ExecuteDeleteAsync(ct);
        await db.NzfPreparations.Where(d => monos.Contains(d.MonographId)).ExecuteDeleteAsync(ct);
        await db.NzfMonographs.Where(m => m.ReleaseId == releaseId).ExecuteDeleteAsync(ct);
    }

    /// <summary>Ingredient names (single-ingredient monographs) and their NZF synonyms become ingredients and other names in the medicines list.</summary>
    private async Task<(int Ingredients, int Aliases)> AddNamesAsync(NzfDocumentParsed doc, CancellationToken ct)
    {
        var ingredients = await db.Ingredients.ToDictionaryAsync(i => i.Name, StringComparer.Ordinal, ct);
        var taken = (await db.Aliases.Select(a => a.Alias).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        int addedI = 0, addedA = 0;
        foreach (var m in doc.Monographs.Where(m => !m.Name.Contains('+', StringComparison.Ordinal)))
        {
            var key = MedicineCatalogue.IngredientKey(NzfParser.BaseName(m.Name));
            if (key.Length < 3 || key.Length > 150 || taken.Contains(key)) continue;
            if (!ingredients.TryGetValue(key, out var ing))
            {
                ing = new Ingredient { Name = key };
                db.Ingredients.Add(ing);
                ingredients[key] = ing;
                addedI++;
            }
            if (m.Synonyms is { } syn)
                foreach (var a in syn.Split(';', ',').Select(x => MedicineCatalogue.IngredientKey(NzfParser.BaseName(x))).Where(a => a.Length >= 3 && a.Length <= 150))
                    if (!taken.Contains(a) && !ingredients.ContainsKey(a)) { db.Aliases.Add(new IngredientAlias { IngredientId = ing.Id, Alias = a }); taken.Add(a); addedA++; }
        }
        await db.SaveChangesAsync(ct);
        return (addedI, addedA);
    }

    // ------------------------------------------------------------------ reading
    public async Task<List<(Guid Id, string Name, string Edition)>> SearchAsync(string? q, CancellationToken ct = default)
    {
        var t = (q ?? "").Trim().ToLowerInvariant();
        var query = from m in db.NzfMonographs.AsNoTracking()
                    join r in db.NzfReleases.AsNoTracking() on m.ReleaseId equals r.Id
                    where r.IsCurrent && (t.Length == 0 || m.Name.Contains(t) || (m.Synonyms != null && m.Synonyms.Contains(t)))
                    orderby m.Name, r.Edition
                    select new { m.Id, m.Name, r.Edition };
        return [.. (await query.Take(100).ToListAsync(ct)).Select(x => (x.Id, x.Name, x.Edition))];
    }

    public async Task<(NzfMonograph Monograph, NzfRelease Release)?> MonographAsync(Guid id, CancellationToken ct = default)
    {
        var m = await db.NzfMonographs.AsNoTracking().Include(x => x.Doses).Include(x => x.Preparations).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return null;
        var r = await db.NzfReleases.AsNoTracking().SingleAsync(x => x.Id == m.ReleaseId, ct);
        m.Doses = [.. m.Doses.OrderBy(d => d.Sequence)];
        m.Preparations = [.. m.Preparations.OrderBy(d => d.Sequence)];
        return (m, r);
    }

    /// <summary>Current monographs about an ingredient (both editions), by base name, name or synonym.</summary>
    public async Task<List<(Guid Id, string Name, string Edition)>> ForIngredientAsync(string ingredient, CancellationToken ct = default)
    {
        var k = ingredient.Trim().ToLowerInvariant();
        var query = from m in db.NzfMonographs.AsNoTracking()
                    join r in db.NzfReleases.AsNoTracking() on m.ReleaseId equals r.Id
                    where r.IsCurrent && (m.BaseName == k || m.Name == k)
                    orderby r.Edition, m.Name
                    select new { m.Id, m.Name, r.Edition };
        return [.. (await query.ToListAsync(ct)).Select(x => (x.Id, x.Name, x.Edition))];
    }

    private async Task<List<(NzfDose Dose, string Edition, int? Release)>> DosesForAsync(string ingredient, CancellationToken ct)
    {
        var k = ingredient.Trim().ToLowerInvariant();
        var rows = await (from d in db.NzfDoses.AsNoTracking()
                          join m in db.NzfMonographs.AsNoTracking() on d.MonographId equals m.Id
                          join r in db.NzfReleases.AsNoTracking() on m.ReleaseId equals r.Id
                          where r.IsCurrent && (m.BaseName == k || m.Name == k)
                          select new { d, r.Edition, r.ReleaseNumber }).ToListAsync(ct);
        return [.. rows.Select(x => (x.d, x.Edition, x.ReleaseNumber))];
    }

    private static bool AgeFits(NzfDose d, int? age) => age is null || ((d.MinAgeYears is null || age >= d.MinAgeYears) && (d.MaxAgeYears is null || age < d.MaxAgeYears));
    private static bool WeightFits(NzfDose d, decimal? kg) => kg is null || ((d.MinWeightKg is null || kg >= d.MinWeightKg) && (d.MaxWeightKg is null || kg < d.MaxWeightKg));
    private static bool RouteFits(NzfDose d, string route) => d.RouteCodes.Split(',').Contains(Routes.Normal(route), StringComparer.OrdinalIgnoreCase);

    /// <summary>NZF dosing lines for an ingredient, route and patient (NZFC for under-18s, NZF for adults) – shown when TDS has no dose limit.</summary>
    public async Task<List<(string Text, string Edition, int? Release)>> HintsAsync(string ingredient, string route, int? age, decimal? weightKg, int max, CancellationToken ct = default)
    {
        var edition = age is < 18 ? "NZFC" : "NZF";
        var rows = (await DosesForAsync(ingredient, ct)).Where(x => x.Edition == edition && !x.Dose.Neonate && RouteFits(x.Dose, route) && AgeFits(x.Dose, age) && WeightFits(x.Dose, weightKg) && x.Dose.Dose.Length > 0).ToList();
        return [.. rows.OrderBy(x => x.Dose.Titration).ThenByDescending(x => x.Dose.MaxDaily ?? 0).Take(max)
            .Select(x => ($"{x.Dose.Population} – {x.Dose.Dose} ({x.Dose.Indication})".Replace("  ", " ", StringComparison.Ordinal), x.Edition, x.Release))];
    }

    // ------------------------------------------------------------------ rule suggestions
    private static decimal? ToUnit(decimal? v, string? from, string to) => v is null || from is null ? null : DoseParser.ToBase(v.Value, from) is { } b && DoseParser.ToBase(1, to) is { } t && b.Unit == t.Unit ? b.Amount / t.Amount : null;

    /// <summary>
    /// Suggested dose limits for ingredients, from the current NZF (adults) and NZFC (children): for each route and population band the highest
    /// limit across the indications (a safety limit must not stop a dose the NZF allows for some indication). Clinicians review before approving.
    /// </summary>
    public async Task<List<NzfSuggestion>> SuggestAsync(IEnumerable<string> ingredients, bool children, CancellationToken ct = default)
    {
        var list = new List<NzfSuggestion>();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ing in ingredients.Select(i => i.Trim().ToLowerInvariant()).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal))
        {
            var rows = (await DosesForAsync(ing, ct)).Where(x => !x.Dose.Neonate && (x.Edition == "NZF" ? x.Dose.MaxAgeYears is null || x.Dose.MinAgeYears >= 16 : children && (x.Dose.MaxAgeYears ?? 18) <= 18))
                .Where(x => x.Dose.MaxSingle is not null || x.Dose.MaxSinglePerKg is not null || x.Dose.MaxDaily is not null || x.Dose.MaxDailyPerKg is not null).ToList();
            var expanded = rows.SelectMany(x => x.Dose.RouteCodes.Split(',').Where(c => c != "Other").Select(c => (x.Dose, x.Edition, x.Release, Route: c))).ToList();
            var n = 0;
            foreach (var g in expanded.GroupBy(x => (x.Route, x.Edition, x.Dose.MinAgeYears, x.Dose.MaxAgeYears, x.Dose.MinWeightKg, x.Dose.MaxWeightKg)).OrderBy(g => g.Key.Edition).ThenBy(g => g.Key.Route))
            {
                var unit = g.All(x => x.Dose.Unit == "microgram") ? "microgram" : g.All(x => x.Dose.Unit == "unit") ? "unit" : g.All(x => x.Dose.Unit == "mmol") ? "mmol" : "mg";
                decimal? Max(Func<NzfDose, decimal?> f) => g.Select(x => ToUnit(f(x.Dose), x.Dose.Unit, unit)).Where(v => v is not null).Max();
                decimal? MaxKg(Func<NzfDose, decimal?> f) => g.Select(x => ToUnit(f(x.Dose), x.Dose.Unit, unit)).Where(v => v is not null).Max();
                var single = Max(d => d.MaxSingle); var daily = Max(d => d.MaxDaily);
                var singleKg = MaxKg(d => d.MaxSinglePerKg); var dailyKg = MaxKg(d => d.MaxDailyPerKg);
                if (single is null && daily is null && singleKg is null && dailyKg is null) continue;
                var interval = g.Where(x => x.Dose.IntervalHours is not null).Select(x => x.Dose.IntervalHours).Min();
                var stem = new string([.. ing.ToUpperInvariant().Where(char.IsLetter).Take(6)]);
                string code;
                do code = $"{stem}-{(g.Key.Edition == "NZFC" ? "C" : "N")}{++n}"; while (!codes.Add(code));
                var minAge = g.Key.MinAgeYears ?? (g.Key.Edition == "NZFC" ? null : 18);
                var maxAge = g.Key.MaxAgeYears ?? (g.Key.Edition == "NZFC" ? 18 : null);
                var release = g.First().Release;
                var indications = string.Join("; ", g.Select(x => x.Dose.Indication).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal));
                var reference = $"{g.Key.Edition} release {release?.ToString(Inv) ?? "?"}: {indications}";
                var note = string.Join(" | ", g.Select(x => x.Dose.Population + " " + x.Dose.Dose).Distinct(StringComparer.Ordinal));
                var rule = new DoseRuleEdit(code, ing, g.Key.Route, minAge, maxAge, unit, single, daily, singleKg, dailyKg, interval,
                    daily is not null || dailyKg is not null ? CheckSeverity.Stop : CheckSeverity.Warn, Clip(reference, 300), Clip(note, 500), g.Key.MinWeightKg, g.Key.MaxWeightKg);
                list.Add(new(rule, g.Key.Edition, note));
            }
        }
        return list;
    }
}
