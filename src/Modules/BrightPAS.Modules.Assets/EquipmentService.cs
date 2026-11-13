using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;

namespace BrightPAS.Modules.Assets;

public sealed record SupplierEdit(Guid? Id, string Name, string? ContactName, string? Email, string? Phone, string? XeroContactName, bool IsActive);
public sealed record EquipmentEdit(Guid? Id, string AssetNumber, string Name, EquipmentCategory Category, string? Make, string? Model, string? SerialNumber, Guid? LocationId,
    string? Owner, Guid? SupplierId, DateOnly? PurchasedOn, decimal? Value, DateOnly? WarrantyUntil, RiskClass Risk, string? NetworkAddress, string? Notes);
public sealed record PlanEdit(Guid EquipmentId, MaintenanceKind Kind, int IntervalMonths, DateOnly NextDue, string? DoneBy);
public sealed record MaintenanceEdit(Guid EquipmentId, MaintenanceKind Kind, DateOnly DoneOn, string DoneBy, bool Passed, string? TagNumber, string? Notes);
public sealed record EquipmentRow(Guid Id, string AssetNumber, string Name, string Category, string? Make, string? Model, string? SerialNumber, Guid? LocationId,
    string? Owner, Guid? SupplierId, string? Supplier, DateOnly? PurchasedOn, decimal? Value, DateOnly? WarrantyUntil, string Risk, string Status, string? OutOfServiceReason,
    string? LockedOutBy, DateTimeOffset? LockedOutAt, string? NetworkAddress, string? Notes, DateOnly? NextDue, string? NextDueKind, string DueState);
public sealed record DueRow(Guid EquipmentId, string AssetNumber, string Name, string Kind, DateOnly NextDue, string State, string? DoneBy);

/// <summary>Equipment register, planned maintenance (PPM, calibration, test-and-tag, biomedical checks) and out-of-service lockout.</summary>
public sealed class EquipmentService(AssetsDbContext db, PasDbContext core, AuditWriter audit, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<AssetsSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is not null) return s;
        s = new AssetsSettings();
        db.Settings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<IReadOnlyList<string>> UpdateSettingsAsync(int scopeStorageHours, int maintenanceWarningDays, bool releaseNeedsSecondPerson, string user, CancellationToken ct = default)
    {
        if (scopeStorageHours is < 1 or > 2160) return ["Scope storage time must be between 1 hour and 90 days."];
        if (maintenanceWarningDays is < 1 or > 180) return ["Warn between 1 and 180 days before maintenance is due."];
        var s = await SettingsAsync(ct);
        s.ScopeStorageHours = scopeStorageHours; s.MaintenanceWarningDays = maintenanceWarningDays; s.ReleaseNeedsSecondPerson = releaseNeedsSecondPerson;
        s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "AssetsSettings", "1", reason: $"scope storage {scopeStorageHours} h; warn {maintenanceWarningDays} days; second person release {releaseNeedsSecondPerson}", ct: ct);
        return [];
    }

    // ---------------------------------------------------------------- suppliers
    public async Task<IReadOnlyList<Supplier>> SuppliersAsync(bool activeOnly, CancellationToken ct = default) =>
        await db.Suppliers.AsNoTracking().Where(s => !activeOnly || s.IsActive).OrderBy(s => s.Name).ToListAsync(ct);

    public async Task<(Supplier? Supplier, IReadOnlyList<string> Errors)> SaveSupplierAsync(SupplierEdit e, string user, CancellationToken ct = default)
    {
        var name = Clip(e.Name, 150);
        var errors = new List<string>();
        if (name is null) errors.Add("Enter the supplier's name.");
        if (!string.IsNullOrWhiteSpace(e.Email) && !e.Email.Contains('@', StringComparison.Ordinal)) errors.Add("Enter a valid email address.");
        if (name is not null && await db.Suppliers.AnyAsync(s => s.Name == name && s.Id != e.Id, ct)) errors.Add($"A supplier called {name} already exists.");
        if (errors.Count > 0) return (null, errors);
        Supplier row;
        if (e.Id is { } id) row = await db.Suppliers.SingleAsync(s => s.Id == id, ct); else { row = new Supplier(); db.Suppliers.Add(row); }
        row.Name = name!; row.ContactName = Clip(e.ContactName, 100); row.Email = Clip(e.Email, 200); row.Phone = Clip(e.Phone, 40);
        row.XeroContactName = Clip(e.XeroContactName, 150); row.IsActive = e.IsActive;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "Supplier", row.Id.ToString(), reason: row.Name, ct: ct);
        return (row, []);
    }

    // ---------------------------------------------------------------- equipment
    public async Task<IReadOnlyList<EquipmentRow>> ListAsync(string? category, string? q, bool includeRetired, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var query = db.Equipment.AsNoTracking().AsQueryable();
        if (!includeRetired) query = query.Where(x => x.Status != EquipmentStatus.Retired);
        if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<EquipmentCategory>(category, true, out var cat)) query = query.Where(x => x.Category == cat);
        if (!string.IsNullOrWhiteSpace(q)) { var t = q.Trim(); query = query.Where(x => x.Name.Contains(t) || x.AssetNumber.Contains(t) || (x.SerialNumber != null && x.SerialNumber.Contains(t)) || (x.Model != null && x.Model.Contains(t))); }
        var list = await query.OrderBy(x => x.AssetNumber).Take(1000).ToListAsync(ct);
        var ids = list.Select(x => x.Id).ToList();
        var plans = await db.Plans.AsNoTracking().Where(p => ids.Contains(p.EquipmentId)).ToListAsync(ct);
        var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return list.Select(x => Row(x, plans.Where(p => p.EquipmentId == x.Id).OrderBy(p => p.NextDue).FirstOrDefault(), suppliers, s.MaintenanceWarningDays)).ToList();
    }

    private EquipmentRow Row(Equipment x, MaintenancePlan? next, IReadOnlyDictionary<Guid, string> suppliers, int warn) =>
        new(x.Id, x.AssetNumber, x.Name, x.Category.ToString(), x.Make, x.Model, x.SerialNumber, x.LocationId, x.Owner, x.SupplierId,
            x.SupplierId is { } sid ? suppliers.GetValueOrDefault(sid) : null, x.PurchasedOn, x.Value, x.WarrantyUntil, x.Risk.ToString(), x.Status.ToString(),
            x.OutOfServiceReason, x.LockedOutBy, x.LockedOutAt, x.NetworkAddress, x.Notes, next?.NextDue, next?.Kind.ToString(), next is null ? "None" : DueState(next.NextDue, warn));

    public string DueState(DateOnly due, int warn) => due < Today ? "Overdue" : due <= Today.AddDays(warn) ? "DueSoon" : "Current";

    public async Task<Equipment?> GetAsync(Guid id, CancellationToken ct = default) => await db.Equipment.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<(Equipment? Equipment, IReadOnlyList<string> Errors)> SaveAsync(EquipmentEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var number = Clip(e.AssetNumber, 40)?.ToUpperInvariant();
        var name = Clip(e.Name, 150);
        if (number is null) errors.Add("Enter the asset number (the number on the asset label).");
        if (name is null) errors.Add("Enter what the equipment is.");
        if (!Enum.IsDefined(e.Category)) errors.Add("Choose the category.");
        if (!Enum.IsDefined(e.Risk)) errors.Add("Choose the risk class.");
        if (number is not null && await db.Equipment.AnyAsync(x => x.AssetNumber == number && x.Id != e.Id, ct)) errors.Add($"Asset number {number} is already used.");
        if (e.LocationId is { } loc && !await core.Locations.AnyAsync(l => l.Id == loc, ct)) errors.Add("Choose a room from the list.");
        if (e.SupplierId is { } sup && !await db.Suppliers.AnyAsync(s => s.Id == sup, ct)) errors.Add("Choose a supplier from the list.");
        if (e.Value is < 0) errors.Add("The value cannot be negative.");
        if (errors.Count > 0) return (null, errors);
        Equipment row;
        if (e.Id is { } id) row = await db.Equipment.SingleAsync(x => x.Id == id, ct);
        else { row = new Equipment { CreatedAt = clock.GetUtcNow() }; db.Equipment.Add(row); }
        row.AssetNumber = number!; row.Name = name!; row.Category = e.Category; row.Make = Clip(e.Make, 80); row.Model = Clip(e.Model, 80); row.SerialNumber = Clip(e.SerialNumber, 80);
        row.LocationId = e.LocationId; row.Owner = Clip(e.Owner, 100); row.SupplierId = e.SupplierId; row.PurchasedOn = e.PurchasedOn; row.Value = e.Value;
        row.WarrantyUntil = e.WarrantyUntil; row.Risk = e.Risk; row.NetworkAddress = Clip(e.NetworkAddress, 100); row.Notes = Clip(e.Notes, 1000);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "Equipment", row.Id.ToString(), reason: $"{row.AssetNumber} {row.Name}", ct: ct);
        return (row, []);
    }

    /// <summary>Takes equipment out of service (locked out) with a reason; it cannot be used for cycles or cases until returned to service.</summary>
    public async Task<string?> LockOutAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "Enter why it is out of service (this goes on the lockout tag).";
        var row = await db.Equipment.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return "Equipment not found.";
        row.Status = EquipmentStatus.OutOfService; row.OutOfServiceReason = Clip(reason, 300); row.LockedOutBy = user; row.LockedOutAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "LockOut", "Equipment", id.ToString(), reason: $"{row.AssetNumber}: {row.OutOfServiceReason}", ct: ct);
        return null;
    }

    public async Task<string?> ReturnToServiceAsync(Guid id, string note, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note)) return "Enter what was done to make it safe to use again.";
        var row = await db.Equipment.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return "Equipment not found.";
        if (row.Status != EquipmentStatus.OutOfService) return "It is not out of service.";
        var overdueSafety = await db.Plans.AnyAsync(p => p.EquipmentId == id && p.NextDue < Today && (p.Kind == MaintenanceKind.TestAndTag || p.Kind == MaintenanceKind.BiomedicalSafety), ct);
        if (overdueSafety) return "Its electrical safety test is overdue – record a passed test-and-tag or biomedical check first.";
        row.Status = EquipmentStatus.InService; row.OutOfServiceReason = null; row.LockedOutBy = null; row.LockedOutAt = null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ReturnToService", "Equipment", id.ToString(), reason: $"{row.AssetNumber}: {Clip(note, 300)}", ct: ct);
        return null;
    }

    public async Task<string?> RetireAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "Enter why it is being retired (sold, scrapped, returned…).";
        var row = await db.Equipment.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return "Equipment not found.";
        row.Status = EquipmentStatus.Retired; row.OutOfServiceReason = Clip(reason, 300);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Retire", "Equipment", id.ToString(), reason: $"{row.AssetNumber}: {row.OutOfServiceReason}", ct: ct);
        return null;
    }

    // ---------------------------------------------------------------- maintenance
    public async Task<IReadOnlyList<MaintenancePlan>> PlansAsync(Guid equipmentId, CancellationToken ct = default) =>
        await db.Plans.AsNoTracking().Where(p => p.EquipmentId == equipmentId).OrderBy(p => p.NextDue).ToListAsync(ct);

    public async Task<IReadOnlyList<MaintenanceRecord>> HistoryAsync(Guid equipmentId, CancellationToken ct = default) =>
        await db.Maintenance.AsNoTracking().Where(p => p.EquipmentId == equipmentId).OrderByDescending(p => p.DoneOn).ThenByDescending(p => p.RecordedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> SavePlanAsync(PlanEdit e, string user, CancellationToken ct = default)
    {
        if (!await db.Equipment.AnyAsync(x => x.Id == e.EquipmentId, ct)) return ["Equipment not found."];
        if (!Enum.IsDefined(e.Kind) || e.Kind == MaintenanceKind.Repair) return ["Choose PPM, calibration, test-and-tag, biomedical safety or validation."];
        if (e.IntervalMonths is < 1 or > 120) return ["The interval must be between 1 and 120 months."];
        var row = await db.Plans.SingleOrDefaultAsync(p => p.EquipmentId == e.EquipmentId && p.Kind == e.Kind, ct);
        if (row is null) { row = new MaintenancePlan { EquipmentId = e.EquipmentId, Kind = e.Kind }; db.Plans.Add(row); }
        row.IntervalMonths = e.IntervalMonths; row.NextDue = e.NextDue; row.DoneBy = Clip(e.DoneBy, 100);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "MaintenancePlan", row.Id.ToString(), reason: $"{e.EquipmentId}: {e.Kind} every {e.IntervalMonths} months, next {e.NextDue:yyyy-MM-dd}", ct: ct);
        return [];
    }

    /// <summary>
    /// Records a check. A pass moves the plan's next due date on by its interval. A failed electrical safety check (test-and-tag or biomedical)
    /// locks the equipment out automatically, as AS/NZS 3760 requires failed equipment to be withdrawn from use.
    /// </summary>
    public async Task<(MaintenanceRecord? Record, IReadOnlyList<string> Errors)> RecordAsync(MaintenanceEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var eq = await db.Equipment.SingleOrDefaultAsync(x => x.Id == e.EquipmentId, ct);
        if (eq is null) return (null, ["Equipment not found."]);
        if (!Enum.IsDefined(e.Kind)) errors.Add("Choose what kind of check this was.");
        if (e.DoneOn > Today) errors.Add("The date cannot be in the future.");
        if (string.IsNullOrWhiteSpace(e.DoneBy)) errors.Add("Enter who did it (person or company).");
        if (e.Kind == MaintenanceKind.TestAndTag && e.Passed && string.IsNullOrWhiteSpace(e.TagNumber)) errors.Add("Enter the test tag number.");
        if (errors.Count > 0) return (null, errors);
        var rec = new MaintenanceRecord
        {
            EquipmentId = eq.Id, Kind = e.Kind, DoneOn = e.DoneOn, DoneBy = Clip(e.DoneBy, 100)!, Passed = e.Passed, TagNumber = Clip(e.TagNumber, 40), Notes = Clip(e.Notes, 1000),
            RecordedBy = user, RecordedAt = clock.GetUtcNow(),
        };
        db.Maintenance.Add(rec);
        if (e.Passed && await db.Plans.SingleOrDefaultAsync(p => p.EquipmentId == eq.Id && p.Kind == e.Kind, ct) is { } plan && e.DoneOn.AddMonths(plan.IntervalMonths) > plan.NextDue)
            plan.NextDue = e.DoneOn.AddMonths(plan.IntervalMonths);
        if (!e.Passed && e.Kind is MaintenanceKind.TestAndTag or MaintenanceKind.BiomedicalSafety or MaintenanceKind.Calibration && eq.Status == EquipmentStatus.InService)
        {
            eq.Status = EquipmentStatus.OutOfService; eq.OutOfServiceReason = $"Failed {Label(e.Kind)} on {e.DoneOn:d MMM yyyy}"; eq.LockedOutBy = user; eq.LockedOutAt = clock.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Maintenance", "Equipment", eq.Id.ToString(), reason: $"{eq.AssetNumber}: {e.Kind} {(e.Passed ? "passed" : "FAILED")} {e.DoneOn:yyyy-MM-dd} by {rec.DoneBy}", ct: ct);
        return (rec, []);
    }

    public static string Label(MaintenanceKind k) => k switch
    {
        MaintenanceKind.Ppm => "planned maintenance", MaintenanceKind.Calibration => "calibration", MaintenanceKind.TestAndTag => "electrical test-and-tag",
        MaintenanceKind.BiomedicalSafety => "biomedical safety check", MaintenanceKind.Validation => "validation", _ => "repair",
    };

    public async Task<IReadOnlyList<DueRow>> DueAsync(int days, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var until = Today.AddDays(days);
        var rows = await (from p in db.Plans.AsNoTracking()
                          join e in db.Equipment.AsNoTracking() on p.EquipmentId equals e.Id
                          where p.NextDue <= until && e.Status != EquipmentStatus.Retired
                          orderby p.NextDue
                          select new { e.Id, e.AssetNumber, e.Name, p.Kind, p.NextDue, p.DoneBy }).ToListAsync(ct);
        return rows.Select(r => new DueRow(r.Id, r.AssetNumber, r.Name, r.Kind.ToString(), r.NextDue, DueState(r.NextDue, s.MaintenanceWarningDays), r.DoneBy)).ToList();
    }

    internal static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length <= max ? s.Trim() : s.Trim()[..max]);
}
