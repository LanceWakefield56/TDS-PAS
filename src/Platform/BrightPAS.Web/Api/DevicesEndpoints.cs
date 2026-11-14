using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Data;
using BrightPAS.Identity;
using BrightPAS.Modules.Assets;
using BrightPAS.Modules.Devices;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Messaging;
using BrightPAS.Modules.Patients;
using BrightPAS.Web.Integration;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>Devices &amp; SaferSleep (0.17.0, ADR 0029): monitor register and live status, connecting patients, validating readings, SaferSleep records.</summary>
public static class DevicesEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Done(string? error) => error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });
    private static IResult Errors(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record MonitorEdit(Guid EquipmentId, string Code, bool IsActive);
    public sealed record ConnectRequest(Guid MonitorId);
    public sealed record RejectRequest(string Reason);
    public sealed record FileRequest(Guid EpisodeId);

    private static async Task<IResult> Guard(Func<Task<IResult>> f)
    {
        try { return await f(); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { errors = new[] { ex.Message } }); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { errors = new[] { ex.Message } }); }
    }

    public static void MapDevicesEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/devices").RequireModule(DevicesModule.Id);

        g.MapGet("/overview", async (DeviceService svc, DevicesDbContext db, AssetsDbContext assets, PasDbContext core, PatientsDbContext patients, MessagingService msg) =>
        {
            var s = await svc.SettingsAsync();
            var mons = await svc.MonitorsAsync();
            var eqIds = mons.Select(m => m.EquipmentId).ToList();
            var eq = await assets.Equipment.AsNoTracking().Where(e => eqIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id);
            var locIds = eq.Values.Where(e => e.LocationId != null).Select(e => e.LocationId!.Value).ToList();
            var locs = await core.Locations.AsNoTracking().Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name);
            var open = await svc.OpenAssociationsAsync();
            var pids = open.Select(a => a.PatientId).ToList();
            var pts = await patients.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            var rows = new List<object>();
            foreach (var m in mons)
            {
                var e = eq.GetValueOrDefault(m.EquipmentId);
                var a = open.FirstOrDefault(x => x.MonitorId == m.Id);
                var p = a is null ? null : pts.GetValueOrDefault(a.PatientId);
                rows.Add(new
                {
                    m.Id,
                    m.Code,
                    m.IsActive,
                    m.EquipmentId,
                    assetNumber = e?.AssetNumber,
                    name = e?.Name,
                    make = e?.Make,
                    model = e?.Model,
                    address = e?.NetworkAddress,
                    location = e?.LocationId is { } lid ? locs.GetValueOrDefault(lid) : null,
                    equipmentStatus = e?.Status.ToString(),
                    online = svc.Online(m, s),
                    m.LastSeenAt,
                    m.LastNhi,
                    live = await svc.LiveAsync(m.Id),
                    connected = a is null ? null : new
                    {
                        associationId = a.Id,
                        a.EpisodeId,
                        a.PatientId,
                        a.StartedAt,
                        a.StartedBy,
                        a.Method,
                        patient = p is null ? null : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}",
                        nhi = p?.Nhi,
                        nhiMismatch = m.LastNhi is not null && p?.Nhi is not null && !string.Equals(m.LastNhi, p.Nhi, StringComparison.OrdinalIgnoreCase)
                    },
                });
            }
            var ifaces = (await msg.InterfacesAsync()).Where(i => i.Handler is "DeviceObservations" or "AnaesthesiaRecord" || i.Code == s.SaferSleepOutCode)
                .Select(i => new { i.Code, i.Name, i.Direction, i.Transport, i.Handler, i.Port, i.Host, i.IsActive }).ToList();
            var unmatched = await db.Records.CountAsync(r => r.EpisodeId == null && r.SupersededById == null);
            return Results.Ok(new { monitors = rows, interfaces = ifaces, saferSleep = new { s.SaferSleepEnabled, s.SaferSleepOutCode, unmatched }, offlineAfterSeconds = s.OfflineAfterSeconds });
        }).RequireAuthorization(Permissions.DevicesAny);

        g.MapGet("/settings", async (DeviceService svc) => Results.Ok(await svc.SettingsAsync())).RequireAuthorization(Permissions.DevicesManage);
        g.MapPut("/settings", async (DeviceSettingsEdit e, ClaimsPrincipal u, DeviceService svc) => Errors(await svc.SaveSettingsAsync(e, Me(u)))).RequireAuthorization(Permissions.DevicesManage);

        g.MapGet("/equipment", async (AssetsDbContext assets, DevicesDbContext db) =>
        {
            var used = await db.Monitors.AsNoTracking().Select(m => m.EquipmentId).ToListAsync();
            return Results.Ok(await assets.Equipment.AsNoTracking().Where(e => e.Category == EquipmentCategory.Monitor || e.Category == EquipmentCategory.Electromedical)
                .OrderBy(e => e.AssetNumber).Select(e => new { e.Id, e.AssetNumber, e.Name, e.Make, e.Model, e.NetworkAddress, used = used.Contains(e.Id) }).ToListAsync());
        }).RequireAuthorization(Permissions.DevicesManage);

        g.MapPost("/monitors", async (MonitorEdit e, ClaimsPrincipal u, DeviceService svc, AssetsDbContext assets) =>
        {
            if (!await assets.Equipment.AnyAsync(x => x.Id == e.EquipmentId)) return Results.BadRequest(new { errors = new[] { "Choose the monitor from the equipment register." } });
            var (m, err) = await svc.SaveMonitorAsync(null, e.EquipmentId, e.Code, e.IsActive, Me(u));
            return m is null ? Done(err) : Results.Ok(new { m.Id });
        }).RequireAuthorization(Permissions.DevicesManage);
        g.MapPut("/monitors/{id:guid}", async (Guid id, MonitorEdit e, ClaimsPrincipal u, DeviceService svc, AssetsDbContext assets) =>
        {
            if (!await assets.Equipment.AnyAsync(x => x.Id == e.EquipmentId)) return Results.BadRequest(new { errors = new[] { "Choose the monitor from the equipment register." } });
            var (m, err) = await svc.SaveMonitorAsync(id, e.EquipmentId, e.Code, e.IsActive, Me(u));
            return m is null ? Done(err) : Results.Ok(new { m.Id });
        }).RequireAuthorization(Permissions.DevicesManage);

        // ---------------------------------------------------------------- episode view (eMR tab)
        g.MapGet("/episodes/{id:guid}", async (Guid id, DeviceService svc, DevicesDbContext db, EmrDbContext emr, AssetsDbContext assets, PasDbContext core) =>
        {
            var ep = await emr.Episodes.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
            if (ep is null) return Results.NotFound();
            var s = await svc.SettingsAsync();
            var mons = await svc.MonitorsAsync();
            var eqIds = mons.Select(m => m.EquipmentId).ToList();
            var eq = await assets.Equipment.AsNoTracking().Where(e => eqIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id);
            var locIds = eq.Values.Where(e => e.LocationId != null).Select(e => e.LocationId!.Value).ToList();
            var locs = await core.Locations.AsNoTracking().Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name);
            var open = await svc.OpenAssociationsAsync();
            string Label(BrightPAS.Modules.Devices.Monitor m) { var e = eq.GetValueOrDefault(m.EquipmentId); var loc = e?.LocationId is { } l ? locs.GetValueOrDefault(l) : null; return $"{e?.AssetNumber ?? m.Code} – {e?.Name ?? "monitor"}{(loc is null ? "" : $" ({loc})")}"; }
            var mine = open.FirstOrDefault(a => a.EpisodeId == id);
            var available = mons.Where(m => m.IsActive && (open.All(a => a.MonitorId != m.Id) || mine?.MonitorId == m.Id))
                .Select(m => new { m.Id, label = Label(m), m.Code, online = svc.Online(m, s), m.LastNhi }).ToList();
            var assocs = await db.Associations.AsNoTracking().Where(a => a.EpisodeId == id).OrderBy(a => a.StartedAt).ToListAsync();
            var assocIds = assocs.Select(a => a.Id).ToList();
            var caps = await db.Captures.AsNoTracking().Where(c => c.EpisodeId == id).OrderByDescending(c => c.At).Take(200).ToListAsync();
            var readings = await db.Readings.AsNoTracking().Where(r => r.AssociationId != null && assocIds.Contains(r.AssociationId.Value) && (r.Code == "HR" || r.Code == "SpO2" || r.Code == "NIBP-S" || r.Code == "NIBP-D" || r.Code == "RR" || r.Code == "EtCO2"))
                .OrderBy(r => r.At).Take(5000).Select(r => new { r.At, r.Code, r.Value }).ToListAsync();
            var records = await db.Records.AsNoTracking().Include(r => r.Drugs).Include(r => r.Events).Include(r => r.Vitals).Where(r => r.EpisodeId == id).OrderByDescending(r => r.ReceivedAt).ToListAsync();
            var current = records.FirstOrDefault(r => r.SupersededById == null);
            var adt = await db.AdtSends.AsNoTracking().Where(a => a.EpisodeId == id).OrderByDescending(a => a.At).Take(10).ToListAsync();
            return Results.Ok(new
            {
                stage = ep.Stage.ToString(),
                open = DeviceService.ActiveStages.Contains(ep.Stage),
                connected = mine is null ? null : new
                {
                    associationId = mine.Id,
                    monitorId = mine.MonitorId,
                    label = Label(mons.Single(m => m.Id == mine.MonitorId)),
                    mine.StartedAt,
                    mine.StartedBy,
                    mine.Method,
                    live = await svc.LiveAsync(mine.MonitorId),
                    online = svc.Online(mons.Single(m => m.Id == mine.MonitorId), s)
                },
                monitors = available,
                history = assocs.Select(a => new { a.Id, label = mons.FirstOrDefault(m => m.Id == a.MonitorId) is { } m ? Label(m) : "?", a.StartedAt, a.StartedBy, a.EndedAt, a.EndedBy, a.Method }),
                pending = caps.Where(c => c.Status == CaptureStatus.Pending).OrderBy(c => c.At),
                decided = caps.Where(c => c.Status != CaptureStatus.Pending).Take(50).Select(c => new { c.Id, c.At, c.HeartRate, c.SpO2, c.SystolicBp, c.DiastolicBp, c.RespiratoryRate, c.TemperatureC, status = c.Status.ToString(), c.DecidedBy, c.DecidedAt, c.RejectReason }),
                trend = readings,
                intervals = new { theatre = s.TheatreChartMinutes, recovery = s.RecoveryChartMinutes, ward = s.WardChartMinutes },
                saferSleep = new { enabled = s.SaferSleepEnabled, sends = adt.Select(a => new { a.Event, a.At, a.By, a.Error }) },
                record = current is null ? null : new
                {
                    current.Id,
                    current.CaseNumber,
                    current.IsCorrection,
                    current.Anaesthetist,
                    current.StartAt,
                    current.EndAt,
                    current.Asa,
                    current.Technique,
                    current.Airway,
                    current.Comments,
                    current.DocumentId,
                    current.NoteId,
                    current.ReceivedAt,
                    drugs = current.Drugs.OrderBy(d => d.At).Select(d => new { d.At, d.Name, d.Dose, d.Unit, d.Route, d.Rate }),
                    events = current.Events.OrderBy(e => e.At).Select(e => new { e.At, e.Text }),
                    vitals = current.Vitals.OrderBy(v => v.At).Select(v => new { v.At, v.Code, v.Value }),
                    versions = records.Count,
                },
            });
        })
        // Clinical content (readings, anaesthetic record): the clinical record permission is needed as well – managing devices alone is not enough.
        .RequireAuthorization(Permissions.DevicesAny).RequireAuthorization(Permissions.EmrView);

        g.MapPost("/episodes/{id:guid}/connect", (Guid id, ConnectRequest r, ClaimsPrincipal u, DeviceService svc) => Guard(async () =>
        {
            var (a, err) = await svc.ConnectAsync(r.MonitorId, id, Me(u));
            return a is null ? Done(err) : Results.Ok(new { a.Id });
        })).RequireAuthorization(Permissions.DevicesUse);
        g.MapPost("/associations/{id:guid}/disconnect", (Guid id, ClaimsPrincipal u, DeviceService svc) => Guard(async () => Done(await svc.DisconnectAsync(id, Me(u))))).RequireAuthorization(Permissions.DevicesUse);
        g.MapPost("/captures/{id:guid}/validate", (Guid id, ValidateRequest r, ClaimsPrincipal u, DeviceService svc) => Guard(async () =>
        {
            var (obs, errors) = await svc.ValidateAsync(id, r, Me(u));
            return obs is null ? Errors(errors) : Results.Ok(new { observationId = obs });
        })).RequireAuthorization(Permissions.DevicesUse).RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/captures/{id:guid}/reject", (Guid id, RejectRequest r, ClaimsPrincipal u, DeviceService svc) => Guard(async () => Done(await svc.RejectAsync(id, r.Reason, Me(u))))).RequireAuthorization(Permissions.DevicesUse);

        // ---------------------------------------------------------------- SaferSleep
        g.MapPost("/episodes/{id:guid}/safersleep", (Guid id, ClaimsPrincipal u, IServiceProvider sp) => Guard(async () =>
        {
            var (ok, error) = await DevicesWorker.SendAdtAsync(sp, id, null, Me(u), default);
            return ok ? Results.Ok(new { message = "Patient and booking sent to SaferSleep." }) : Done(error);
        })).RequireAuthorization(Permissions.DevicesUse);

        g.MapGet("/records", async (bool? unmatched, DevicesDbContext db, PatientsDbContext patients) =>
        {
            var q = db.Records.AsNoTracking().Where(r => r.SupersededById == null);
            if (unmatched == true) q = q.Where(r => r.EpisodeId == null);
            var rows = await q.OrderByDescending(r => r.ReceivedAt).Take(100).Select(r => new
            {
                r.Id,
                r.CaseNumber,
                r.Nhi,
                r.Anaesthetist,
                r.StartAt,
                r.ReceivedAt,
                r.EpisodeId,
                r.PatientId,
                r.IsCorrection,
                r.DocumentId,
                drugs = r.Drugs.Count
            }).ToListAsync();
            var pids = rows.Where(r => r.PatientId != null).Select(r => r.PatientId!.Value).ToList();
            var pts = await patients.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}");
            return Results.Ok(rows.Select(r => new
            {
                r.Id,
                r.CaseNumber,
                r.Nhi,
                r.Anaesthetist,
                r.StartAt,
                r.ReceivedAt,
                r.EpisodeId,
                r.PatientId,
                r.IsCorrection,
                r.DocumentId,
                r.drugs,
                patient = r.PatientId is { } p ? pts.GetValueOrDefault(p) : null
            }));
        }).RequireAuthorization(Permissions.DevicesAny);

        // Episodes the unmatched record could belong to (same NHI, or today's admitted patients).
        g.MapGet("/records/{id:guid}/candidates", async (Guid id, DevicesDbContext db, EmrDbContext emr, PatientsDbContext patients) =>
        {
            var r = await db.Records.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (r is null) return Results.NotFound();
            var eps = await emr.Episodes.AsNoTracking().Where(e => e.AdmittedAt != null).OrderByDescending(e => e.AdmittedAt).Take(300).ToListAsync();
            var pids = eps.Select(e => e.PatientId).Distinct().ToList();
            var pts = await patients.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            var list = eps.Where(e => pts.ContainsKey(e.PatientId) && (r.Nhi is null || pts[e.PatientId].Nhi == r.Nhi)).Take(30)
                .Select(e => new { episodeId = e.Id, e.BookingId, patient = $"{pts[e.PatientId].FamilyName.ToUpperInvariant()}, {pts[e.PatientId].GivenNames}", nhi = pts[e.PatientId].Nhi, e.AdmittedAt, stage = e.Stage.ToString() });
            return Results.Ok(list);
        }).RequireAuthorization(Permissions.DevicesManage);

        g.MapPost("/records/{id:guid}/file", (Guid id, FileRequest f, ClaimsPrincipal u, AnaesthesiaService svc, DevicesDbContext db, MessagingService msg, MsgDbContext mdb) => Guard(async () =>
        {
            var rec = await db.Records.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new KeyNotFoundException("Record not found.");
            byte[]? pdf = null;
            if (rec.MessageId is { } mid && await mdb.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == mid) is { } m)
            {
                try { pdf = DeviceHl7.ParseSaferSleep(msg.ContentOf(m), TimeZoneInfo.Utc).Pdf; } catch (BrightPAS.Messaging.Hl7.Hl7Exception) { }
            }
            return Done(await svc.FileToEpisodeAsync(id, f.EpisodeId, pdf, Me(u)));
        })).RequireAuthorization(Permissions.DevicesManage);
    }
}
