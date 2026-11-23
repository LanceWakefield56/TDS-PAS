using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Estimates;

public sealed record SpecialtyEdit(string Name, string? DefaultNotes, bool IsActive, IReadOnlyList<Guid>? SurgeonIds = null);
public sealed record ProcedureEdit(string Code, Guid SpecialtyId, string? Group, string Name, string? PrintTitle, int Minutes, Anaesthetic DefaultAnaesthetic,
    decimal? Facility, decimal? FacilitySpecial, decimal? Anaesthetist, decimal? AnaesthetistSpecial, decimal? ConsumablesLine, string? IncludedAddOnCodes,
    DateOnly? EffectiveFrom, bool IsActive, string? Source);
public sealed record FeeEdit(Guid? SurgeonId, decimal? Fee, decimal? SpecialFee, string? Notes);
public sealed record ConsumableEdit(Guid? StockItemId, string Description, decimal Quantity, decimal? UnitCost);
public sealed record SurgeonEdit(string Name, Guid? ClinicianId, bool IsActive, IReadOnlyList<Guid> SpecialtyIds);
public sealed record AddOnEdit(string Code, Guid SpecialtyId, string? Group, string Name, decimal Fee, AddOnType Type, Guid? SurgeonId, string? Source, bool IsActive);

/// <summary>Usage of a price-list item: anything used by an estimate or template can only be hidden, so old estimates and the audit trail stay intact.</summary>
public sealed record Usage(int Estimates, int Templates, int Other, string? OtherWhat = null)
{
    public bool InUse => Estimates + Templates + Other > 0;
    public string Describe() => string.Join(", ", new[]
    {
        Estimates > 0 ? $"{Estimates} estimate(s)" : null,
        Templates > 0 ? $"{Templates} template(s)" : null,
        Other > 0 ? $"{Other} {OtherWhat}" : null
    }.Where(x => x is not null));
}

/// <summary>Estimates › Admin: specialties, procedures (fees, surgeon fees, costing), surgeons, add-ons, stock and settings, with safe delete.</summary>
public sealed class EstimateCatalogue(EstDbContext db, AuditWriter audit, TimeProvider clock)
{
    private static string? Blank(string? s) => EstimateService.Blank(s);
    private static string? Money(decimal? v) => v is < 0 ? "Prices cannot be negative." : v is > 1_000_000 ? "Check the price – it is over $1,000,000." : null;

    // ------------------------------------------------------------------ reading
    public Task<List<EstSpecialty>> SpecialtiesAsync(CancellationToken ct = default) => db.Specialties.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
    public Task<List<EstProcedure>> ProceduresAsync(CancellationToken ct = default) => db.Procedures.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct);
    public Task<List<EstSurgeon>> SurgeonsAsync(CancellationToken ct = default) => db.Surgeons.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
    public Task<List<EstSurgeonSpecialty>> SurgeonSpecialtiesAsync(CancellationToken ct = default) => db.SurgeonSpecialties.AsNoTracking().ToListAsync(ct);
    public Task<List<EstAddOn>> AddOnsAsync(CancellationToken ct = default) => db.AddOns.AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct);
    public Task<List<EstSurgeonFee>> FeesAsync(Guid? procedureId, CancellationToken ct = default) =>
        db.SurgeonFees.AsNoTracking().Where(f => procedureId == null || f.ProcedureId == procedureId).ToListAsync(ct);
    public Task<List<EstProcedureConsumable>> ConsumablesAsync(Guid procedureId, CancellationToken ct = default) =>
        db.Consumables.AsNoTracking().Where(c => c.ProcedureId == procedureId).ToListAsync(ct);
    public Task<List<EstStockItem>> StockAsync(CancellationToken ct = default) => db.Stock.AsNoTracking().OrderBy(s => s.Supplier).ThenBy(s => s.Name).ToListAsync(ct);
    public Task<List<EstPendingPrice>> PendingPricesAsync(CancellationToken ct = default) =>
        db.PendingPrices.AsNoTracking().Where(p => p.AppliedAt == null).OrderBy(p => p.EffectiveFrom).ToListAsync(ct);

    // ------------------------------------------------------------------ specialties
    public async Task<(Guid? Id, string? Error)> SaveSpecialtyAsync(Guid? id, SpecialtyEdit e, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(e.Name)) return (null, "Enter the specialty name.");
        var name = e.Name.Trim();
        if (await db.Specialties.AnyAsync(s => s.Name == name && s.Id != id, ct)) return (null, $"{name} already exists.");
        var s = id is null ? null : await db.Specialties.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && s is null) return (null, "Specialty not found.");
        if (s is null) { s = new EstSpecialty(); db.Specialties.Add(s); }
        (s.Name, s.DefaultNotes, s.IsActive) = (name, Blank(e.DefaultNotes), e.IsActive);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Save", "EstSpecialty", s.Id.ToString(), reason: s.Name, ct: ct);
        return (s.Id, null);
    }

    public async Task<Usage> SpecialtyUsageAsync(Guid id, CancellationToken ct = default) =>
        new(0, 0, await db.Procedures.CountAsync(p => p.SpecialtyId == id, ct) + await db.AddOns.CountAsync(a => a.SpecialtyId == id, ct), "procedure(s) or add-on(s)");

    public async Task<string?> DeleteSpecialtyAsync(Guid id, string user, CancellationToken ct = default)
    {
        var s = await db.Specialties.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return "Specialty not found.";
        var u = await SpecialtyUsageAsync(id, ct);
        if (u.InUse) return $"This specialty is used by {u.Describe()} – hide it instead.";
        db.Specialties.Remove(s);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "EstSpecialty", id.ToString(), reason: s.Name, ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ procedures
    public async Task<(Guid? Id, IReadOnlyList<string> Errors)> SaveProcedureAsync(Guid? id, ProcedureEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var code = e.Code?.Trim().ToUpperInvariant() ?? "";
        if (code.Length is 0 or > 20) errors.Add("Enter a code of up to 20 characters (e.g. GYN-015).");
        if (string.IsNullOrWhiteSpace(e.Name)) errors.Add("Enter the procedure name.");
        if (e.Minutes is < 0 or > 1440) errors.Add("Check the minutes.");
        if (!await db.Specialties.AnyAsync(s => s.Id == e.SpecialtyId, ct)) errors.Add("Choose the specialty.");
        foreach (var m in new[] { e.Facility, e.FacilitySpecial, e.Anaesthetist, e.AnaesthetistSpecial, e.ConsumablesLine }.Select(Money).OfType<string>().Distinct()) errors.Add(m);
        if (await db.Procedures.AnyAsync(p => p.Code == code && p.Id != id, ct)) errors.Add($"Code {code} is already used.");
        var p = id is null ? null : await db.Procedures.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && p is null) errors.Add("Procedure not found.");
        if (p is not null && !string.Equals(p.Code, code, StringComparison.Ordinal) && await db.Estimates.AnyAsync(x => x.ProcedureId == p.Id, ct))
            errors.Add("Procedure codes are permanent once used on an estimate.");
        if (errors.Count > 0) return (null, errors);
        if (p is null) { p = new EstProcedure(); db.Procedures.Add(p); }
        p.Code = code; p.SpecialtyId = e.SpecialtyId; p.Group = e.Group?.Trim() ?? ""; p.Name = e.Name.Trim(); p.PrintTitle = Blank(e.PrintTitle) ?? p.Name;
        p.Minutes = e.Minutes; p.DefaultAnaesthetic = e.DefaultAnaesthetic;
        (p.Facility, p.FacilitySpecial, p.Anaesthetist, p.AnaesthetistSpecial, p.ConsumablesLine) = (e.Facility, Zero(e.FacilitySpecial), e.Anaesthetist, Zero(e.AnaesthetistSpecial), Zero(e.ConsumablesLine));
        p.IncludedAddOnCodes = Blank(string.Join(",", (e.IncludedAddOnCodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(c => c.ToUpperInvariant())));
        (p.EffectiveFrom, p.IsActive, p.Source) = (e.EffectiveFrom, e.IsActive, Blank(e.Source));
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, id is null ? "Create" : "Update", "EstProcedure", p.Id.ToString(),
            reason: $"{p.Code} facility {p.Facility:0.00} anaesthetist {p.Anaesthetist:0.00} consumables {p.ConsumablesLine:0.00}", ct: ct);
        return (p.Id, []);
    }

    private static decimal? Zero(decimal? v) => v is 0 ? null : v;

    /// <summary>Replaces the surgeon fees for a procedure (one "All surgeons" row plus any surgeon-specific rows).</summary>
    public async Task<string?> SaveFeesAsync(Guid procedureId, IReadOnlyList<FeeEdit> fees, string user, CancellationToken ct = default)
    {
        var p = await db.Procedures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == procedureId, ct);
        if (p is null) return "Procedure not found.";
        if (fees.GroupBy(f => f.SurgeonId).Any(g => g.Count() > 1)) return "Each surgeon (and ‘All surgeons’) can only have one fee row.";
        foreach (var f in fees) if ((Money(f.Fee) ?? Money(f.SpecialFee)) is { } m) return m;
        foreach (var sid in fees.Where(f => f.SurgeonId is not null).Select(f => f.SurgeonId!.Value))
            if (!await db.Surgeons.AnyAsync(s => s.Id == sid, ct)) return "Surgeon not found.";
        var existing = await db.SurgeonFees.Where(f => f.ProcedureId == procedureId).ToListAsync(ct);
        db.SurgeonFees.RemoveRange(existing.Where(x => fees.All(f => f.SurgeonId != x.SurgeonId)));
        foreach (var f in fees)
        {
            var row = existing.Find(x => x.SurgeonId == f.SurgeonId);
            if (row is null) { row = new EstSurgeonFee { ProcedureId = procedureId, SurgeonId = f.SurgeonId }; db.SurgeonFees.Add(row); }
            (row.Fee, row.SpecialFee, row.Notes) = (f.Fee, Zero(f.SpecialFee), Blank(f.Notes));
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "EstSurgeonFee", procedureId.ToString(), reason: $"{p.Code}: " + string.Join("; ", fees.Select(f => $"{(f.SurgeonId is null ? "all" : "surgeon")} {f.Fee:0.00}/{f.SpecialFee:0.00}")), ct: ct);
        return null;
    }

    public async Task<string?> SaveConsumablesAsync(Guid procedureId, IReadOnlyList<ConsumableEdit> lines, string user, CancellationToken ct = default)
    {
        if (!await db.Procedures.AnyAsync(x => x.Id == procedureId, ct)) return "Procedure not found.";
        if (lines.Any(l => l.Quantity <= 0 || l.UnitCost < 0)) return "Check the quantities and costs.";
        db.Consumables.RemoveRange(await db.Consumables.Where(c => c.ProcedureId == procedureId).ToListAsync(ct));
        foreach (var l in lines)
        {
            if (l.StockItemId is not null && !await db.Stock.AnyAsync(s => s.Id == l.StockItemId, ct)) return "Stock item not found.";
            db.Consumables.Add(new EstProcedureConsumable { ProcedureId = procedureId, StockItemId = l.StockItemId, Description = l.Description?.Trim() ?? "", Quantity = l.Quantity, UnitCost = l.UnitCost });
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "EstProcedureConsumable", procedureId.ToString(), reason: $"{lines.Count} costing line(s)", ct: ct);
        return null;
    }

    public async Task<Usage> ProcedureUsageAsync(Guid id, CancellationToken ct = default) =>
        new(await db.Estimates.CountAsync(e => e.ProcedureId == id, ct), await db.Templates.CountAsync(t => t.ProcedureId == id, ct), 0);

    public async Task<string?> DeleteProcedureAsync(Guid id, string user, CancellationToken ct = default)
    {
        var p = await db.Procedures.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return "Procedure not found.";
        var u = await ProcedureUsageAsync(id, ct);
        if (u.InUse) return $"This procedure has been used ({u.Describe()}) – hide it instead.";
        db.Procedures.Remove(p);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "EstProcedure", id.ToString(), reason: p.Code, ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ surgeons
    public async Task<(Guid? Id, string? Error)> SaveSurgeonAsync(Guid? id, SurgeonEdit e, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(e.Name)) return (null, "Enter the surgeon's name.");
        var name = e.Name.Trim();
        if (await db.Surgeons.AnyAsync(s => s.Name == name && s.Id != id, ct)) return (null, $"{name} already exists.");
        var s = id is null ? null : await db.Surgeons.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && s is null) return (null, "Surgeon not found.");
        if (s is null) { s = new EstSurgeon(); db.Surgeons.Add(s); }
        (s.Name, s.ClinicianId, s.IsActive) = (name, e.ClinicianId, e.IsActive);
        db.SurgeonSpecialties.RemoveRange(await db.SurgeonSpecialties.Where(x => x.SurgeonId == s.Id).ToListAsync(ct));
        foreach (var sp in e.SpecialtyIds.Distinct())
            if (await db.Specialties.AnyAsync(x => x.Id == sp, ct)) db.SurgeonSpecialties.Add(new EstSurgeonSpecialty { SurgeonId = s.Id, SpecialtyId = sp });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Save", "EstSurgeon", s.Id.ToString(), reason: s.Name, ct: ct);
        return (s.Id, null);
    }

    public async Task<Usage> SurgeonUsageAsync(Guid id, CancellationToken ct = default) =>
        new(await db.Estimates.CountAsync(e => e.SurgeonId == id, ct), await db.Templates.CountAsync(t => t.SurgeonId == id, ct),
            await db.SurgeonFees.CountAsync(f => f.SurgeonId == id, ct) + await db.AddOns.CountAsync(a => a.SurgeonId == id, ct), "fee(s) or add-on(s)");

    public async Task<string?> DeleteSurgeonAsync(Guid id, string user, CancellationToken ct = default)
    {
        var s = await db.Surgeons.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return "Surgeon not found.";
        var u = await SurgeonUsageAsync(id, ct);
        if (u.InUse) return $"This surgeon is used by {u.Describe()} – hide them instead.";
        db.Surgeons.Remove(s);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "EstSurgeon", id.ToString(), reason: s.Name, ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ add-ons
    public async Task<(Guid? Id, IReadOnlyList<string> Errors)> SaveAddOnAsync(Guid? id, AddOnEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var code = e.Code?.Trim().ToUpperInvariant() ?? "";
        if (code.Length is 0 or > 20) errors.Add("Enter a code of up to 20 characters (e.g. GYN-A20).");
        if (string.IsNullOrWhiteSpace(e.Name)) errors.Add("Enter the add-on name.");
        if (Money(e.Fee) is { } m) errors.Add(m);
        if (!await db.Specialties.AnyAsync(s => s.Id == e.SpecialtyId, ct)) errors.Add("Choose the specialty.");
        if (e.SurgeonId is not null && !await db.Surgeons.AnyAsync(s => s.Id == e.SurgeonId, ct)) errors.Add("Surgeon not found.");
        if (await db.AddOns.AnyAsync(a => a.Code == code && a.Id != id, ct)) errors.Add($"Code {code} is already used.");
        var a = id is null ? null : await db.AddOns.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && a is null) errors.Add("Add-on not found.");
        if (a is not null && !string.Equals(a.Code, code, StringComparison.Ordinal) && (await AddOnUsageAsync(a.Code, ct)).InUse) errors.Add("Add-on codes are permanent once used.");
        if (errors.Count > 0) return (null, errors);
        if (a is null) { a = new EstAddOn(); db.AddOns.Add(a); }
        (a.Code, a.SpecialtyId, a.Group, a.Name, a.Fee, a.Type, a.SurgeonId, a.Source, a.IsActive) =
            (code, e.SpecialtyId, Blank(e.Group), e.Name.Trim(), e.Fee, e.Type, e.SurgeonId, Blank(e.Source), e.IsActive);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, id is null ? "Create" : "Update", "EstAddOn", a.Id.ToString(), reason: $"{a.Code} {a.Name} {a.Fee:0.00} {a.Type}", ct: ct);
        return (a.Id, []);
    }

    public async Task<Usage> AddOnUsageAsync(string code, CancellationToken ct = default)
    {
        var pattern = $"%\"code\":\"{code}\"%";
        return new(await db.Estimates.CountAsync(e => e.InputsJson != null && EF.Functions.Like(e.InputsJson, pattern), ct),
            await db.Templates.CountAsync(t => t.InputsJson != null && EF.Functions.Like(t.InputsJson, pattern), ct),
            await db.Procedures.CountAsync(p => p.IncludedAddOnCodes != null && EF.Functions.Like("," + p.IncludedAddOnCodes + ",", "%," + code + ",%"), ct), "procedure(s) that include it");
    }

    public async Task<string?> DeleteAddOnAsync(Guid id, string user, CancellationToken ct = default)
    {
        var a = await db.AddOns.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return "Add-on not found.";
        var u = await AddOnUsageAsync(a.Code, ct);
        if (u.InUse) return $"This add-on has been used ({u.Describe()}) – hide it instead.";
        db.AddOns.Remove(a);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "EstAddOn", id.ToString(), reason: a.Code, ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ settings
    public async Task<IReadOnlyList<string>> SaveSettingsAsync(EstSettings input, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (input.GstRate is < 0 or > 0.5m) errors.Add("GST rate is a fraction, e.g. 0.15.");
        if (input.Loading is < 0 or > 5 || input.Markup is < 0 or > 5) errors.Add("Loading and markup are fractions, e.g. 0.15 and 0.25.");
        if (input.ExtendedTimePer15 is < 0 or > 10000) errors.Add("Check the extended time rate.");
        if (input.ValidityMonths is < 1 or > 24) errors.Add("Validity must be 1–24 months.");
        if (input.DepositPercent is < 0 or > 100) errors.Add("The deposit is a percentage (0–100).");
        if (input.DepositDaysBefore is < 0 or > 90 || input.BalanceDaysAfter is < 0 or > 90) errors.Add("Check the deposit and balance days.");
        if (input.HoldMinutes is < 0 or > 1440) errors.Add("The hold period must be 0–1440 minutes.");
        if (string.IsNullOrWhiteSpace(input.SurgeonFeeLabel)) errors.Add("Enter the label for the surgeon line.");
        if (string.IsNullOrWhiteSpace(input.IncisiveFileName) || input.IncisiveFileName.IndexOfAny(['/', '\\', ':']) >= 0) errors.Add("The Incisive file name pattern can't contain / \\ or :.");
        if (input.StaffCostPerMinute < 0 || input.OverheadPerCase < 0) errors.Add("Costing values cannot be negative.");
        if (errors.Count > 0) return errors;
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is null) { s = new EstSettings(); db.Settings.Add(s); }
        var seeded = s.TemplatesSeeded;
        db.Entry(s).CurrentValues.SetValues(input);
        s.Id = 1; s.TemplatesSeeded = seeded;
        s.IncisiveFolder = Blank(input.IncisiveFolder); s.BccAddress = Blank(input.BccAddress);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "EstSettings", "1", reason: $"GST {s.GstRate}; ext {s.ExtendedTimePer15}/15 min; deposit {s.DepositPercent}%; {s.SendMode}, hold {s.HoldMinutes} min; patient PDF password {s.PatientPassword}", ct: ct);
        return [];
    }

    // ------------------------------------------------------------------ price-change impact (handover "still to do")
    public sealed record PriceImpact(string Code, string Name, decimal? Current, decimal? Next, DateOnly EffectiveFrom, string Field, int OpenEstimates);

    /// <summary>Scheduled price changes and how many issued estimates (still valid) are on the old price.</summary>
    public async Task<List<PriceImpact>> PriceImpactAsync(CancellationToken ct = default)
    {
        var today = BrightPAS.Core.NzTime.Today(clock);
        var pend = await (from x in db.PendingPrices.AsNoTracking()
                          where x.AppliedAt == null
                          join p in db.Procedures.AsNoTracking() on x.ProcedureId equals p.Id
                          select new { x, p }).ToListAsync(ct);
        var list = new List<PriceImpact>();
        foreach (var r in pend.OrderBy(r => r.x.EffectiveFrom))
        {
            decimal? cur = r.x.Field switch
            {
                "Facility" => r.p.Facility,
                "FacilitySpecial" => r.p.FacilitySpecial,
                "Anaesthetist" => r.p.Anaesthetist,
                "AnaesthetistSpecial" => r.p.AnaesthetistSpecial,
                "ConsumablesLine" => r.p.ConsumablesLine,
                _ => null
            };
            var open = await db.Estimates.CountAsync(e => e.ProcedureId == r.p.Id && e.Status == EstimateStatus.Issued && e.ValidUntil >= today, ct);
            list.Add(new PriceImpact(r.p.Code, r.p.Name, cur, r.x.NewValue, r.x.EffectiveFrom, r.x.Field, open));
        }
        return list;
    }
}
