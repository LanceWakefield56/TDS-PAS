using Microsoft.EntityFrameworkCore;
using BrightPAS.Licensing;
using BrightPAS.Modules.Devices;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Messaging;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>
/// Devices &amp; SaferSleep background work (0.17.0): every 15 seconds offers monitor readings to the chart and sends ADT to SaferSleep for
/// patients admitted (or in theatre) and discharged today; hourly deletes readings with no patient and old monitor messages from the store.
/// </summary>
public sealed class DevicesWorker(IServiceScopeFactory scopes, LicenceCache licence, TimeProvider clock, ILogger<DevicesWorker> log) : BackgroundService
{
    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            if (licence.Status.AccessTo(DevicesModule.Id) != ModuleAccess.Full) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sp = scope.ServiceProvider;
                var made = await sp.GetRequiredService<DeviceService>().ChartDueAsync(stoppingToken);
                if (made > 0) log.LogInformation("Devices: {Count} monitor reading(s) waiting for validation.", made);
                await AdtCycleAsync(sp, stoppingToken);
                if (clock.GetUtcNow() - _lastPurge > TimeSpan.FromHours(1))
                {
                    _lastPurge = clock.GetUtcNow();
                    var (readings, messages) = await PurgeAsync(sp, stoppingToken);
                    if (readings + messages > 0) log.LogInformation("Devices: deleted {Readings} reading(s) with no patient and {Messages} old monitor message(s).", readings, messages);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Devices cycle failed; it will run again shortly."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static async Task<(int Readings, int Messages)> PurgeAsync(IServiceProvider sp, CancellationToken ct)
    {
        var readings = await sp.GetRequiredService<DeviceService>().PurgeAsync(ct);
        var s = await sp.GetRequiredService<DeviceService>().SettingsAsync(ct);
        var mdb = sp.GetRequiredService<MsgDbContext>();
        var ifaces = await mdb.Interfaces.AsNoTracking().Where(i => i.Handler == InboundHandler.DeviceObservations).Select(i => i.Id).ToListAsync(ct);
        if (ifaces.Count == 0) return (readings, 0);
        var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
        var ok = now.AddHours(-s.KeepMonitorMessagesHours);
        var bad = now.AddDays(-7);
        var messages = await mdb.Messages.Where(m => ifaces.Contains(m.InterfaceId) && !mdb.Messages.Any(c => c.ParentId == m.Id)
            && ((m.Status == MessageStatus.Processed && m.CreatedAt < ok) || m.CreatedAt < bad)).ExecuteDeleteAsync(ct);
        return (readings, messages);
    }

    private static async Task AdtCycleAsync(IServiceProvider sp, CancellationToken ct)
    {
        var devices = sp.GetRequiredService<DeviceService>();
        var s = await devices.SettingsAsync(ct);
        if (!s.SaferSleepEnabled) return;
        var clock = sp.GetRequiredService<TimeProvider>();
        var local = clock.GetLocalNow();
        var dayStart = new DateTimeOffset(local.Date, local.Offset).ToUniversalTime();
        var emr = sp.GetRequiredService<EmrDbContext>();
        var svc = sp.GetRequiredService<AnaesthesiaService>();
        var stages = s.SendOn == SendAdtOn.Admission ? DeviceService.ActiveStages : [EpisodeStage.InTheatre, EpisodeStage.Recovery, EpisodeStage.ReadyForDischarge];
        var admit = await emr.Episodes.AsNoTracking().Where(e => e.AdmittedAt >= dayStart && stages.Contains(e.Stage)).Select(e => e.Id).ToListAsync(ct);
        foreach (var id in admit)
            if (!await svc.SentAsync(id, "A01", ct)) await SendAdtAsync(sp, id, "A01", "system", ct);
        var gone = await emr.Episodes.AsNoTracking().Where(e => e.DischargedAt >= dayStart && e.Stage == EpisodeStage.Discharged).Select(e => e.Id).ToListAsync(ct);
        foreach (var id in gone)
            if (await svc.SentAsync(id, "A01", ct) && !await svc.SentAsync(id, "A03", ct)) await SendAdtAsync(sp, id, "A03", "system", ct);
    }

    /// <summary>Builds and queues one ADT for the episode on the SaferSleep outbound interface. evt null = A01 first time, A08 after.</summary>
    public static async Task<(bool Ok, string? Error)> SendAdtAsync(IServiceProvider sp, Guid episodeId, string? evt, string user, CancellationToken ct)
    {
        var devices = sp.GetRequiredService<DeviceService>();
        var svc = sp.GetRequiredService<AnaesthesiaService>();
        var s = await devices.SettingsAsync(ct);
        if (!s.SaferSleepEnabled) return (false, "The SaferSleep interface is switched off (Devices › Settings).");
        var ep = await sp.GetRequiredService<EmrDbContext>().Episodes.AsNoTracking().SingleOrDefaultAsync(e => e.Id == episodeId, ct) ?? throw new KeyNotFoundException("Episode not found.");
        if (ep.Stage == EpisodeStage.PreAdmission) return (false, "Admit the patient first.");
        evt ??= ep.Stage == EpisodeStage.Discharged ? "A03" : await svc.SentAsync(ep.Id, "A01", ct) ? "A08" : "A01";
        var pdb = sp.GetRequiredService<PatientsDbContext>();
        var p = await pdb.Patients.AsNoTracking().SingleAsync(x => x.Id == ep.PatientId, ct);
        if (p.Nhi is null) { await svc.RecordAdtAsync(ep.Id, evt, null, "No NHI – SaferSleep needs the NHI.", user, ct); return (false, "The patient has no NHI – add it before sending to SaferSleep."); }
        var allergies = await pdb.Set<PatientAlert>().AsNoTracking().Where(a => a.PatientId == p.Id && a.IsActive && a.Type == AlertType.Allergy).Select(a => a.Description).ToListAsync(ct);
        var tdb = sp.GetRequiredService<TheatreDbContext>();
        var booking = await tdb.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == ep.BookingId, ct);
        string location = "THEATRE", procedure = booking?.Procedure ?? "Procedure"; string? surgeon = null, anaes = null; DateTimeOffset? planned = null;
        if (booking is not null && await tdb.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == booking.SessionId, ct) is { } session)
        {
            var view = (await sp.GetRequiredService<TheatreService>().SessionsAsync(session.Date, session.Date, ct)).FirstOrDefault(v => v.Id == session.Id);
            if (view is not null)
            {
                location = view.LocationName; surgeon = view.Surgeon; anaes = view.Anaesthetist;
                if (view.Bookings.FirstOrDefault(b => b.Id == booking.Id) is { } bv)
                {
                    var dt = session.Date.ToDateTime(bv.PlannedStart);
                    planned = new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
                }
            }
        }
        var data = new AdtData(ep.Id, p.Nhi, p.Mrn, p.FamilyName, p.GivenNames, p.DateOfBirth, p.Sex switch { BirthSex.Female => "F", BirthSex.Male => "M", _ => "U" },
            p.AddressLine1, p.City, p.Postcode, location, surgeon, anaes, procedure, planned, ep.AdmittedAt, ep.DischargedAt, allergies);
        var hl7 = DeviceHl7.BuildAdt(evt, data, s.SaferSleepApplication, s.SaferSleepFacility, sp.GetRequiredService<TimeProvider>().GetLocalNow());
        var (m, err) = await sp.GetRequiredService<MessagingService>().QueueAsync(s.SaferSleepOutCode, hl7, user, p.Id, ct);
        await svc.RecordAdtAsync(ep.Id, evt, m?.Id, err, user, ct);
        if (m is null) return (false, $"Could not queue the message for SaferSleep: {err}");
        await sp.GetRequiredService<BrightPAS.Core.Audit.AuditWriter>().WriteAsync(user, "SendSaferSleep", "Episode", ep.Id.ToString(), p.Id.ToString(), reason: $"ADT^{evt} queued on {s.SaferSleepOutCode}", ct: ct);
        return (true, null);
    }
}
