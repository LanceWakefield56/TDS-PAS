using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Estimates;

public enum ChangeStatus { New = 1, Changed = 2, Unchanged = 3, NotInFile = 4 }

public sealed class ProcRow
{
    public int Row { get; set; }
    public string Code { get; set; } = "";
    public string Specialty { get; set; } = "";
    public string Group { get; set; } = "";
    public string Name { get; set; } = "";
    public string PrintTitle { get; set; } = "";
    public Anaesthetic Anaesthetic { get; set; } = Anaesthetic.General;
    public int Minutes { get; set; }
    public decimal? Facility { get; set; }
    public decimal? FacilitySpecial { get; set; }
    public decimal? Anaesthetist { get; set; }
    public decimal? AnaesthetistSpecial { get; set; }
    public decimal? Consumables { get; set; }
    public string? Includes { get; set; }
    public string? Source { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public bool Active { get; set; } = true;
}

public sealed record FieldDiff(string Field, string Old, string New);

public sealed class ProcChange
{
    public ProcRow? Row { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? ExistingId { get; set; }
    public ChangeStatus Status { get; set; }
    public List<FieldDiff> Diffs { get; } = [];
    public bool Scheduled { get; set; }
    public static readonly string[] PriceFields = ["Facility", "FacilitySpecial", "Anaesthetist", "AnaesthetistSpecial", "ConsumablesLine"];
}

public sealed record FeeRow(string Code, string Surgeon, decimal? Fee, decimal? Special, string? Notes);
public sealed record AddOnRow(string Code, string Specialty, string? Group, string Name, decimal Fee, string Type, string? Surgeon, string? Source);

public sealed class PricingPreview
{
    public string FileName { get; set; } = "";
    public List<ProcChange> Procedures { get; } = [];
    public List<FeeRow> Fees { get; } = [];
    public List<AddOnRow> AddOns { get; } = [];
    public List<string> Errors { get; } = [];
    /// <summary>Changes that need an explicit confirm: a price becoming blank, or moving by more than 25%.</summary>
    public List<string> Warnings { get; } = [];
    public int New => Procedures.Count(p => p.Status == ChangeStatus.New);
    public int Changed => Procedures.Count(p => p.Status == ChangeStatus.Changed);
    public int Unchanged => Procedures.Count(p => p.Status == ChangeStatus.Unchanged);
    public int NotInFile => Procedures.Count(p => p.Status == ChangeStatus.NotInFile);
}

public sealed class StockRow
{
    public string Supplier { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Reference { get; set; }
    public string? WhereUsed { get; set; }
    public string? Issues { get; set; }
    public decimal? PackPrice { get; set; }
    public decimal? Units { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? InStock { get; set; }
    public decimal? Needed { get; set; }
}

public sealed class StockPreview
{
    public string FileName { get; set; } = "";
    public List<StockRow> Items { get; } = [];
    public int Suppliers { get; set; }
    public int New { get; set; }
    public int Updated { get; set; }
    public List<string> Errors { get; } = [];
}

/// <summary>
/// Pricing and stock spreadsheets (unchanged layouts from TDS Estimates): the "Export" tab of
/// <c>Estimates - Facility &amp; Anaesthetist.xlsx</c> (headings row 3, data from row 4) with optional "Surgeon Fees" and "Add-ons" tabs,
/// and the "Export" tab of the supplier stock list (or its supplier blocks). Imports show the differences first and need approval;
/// future-dated prices are scheduled. The same layouts are exported, so a download can be edited and imported back.
/// </summary>
public sealed class EstimateImport(EstDbContext db, EstimateService estimates, AuditWriter audit, TimeProvider clock)
{
    public const long MaxFileBytes = 20 * 1024 * 1024;
    private static readonly CultureInfo Nz = CultureInfo.GetCultureInfo("en-NZ");

    private static string S(IXLCell c)
    {
        try { return c.GetFormattedString()?.Trim() ?? ""; }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException) { return c.CachedValue.ToString(Nz)?.Trim() ?? ""; }
    }

    private static decimal? D(IXLCell c)
    {
        try
        {
            var v = c.HasFormula ? c.CachedValue : c.Value;
            if (v.IsBlank) { if (c.HasFormula) v = c.Value; else return null; }
            if (v.IsNumber) return (decimal)v.GetNumber();
            if (v.IsText && decimal.TryParse(v.GetText().Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or OverflowException) { }
        return null;
    }

    private static DateOnly? Dt(IXLCell c)
    {
        try
        {
            var v = c.HasFormula ? c.CachedValue : c.Value;
            if (v.IsDateTime) return DateOnly.FromDateTime(v.GetDateTime());
            if (v.IsNumber) return DateOnly.FromDateTime(DateTime.FromOADate(v.GetNumber()));
            if (v.IsText && DateTime.TryParse(v.GetText(), Nz, DateTimeStyles.None, out var d)) return DateOnly.FromDateTime(d);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException) { }
        return null;
    }

    private static string Money(decimal? v) => v is null ? "–" : v.Value.ToString("$#,##0.00", Nz);
    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static XLWorkbook Open(Stream file, List<string> errors)
    {
        try { return new XLWorkbook(file); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException or IOException or NotSupportedException or FormatException)
        {
            errors.Add("This is not an Excel workbook (.xlsx) BrightPAS can read: " + ex.Message);
            return new XLWorkbook();
        }
    }

    // ------------------------------------------------------------------ pricing
    public async Task<PricingPreview> PreviewPricingAsync(Stream file, string fileName, CancellationToken ct = default)
    {
        var pv = new PricingPreview { FileName = fileName };
        using var wb = Open(file, pv.Errors);
        if (pv.Errors.Count > 0) return pv;
        try { wb.RecalculateAllFormulas(); }
        catch (Exception ex) when (ex is InvalidOperationException or NotImplementedException or ArgumentException or FormatException) { /* use the values Excel saved */ }
        if (!wb.TryGetWorksheet("Export", out var ws))
        {
            pv.Errors.Add("No ‘Export’ tab found. The pricing spreadsheet needs the Export tab (headings on row 3, data from row 4).");
            return pv;
        }
        string[] expected = ["Code", "Specialty", "Group", "Procedure", "Print title", "Minutes"];
        for (var i = 0; i < expected.Length; i++)
            if (!S(ws.Cell(3, i + 1)).StartsWith(expected[i], StringComparison.OrdinalIgnoreCase))
                pv.Errors.Add($"Export tab column {i + 1} heading should be ‘{expected[i]}’ (found ‘{S(ws.Cell(3, i + 1))}’). Layout has changed – import stopped.");
        if (pv.Errors.Count > 0) return pv;

        var rows = new List<ProcRow>();
        var last = ws.LastRowUsed()?.RowNumber() ?? 3;
        if (last > 5000) { pv.Errors.Add("The Export tab has more than 5,000 rows – check the file."); return pv; }
        for (var r = 4; r <= last; r++)
        {
            var code = S(ws.Cell(r, 1)).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code)) continue;
            var anText = S(ws.Cell(r, 7));
            var an = Anaesthetics.Parse(anText);
            var pr = new ProcRow
            {
                Row = r,
                Code = code,
                Specialty = S(ws.Cell(r, 2)),
                Group = S(ws.Cell(r, 3)),
                Name = S(ws.Cell(r, 4)),
                PrintTitle = S(ws.Cell(r, 5)),
                Minutes = (int)(D(ws.Cell(r, 6)) ?? 0),
                Anaesthetic = an ?? Anaesthetic.General,
                Facility = D(ws.Cell(r, 8)),
                FacilitySpecial = D(ws.Cell(r, 9)),
                Anaesthetist = D(ws.Cell(r, 10)),
                AnaesthetistSpecial = D(ws.Cell(r, 11)),
                Consumables = D(ws.Cell(r, 12)),
                Includes = NullIfEmpty(S(ws.Cell(r, 13)).ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal)),
                EffectiveFrom = Dt(ws.Cell(r, 14)),
                Active = !S(ws.Cell(r, 15)).Equals("No", StringComparison.OrdinalIgnoreCase),
                Source = NullIfEmpty(S(ws.Cell(r, 16)))
            };
            foreach (var col in new[] { 8, 10 })
            {
                var cell = ws.Cell(r, col);
                if (cell.HasFormula && D(cell) is null && !cell.CachedValue.IsText)
                    pv.Errors.Add($"Row {r}: {code} – the formula in column {col} has no value. Open the file in Excel, save it, then import again.");
            }
            if (pr.FacilitySpecial == 0) pr.FacilitySpecial = null;
            if (pr.AnaesthetistSpecial == 0) pr.AnaesthetistSpecial = null;
            if (pr.Consumables == 0) pr.Consumables = null;
            if (code.Length > 20) pv.Errors.Add($"Row {r}: code {code} is longer than 20 characters.");
            if (string.IsNullOrWhiteSpace(pr.Name)) pv.Errors.Add($"Row {r}: {code} has no procedure name.");
            if (string.IsNullOrWhiteSpace(pr.Specialty)) pv.Errors.Add($"Row {r}: {code} has no specialty.");
            if (an is null) pv.Errors.Add($"Row {r}: {code} anaesthetic ‘{anText}’ not recognised.");
            if (new[] { pr.Facility, pr.FacilitySpecial, pr.Anaesthetist, pr.AnaesthetistSpecial, pr.Consumables }.Any(v => v < 0)) pv.Errors.Add($"Row {r}: {code} has a negative price.");
            if (string.IsNullOrWhiteSpace(pr.PrintTitle)) pr.PrintTitle = pr.Name;
            rows.Add(pr);
        }
        foreach (var dup in rows.GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            pv.Errors.Add($"Code {dup.Key} appears {dup.Count()} times – codes must be unique.");

        var today = NzTime.Today(clock);
        var specs = await db.Specialties.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var existing = await db.Procedures.AsNoTracking().ToListAsync(ct);
        foreach (var row in rows)
        {
            var ex = existing.Find(p => p.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase));
            var ch = new ProcChange { Row = row, Code = row.Code, Name = row.Name, ExistingId = ex?.Id };
            if (ex is null) ch.Status = ChangeStatus.New;
            else
            {
                void Cmp(string f, string? o, string? n) { if ((o ?? "") != (n ?? "")) ch.Diffs.Add(new(f, string.IsNullOrEmpty(o) ? "–" : o, string.IsNullOrEmpty(n) ? "–" : n)); }
                void CmpM(string f, decimal? o, decimal? n) { if (o != n) ch.Diffs.Add(new(f, Money(o), Money(n))); }
                Cmp("Specialty", specs.GetValueOrDefault(ex.SpecialtyId), row.Specialty);
                Cmp("Group", ex.Group, row.Group);
                Cmp("Name", ex.Name, row.Name);
                Cmp("PrintTitle", ex.PrintTitle, row.PrintTitle);
                Cmp("Minutes", ex.Minutes.ToString(Nz), row.Minutes.ToString(Nz));
                Cmp("DefaultAnaesthetic", Anaesthetics.Labels[ex.DefaultAnaesthetic], Anaesthetics.Labels[row.Anaesthetic]);
                CmpM("Facility", ex.Facility, row.Facility);
                CmpM("FacilitySpecial", ex.FacilitySpecial, row.FacilitySpecial);
                CmpM("Anaesthetist", ex.Anaesthetist, row.Anaesthetist);
                CmpM("AnaesthetistSpecial", ex.AnaesthetistSpecial, row.AnaesthetistSpecial);
                CmpM("ConsumablesLine", ex.ConsumablesLine, row.Consumables);
                Cmp("IncludedAddOnCodes", ex.IncludedAddOnCodes, row.Includes);
                Cmp("Active", ex.IsActive ? "Yes" : "No", row.Active ? "Yes" : "No");
                ch.Status = ch.Diffs.Count == 0 ? ChangeStatus.Unchanged : ChangeStatus.Changed;
                ch.Scheduled = row.EffectiveFrom > today && ch.Diffs.Exists(d => ProcChange.PriceFields.Contains(d.Field));
                if (ex.Facility is not null && row.Facility is null) pv.Warnings.Add($"{row.Code} {row.Name}: facility fee would become blank (was {Money(ex.Facility)}).");
                if (ex.Anaesthetist > 0 && row.Anaesthetist is null) pv.Warnings.Add($"{row.Code} {row.Name}: anaesthetist fee would become blank (was {Money(ex.Anaesthetist)}).");
                if (ex.Facility > 0 && row.Facility > 0 && Math.Abs(row.Facility.Value - ex.Facility.Value) / ex.Facility.Value > 0.25m)
                    pv.Warnings.Add($"{row.Code} {row.Name}: facility fee changes by more than 25% ({Money(ex.Facility)} → {Money(row.Facility)}).");
                if (ex.Anaesthetist > 0 && row.Anaesthetist > 0 && Math.Abs(row.Anaesthetist.Value - ex.Anaesthetist.Value) / ex.Anaesthetist.Value > 0.25m)
                    pv.Warnings.Add($"{row.Code} {row.Name}: anaesthetist fee changes by more than 25% ({Money(ex.Anaesthetist)} → {Money(row.Anaesthetist)}).");
            }
            pv.Procedures.Add(ch);
        }
        foreach (var ex in existing.Where(e => e.IsActive && rows.TrueForAll(r => !r.Code.Equals(e.Code, StringComparison.OrdinalIgnoreCase))))
            pv.Procedures.Add(new ProcChange { Code = ex.Code, Name = ex.Name, ExistingId = ex.Id, Status = ChangeStatus.NotInFile });

        if (wb.TryGetWorksheet("Surgeon Fees", out var fs))
        {
            var lf = Math.Min(fs.LastRowUsed()?.RowNumber() ?? 3, 10000);
            for (var r = 4; r <= lf; r++)
            {
                var code = S(fs.Cell(r, 1)).ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(code)) continue;
                var fr = new FeeRow(code, NullIfEmpty(S(fs.Cell(r, 3))) ?? "All surgeons", D(fs.Cell(r, 4)), D(fs.Cell(r, 5)), NullIfEmpty(S(fs.Cell(r, 6))));
                if (fr.Fee < 0 || fr.Special < 0) pv.Errors.Add($"Surgeon Fees row {r}: negative fee.");
                if (rows.TrueForAll(x => x.Code != code) && existing.TrueForAll(x => x.Code != code)) pv.Warnings.Add($"Surgeon Fees row {r}: procedure {code} is not in the price list – skipped.");
                pv.Fees.Add(fr);
            }
        }
        if (wb.TryGetWorksheet("Add-ons", out var aw))
        {
            var la = Math.Min(aw.LastRowUsed()?.RowNumber() ?? 3, 10000);
            for (var r = 4; r <= la; r++)
            {
                var code = S(aw.Cell(r, 1)).ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(code)) continue;
                var ar = new AddOnRow(code, S(aw.Cell(r, 2)), NullIfEmpty(S(aw.Cell(r, 3))), S(aw.Cell(r, 4)), D(aw.Cell(r, 5)) ?? 0, S(aw.Cell(r, 6)), NullIfEmpty(S(aw.Cell(r, 7))), NullIfEmpty(S(aw.Cell(r, 8))));
                if (string.IsNullOrWhiteSpace(ar.Name) || string.IsNullOrWhiteSpace(ar.Specialty)) pv.Errors.Add($"Add-ons row {r}: {code} needs a name and specialty.");
                if (ar.Fee < 0) pv.Errors.Add($"Add-ons row {r}: {code} has a negative fee.");
                pv.AddOns.Add(ar);
            }
            foreach (var dup in pv.AddOns.GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                pv.Errors.Add($"Add-on code {dup.Key} appears {dup.Count()} times – codes must be unique.");
        }
        return pv;
    }

    public async Task<string> ApplyPricingAsync(PricingPreview pv, bool importFees, bool importAddOns, bool retireMissing, string user, CancellationToken ct = default)
    {
        if (pv.Errors.Count > 0) throw new InvalidOperationException("Fix the errors before importing.");
        var today = NzTime.Today(clock);
        var now = clock.GetUtcNow();
        var specs = await db.Specialties.ToListAsync(ct);
        EstSpecialty Spec(string name)
        {
            var s = specs.Find(x => x.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (s is null) { s = new EstSpecialty { Name = name.Trim() }; db.Specialties.Add(s); specs.Add(s); }
            return s;
        }
        int added = 0, changed = 0, scheduled = 0, retired = 0;
        var procs = await db.Procedures.ToListAsync(ct);
        foreach (var ch in pv.Procedures)
        {
            var r = ch.Row;
            if (ch.Status == ChangeStatus.NotInFile)
            {
                if (retireMissing) { procs.Single(p => p.Id == ch.ExistingId).IsActive = false; retired++; }
                continue;
            }
            if (r is null || ch.Status == ChangeStatus.Unchanged) continue;
            var p = ch.Status == ChangeStatus.New ? new EstProcedure { Code = r.Code } : procs.Single(x => x.Id == ch.ExistingId);
            if (ch.Status == ChangeStatus.New) { db.Procedures.Add(p); procs.Add(p); added++; } else changed++;
            p.SpecialtyId = Spec(r.Specialty).Id;
            p.Group = r.Group; p.Name = r.Name; p.PrintTitle = r.PrintTitle; p.Minutes = r.Minutes; p.DefaultAnaesthetic = r.Anaesthetic;
            p.IncludedAddOnCodes = r.Includes; p.IsActive = r.Active; p.Source = r.Source;
            var future = r.EffectiveFrom > today && ch.Status == ChangeStatus.Changed;
            void Price(string field, decimal? current, decimal? next, Action<decimal?> set)
            {
                if (current == next) return;
                if (future)
                {
                    db.PendingPrices.Add(new EstPendingPrice { ProcedureId = p.Id, Field = field, NewValue = next, EffectiveFrom = r.EffectiveFrom!.Value, CreatedBy = user, CreatedAt = now });
                    scheduled++;
                }
                else set(next);
            }
            Price("Facility", p.Facility, r.Facility, v => p.Facility = v);
            Price("FacilitySpecial", p.FacilitySpecial, r.FacilitySpecial, v => p.FacilitySpecial = v);
            Price("Anaesthetist", p.Anaesthetist, r.Anaesthetist, v => p.Anaesthetist = v);
            Price("AnaesthetistSpecial", p.AnaesthetistSpecial, r.AnaesthetistSpecial, v => p.AnaesthetistSpecial = v);
            Price("ConsumablesLine", p.ConsumablesLine, r.Consumables, v => p.ConsumablesLine = v);
            if (!future) p.EffectiveFrom = r.EffectiveFrom;
        }
        await db.SaveChangesAsync(ct);

        int fees = 0, aos = 0;
        var surgeons = await db.Surgeons.ToListAsync(ct);
        var links = await db.SurgeonSpecialties.ToListAsync(ct);
        EstSurgeon? Surg(string? name, Guid specId)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Equals("All surgeons", StringComparison.OrdinalIgnoreCase)) return null;
            var s = surgeons.Find(x => x.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (s is null) { s = new EstSurgeon { Name = name.Trim() }; db.Surgeons.Add(s); surgeons.Add(s); }
            if (!links.Exists(l => l.SurgeonId == s.Id && l.SpecialtyId == specId))
            {
                var l = new EstSurgeonSpecialty { SurgeonId = s.Id, SpecialtyId = specId };
                db.SurgeonSpecialties.Add(l); links.Add(l);
            }
            return s;
        }
        if (importFees && pv.Fees.Count > 0)
        {
            var allFees = await db.SurgeonFees.ToListAsync(ct);
            foreach (var fr in pv.Fees)
            {
                var p = procs.Find(x => x.Code.Equals(fr.Code, StringComparison.OrdinalIgnoreCase));
                if (p is null) continue;
                var s = Surg(fr.Surgeon, p.SpecialtyId);
                var f = allFees.Find(x => x.ProcedureId == p.Id && x.SurgeonId == s?.Id);
                if (f is null) { f = new EstSurgeonFee { ProcedureId = p.Id, SurgeonId = s?.Id }; db.SurgeonFees.Add(f); allFees.Add(f); }
                (f.Fee, f.SpecialFee, f.Notes) = (fr.Fee, fr.Special is 0 ? null : fr.Special, fr.Notes);
                fees++;
            }
        }
        if (importAddOns && pv.AddOns.Count > 0)
        {
            var all = await db.AddOns.ToListAsync(ct);
            foreach (var ar in pv.AddOns)
            {
                var spec = Spec(ar.Specialty);
                var a = all.Find(x => x.Code.Equals(ar.Code, StringComparison.OrdinalIgnoreCase));
                if (a is null) { a = new EstAddOn { Code = ar.Code }; db.AddOns.Add(a); all.Add(a); }
                a.SpecialtyId = spec.Id; a.Group = ar.Group; a.Name = ar.Name; a.Fee = ar.Fee; a.Source = ar.Source;
                a.Type = ar.Type.StartsWith("Surg", StringComparison.OrdinalIgnoreCase) ? AddOnType.Surgeon : AddOnType.AllIn;
                a.SurgeonId = Surg(ar.Surgeon, spec.Id)?.Id;
                aos++;
            }
        }
        await db.SaveChangesAsync(ct);
        await estimates.ApplyDuePricesAsync(user, ct);
        var summary = $"{pv.FileName}: {added} new, {changed} changed ({scheduled} price changes scheduled), {retired} retired" +
                      (importFees ? $", {fees} surgeon fees" : "") + (importAddOns ? $", {aos} add-ons" : "");
        var detail = string.Join("; ", pv.Procedures.Where(p => p.Status == ChangeStatus.Changed).Select(p => $"{p.Code}: " + string.Join(", ", p.Diffs.Select(d => $"{d.Field} {d.Old}→{d.New}"))));
        var full = summary + (detail.Length > 0 ? " | " + detail : "");
        await audit.WriteAsync(user, "Import", "EstPricing", pv.FileName, reason: full.Length > 1900 ? full[..1900] + "…" : full, ct: ct);
        return summary;
    }

    // ------------------------------------------------------------------ stock
    public async Task<StockPreview> PreviewStockAsync(Stream file, string fileName, CancellationToken ct = default)
    {
        var pv = new StockPreview { FileName = fileName };
        using var wb = Open(file, pv.Errors);
        if (pv.Errors.Count > 0) return pv;
        var suppliers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (wb.TryGetWorksheet("Export", out var ex))
        {
            try { wb.RecalculateAllFormulas(); }
            catch (Exception e) when (e is InvalidOperationException or NotImplementedException or ArgumentException or FormatException) { }
            var lastEx = Math.Min(ex.LastRowUsed()?.RowNumber() ?? 3, 20000);
            for (var r = 4; r <= lastEx; r++)
            {
                var name = S(ex.Cell(r, 2));
                if (name.Length == 0) continue;
                var sup = S(ex.Cell(r, 1));
                if (sup.Length > 0) suppliers.Add(sup);
                pv.Items.Add(new StockRow
                {
                    Supplier = sup,
                    Name = name,
                    Reference = NullIfEmpty(S(ex.Cell(r, 3))),
                    PackPrice = D(ex.Cell(r, 4)),
                    Units = D(ex.Cell(r, 5)),
                    InStock = D(ex.Cell(r, 7)),
                    Needed = D(ex.Cell(r, 8)),
                    WhereUsed = NullIfEmpty(S(ex.Cell(r, 9)))
                });
            }
            if (pv.Items.Count == 0) pv.Errors.Add("The Export tab has no items. Open the spreadsheet in Excel, let it calculate, save, then import again.");
        }
        else
        {
            // Supplier blocks: a supplier heading row, then items (name, reference, box price, …, in stock, …, units, needed …).
            var ws = wb.Worksheets.First();
            var last = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 1, 20000);
            var supplier = "";
            for (var r = 1; r <= last; r++)
            {
                var a = S(ws.Cell(r, 1)); var b = S(ws.Cell(r, 2));
                if (string.IsNullOrWhiteSpace(a) || a.Equals("Supplier Name", StringComparison.OrdinalIgnoreCase)) continue;
                var header = b.Equals("REFERENCE", StringComparison.OrdinalIgnoreCase) || Enumerable.Range(2, 6).All(c => string.IsNullOrWhiteSpace(S(ws.Cell(r, c))));
                if (header) { supplier = a; suppliers.Add(supplier); continue; }
                if (a.Equals("Total", StringComparison.OrdinalIgnoreCase)) continue;
                var where = Enumerable.Range(12, 5).Select(c => S(ws.Cell(r, c))).Where(x => x.Length > 0 && !x.Contains('@', StringComparison.Ordinal) && !x.Equals("COMMENTS", StringComparison.OrdinalIgnoreCase));
                pv.Items.Add(new StockRow
                {
                    Supplier = supplier,
                    Name = a,
                    Reference = NullIfEmpty(b),
                    PackPrice = D(ws.Cell(r, 3)),
                    InStock = D(ws.Cell(r, 5)),
                    Units = D(ws.Cell(r, 7)),
                    Needed = D(ws.Cell(r, 8)),
                    WhereUsed = NullIfEmpty(string.Join(", ", where))
                });
            }
        }
        foreach (var it in pv.Items)
        {
            var issues = new List<string>();
            if (it.PackPrice is null) issues.Add("no price");
            if (it.Units is null or 0) issues.Add("no units per box (unit price = box price)");
            it.UnitPrice = it.PackPrice is null ? null : Math.Round(it.PackPrice.Value / (it.Units is null or 0 ? 1 : it.Units.Value), 4);
            it.Issues = issues.Count > 0 ? string.Join("; ", issues) : null;
            if (it.Supplier.Length > 150) it.Supplier = it.Supplier[..150];
            if (it.Name.Length > 300) it.Name = it.Name[..300];
        }
        foreach (var g in pv.Items.Where(i => i.Reference is not null).GroupBy(i => i.Reference!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var i in g) i.Issues = string.Join("; ", new[] { i.Issues, $"reference {g.Key} used {g.Count()} times" }.Where(x => x is not null));
        pv.Suppliers = suppliers.Count;
        var existing = (await db.Stock.AsNoTracking().Select(e => new { e.Supplier, e.Reference, e.Name }).ToListAsync(ct)).Select(e => Key(e.Supplier, e.Reference, e.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var i in pv.Items) { if (existing.Contains(Key(i.Supplier, i.Reference, i.Name))) pv.Updated++; else pv.New++; }
        return pv;
    }

    private static string Key(string sup, string? reference, string name) => $"{sup}|{reference}|{name}".ToUpperInvariant();

    public async Task<string> ApplyStockAsync(StockPreview pv, string user, CancellationToken ct = default)
    {
        if (pv.Errors.Count > 0) throw new InvalidOperationException("Fix the errors before importing.");
        var existing = await db.Stock.ToListAsync(ct);
        var byKey = existing.GroupBy(x => Key(x.Supplier, x.Reference, x.Name), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var now = clock.GetUtcNow();
        foreach (var i in pv.Items)
        {
            if (!byKey.TryGetValue(Key(i.Supplier, i.Reference, i.Name), out var e)) { e = new EstStockItem(); db.Stock.Add(e); byKey[Key(i.Supplier, i.Reference, i.Name)] = e; }
            (e.Supplier, e.Reference, e.Name, e.PackPrice, e.UnitsPerPack, e.UnitPrice, e.InStockQty, e.NeededInHouse, e.WhereUsed, e.Issues, e.UpdatedAt) =
                (i.Supplier, i.Reference, i.Name, i.PackPrice, i.Units, i.UnitPrice, i.InStock, i.Needed, i.WhereUsed, i.Issues, now);
        }
        await db.SaveChangesAsync(ct);
        var s = $"{pv.FileName}: {pv.Items.Count} items from {pv.Suppliers} suppliers ({pv.New} new, {pv.Updated} updated, {pv.Items.Count(i => i.Issues is not null)} with issues)";
        await audit.WriteAsync(user, "Import", "EstStock", pv.FileName, reason: s, ct: ct);
        return s;
    }

    // ------------------------------------------------------------------ downloads (same layouts, for a round trip)
    private static void Style(IXLWorksheet sh)
    {
        sh.Row(3).Style.Font.Bold = true; sh.Row(3).Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E79"); sh.Row(3).Style.Font.FontColor = XLColor.White;
        sh.Columns().AdjustToContents(8.0, 60.0); sh.SheetView.FreezeRows(3);
    }

    private static byte[] Save(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportStockAsync(CancellationToken ct = default)
    {
        var items = await db.Stock.AsNoTracking().OrderBy(i => i.Supplier).ThenBy(i => i.Name).ToListAsync(ct);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Export");
        ws.Cell(1, 1).Value = $"APP IMPORT – stock exported from BrightPAS {clock.GetUtcNow().ToNz():d MMM yyyy HH:mm}. Keep this layout (headings row 3, data from row 4). Prices as supplied (ex GST).";
        string[] h = ["Supplier", "Item", "Reference", "Box price", "Units per box", "Unit price", "In stock qty", "Needed in house", "Where used"];
        for (var i = 0; i < h.Length; i++) ws.Cell(3, i + 1).Value = h[i];
        var r = 4;
        foreach (var i in items)
        {
            ws.Cell(r, 1).Value = i.Supplier; ws.Cell(r, 2).Value = i.Name; ws.Cell(r, 3).Value = i.Reference; ws.Cell(r, 4).Value = i.PackPrice;
            ws.Cell(r, 5).Value = i.UnitsPerPack; ws.Cell(r, 6).Value = i.UnitPrice; ws.Cell(r, 7).Value = i.InStockQty; ws.Cell(r, 8).Value = i.NeededInHouse;
            ws.Cell(r, 9).Value = i.WhereUsed; r++;
        }
        Style(ws);
        return Save(wb);
    }

    public async Task<byte[]> ExportPricingAsync(CancellationToken ct = default)
    {
        var specs = await db.Specialties.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var surgeons = await db.Surgeons.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var procs = (await db.Procedures.AsNoTracking().ToListAsync(ct)).OrderBy(p => specs.GetValueOrDefault(p.SpecialtyId), StringComparer.Ordinal).ThenBy(p => p.Code, StringComparer.Ordinal).ToList();
        var fees = await db.SurgeonFees.AsNoTracking().ToListAsync(ct);
        var addons = await db.AddOns.AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Export");
        ws.Cell(1, 1).Value = $"APP IMPORT – exported from BrightPAS {clock.GetUtcNow().ToNz():d MMM yyyy HH:mm}. Keep this layout (headings row 3, data from row 4). GST incl.";
        string[] h = ["Code", "Specialty", "Group", "Procedure", "Print title", "Minutes", "Default anaesthetic", "Facility", "Facility special", "Anaesthetist", "Anaesthetist special", "Consumables line", "Includes add-ons (codes)", "Effective from", "Active", "Source / notes"];
        for (var i = 0; i < h.Length; i++) ws.Cell(3, i + 1).Value = h[i];
        var r = 4;
        foreach (var p in procs)
        {
            ws.Cell(r, 1).Value = p.Code; ws.Cell(r, 2).Value = specs.GetValueOrDefault(p.SpecialtyId); ws.Cell(r, 3).Value = p.Group; ws.Cell(r, 4).Value = p.Name;
            ws.Cell(r, 5).Value = p.PrintTitle; ws.Cell(r, 6).Value = p.Minutes; ws.Cell(r, 7).Value = Anaesthetics.Labels[p.DefaultAnaesthetic];
            ws.Cell(r, 8).Value = p.Facility; ws.Cell(r, 9).Value = p.FacilitySpecial; ws.Cell(r, 10).Value = p.Anaesthetist;
            ws.Cell(r, 11).Value = p.AnaesthetistSpecial; ws.Cell(r, 12).Value = p.ConsumablesLine; ws.Cell(r, 13).Value = p.IncludedAddOnCodes;
            if (p.EffectiveFrom is not null) { ws.Cell(r, 14).Value = p.EffectiveFrom.Value.ToDateTime(TimeOnly.MinValue); ws.Cell(r, 14).Style.DateFormat.Format = "dd/MM/yyyy"; }
            ws.Cell(r, 15).Value = p.IsActive ? "Yes" : "No"; ws.Cell(r, 16).Value = p.Source; r++;
        }
        var fs = wb.AddWorksheet("Surgeon Fees");
        fs.Cell(1, 1).Value = "Surgeon fees per procedure (GST incl). ‘All surgeons’ is the default.";
        string[] fh = ["Procedure code", "Procedure (info)", "Surgeon", "Fee", "Special fee (self-funding)", "Notes"];
        for (var i = 0; i < fh.Length; i++) fs.Cell(3, i + 1).Value = fh[i];
        r = 4;
        foreach (var p in procs)
            foreach (var f in fees.Where(f => f.ProcedureId == p.Id).OrderBy(f => f.SurgeonId is null ? "" : surgeons.GetValueOrDefault(f.SurgeonId.Value), StringComparer.Ordinal))
            {
                fs.Cell(r, 1).Value = p.Code; fs.Cell(r, 2).Value = p.Name; fs.Cell(r, 3).Value = f.SurgeonId is null ? "All surgeons" : surgeons.GetValueOrDefault(f.SurgeonId.Value);
                fs.Cell(r, 4).Value = f.Fee; fs.Cell(r, 5).Value = f.SpecialFee; fs.Cell(r, 6).Value = f.Notes; r++;
            }
        var aw = wb.AddWorksheet("Add-ons");
        aw.Cell(1, 1).Value = "Type: Surgeon = adds to surgeon fee when included; All-in = own line.";
        string[] ah = ["Add-on code", "Specialty", "Applies to group", "Add-on", "Fee", "Type", "Surgeon", "Source / notes"];
        for (var i = 0; i < ah.Length; i++) aw.Cell(3, i + 1).Value = ah[i];
        r = 4;
        foreach (var a in addons)
        {
            aw.Cell(r, 1).Value = a.Code; aw.Cell(r, 2).Value = specs.GetValueOrDefault(a.SpecialtyId); aw.Cell(r, 3).Value = a.Group; aw.Cell(r, 4).Value = a.Name;
            aw.Cell(r, 5).Value = a.Fee; aw.Cell(r, 6).Value = a.Type == AddOnType.Surgeon ? "Surgeon" : "All-in";
            aw.Cell(r, 7).Value = a.SurgeonId is null ? null : surgeons.GetValueOrDefault(a.SurgeonId.Value); aw.Cell(r, 8).Value = a.Source; r++;
        }
        foreach (var sh in wb.Worksheets) Style(sh);
        return Save(wb);
    }
}
