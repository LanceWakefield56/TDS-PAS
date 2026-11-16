using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Messaging.Hl7;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Devices;

public sealed record DeviceSettingsEdit(int TheatreChartMinutes, int RecoveryChartMinutes, int WardChartMinutes, int StoreEverySeconds, int OfflineAfterSeconds, int UnassociatedKeepHours, int KeepMonitorMessagesHours,
    bool AutoConnectByNhi, bool SaferSleepEnabled, string? SaferSleepOutCode, string? SaferSleepApplication, string? SaferSleepFacility, SendAdtOn SendOn, bool FileDrugsToEmr);

public sealed record IngestResult(bool Accepted, string? Error, Guid? PatientId, int Stored, string? Monitor);

/// <summary>Which captured values the nurse accepts, plus what only a person can assess (consciousness, pain, oxygen, sedation).</summary>
public sealed record ValidateRequest(bool HeartRate, bool SpO2, bool BloodPressure, bool RespiratoryRate, bool Temperature, string? Consciousness, int? Pain, bool OnOxygen, decimal? OxygenLitres, int? Sedation);

public sealed record LiveValue(string Code, string Label, decimal Value, string? Unit, DateTimeOffset At);

/// <summary>Bedside monitor feeds: monitor register, HL7 ingest, connecting patients, chart captures and nurse validation.</summary>
public sealed class DeviceService(DevicesDbContext db, EmrDbContext emr, PatientsDbContext patients, EmrService emrService, AuditWriter audit, TimeProvider clock)
{
    private static readonly TimeZoneInfo Nz = TryZone("Pacific/Auckland");
    private static TimeZoneInfo TryZone(string id) { try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; } }

    public static readonly EpisodeStage[] ActiveStages = [EpisodeStage.Admitted, EpisodeStage.InTheatre, EpisodeStage.Recovery, EpisodeStage.ReadyForDischarge];

    // ------------------------------------------------------------------ settings
    public async Task<DeviceSettings> SettingsAsync(CancellationToken ct = default) =>
        await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new DeviceSettings();

    public async Task<IReadOnlyList<string>> SaveSettingsAsync(DeviceSettingsEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (e.TheatreChartMinutes is < 1 or > 60 || e.RecoveryChartMinutes is < 1 or > 60 || e.WardChartMinutes is < 1 or > 240) errors.Add("Chart intervals: theatre and recovery 1–60 minutes, ward 1–240.");
        if (e.StoreEverySeconds is < 5 or > 600) errors.Add("Keep readings every 5–600 seconds.");
        if (e.OfflineAfterSeconds is < 30 or > 3600) errors.Add("Offline after 30–3600 seconds.");
        if (e.UnassociatedKeepHours is < 1 or > 720) errors.Add("Keep readings with no patient for 1–720 hours.");
        if (e.KeepMonitorMessagesHours is < 1 or > 2160) errors.Add("Keep monitor messages for 1–2160 hours.");
        if (e.SaferSleepEnabled && string.IsNullOrWhiteSpace(e.SaferSleepOutCode)) errors.Add("Name the messaging interface that sends to SaferSleep.");
        if (errors.Count > 0) return errors;
        var s = await db.Settings.SingleOrDefaultAsync(ct);
        if (s is null) { s = new DeviceSettings(); db.Settings.Add(s); }
        s.TheatreChartMinutes = e.TheatreChartMinutes; s.RecoveryChartMinutes = e.RecoveryChartMinutes; s.WardChartMinutes = e.WardChartMinutes; s.StoreEverySeconds = e.StoreEverySeconds;
        s.OfflineAfterSeconds = e.OfflineAfterSeconds; s.UnassociatedKeepHours = e.UnassociatedKeepHours; s.KeepMonitorMessagesHours = e.KeepMonitorMessagesHours; s.AutoConnectByNhi = e.AutoConnectByNhi; s.SaferSleepEnabled = e.SaferSleepEnabled;
        s.SaferSleepOutCode = Clip(e.SaferSleepOutCode, 40) ?? "safersleep-out"; s.SaferSleepApplication = Clip(e.SaferSleepApplication, 40) ?? "SAFERSLEEP"; s.SaferSleepFacility = Clip(e.SaferSleepFacility, 40) ?? "TDS";
        s.SendOn = e.SendOn; s.FileDrugsToEmr = e.FileDrugsToEmr; s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Settings", "Devices", "settings", reason: $"SaferSleep {(s.SaferSleepEnabled ? "on" : "off")}, chart {s.TheatreChartMinutes}/{s.RecoveryChartMinutes}/{s.WardChartMinutes} min", ct: ct);
        return [];
    }

    // ------------------------------------------------------------------ monitors
    public async Task<IReadOnlyList<Monitor>> MonitorsAsync(CancellationToken ct = default) => await db.Monitors.AsNoTracking().OrderBy(m => m.Code).ToListAsync(ct);

    public static string NormaliseCode(string code) => string.Join('/', code.Replace('^', '/').Split('/').Select(p => p.Trim()).Where(p => p.Length > 0)).ToUpperInvariant();

    public async Task<(Monitor? Monitor, string? Error)> SaveMonitorAsync(Guid? id, Guid equipmentId, string code, bool active, string user, CancellationToken ct = default)
    {
        code = NormaliseCode(code ?? "");
        if (code.Length is 0 or > 60) return (null, "Enter what the gateway calls this monitor (bed label such as PACU/BAY1, or the monitor id it sends) – up to 60 characters.");
        if (await db.Monitors.AnyAsync(m => m.Code == code && m.Id != id, ct)) return (null, $"Another monitor already uses '{code}'.");
        if (await db.Monitors.AnyAsync(m => m.EquipmentId == equipmentId && m.Id != id, ct)) return (null, "That equipment is already set up as a monitor.");
        Monitor? m;
        if (id is { } mid)
        {
            m = await db.Monitors.SingleOrDefaultAsync(x => x.Id == mid, ct);
            if (m is null) return (null, "Monitor not found.");
            if (!active && await db.Associations.AnyAsync(a => a.MonitorId == m.Id && a.EndedAt == null, ct)) return (null, "Disconnect the patient before switching this monitor off.");
        }
        else { m = new Monitor { CreatedAt = clock.GetUtcNow(), CreatedBy = user }; db.Monitors.Add(m); }
        m.EquipmentId = equipmentId; m.Code = code; m.IsActive = active;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, id is null ? "Create" : "Update", "Monitor", m.Id.ToString(), reason: $"{code}{(active ? "" : " (off)")}", ct: ct);
        return (m, null);
    }

    public async Task<IReadOnlyList<LiveValue>> LiveAsync(Guid monitorId, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var since = clock.GetUtcNow().AddSeconds(-Math.Max(s.OfflineAfterSeconds, 600));
        var rows = await db.Readings.AsNoTracking().Where(r => r.MonitorId == monitorId && r.At >= since).ToListAsync(ct);
        return rows.GroupBy(r => r.Code).Select(g => g.OrderByDescending(r => r.At).First())
            .OrderBy(r => Array.IndexOf(Order, r.Code) is var i && i < 0 ? 99 : i)
            .Select(r => new LiveValue(r.Code, DeviceHl7.Labels.GetValueOrDefault(r.Code, r.Code), r.Value, r.Unit, r.At)).ToList();
    }

    private static readonly string[] Order = ["HR", "PR", "SpO2", "NIBP-S", "NIBP-D", "NIBP-M", "ART-S", "ART-D", "ART-M", "RR", "EtCO2", "Temp"];

    public bool Online(Monitor m, DeviceSettings s) => m.LastSeenAt is { } t && clock.GetUtcNow() - t <= TimeSpan.FromSeconds(s.OfflineAfterSeconds);

    // ------------------------------------------------------------------ ingest
    /// <summary>
    /// Offline import (0.18.0): one monitor ORU taken from a file (USB export, network share) while the gateway connection was down.
    /// Readings are filed to whichever patient was connected to the monitor <em>at the time of each reading</em>; the monitor is not marked
    /// online, nobody is auto-connected, and readings already held (same monitor, measurement and time) are skipped, so a file can be imported twice safely.
    /// </summary>
    public async Task<IngestResult> ImportAsync(string raw, CancellationToken ct = default)
    {
        MonitorMessage msg;
        try { msg = DeviceHl7.ParseMonitor(raw, Nz, clock.GetUtcNow()); }
        catch (Hl7Exception ex) { return new(false, ex.Message, null, 0, null); }
        var monitors = await db.Monitors.AsNoTracking().Where(m => msg.DeviceKeys.Contains(m.Code)).ToListAsync(ct);
        var mon = msg.DeviceKeys.Select(k => monitors.FirstOrDefault(m => m.Code == k)).FirstOrDefault(m => m is not null);
        if (mon is null) return new(false, $"Unknown monitor ({string.Join(", ", msg.DeviceKeys.DefaultIfEmpty("no bed or device id"))}) – add it under Devices › Monitors.", null, 0, null);
        var s = await SettingsAsync(ct);
        var now = clock.GetUtcNow();
        var values = msg.Values.Where(v => DeviceHl7.Plausible(v.Code, v.Value) && v.At <= now.AddMinutes(5)).OrderBy(v => v.At).ToList();
        if (values.Count == 0) return new(true, null, null, 0, mon.Code);
        var from = values[0].At.AddSeconds(-s.StoreEverySeconds); var to = values[^1].At.AddSeconds(s.StoreEverySeconds);
        var assocs = await db.Associations.AsNoTracking().Where(a => a.MonitorId == mon.Id && a.StartedAt <= to && (a.EndedAt == null || a.EndedAt >= from)).ToListAsync(ct);
        var held = await db.Readings.AsNoTracking().Where(r => r.MonitorId == mon.Id && r.At >= from && r.At <= to).Select(r => new { r.Code, r.At }).ToListAsync(ct);
        var kept = held.GroupBy(r => r.Code).ToDictionary(g => g.Key, g => g.Select(r => r.At).ToList());
        var stored = 0; Guid? patient = null;
        foreach (var v in values)
        {
            var times = kept.TryGetValue(v.Code, out var t) ? t : kept[v.Code] = [];
            if (times.Any(x => Math.Abs((x - v.At).TotalSeconds) < s.StoreEverySeconds)) continue; // already held, or within the keep interval
            var a = assocs.FirstOrDefault(x => x.StartedAt <= v.At && (x.EndedAt is null || x.EndedAt >= v.At));
            db.Readings.Add(new Reading { MonitorId = mon.Id, AssociationId = a?.Id, At = v.At, Code = v.Code, Value = v.Value, Unit = v.Unit });
            times.Add(v.At); stored++;
            patient ??= a?.PatientId;
        }
        await db.SaveChangesAsync(ct);
        return new(true, null, patient, stored, mon.Code);
    }

    /// <summary>Takes one monitor ORU from the live gateway connection (messaging engine). Unknown monitors are rejected so the gateway configuration problem is visible.</summary>
    public async Task<IngestResult> IngestAsync(string raw, CancellationToken ct = default)
    {
        MonitorMessage msg;
        try { msg = DeviceHl7.ParseMonitor(raw, Nz, clock.GetUtcNow()); }
        catch (Hl7Exception ex) { return new(false, ex.Message, null, 0, null); }
        var monitors = await db.Monitors.Where(m => msg.DeviceKeys.Contains(m.Code)).ToListAsync(ct);
        var mon = msg.DeviceKeys.Select(k => monitors.FirstOrDefault(m => m.Code == k)).FirstOrDefault(m => m is not null);
        if (mon is null) return new(false, $"Unknown monitor ({string.Join(", ", msg.DeviceKeys.DefaultIfEmpty("no bed or device id"))}) – add it under Devices › Monitors.", null, 0, null);
        if (!mon.IsActive) return new(false, $"Monitor {mon.Code} is switched off in BrightPAS.", null, 0, mon.Code);
        var s = await SettingsAsync(ct);
        var now = clock.GetUtcNow();
        mon.LastSeenAt = now;
        mon.LastNhi = msg.Nhi;
        var assoc = await db.Associations.SingleOrDefaultAsync(a => a.MonitorId == mon.Id && a.EndedAt == null, ct);
        if (assoc is null && s.AutoConnectByNhi && msg.Nhi is { } nhi)
        {
            var pid = await patients.Patients.AsNoTracking().Where(p => p.Nhi == nhi && p.MergedIntoId == null).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
            var ep = pid is null ? null : await emr.Episodes.AsNoTracking().Where(e => e.PatientId == pid && ActiveStages.Contains(e.Stage)).OrderByDescending(e => e.AdmittedAt).FirstOrDefaultAsync(ct);
            if (ep is not null && !await db.Associations.AnyAsync(a => a.EpisodeId == ep.Id && a.EndedAt == null, ct))
            {
                assoc = new Association { MonitorId = mon.Id, EpisodeId = ep.Id, PatientId = ep.PatientId, StartedAt = now, StartedBy = "monitor", Method = "Monitor NHI" };
                db.Associations.Add(assoc);
                await audit.WriteAsync("monitor", "Connect", "Monitor", mon.Id.ToString(), ep.PatientId.ToString(), reason: $"{mon.Code} connected by NHI sent from the monitor", ct: ct);
            }
        }
        // Downsample: keep one reading per measurement every StoreEverySeconds.
        var window = now.AddSeconds(-s.StoreEverySeconds * 2 - 60);
        var last = await db.Readings.AsNoTracking().Where(r => r.MonitorId == mon.Id && r.At >= window).GroupBy(r => r.Code).Select(g => new { g.Key, At = g.Max(r => r.At) }).ToListAsync(ct);
        var lastAt = last.ToDictionary(x => x.Key, x => x.At);
        var stored = 0;
        foreach (var v in msg.Values.Where(v => DeviceHl7.Plausible(v.Code, v.Value)).OrderBy(v => v.At))
        {
            if (v.At > now.AddMinutes(5)) continue;
            if (lastAt.TryGetValue(v.Code, out var prev) && (v.At - prev).TotalSeconds < s.StoreEverySeconds) continue;
            db.Readings.Add(new Reading { MonitorId = mon.Id, AssociationId = assoc?.Id, At = v.At, Code = v.Code, Value = v.Value, Unit = v.Unit });
            lastAt[v.Code] = v.At; stored++;
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (assoc is not null && db.Entry(assoc).State == EntityState.Added)
        {
            // Another message or a nurse connected this monitor or patient at the same moment: keep the readings, without a second connection.
            db.Entry(assoc).State = EntityState.Detached;
            foreach (var r in db.ChangeTracker.Entries<Reading>().Where(e => e.State == EntityState.Added)) r.Entity.AssociationId = null;
            assoc = null;
            await db.SaveChangesAsync(ct);
        }
        return new(true, null, assoc?.PatientId, stored, mon.Code);
    }

    // ------------------------------------------------------------------ connecting patients
    public async Task<(Association? Association, string? Error)> ConnectAsync(Guid monitorId, Guid episodeId, string user, CancellationToken ct = default)
    {
        var mon = await db.Monitors.SingleOrDefaultAsync(m => m.Id == monitorId, ct);
        if (mon is null || !mon.IsActive) return (null, "Choose a monitor that is switched on in BrightPAS.");
        var ep = await emr.Episodes.AsNoTracking().SingleOrDefaultAsync(e => e.Id == episodeId, ct) ?? throw new KeyNotFoundException("Episode not found.");
        if (!ActiveStages.Contains(ep.Stage)) return (null, "Admit the patient before connecting a monitor (and not after discharge).");
        var busy = await db.Associations.SingleOrDefaultAsync(a => a.MonitorId == mon.Id && a.EndedAt == null, ct);
        if (busy is not null && busy.EpisodeId == ep.Id) return (busy, null);
        if (busy is not null) return (null, $"Monitor {mon.Code} is connected to another patient – disconnect them first.");
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == ep.PatientId, ct);
        if (mon.LastNhi is { } nhi && p.Nhi is { } pn && !string.Equals(nhi, pn, StringComparison.OrdinalIgnoreCase) && Online(mon, await SettingsAsync(ct)))
            return (null, $"Monitor {mon.Code} is admitted to a different NHI ({nhi}). Check you have the right monitor, or discharge the patient on the monitor first.");
        var now = clock.GetUtcNow();
        // Moving bed (theatre → recovery): the patient's previous monitor is disconnected, then the new one connected – both or neither.
        // Runs inside the configured execution strategy (SQL Server retry on transient faults) so the transaction is retried as a unit.
        Association? a = null;
        var connected = await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(token);
            try
            {
                foreach (var old in await db.Associations.Where(x => x.EpisodeId == ep.Id && x.EndedAt == null).ToListAsync(token)) { old.EndedAt = now; old.EndedBy = user; }
                await db.SaveChangesAsync(token);
                a = new Association { MonitorId = mon.Id, EpisodeId = ep.Id, PatientId = ep.PatientId, StartedAt = now, StartedBy = user, Method = "Manual" };
                db.Associations.Add(a);
                await db.SaveChangesAsync(token);
                await tx.CommitAsync(token);
                return true;
            }
            catch (DbUpdateException)
            {
                // The unique indexes refused it: someone else connected this monitor or patient at the same moment.
                await tx.RollbackAsync(token);
                db.ChangeTracker.Clear();
                return false;
            }
        }, ct);
        if (!connected || a is null) return (null, $"Monitor {mon.Code} or this patient was connected by someone else just now – refresh and check.");
        await audit.WriteAsync(user, "Connect", "Monitor", mon.Id.ToString(), ep.PatientId.ToString(), reason: $"{mon.Code} connected to the patient", ct: ct);
        return (a, null);
    }

    public async Task<string?> DisconnectAsync(Guid associationId, string user, CancellationToken ct = default)
    {
        var a = await db.Associations.SingleOrDefaultAsync(x => x.Id == associationId, ct) ?? throw new KeyNotFoundException("Connection not found.");
        if (a.EndedAt is not null) return null;
        a.EndedAt = clock.GetUtcNow(); a.EndedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Disconnect", "Monitor", a.MonitorId.ToString(), a.PatientId.ToString(), ct: ct);
        return null;
    }

    public async Task<IReadOnlyList<Association>> OpenAssociationsAsync(CancellationToken ct = default) => await db.Associations.AsNoTracking().Where(a => a.EndedAt == null).ToListAsync(ct);

    // ------------------------------------------------------------------ chart captures
    /// <summary>
    /// Run by the devices worker: disconnects monitors from patients who have left, then offers a capture to the chart for each connected patient
    /// when their interval has passed (theatre / recovery / ward). NIBP is taken from the latest cycle within the interval.
    /// </summary>
    public async Task<int> ChartDueAsync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var now = clock.GetUtcNow();
        var open = await db.Associations.Where(a => a.EndedAt == null).ToListAsync(ct);
        if (open.Count == 0) return 0;
        var epIds = open.Select(a => a.EpisodeId).ToList();
        var eps = await emr.Episodes.AsNoTracking().Where(e => epIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var made = 0;
        foreach (var a in open)
        {
            if (!eps.TryGetValue(a.EpisodeId, out var ep) || !ActiveStages.Contains(ep.Stage)) { a.EndedAt = now; a.EndedBy = "system (patient left)"; continue; }
            var minutes = ep.Stage switch { EpisodeStage.InTheatre => s.TheatreChartMinutes, EpisodeStage.Recovery => s.RecoveryChartMinutes, _ => s.WardChartMinutes };
            var lastCap = await db.Captures.AsNoTracking().Where(c => c.AssociationId == a.Id).MaxAsync(c => (DateTimeOffset?)c.At, ct);
            if (lastCap is { } lc && now - lc < TimeSpan.FromMinutes(minutes)) continue;
            if (lastCap is null && now - a.StartedAt < TimeSpan.FromSeconds(20)) continue;
            var since = now.AddMinutes(-minutes).AddSeconds(-30);
            var fresh = now.AddSeconds(-s.OfflineAfterSeconds);
            var rows = await db.Readings.AsNoTracking().Where(r => r.MonitorId == a.MonitorId && r.At >= since && r.At >= a.StartedAt.AddMinutes(-1)).ToListAsync(ct);
            decimal? Latest(string code, bool needFresh = true) => rows.Where(r => r.Code == code && (!needFresh || r.At >= fresh)).OrderByDescending(r => r.At).Select(r => (decimal?)r.Value).FirstOrDefault();
            var c = new Capture
            {
                AssociationId = a.Id,
                EpisodeId = a.EpisodeId,
                MonitorId = a.MonitorId,
                At = now,
                HeartRate = Int(Latest("HR") ?? Latest("PR")),
                SpO2 = Int(Latest("SpO2")),
                SystolicBp = Int(Latest("NIBP-S", false) ?? Latest("ART-S")),
                DiastolicBp = Int(Latest("NIBP-D", false) ?? Latest("ART-D")),
                MeanBp = Int(Latest("NIBP-M", false) ?? Latest("ART-M")),
                RespiratoryRate = Int(Latest("RR")),
                TemperatureC = Latest("Temp", false),
                EtCo2 = Latest("EtCO2"),
            };
            if (c.HeartRate is null && c.SpO2 is null && c.SystolicBp is null && c.RespiratoryRate is null && c.TemperatureC is null) continue;
            db.Captures.Add(c); made++;
        }
        await db.SaveChangesAsync(ct);
        return made;
    }

    private static int? Int(decimal? v) => v is null ? null : (int)Math.Round(v.Value, MidpointRounding.AwayFromZero);

    public async Task<(Guid? ObservationId, IReadOnlyList<string> Errors)> ValidateAsync(Guid captureId, ValidateRequest r, string user, CancellationToken ct = default)
    {
        var c = await db.Captures.SingleOrDefaultAsync(x => x.Id == captureId, ct) ?? throw new KeyNotFoundException("Monitor reading not found.");
        if (c.Status != CaptureStatus.Pending) return (null, ["This reading has already been validated or rejected."]);
        var mon = await db.Monitors.AsNoTracking().SingleAsync(m => m.Id == c.MonitorId, ct);
        var req = new ObservationRequest(c.At, r.RespiratoryRate ? c.RespiratoryRate : null, r.SpO2 ? c.SpO2 : null, r.OnOxygen, r.OxygenLitres, r.BloodPressure ? c.SystolicBp : null,
            r.BloodPressure ? c.DiastolicBp : null, r.HeartRate ? c.HeartRate : null, r.Temperature ? c.TemperatureC : null, string.IsNullOrWhiteSpace(r.Consciousness) ? null : r.Consciousness.Trim(),
            r.Pain, r.Sedation, "Device", mon.EquipmentId);
        var (obs, errors) = await emrService.ObserveAsync(c.EpisodeId, req, user, ct);
        if (obs is null) return (null, errors);
        c.Status = CaptureStatus.Validated; c.ObservationId = obs.Id; c.DecidedBy = user; c.DecidedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (obs.Id, []);
    }

    public async Task<string?> RejectAsync(Guid captureId, string reason, string user, CancellationToken ct = default)
    {
        var c = await db.Captures.SingleOrDefaultAsync(x => x.Id == captureId, ct) ?? throw new KeyNotFoundException("Monitor reading not found.");
        if (c.Status != CaptureStatus.Pending) return "This reading has already been validated or rejected.";
        if (string.IsNullOrWhiteSpace(reason)) return "Say why the reading is not being charted (e.g. artefact, probe off, patient moving).";
        c.Status = CaptureStatus.Rejected; c.RejectReason = Clip(reason, 200); c.DecidedBy = user; c.DecidedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        var ep = await emr.Episodes.AsNoTracking().SingleAsync(e => e.Id == c.EpisodeId, ct);
        await audit.WriteAsync(user, "RejectReading", "Episode", ep.Id.ToString(), ep.PatientId.ToString(), reason: c.RejectReason, ct: ct);
        return null;
    }

    /// <summary>Deletes readings from monitors with no patient connected after the keep time (they belong to no one).</summary>
    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var cutoff = clock.GetUtcNow().AddHours(-s.UnassociatedKeepHours);
        return await db.Readings.Where(r => r.AssociationId == null && r.At < cutoff).ExecuteDeleteAsync(ct);
    }

    internal static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];
}
