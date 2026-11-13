using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Assets;

public sealed record TraySetEdit(Guid? Id, string Code, string Name, string? Specialty, int ShelfLifeDays, bool IsActive, IReadOnlyList<TrayItemEdit> Items);
public sealed record TrayItemEdit(string Description, int Quantity);
public sealed record CycleEdit(Guid MachineId, CycleKind Kind, string? MachineCycleNumber, string? Program, DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    decimal? MaxTemperatureC, int? HoldSeconds, decimal? PressureKpa, int? A0, bool MachinePassed, IndicatorResult ChemicalIndicator, IndicatorResult BiologicalIndicator,
    bool BowieDickTest, IReadOnlyList<string>? TrayBarcodes, IReadOnlyList<CycleItemEdit>? OtherItems);
public sealed record CycleItemEdit(string Description, int Quantity);
public sealed record ParsedLog(string? MachineCycleNumber, string? Program, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, decimal? MaxTemperatureC, int? HoldSeconds,
    decimal? PressureKpa, int? A0, bool? Passed, IReadOnlyList<string> Notes);
public sealed record CycleRow(Guid Id, string LoadNumber, Guid MachineId, string Machine, string Kind, string? Program, string? MachineCycleNumber, DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt, decimal? MaxTemperatureC, int? HoldSeconds, decimal? PressureKpa, int? A0, bool MachinePassed, string ChemicalIndicator, string BiologicalIndicator,
    bool BowieDickTest, string Release, string? ReleasedBy, DateTimeOffset? ReleasedAt, string? ReleaseNote, string Operator, string Source, int Trays, int OtherItems);
public sealed record TrayRow(Guid Id, string Barcode, Guid TraySetId, string Code, string Name, string Status, string? LastLoad, DateOnly? SterileUntil, string State, string? PackedBy,
    DateTimeOffset? PackedAt, DateTimeOffset UpdatedAt);
public sealed record IssueRow(Guid Id, Guid TrayInstanceId, string Barcode, string Tray, string? LoadNumber, Guid? BookingId, Guid? PatientId, DateTimeOffset IssuedAt, string IssuedBy, DateTimeOffset? ReturnedAt);
public sealed record ScopeRow(Guid Id, string AssetNumber, string Name, string? Model, string? SerialNumber, string Status, string State, string StateText, DateTimeOffset? LastReprocessed,
    DateTimeOffset? LastUsed, string? Equipment);
public sealed record ScopeEventEdit(Guid ScopeId, ScopeEventKind Kind, bool? Passed, Guid? CycleId, Guid? BookingId, Guid? PatientId, string? Notes, DateTimeOffset? At = null);
public sealed record LoanEdit(Guid? Id, Guid? SupplierId, string Description, int ItemCount, Guid? BookingId);
public sealed record NcEdit(Guid? Id, string Category, string Description, Guid? CycleId, Guid? TrayInstanceId, Guid? EquipmentId, string? ImmediateAction,
    string? RootCause, string? CorrectiveAction, string? PreventiveAction);
public sealed record CssdDashboard(int Dirty, int Washed, int Packed, int Sterile, int Issued, int Quarantine, int LoadsAwaitingRelease, int BiPending, int ExpiringSoon,
    int Expired, int ScopesNotReady, int OpenNonconformances, int LoansInHouse);

/// <summary>
/// Sterile services traceability (AS/NZS 5369:2023): tray catalogue and barcoded trays, washer/steriliser/AER cycles (entered or imported from the
/// machine log), parametric release, packing, issue to a theatre case, return, recall by load; endoscope reprocessing; loan sets; nonconformance/CAPA.
/// </summary>
public sealed partial class CssdService(AssetsDbContext db, EquipmentService equipment, AuditWriter audit, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    private static readonly CycleKind[] SterilisingKinds = [CycleKind.Steam, CycleKind.LowTemperature];

    // ---------------------------------------------------------------- catalogue and trays
    public async Task<IReadOnlyList<TraySet>> TraySetsAsync(bool activeOnly, CancellationToken ct = default) =>
        await db.TraySets.AsNoTracking().Include(t => t.Items.OrderBy(i => i.Sort)).Where(t => !activeOnly || t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);

    public async Task<(TraySet? Set, IReadOnlyList<string> Errors)> SaveTraySetAsync(TraySetEdit e, string user, CancellationToken ct = default)
    {
        var code = EquipmentService.Clip(e.Code, 30)?.ToUpperInvariant();
        var name = EquipmentService.Clip(e.Name, 150);
        var errors = new List<string>();
        if (code is null) errors.Add("Enter a short code for the tray (e.g. LAPCHOLE).");
        if (name is null) errors.Add("Enter the tray's name.");
        if (e.ShelfLifeDays is < 1 or > 3650) errors.Add("Storage life must be between 1 and 3650 days.");
        var items = (e.Items ?? []).Where(i => !string.IsNullOrWhiteSpace(i.Description)).ToList();
        if (items.Any(i => i.Quantity is < 1 or > 500)) errors.Add("Each item's quantity must be between 1 and 500.");
        if (code is not null && await db.TraySets.AnyAsync(t => t.Code == code && t.Id != e.Id, ct)) errors.Add($"Tray code {code} is already used.");
        if (errors.Count > 0) return (null, errors);
        TraySet row;
        if (e.Id is { } id) row = await db.TraySets.Include(t => t.Items).SingleAsync(t => t.Id == id, ct);
        else { row = new TraySet(); db.TraySets.Add(row); }
        row.Code = code!; row.Name = name!; row.Specialty = EquipmentService.Clip(e.Specialty, 60); row.ShelfLifeDays = e.ShelfLifeDays; row.IsActive = e.IsActive;
        db.TrayItems.RemoveRange(row.Items);
        row.Items = items.Select((i, n) => new TrayItem { TraySetId = row.Id, Sort = n + 1, Description = EquipmentService.Clip(i.Description, 200)!, Quantity = i.Quantity }).ToList();
        db.TrayItems.AddRange(row.Items);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "TraySet", row.Id.ToString(), reason: $"{row.Code} {row.Name}, {row.Items.Count} items", ct: ct);
        return (row, []);
    }

    /// <summary>Adds physical trays for a catalogue entry, numbered CODE-01, CODE-02… (barcodes for the labels).</summary>
    public async Task<(IReadOnlyList<TrayInstance> Trays, string? Error)> AddTraysAsync(Guid traySetId, int count, string user, CancellationToken ct = default)
    {
        if (count is < 1 or > 50) return ([], "Add between 1 and 50 trays at a time.");
        var set = await db.TraySets.SingleOrDefaultAsync(t => t.Id == traySetId, ct);
        if (set is null) return ([], "Tray not found in the catalogue.");
        var existing = await db.Trays.Where(t => t.TraySetId == traySetId).Select(t => t.Barcode).ToListAsync(ct);
        var n = existing.Count;
        var added = new List<TrayInstance>();
        while (added.Count < count)
        {
            n++;
            var code = $"{set.Code}-{n:00}";
            if (existing.Contains(code) || await db.Trays.AnyAsync(t => t.Barcode == code, ct)) continue;
            var t = new TrayInstance { Barcode = code, TraySetId = set.Id, Status = TrayStatus.Dirty, UpdatedAt = clock.GetUtcNow() };
            db.Trays.Add(t); added.Add(t);
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "Tray", set.Id.ToString(), reason: $"{string.Join(", ", added.Select(a => a.Barcode))}", ct: ct);
        return (added, null);
    }

    public string TrayState(TrayInstance t) =>
        t.Status == TrayStatus.Sterile && t.SterileUntil is { } until ? (until < Today ? "Expired" : until <= Today.AddDays(7) ? "ExpiringSoon" : "Sterile") : t.Status.ToString();

    public async Task<IReadOnlyList<TrayRow>> TraysAsync(string? status, string? q, CancellationToken ct = default)
    {
        var query = db.Trays.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TrayStatus>(status, true, out var st)) query = query.Where(t => t.Status == st);
        if (!string.IsNullOrWhiteSpace(q)) { var x = q.Trim().ToUpperInvariant(); query = query.Where(t => t.Barcode.Contains(x)); }
        var list = await query.OrderBy(t => t.Barcode).Take(1000).ToListAsync(ct);
        var sets = await db.TraySets.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var loadIds = list.Where(t => t.LastCycleId != null).Select(t => t.LastCycleId!.Value).Distinct().ToList();
        var loads = await db.Cycles.AsNoTracking().Where(c => loadIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.LoadNumber, ct);
        return list.Select(t => new TrayRow(t.Id, t.Barcode, t.TraySetId, sets[t.TraySetId].Code, sets[t.TraySetId].Name, t.Status.ToString(),
            t.LastCycleId is { } lc ? loads.GetValueOrDefault(lc) : null, t.SterileUntil, TrayState(t), t.PackedBy, t.PackedAt, t.UpdatedAt)).ToList();
    }

    public async Task<TrayInstance?> FindTrayAsync(string barcode, CancellationToken ct = default)
    {
        var b = barcode.Trim().ToUpperInvariant();
        return await db.Trays.SingleOrDefaultAsync(t => t.Barcode == b, ct);
    }

    /// <summary>Scanned back into the CSSD red zone after use (or found contaminated): the tray is dirty and any open issue is closed.</summary>
    public async Task<string?> ReturnDirtyAsync(string barcode, string user, CancellationToken ct = default)
    {
        var t = await FindTrayAsync(barcode, ct);
        if (t is null) return $"No tray with barcode {barcode}.";
        if (t.Status == TrayStatus.Retired) return $"{t.Barcode} has been retired.";
        var open = await db.Issues.Where(i => i.TrayInstanceId == t.Id && i.ReturnedAt == null).ToListAsync(ct);
        foreach (var i in open) i.ReturnedAt = clock.GetUtcNow();
        t.Status = TrayStatus.Dirty; t.SterileUntil = null; t.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Return", "Tray", t.Id.ToString(), reason: $"{t.Barcode} back to CSSD (dirty)", ct: ct);
        return null;
    }

    /// <summary>Inspection and assembly: a washed tray is checked against its contents list and packed (count sheet confirmed).</summary>
    public async Task<string?> PackAsync(string barcode, bool contentsChecked, string? notes, string user, CancellationToken ct = default)
    {
        var t = await FindTrayAsync(barcode, ct);
        if (t is null) return $"No tray with barcode {barcode}.";
        if (t.Status != TrayStatus.Washed) return $"{t.Barcode} is {t.Status.ToString().ToLowerInvariant()} – only washed trays can be packed. Wash it first.";
        if (!contentsChecked) return "Confirm every item on the contents list is present, clean, and working before packing.";
        t.Status = TrayStatus.Packed; t.PackedBy = user; t.PackedAt = clock.GetUtcNow(); t.Notes = EquipmentService.Clip(notes, 500); t.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Pack", "Tray", t.Id.ToString(), reason: $"{t.Barcode} packed{(notes is null ? "" : ": " + notes)}", ct: ct);
        return null;
    }

    // ---------------------------------------------------------------- cycles
    private async Task<string> NextLoadNumberAsync(CycleKind kind, CancellationToken ct)
    {
        var s = await equipment.SettingsAsync(ct);
        var prefix = kind switch { CycleKind.Wash => "W", CycleKind.Aer => "A", CycleKind.LowTemperature => "L", _ => "S" };
        var number = $"{prefix}{Today:yyMMdd}-{s.NextLoad:000}";
        s.NextLoad++;
        return number;
    }

    public async Task<(Cycle? Cycle, IReadOnlyList<string> Errors)> CreateCycleAsync(CycleEdit e, string source, string? rawLog, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var machine = await db.Equipment.SingleOrDefaultAsync(x => x.Id == e.MachineId, ct);
        if (machine is null) errors.Add("Choose the machine.");
        else
        {
            var fits = e.Kind switch
            {
                CycleKind.Wash => machine.Category == EquipmentCategory.Washer, CycleKind.Aer => machine.Category == EquipmentCategory.Aer,
                _ => machine.Category == EquipmentCategory.Steriliser,
            };
            if (!fits) errors.Add($"{machine.Name} is not a {(e.Kind == CycleKind.Wash ? "washer-disinfector" : e.Kind == CycleKind.Aer ? "endoscope reprocessor (AER)" : "steriliser")}.");
            if (machine.Status != EquipmentStatus.InService) errors.Add($"{machine.Name} is out of service ({machine.OutOfServiceReason}) – it cannot be used.");
        }
        if (e.EndedAt is { } end && end < e.StartedAt) errors.Add("The cycle cannot end before it started.");
        if (e.StartedAt > clock.GetUtcNow().AddMinutes(10)) errors.Add("The start time is in the future.");
        var barcodes = (e.TrayBarcodes ?? []).Select(b => b.Trim().ToUpperInvariant()).Where(b => b.Length > 0).Distinct().ToList();
        var trays = await db.Trays.Where(t => barcodes.Contains(t.Barcode)).ToListAsync(ct);
        foreach (var b in barcodes.Where(b => trays.All(t => t.Barcode != b))) errors.Add($"No tray with barcode {b}.");
        foreach (var t in trays)
        {
            if (e.Kind == CycleKind.Wash && t.Status is not (TrayStatus.Dirty or TrayStatus.Quarantine)) errors.Add($"{t.Barcode} is {t.Status.ToString().ToLowerInvariant()}, not dirty – it does not need washing.");
            if (SterilisingKinds.Contains(e.Kind) && t.Status != TrayStatus.Packed) errors.Add($"{t.Barcode} is {t.Status.ToString().ToLowerInvariant()} – only packed trays go in the steriliser.");
        }
        var others = (e.OtherItems ?? []).Where(o => !string.IsNullOrWhiteSpace(o.Description)).ToList();
        if (others.Any(o => o.Quantity is < 1 or > 500)) errors.Add("Each item's quantity must be between 1 and 500.");
        if (e.Kind != CycleKind.Aer && barcodes.Count == 0 && others.Count == 0) errors.Add("Scan the trays in the load, or list the other items (steri packs, devices).");
        if (errors.Count > 0) return (null, errors);
        var c = new Cycle
        {
            LoadNumber = await NextLoadNumberAsync(e.Kind, ct), MachineId = machine!.Id, Kind = e.Kind, MachineCycleNumber = EquipmentService.Clip(e.MachineCycleNumber, 40),
            Program = EquipmentService.Clip(e.Program, 80), StartedAt = e.StartedAt, EndedAt = e.EndedAt, MaxTemperatureC = e.MaxTemperatureC, HoldSeconds = e.HoldSeconds,
            PressureKpa = e.PressureKpa, A0 = e.A0, MachinePassed = e.MachinePassed, ChemicalIndicator = e.ChemicalIndicator, BiologicalIndicator = e.BiologicalIndicator,
            BowieDickTest = e.BowieDickTest, Operator = user, Source = EquipmentService.Clip(source, 200) ?? "Manual", RawLog = rawLog is null ? null : rawLog.Length > 200_000 ? rawLog[..200_000] : rawLog,
        };
        c.Items = trays.Select(t => new CycleItem { CycleId = c.Id, TrayInstanceId = t.Id }).Concat(others.Select(o => new CycleItem { CycleId = c.Id, Description = EquipmentService.Clip(o.Description, 200), Quantity = o.Quantity })).ToList();
        db.Cycles.Add(c);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "Cycle", c.Id.ToString(), reason: $"{c.LoadNumber} {machine.Name} {c.Kind} {(c.MachinePassed ? "machine OK" : "machine FAULT")}, {trays.Count} trays, {others.Count} other", ct: ct);
        return (c, []);
    }

    /// <summary>
    /// Reads a washer-disinfector or steriliser cycle log (Belimed and similar text/CSV exports, or a printout typed/scanned to text). It is tolerant:
    /// it looks for labelled values (cycle/batch number, program, start, end, temperature, holding time, pressure, A0, result) and lists anything it could not
    /// read. The exact Belimed WD 290 IQ export layout must be confirmed with a real log file before relying on it.
    /// </summary>
    public static ParsedLog ParseMachineLog(string text)
    {
        var notes = new List<string>();
        string? cycle = null, program = null; DateTimeOffset? start = null, end = null; decimal? temp = null, pressure = null; int? hold = null, a0 = null; bool? passed = null;
        var temps = new List<decimal>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var m = KeyValue().Match(line);
            var key = m.Success ? m.Groups[1].Value.Trim().ToLowerInvariant() : "";
            var val = m.Success ? m.Groups[2].Value.Trim() : line;
            if (key.Length > 0)
            {
                if (cycle is null && Regex.IsMatch(key, @"^(cycle|charge|batch|load)\s*(no|nr|number|#|counter)?\.?$")) cycle = FirstToken(val);
                else if (program is null && Regex.IsMatch(key, @"^(program|programme|prog)(\s*name)?$")) program = val;
                else if (start is null && Regex.IsMatch(key, @"^(start|begin|started|start time|cycle start)")) start = Time(val);
                else if (end is null && Regex.IsMatch(key, @"^(end|finish|ended|end time|cycle end|stop)")) end = Time(val);
                else if (Regex.IsMatch(key, @"\ba0\b")) a0 = Int(val);
                else if (Regex.IsMatch(key, @"(hold|holding|exposure|plateau)")) hold = Seconds(val);
                else if (Regex.IsMatch(key, @"(pressure|kpa|bar)")) pressure = Pressure(val, key);
                else if (Regex.IsMatch(key, @"(temp|°c)")) { if (Dec(val) is { } t) temps.Add(t); }
                else if (Regex.IsMatch(key, @"^(result|status|cycle status|cycle result|outcome)$")) passed = PassWord(val);
            }
            // Any line (labelled or not) that reports a fault fails the cycle; a free-text "cycle OK" passes it if nothing else said.
            if (Regex.IsMatch(line, @"\b(fault|error|aborted|abort|cycle fail(ed)?|not ok)\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(line, @"\bno (faults?|errors?)\b", RegexOptions.IgnoreCase)) passed = false;
            else if (key.Length == 0 && Regex.IsMatch(line, @"\b(cycle (ok|complete|completed|passed)|process (ok|completed)|no faults?)\b", RegexOptions.IgnoreCase)) passed ??= true;
            if (key.Length == 0) foreach (Match tm in Regex.Matches(line, @"(\d{2,3}(?:[.,]\d)?)\s*°\s*C")) if (Dec(tm.Groups[1].Value) is { } t) temps.Add(t);
        }
        if (temps.Count > 0) temp = temps.Max();
        if (cycle is null) notes.Add("Cycle/batch number not found.");
        if (start is null) notes.Add("Start time not found – enter it.");
        if (passed is null) notes.Add("No pass/fault result found – check the printout.");
        if (temp is null) notes.Add("No temperature found.");
        return new ParsedLog(cycle, program, start, end, temp, hold, pressure, a0, passed, notes);
    }

    [GeneratedRegex(@"^([A-Za-z0-9°#.\s/()_-]{1,40}?)\s*[:=;\t]\s*(.+)$")]
    private static partial Regex KeyValue();

    private static string FirstToken(string v) => v.Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? v;
    private static decimal? Dec(string v) { var m = Regex.Match(v.Replace(',', '.'), @"-?\d+(\.\d+)?"); return m.Success && decimal.TryParse(m.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null; }
    private static int? Int(string v) => Dec(v) is { } d ? (int)Math.Round(d) : null;
    private static int? Seconds(string v)
    {
        var mmss = Regex.Match(v, @"(\d+):(\d{2})");
        if (mmss.Success) return int.Parse(mmss.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(mmss.Groups[2].Value, CultureInfo.InvariantCulture);
        var d = Dec(v); if (d is null) return null;
        return Regex.IsMatch(v, @"\bmin", RegexOptions.IgnoreCase) ? (int)(d * 60) : (int)d;
    }
    private static decimal? Pressure(string v, string key) { var d = Dec(v); if (d is null) return null; return Regex.IsMatch(v + " " + key, @"\bbar\b", RegexOptions.IgnoreCase) ? d * 100 : d; }
    private static bool? PassWord(string v) =>
        Regex.IsMatch(v, @"\b(ok|pass|passed|complete|completed|good|success)\b", RegexOptions.IgnoreCase) ? true
        : Regex.IsMatch(v, @"\b(fail|failed|fault|error|abort|aborted|not ok)\b", RegexOptions.IgnoreCase) ? false : null;
    private static DateTimeOffset? Time(string v)
    {
        string[] formats = ["dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "d/M/yyyy H:mm", "dd-MM-yyyy HH:mm:ss"];
        var t = v.Trim();
        if (DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)) return new DateTimeOffset(dt);
        return DateTime.TryParse(t, CultureInfo.GetCultureInfo("en-NZ"), DateTimeStyles.AssumeLocal, out dt) ? new DateTimeOffset(dt) : null;
    }

    public async Task<IReadOnlyList<CycleRow>> CyclesAsync(string? view, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var q = db.Cycles.AsNoTracking().AsQueryable();
        if (view == "pending") q = q.Where(c => c.Release == CycleRelease.Pending);
        if (from is { } f) { var fs = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), clock.GetLocalNow().Offset); q = q.Where(c => c.StartedAt >= fs); }
        if (to is { } t) { var ts = new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), clock.GetLocalNow().Offset); q = q.Where(c => c.StartedAt < ts); }
        var list = await q.OrderByDescending(c => c.StartedAt).Take(500).Include(c => c.Items).ToListAsync(ct);
        var machines = await db.Equipment.AsNoTracking().Where(e => e.Category == EquipmentCategory.Washer || e.Category == EquipmentCategory.Steriliser || e.Category == EquipmentCategory.Aer)
            .ToDictionaryAsync(e => e.Id, e => e.Name, ct);
        return list.Select(c => ToRow(c, machines.GetValueOrDefault(c.MachineId) ?? "?")).ToList();
    }

    private static CycleRow ToRow(Cycle c, string machine) => new(c.Id, c.LoadNumber, c.MachineId, machine, c.Kind.ToString(), c.Program, c.MachineCycleNumber, c.StartedAt, c.EndedAt,
        c.MaxTemperatureC, c.HoldSeconds, c.PressureKpa, c.A0, c.MachinePassed, c.ChemicalIndicator.ToString(), c.BiologicalIndicator.ToString(), c.BowieDickTest, c.Release.ToString(),
        c.ReleasedBy, c.ReleasedAt, c.ReleaseNote, c.Operator, c.Source, c.Items.Count(i => i.TrayInstanceId != null), c.Items.Where(i => i.TrayInstanceId == null).Sum(i => i.Quantity));

    public async Task<Cycle?> CycleAsync(Guid id, CancellationToken ct = default) => await db.Cycles.AsNoTracking().Include(c => c.Items).SingleOrDefaultAsync(c => c.Id == id, ct);

    /// <summary>Records a late result (e.g. the biological indicator read after incubation, or the chemical indicator checked at unloading).</summary>
    public async Task<string?> UpdateIndicatorsAsync(Guid id, IndicatorResult chemical, IndicatorResult biological, string user, CancellationToken ct = default)
    {
        var c = await db.Cycles.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "Load not found.";
        var before = $"CI {c.ChemicalIndicator}, BI {c.BiologicalIndicator}";
        c.ChemicalIndicator = chemical; c.BiologicalIndicator = biological;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "Cycle", c.Id.ToString(), reason: $"{c.LoadNumber}: {before} → CI {chemical}, BI {biological}", ct: ct);
        if (c.Release == CycleRelease.Released && (chemical == IndicatorResult.Fail || biological == IndicatorResult.Fail))
            return $"Recorded. WARNING: load {c.LoadNumber} was already released and now has a failed indicator – start a recall (Trace › load {c.LoadNumber}) and raise a nonconformance.";
        return null;
    }

    /// <summary>
    /// Parametric release: a load is released only when the machine reported a good cycle and no indicator failed (a biological indicator that is
    /// used must have passed). Washer loads make their trays "washed"; steriliser loads make them "sterile" until the storage date. Failing a load
    /// quarantines its trays and raises a nonconformance.
    /// </summary>
    public async Task<(bool Ok, string Message)> ReleaseAsync(Guid id, bool release, string? note, string user, CancellationToken ct = default)
    {
        var c = await db.Cycles.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return (false, "Load not found.");
        if (c.Release != CycleRelease.Pending) return (false, $"Load {c.LoadNumber} has already been {c.Release.ToString().ToLowerInvariant()}.");
        var s = await equipment.SettingsAsync(ct);
        if (release)
        {
            var problems = new List<string>();
            if (!c.MachinePassed) problems.Add("the machine did not report a good cycle");
            if (c.ChemicalIndicator == IndicatorResult.Fail) problems.Add("the chemical indicator failed");
            if (c.ChemicalIndicator == IndicatorResult.Pending) problems.Add("the chemical indicator has not been checked");
            if (c.BiologicalIndicator is IndicatorResult.Pending or IndicatorResult.Fail) problems.Add(c.BiologicalIndicator == IndicatorResult.Fail ? "the biological indicator failed" : "the biological indicator result is not in yet");
            if (SterilisingKinds.Contains(c.Kind) && c.MaxTemperatureC is null) problems.Add("no sterilising temperature is recorded");
            if (s.ReleaseNeedsSecondPerson && string.Equals(c.Operator, user, StringComparison.OrdinalIgnoreCase)) problems.Add("a second person (not the operator) must release loads");
            if (problems.Count > 0) return (false, $"Load {c.LoadNumber} cannot be released: {string.Join("; ", problems)}.");
        }
        else if (string.IsNullOrWhiteSpace(note)) return (false, "Enter why the load failed.");
        c.Release = release ? CycleRelease.Released : CycleRelease.Failed; c.ReleasedBy = user; c.ReleasedAt = clock.GetUtcNow(); c.ReleaseNote = EquipmentService.Clip(note, 500);
        var trayIds = c.Items.Where(i => i.TrayInstanceId != null).Select(i => i.TrayInstanceId!.Value).ToList();
        var trays = await db.Trays.Where(t => trayIds.Contains(t.Id)).ToListAsync(ct);
        var sets = await db.TraySets.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        foreach (var t in trays)
        {
            t.UpdatedAt = clock.GetUtcNow();
            if (!release) { t.Status = TrayStatus.Quarantine; t.SterileUntil = null; continue; }
            if (c.Kind == CycleKind.Wash) t.Status = TrayStatus.Washed;
            else if (SterilisingKinds.Contains(c.Kind)) { t.Status = TrayStatus.Sterile; t.LastCycleId = c.Id; t.SterileUntil = Today.AddDays(sets[t.TraySetId].ShelfLifeDays); }
        }
        string? nc = null;
        if (!release)
        {
            var (row, _) = await RaiseAsync(new NcEdit(null, "Failed cycle", $"Load {c.LoadNumber} failed: {note}", c.Id, null, c.MachineId, $"{trays.Count} tray(s) quarantined.", null, null, null), user, ct, save: false);
            nc = row?.Number;
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, release ? "Release" : "Fail", "Cycle", c.Id.ToString(), reason: $"{c.LoadNumber}: {(release ? "released" : "FAILED " + note)}; {trays.Count} trays", ct: ct);
        return (true, release ? $"Load {c.LoadNumber} released – {trays.Count} tray(s) {(c.Kind == CycleKind.Wash ? "washed, ready to pack" : c.Kind == CycleKind.Aer ? "reprocessed" : "sterile")}."
            : $"Load {c.LoadNumber} failed – {trays.Count} tray(s) quarantined; nonconformance {nc} raised.");
    }

    // ---------------------------------------------------------------- issue to case and recall
    public async Task<(IssueRow? Issue, string? Error)> IssueAsync(string barcode, Guid? bookingId, Guid? patientId, string user, CancellationToken ct = default)
    {
        var t = await FindTrayAsync(barcode, ct);
        if (t is null) return (null, $"No tray with barcode {barcode}.");
        if (t.Status != TrayStatus.Sterile) return (null, $"{t.Barcode} is {t.Status.ToString().ToLowerInvariant()} – only sterile trays can be issued.");
        if (t.SterileUntil is { } until && until < Today) return (null, $"{t.Barcode} passed its storage date ({until:d MMM yyyy}) – reprocess it.");
        if (t.LastCycleId is not { } cycleId || !await db.Cycles.AnyAsync(c => c.Id == cycleId && c.Release == CycleRelease.Released, ct)) return (null, $"{t.Barcode}'s steriliser load has not been released.");
        if (bookingId is null && patientId is null) return (null, "Choose the case (patient and booking) the tray is for.");
        var issue = new TrayIssue { TrayInstanceId = t.Id, CycleId = t.LastCycleId, BookingId = bookingId, PatientId = patientId, IssuedAt = clock.GetUtcNow(), IssuedBy = user };
        db.Issues.Add(issue);
        t.Status = TrayStatus.Issued; t.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Issue", "Tray", t.Id.ToString(), patientId?.ToString(), reason: $"{t.Barcode} to booking {bookingId}", ct: ct);
        var set = await db.TraySets.AsNoTracking().SingleAsync(s => s.Id == t.TraySetId, ct);
        var load = await db.Cycles.AsNoTracking().Where(c => c.Id == issue.CycleId).Select(c => c.LoadNumber).SingleOrDefaultAsync(ct);
        return (new IssueRow(issue.Id, t.Id, t.Barcode, set.Name, load, bookingId, patientId, issue.IssuedAt, user, null), null);
    }

    public async Task<IReadOnlyList<IssueRow>> IssuesAsync(Guid? cycleId, Guid? bookingId, Guid? patientId, CancellationToken ct = default)
    {
        var q = db.Issues.AsNoTracking().AsQueryable();
        if (cycleId is { } c) q = q.Where(i => i.CycleId == c);
        if (bookingId is { } b) q = q.Where(i => i.BookingId == b);
        if (patientId is { } p) q = q.Where(i => i.PatientId == p);
        var list = await q.OrderByDescending(i => i.IssuedAt).Take(1000).ToListAsync(ct);
        var trays = await db.Trays.AsNoTracking().Where(t => list.Select(i => i.TrayInstanceId).Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var sets = await db.TraySets.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var loads = await db.Cycles.AsNoTracking().Where(x => list.Select(i => i.CycleId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.LoadNumber, ct);
        return list.Select(i => new IssueRow(i.Id, i.TrayInstanceId, trays[i.TrayInstanceId].Barcode, sets[trays[i.TrayInstanceId].TraySetId], i.CycleId is { } lc ? loads.GetValueOrDefault(lc) : null,
            i.BookingId, i.PatientId, i.IssuedAt, i.IssuedBy, i.ReturnedAt)).ToList();
    }

    /// <summary>Recall by load: every tray from the load still in store is quarantined; the cases it was used on are listed for follow-up.</summary>
    public async Task<(int Quarantined, IReadOnlyList<IssueRow> Used, string? Nc)> RecallAsync(Guid cycleId, string reason, string user, CancellationToken ct = default)
    {
        var c = await db.Cycles.SingleOrDefaultAsync(x => x.Id == cycleId, ct) ?? throw new InvalidOperationException("Load not found.");
        var inStore = await db.Trays.Where(t => t.LastCycleId == cycleId && t.Status == TrayStatus.Sterile).ToListAsync(ct);
        foreach (var t in inStore) { t.Status = TrayStatus.Quarantine; t.SterileUntil = null; t.UpdatedAt = clock.GetUtcNow(); }
        var (nc, _) = await RaiseAsync(new NcEdit(null, "Recall", $"Recall of load {c.LoadNumber}: {reason}", c.Id, null, c.MachineId, $"{inStore.Count} tray(s) in store quarantined.", null, null, null), user, ct, save: false);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Recall", "Cycle", c.Id.ToString(), reason: $"{c.LoadNumber}: {reason}; {inStore.Count} quarantined", ct: ct);
        return (inStore.Count, await IssuesAsync(cycleId, null, null, ct), nc?.Number);
    }

    // ---------------------------------------------------------------- endoscopes
    /// <summary>
    /// Scope state from its history: Ready (reprocessed after its last use, leak test passed, within the storage time), NeedsReprocessing, StorageExpired,
    /// Quarantined (failed leak test / microbiology / repair) or OutOfService.
    /// </summary>
    public async Task<IReadOnlyList<ScopeRow>> ScopesAsync(CancellationToken ct = default)
    {
        var s = await equipment.SettingsAsync(ct);
        var scopes = await db.Equipment.AsNoTracking().Where(e => e.Category == EquipmentCategory.Endoscope && e.Status != EquipmentStatus.Retired).OrderBy(e => e.AssetNumber).ToListAsync(ct);
        var ids = scopes.Select(x => x.Id).ToList();
        var events = await db.ScopeEvents.AsNoTracking().Where(e => ids.Contains(e.ScopeId)).OrderBy(e => e.At).ToListAsync(ct);
        return scopes.Select(sc =>
        {
            var (state, text, lastRe, lastUse) = ScopeState(sc, events.Where(e => e.ScopeId == sc.Id).ToList(), s.ScopeStorageHours);
            return new ScopeRow(sc.Id, sc.AssetNumber, sc.Name, sc.Model, sc.SerialNumber, sc.Status.ToString(), state, text, lastRe, lastUse, sc.Make);
        }).ToList();
    }

    public (string State, string Text, DateTimeOffset? LastReprocessed, DateTimeOffset? LastUsed) ScopeState(Equipment scope, IReadOnlyList<ScopeEvent> history, int storageHours)
    {
        var lastUse = history.LastOrDefault(e => e.Kind == ScopeEventKind.Use)?.At;
        var lastAer = history.LastOrDefault(e => e.Kind == ScopeEventKind.AerCycle && e.Passed == true)?.At;
        if (scope.Status == EquipmentStatus.OutOfService) return ("OutOfService", $"Out of service: {scope.OutOfServiceReason}", lastAer, lastUse);
        var lastBad = history.LastOrDefault(e => (e.Kind is ScopeEventKind.LeakTest or ScopeEventKind.MicroSample && e.Passed == false) || e.Kind is ScopeEventKind.Quarantine or ScopeEventKind.Repair);
        if (lastBad is not null && (lastAer is null || lastAer < lastBad.At))
            return ("Quarantined", lastBad.Kind switch
            {
                ScopeEventKind.LeakTest => "Failed leak test – send for repair", ScopeEventKind.MicroSample => "Microbiology sample failed – quarantined",
                ScopeEventKind.Repair => "Back from repair – leak test and reprocess before use", _ => $"Quarantined: {lastBad.Notes}",
            }, lastAer, lastUse);
        if (lastAer is null || (lastUse is { } u && u >= lastAer)) return ("NeedsReprocessing", "Used – needs leak test, manual clean and AER", lastAer, lastUse);
        var leakSinceUse = history.Any(e => e.Kind == ScopeEventKind.LeakTest && e.Passed == true && (lastUse is null || e.At > lastUse) && e.At <= lastAer);
        if (!leakSinceUse) return ("NeedsReprocessing", "No passed leak test recorded before the AER cycle", lastAer, lastUse);
        var hours = (clock.GetUtcNow() - lastAer.Value).TotalHours;
        if (hours > storageHours) return ("StorageExpired", $"Stored {hours:0} h (limit {storageHours} h) – reprocess before use", lastAer, lastUse);
        return ("Ready", $"Ready – reprocessed {lastAer.Value.ToLocalTime():d MMM HH:mm}", lastAer, lastUse);
    }

    public async Task<IReadOnlyList<ScopeEvent>> ScopeHistoryAsync(Guid scopeId, CancellationToken ct = default) =>
        await db.ScopeEvents.AsNoTracking().Where(e => e.ScopeId == scopeId).OrderByDescending(e => e.At).Take(500).ToListAsync(ct);

    public async Task<(ScopeEvent? Event, string? Error, string? Warning)> RecordScopeEventAsync(ScopeEventEdit e, string user, CancellationToken ct = default)
    {
        var scope = await db.Equipment.SingleOrDefaultAsync(x => x.Id == e.ScopeId && x.Category == EquipmentCategory.Endoscope, ct);
        if (scope is null) return (null, "Endoscope not found.", null);
        if (!Enum.IsDefined(e.Kind)) return (null, "Choose what happened.", null);
        if (e.Kind is ScopeEventKind.LeakTest or ScopeEventKind.MicroSample or ScopeEventKind.AerCycle && e.Passed is null) return (null, "Record whether it passed.", null);
        string? warning = null;
        if (e.Kind == ScopeEventKind.Use)
        {
            if (e.PatientId is null && e.BookingId is null) return (null, "Choose the patient/case the scope was used on.", null);
            var s = await equipment.SettingsAsync(ct);
            var (state, text, _, _) = ScopeState(scope, await db.ScopeEvents.AsNoTracking().Where(x => x.ScopeId == scope.Id).OrderBy(x => x.At).ToListAsync(ct), s.ScopeStorageHours);
            if (state != "Ready") return (null, $"{scope.Name} ({scope.AssetNumber}) is not ready: {text}.", null);
        }
        if (e.CycleId is { } cid && !await db.Cycles.AnyAsync(c => c.Id == cid && c.Kind == CycleKind.Aer, ct)) return (null, "That AER cycle was not found.", null);
        var ev = new ScopeEvent { ScopeId = scope.Id, Kind = e.Kind, At = e.At ?? clock.GetUtcNow(), By = user, Passed = e.Passed, CycleId = e.CycleId, BookingId = e.BookingId, PatientId = e.PatientId, Notes = EquipmentService.Clip(e.Notes, 500) };
        if (ev.At > clock.GetUtcNow().AddMinutes(5)) return (null, "The time cannot be in the future.", null);
        db.ScopeEvents.Add(ev);
        if (e.Passed == false && e.Kind is ScopeEventKind.LeakTest or ScopeEventKind.MicroSample)
        {
            await RaiseAsync(new NcEdit(null, e.Kind == ScopeEventKind.LeakTest ? "Endoscope leak test failed" : "Endoscope microbiology failed",
                $"{scope.Name} ({scope.AssetNumber}, serial {scope.SerialNumber}): {e.Notes}", null, null, scope.Id, "Scope quarantined.", null, null, null), user, ct, save: false);
            warning = "Scope quarantined and a nonconformance raised." + (e.Kind == ScopeEventKind.MicroSample ? " Check the patients it was used on since its last good sample (Trace)." : "");
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ScopeEvent", "Equipment", scope.Id.ToString(), e.PatientId?.ToString(), reason: $"{scope.AssetNumber}: {e.Kind}{(e.Passed is null ? "" : e.Passed.Value ? " pass" : " FAIL")}", ct: ct);
        return (ev, null, warning);
    }

    // ---------------------------------------------------------------- loan sets
    public async Task<IReadOnlyList<LoanSet>> LoansAsync(bool openOnly, CancellationToken ct = default) =>
        await db.Loans.AsNoTracking().Where(l => !openOnly || l.Status != LoanStatus.Returned).OrderByDescending(l => l.UpdatedAt).Take(500).ToListAsync(ct);

    public async Task<(LoanSet? Loan, IReadOnlyList<string> Errors)> SaveLoanAsync(LoanEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.Description)) errors.Add("Describe the loan set (supplier's set name).");
        if (e.ItemCount is < 0 or > 1000) errors.Add("The item count must be between 0 and 1000.");
        if (e.SupplierId is { } sid && !await db.Suppliers.AnyAsync(s => s.Id == sid, ct)) errors.Add("Choose a supplier from the list.");
        if (errors.Count > 0) return (null, errors);
        LoanSet row;
        if (e.Id is { } id) row = await db.Loans.SingleAsync(l => l.Id == id, ct);
        else
        {
            var s = await equipment.SettingsAsync(ct);
            row = new LoanSet { Number = $"LN{Today:yy}-{s.NextLoan:0000}" }; s.NextLoan++; db.Loans.Add(row);
        }
        row.SupplierId = e.SupplierId; row.Description = EquipmentService.Clip(e.Description, 300)!; row.ItemCount = e.ItemCount; row.BookingId = e.BookingId; row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "LoanSet", row.Id.ToString(), reason: $"{row.Number} {row.Description}", ct: ct);
        return (row, []);
    }

    /// <summary>Loan workflow: Expected → Received (checked in against the supplier's list) → Checked → Reprocessed (in a released load) → Used → Returned (reprocessed before return).</summary>
    public async Task<string?> AdvanceLoanAsync(Guid id, LoanStatus to, string? notes, Guid? cycleId, string user, CancellationToken ct = default)
    {
        var l = await db.Loans.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return "Loan set not found.";
        var allowed = l.Status switch
        {
            LoanStatus.Expected => [LoanStatus.Received], LoanStatus.Received => [LoanStatus.Checked], LoanStatus.Checked => [LoanStatus.Reprocessed],
            LoanStatus.Reprocessed => [LoanStatus.Used, LoanStatus.Returned], LoanStatus.Used => new[] { LoanStatus.Reprocessed, LoanStatus.Returned }, _ => [],
        };
        if (!allowed.Contains(to)) return $"A loan set that is {l.Status.ToString().ToLowerInvariant()} cannot go to {to.ToString().ToLowerInvariant()}.";
        if (to == LoanStatus.Checked && string.IsNullOrWhiteSpace(notes)) return "Record the check (items counted against the supplier's list, condition).";
        if (to == LoanStatus.Reprocessed)
        {
            if (cycleId is not { } cid || !await db.Cycles.AnyAsync(c => c.Id == cid && c.Release == CycleRelease.Released && (c.Kind == CycleKind.Steam || c.Kind == CycleKind.LowTemperature), ct))
                return "Choose the released steriliser load it was processed in.";
            l.CycleId = cid;
        }
        if (to == LoanStatus.Returned && l.Status == LoanStatus.Used) return "Reprocess the set after use before returning it to the supplier.";
        if (to == LoanStatus.Received) l.ReceivedAt = clock.GetUtcNow();
        if (to == LoanStatus.Checked) { l.CheckedBy = user; l.CheckNotes = EquipmentService.Clip(notes, 500); }
        if (to == LoanStatus.Returned) l.ReturnedAt = clock.GetUtcNow();
        l.Status = to; l.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "LoanSet", l.Id.ToString(), reason: $"{l.Number} → {to}{(notes is null ? "" : ": " + notes)}", ct: ct);
        return null;
    }

    // ---------------------------------------------------------------- nonconformance / CAPA
    public async Task<IReadOnlyList<Nonconformance>> NonconformancesAsync(bool openOnly, CancellationToken ct = default) =>
        await db.Nonconformances.AsNoTracking().Where(n => !openOnly || n.Status != NcStatus.Closed).OrderByDescending(n => n.RaisedOn).ThenByDescending(n => n.Number).Take(500).ToListAsync(ct);

    public async Task<(Nonconformance? Nc, IReadOnlyList<string> Errors)> RaiseAsync(NcEdit e, string user, CancellationToken ct = default, bool save = true)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.Category)) errors.Add("Choose the category.");
        if (string.IsNullOrWhiteSpace(e.Description)) errors.Add("Describe what went wrong.");
        if (errors.Count > 0) return (null, errors);
        Nonconformance row;
        if (e.Id is { } id)
        {
            row = await db.Nonconformances.SingleAsync(n => n.Id == id, ct);
            if (row.Status == NcStatus.Closed) return (null, ["This nonconformance is closed."]);
        }
        else
        {
            var s = await equipment.SettingsAsync(ct);
            row = new Nonconformance { Number = $"NC{Today:yy}-{s.NextNc:000}", RaisedOn = Today, RaisedBy = user }; s.NextNc++; db.Nonconformances.Add(row);
        }
        row.Category = EquipmentService.Clip(e.Category, 60)!; row.Description = EquipmentService.Clip(e.Description, 2000)!; row.CycleId = e.CycleId; row.TrayInstanceId = e.TrayInstanceId;
        row.EquipmentId = e.EquipmentId; row.ImmediateAction = EquipmentService.Clip(e.ImmediateAction, 1000); row.RootCause = EquipmentService.Clip(e.RootCause, 1000);
        row.CorrectiveAction = EquipmentService.Clip(e.CorrectiveAction, 1000); row.PreventiveAction = EquipmentService.Clip(e.PreventiveAction, 1000);
        if (row.Status == NcStatus.Open && row.CorrectiveAction is not null && row.RootCause is not null) row.Status = NcStatus.ActionsAgreed;
        if (save)
        {
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "Nonconformance", row.Id.ToString(), reason: $"{row.Number} {row.Category}", ct: ct);
        }
        return (row, []);
    }

    public async Task<string?> CloseNcAsync(Guid id, string user, CancellationToken ct = default)
    {
        var n = await db.Nonconformances.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (n is null) return "Nonconformance not found.";
        if (n.Status == NcStatus.Closed) return "Already closed.";
        if (n.RootCause is null || n.CorrectiveAction is null) return "Record the root cause and the corrective action before closing.";
        n.Status = NcStatus.Closed; n.ClosedBy = user; n.ClosedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Close", "Nonconformance", n.Id.ToString(), reason: n.Number, ct: ct);
        return null;
    }

    // ---------------------------------------------------------------- dashboard and labels
    public async Task<CssdDashboard> DashboardAsync(CancellationToken ct = default)
    {
        var trays = await db.Trays.AsNoTracking().Where(t => t.Status != TrayStatus.Retired).ToListAsync(ct);
        int Count(TrayStatus s) => trays.Count(t => t.Status == s);
        var scopes = await ScopesAsync(ct);
        return new CssdDashboard(Count(TrayStatus.Dirty), Count(TrayStatus.Washed), Count(TrayStatus.Packed), Count(TrayStatus.Sterile), Count(TrayStatus.Issued), Count(TrayStatus.Quarantine),
            await db.Cycles.CountAsync(c => c.Release == CycleRelease.Pending, ct), await db.Cycles.CountAsync(c => c.BiologicalIndicator == IndicatorResult.Pending, ct),
            trays.Count(t => TrayState(t) == "ExpiringSoon"), trays.Count(t => TrayState(t) == "Expired"),
            scopes.Count(s => s.State != "Ready"), await db.Nonconformances.CountAsync(n => n.Status != NcStatus.Closed, ct),
            await db.Loans.CountAsync(l => l.Status != LoanStatus.Returned && l.Status != LoanStatus.Expected, ct));
    }

    /// <summary>Tray label for a Zebra printer (autoclave-resistant stock): tray name, barcode, load, sterilised and use-by dates, packer.</summary>
    public async Task<string?> TrayLabelZplAsync(Guid trayId, int dpi, int widthMm, int lengthMm, CancellationToken ct = default)
    {
        var t = await db.Trays.AsNoTracking().SingleOrDefaultAsync(x => x.Id == trayId, ct);
        if (t is null) return null;
        var set = await db.TraySets.AsNoTracking().SingleAsync(s => s.Id == t.TraySetId, ct);
        var load = t.LastCycleId is { } lc ? await db.Cycles.AsNoTracking().Where(c => c.Id == lc).Select(c => new { c.LoadNumber, c.StartedAt }).SingleOrDefaultAsync(ct) : null;
        int D(int mm) => (int)Math.Round(mm * dpi / 25.4);
        int F(int pt) => (int)Math.Round(pt * dpi / 72.0);
        static string Z(string s) => s.Replace("^", " ").Replace("~", " ");
        var w = D(widthMm); var m = D(2);
        var sb = new StringBuilder("^XA^CI28");
        sb.Append(CultureInfo.InvariantCulture, $"^PW{w}^LL{D(lengthMm)}");
        sb.Append(CultureInfo.InvariantCulture, $"^FO{m},{m}^A0N,{F(10)},{F(10)}^FB{w - 2 * m},1,0,L^FD{Z(set.Name)}^FS");
        sb.Append(CultureInfo.InvariantCulture, $"^FO{m},{m + F(12)}^A0N,{F(8)},{F(8)}^FDTray {Z(t.Barcode)}  Load {Z(load?.LoadNumber ?? "-")}^FS");
        sb.Append(CultureInfo.InvariantCulture, $"^FO{m},{m + F(21)}^A0N,{F(8)},{F(8)}^FDSterilised {load?.StartedAt.ToLocalTime():dd/MM/yyyy}  Use by {t.SterileUntil:dd/MM/yyyy}^FS");
        sb.Append(CultureInfo.InvariantCulture, $"^FO{m},{m + F(30)}^A0N,{F(7)},{F(7)}^FDPacked by {Z(t.PackedBy ?? "-")}^FS");
        sb.Append(CultureInfo.InvariantCulture, $"^FO{m},{m + F(39)}^BY2^BCN,{F(14)},N,N,N^FD{Z(t.Barcode)}^FS");
        sb.Append("^PQ1^XZ");
        return sb.ToString();
    }
}
