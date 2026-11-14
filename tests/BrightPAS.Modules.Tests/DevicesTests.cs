using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Modules.Devices;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Tests;

/// <summary>0.17.0 Devices &amp; SaferSleep: GE monitor HL7 parsing, connecting patients, chart captures with nurse validation, SaferSleep record filing and corrections, ADT.</summary>
public class DevicesTests
{
    private static string Ts(DateTimeOffset t) => t.ToString("yyyyMMddHHmmsszzz").Replace(":", "");

    public static string MonitorOru(string bed, string? nhi, DateTimeOffset at, int hr = 72, int spo2 = 98, int sys = 124, int dia = 78, int rr = 14, string temp = "36.8", string etco2 = "4.8", string? deviceId = "MON-02") =>
        $"MSH|^~\\&|CARESCAPE GW|GEHC|BrightPAS|TDS|{Ts(at)}||ORU^R01^ORU_R01|GE{at:HHmmssfff}|P|2.6\r" +
        $"PID|||{(nhi is null ? "" : nhi + "^^^NZLMOH^NHI")}\r" +
        $"PV1||I|{bed.Replace('/', '^')}\r" +
        $"OBR|1|||69837^MDC_DEV_MON_PHYSIO_MULTI_PARAM_MDS^MDC|||{Ts(at)}\r" +
        $"OBX|1|NM|147842^MDC_ECG_HEART_RATE^MDC|1.1.1.1|{hr}|264864^MDC_DIM_BEAT_PER_MIN^MDC|||||R|||{Ts(at)}||||{deviceId}\r" +
        $"OBX|2|NM|150456^MDC_PULS_OXIM_SAT_O2^MDC|1.1.2.1|{spo2}|262688^MDC_DIM_PERCENT^MDC|||||R|||{Ts(at)}\r" +
        $"OBX|3|NM|150021^MDC_PRESS_BLD_NONINV_SYS^MDC|1.1.3.1|{sys}|266016^MDC_DIM_MMHG^MDC|||||R|||{Ts(at)}\r" +
        $"OBX|4|NM|150022^MDC_PRESS_BLD_NONINV_DIA^MDC|1.1.3.2|{dia}|266016^MDC_DIM_MMHG^MDC|||||R|||{Ts(at)}\r" +
        $"OBX|5|NM|151562^MDC_RESP_RATE^MDC|1.1.4.1|{rr}|264928^MDC_DIM_RESP_PER_MIN^MDC|||||R|||{Ts(at)}\r" +
        $"OBX|6|NM|150344^MDC_TEMP^MDC|1.1.5.1|{temp}|268192^MDC_DIM_DEGC^MDC|||||R|||{Ts(at)}\r" +
        $"OBX|7|NM|151708^MDC_AWAY_CO2_ET^MDC|1.1.6.1|{etco2}|^kPa|||||R|||{Ts(at)}\r" +
        $"OBX|8|NM|99999^MDC_SOMETHING_ELSE^MDC|1.1.7.1|5|||||||R\r";

    [Fact]
    public void Parses_GE_style_ORU_with_MDC_codes_units_and_plausibility()
    {
        var at = new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.FromHours(13));
        var m = DeviceHl7.ParseMonitor(MonitorOru("PACU/BAY1", "ZSC21TN", at), TimeZoneInfo.Utc, at);
        Assert.Equal(["PACU/BAY1", "MON-02"], m.DeviceKeys);
        Assert.Equal("ZSC21TN", m.Nhi);
        Assert.Equal(7, m.Values.Count);
        Assert.Equal(72, m.Values.Single(v => v.Code == "HR").Value);
        Assert.Equal(36m, Math.Round(m.Values.Single(v => v.Code == "EtCO2").Value)); // 4.8 kPa ≈ 36 mmHg
        Assert.Equal("mmHg", m.Values.Single(v => v.Code == "EtCO2").Unit);
        Assert.Equal(98.6m, DeviceHl7.Normalise("Temp", 98.6m, "°C").Value);
        Assert.Equal(37.0m, DeviceHl7.Normalise("Temp", 98.6m, "degF").Value);
        Assert.False(DeviceHl7.Plausible("SpO2", 0)); Assert.False(DeviceHl7.Plausible("HR", 400)); Assert.True(DeviceHl7.Plausible("NIBP-S", 120));
        Assert.Equal("HR", DeviceHl7.Code("HR", "")); Assert.Equal("NIBP-S", DeviceHl7.Code("x", "NIBP_SYS"));
        Assert.Throws<BrightPAS.Messaging.Hl7.Hl7Exception>(() => DeviceHl7.ParseMonitor("MSH|^~\\&|X|Y|Z|W|20261109||ADT^A01|1|P|2.4\r", TimeZoneInfo.Utc, at));
    }

    private static async Task<(Episode Episode, Patient Patient)> AdmittedAsync(ModulesHost h, IServiceProvider sp, string nhi = "ZSC21TN", string family = "Ngātai")
    {
        var p = await h.PatientAsync(family, nhi: nhi);
        var emr = sp.GetRequiredService<EmrService>();
        var e = await emr.EpisodeAsync(Guid.NewGuid(), p.Id, "rn");
        Assert.Empty((await emr.AssessAsync(e.Id, new(AssessmentKind.NursingAdmission, new Dictionary<string, string> { ["id_band"] = "yes", ["allergies_checked"] = "Penicillin", ["fasting_from"] = "02:00", ["escort_name"] = "Hemi" }, null), "rn")).Errors);
        Assert.Null(await emr.AdmitAsync(e.Id, true, "rn"));
        return (e, p);
    }

    [Fact]
    public async Task Monitor_feed_connect_capture_validate_and_reject()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var dev = sp.GetRequiredService<DeviceService>();
        var (e, p) = await AdmittedAsync(h, sp);
        var eq = Guid.NewGuid();
        var (mon, err) = await dev.SaveMonitorAsync(null, eq, "pacu^bay1", true, "admin");
        Assert.Null(err); Assert.Equal("PACU/BAY1", mon!.Code);
        Assert.NotNull((await dev.SaveMonitorAsync(null, Guid.NewGuid(), "PACU/BAY1", true, "admin")).Error);

        // Unknown bed is rejected; a known one is stored and downsampled.
        Assert.False((await dev.IngestAsync(MonitorOru("PACU/BAY9", null, h.Clock.GetUtcNow(), deviceId: null))).Accepted);
        var r1 = await dev.IngestAsync(MonitorOru("PACU/BAY1", null, h.Clock.GetUtcNow()));
        Assert.True(r1.Accepted); Assert.Equal(7, r1.Stored); Assert.Null(r1.PatientId);
        h.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, (await dev.IngestAsync(MonitorOru("PACU/BAY1", null, h.Clock.GetUtcNow()))).Stored); // within 30 s
        h.Clock.Advance(TimeSpan.FromSeconds(25));
        Assert.Equal(6, (await dev.IngestAsync(MonitorOru("PACU/BAY1", null, h.Clock.GetUtcNow(), spo2: 0))).Stored); // SpO2 0 = artefact, dropped
        Assert.Equal(98, (await dev.LiveAsync(mon.Id)).Single(v => v.Code == "SpO2").Value);

        // Connect; a monitor sending a different NHI is refused.
        Assert.Null((await dev.ConnectAsync(mon.Id, e.Id, "rn")).Error);
        var (e2, _) = await AdmittedAsync(h, sp, "ZAA5280", "Smith");
        Assert.Contains("connected to another patient", (await dev.ConnectAsync(mon.Id, e2.Id, "rn")).Error);

        // Capture after the interval (patient in recovery: 5 minutes); nothing reaches the chart until validated.
        Assert.Null(await sp.GetRequiredService<EmrService>().SetStageAsync(e.Id, EpisodeStage.Recovery, "rn"));
        h.Clock.Advance(TimeSpan.FromSeconds(30));
        await dev.IngestAsync(MonitorOru("PACU/BAY1", null, h.Clock.GetUtcNow(), hr: 80));
        Assert.Equal(1, await dev.ChartDueAsync());
        Assert.Equal(0, await dev.ChartDueAsync()); // not again within 5 minutes
        var db = sp.GetRequiredService<DevicesDbContext>();
        var cap = await db.Captures.SingleAsync();
        Assert.Equal(80, cap.HeartRate); Assert.Equal(124, cap.SystolicBp); Assert.Equal(36.8m, cap.TemperatureC); Assert.Equal(36.0m, Math.Round(cap.EtCo2!.Value));
        var emrDb = sp.GetRequiredService<EmrDbContext>();
        Assert.Empty(await emrDb.Observations.ToListAsync());
        var (obsId, errors) = await dev.ValidateAsync(cap.Id, new ValidateRequest(true, true, true, true, false, "A", 2, false, null, null), "rn");
        Assert.Empty(errors);
        var obs = await emrDb.Observations.SingleAsync();
        Assert.Equal(obsId, obs.Id); Assert.Equal("Device", obs.Source); Assert.Equal(eq, obs.DeviceId); Assert.Null(obs.TemperatureC); Assert.Equal(80, obs.HeartRate);
        Assert.NotEmpty((await dev.ValidateAsync(cap.Id, new ValidateRequest(true, true, true, true, true, "A", null, false, null, null), "rn")).Errors);

        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await dev.IngestAsync(MonitorOru("PACU/BAY1", null, h.Clock.GetUtcNow(), spo2: 71));
        Assert.Equal(1, await dev.ChartDueAsync());
        var cap2 = await db.Captures.OrderBy(c => c.At).LastAsync();
        Assert.NotNull(await dev.RejectAsync(cap2.Id, " ", "rn"));
        Assert.Null(await dev.RejectAsync(cap2.Id, "Artefact – probe off", "rn"));
        Assert.Single(await emrDb.Observations.ToListAsync());

        // Discharge ends the connection; readings with no patient are purged after the keep time.
        var a = await db.Associations.SingleAsync(x => x.EndedAt == null);
        Assert.Null(await dev.DisconnectAsync(a.Id, "rn"));
        h.Clock.Advance(TimeSpan.FromHours(25));
        Assert.True(await dev.PurgeAsync() > 0);
        Assert.True(await db.Readings.AnyAsync(r => r.AssociationId != null));
    }

    [Fact]
    public async Task Auto_connect_by_NHI_sent_from_the_monitor()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var dev = sp.GetRequiredService<DeviceService>();
        var (e, p) = await AdmittedAsync(h, sp);
        var (mon, _) = await dev.SaveMonitorAsync(null, Guid.NewGuid(), "THEATRE1/OR1", true, "admin");
        var r = await dev.IngestAsync(MonitorOru("THEATRE1/OR1", p.Nhi, h.Clock.GetUtcNow(), deviceId: "MON-01"));
        Assert.Equal(p.Id, r.PatientId);
        var a = await sp.GetRequiredService<DevicesDbContext>().Associations.SingleAsync();
        Assert.Equal("Monitor NHI", a.Method); Assert.Equal(e.Id, a.EpisodeId);
    }

    [Fact]
    public async Task SaferSleep_record_is_filed_and_a_correction_replaces_it()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var (e, p) = await AdmittedAsync(h, sp);
        var ana = sp.GetRequiredService<AnaesthesiaService>();
        var t0 = h.Clock.GetUtcNow().AddHours(-2);
        (int, string, string, string, string)[] drugs = [(1, "Midazolam", "2", "mg", "IV"), (2, "Fentanyl", "100", "microgram", "IV"), (8, "Ondansetron", "8", "mg", "IV")];
        var r = await ana.IngestAsync(SimplePdf.SaferSleepOru("SS1", "CASE-1", e.Id.ToString("N"), p.Nhi!, p.FamilyName, p.GivenNames, t0, false, drugs, "6 mg/kg/h"), null, "test");
        Assert.True(r.Accepted, r.Error); Assert.True(r.Filed); Assert.Equal(p.Id, r.PatientId);
        Assert.True((await ana.IngestAsync(SimplePdf.SaferSleepOru("SS1", "CASE-1", e.Id.ToString("N"), p.Nhi!, p.FamilyName, p.GivenNames, t0, false, drugs), null, "test")).Duplicate);
        var db = sp.GetRequiredService<DevicesDbContext>();
        var rec = await db.Records.Include(x => x.Drugs).Include(x => x.Events).Include(x => x.Vitals).SingleAsync();
        Assert.Equal(4, rec.Drugs.Count); Assert.Equal("6 mg/kg/h", rec.Drugs.Single(d => d.Rate != null).Rate); Assert.Equal(3, rec.Events.Count); Assert.Equal(36, rec.Vitals.Count);
        Assert.Equal("2", rec.Asa); Assert.Equal("Dr Sarah Lee", rec.Anaesthetist);
        Assert.NotNull(rec.DocumentId); Assert.NotNull(rec.NoteId);
        var emr = sp.GetRequiredService<EmrDbContext>();
        Assert.Equal(4, await emr.Medications.CountAsync(m => m.Source == "SaferSleep" && m.ExternalRef == "CASE-1"));
        var note = await emr.Notes.SingleAsync(n => n.Id == rec.NoteId);
        Assert.Equal(NoteKind.Anaesthetic, note.Kind); Assert.True(note.Signed); Assert.Contains("Fentanyl", note.Text);
        var doc = await sp.GetRequiredService<PatientsDbContext>().Documents.SingleAsync(d => d.Id == rec.DocumentId);
        Assert.Equal(DocumentCategory.Clinical, doc.Category);

        // Correction: supersedes, amends the note, replaces the medicines.
        var r2 = await ana.IngestAsync(SimplePdf.SaferSleepOru("SS2", "CASE-1", e.Id.ToString("N"), p.Nhi!, p.FamilyName, p.GivenNames, t0, true, drugs.Take(2).ToArray()), null, "test");
        Assert.True(r2.Filed);
        Assert.Equal(r2.RecordId, (await db.Records.SingleAsync(x => x.ControlId == "SS1")).SupersededById);
        Assert.Equal(2, await emr.Medications.CountAsync(m => m.Source == "SaferSleep"));
        var rec2 = await db.Records.SingleAsync(x => x.ControlId == "SS2");
        Assert.Equal(rec.NoteId, (await emr.Notes.SingleAsync(n => n.Id == rec2.NoteId)).AmendsNoteId);

        // Wrong patient for the visit number is refused; unknown visit with another day's start is kept unmatched and filed by hand.
        Assert.Contains("different patient", (await ana.IngestAsync(SimplePdf.SaferSleepOru("SS3", "CASE-2", e.Id.ToString("N"), "ZAA5280", "Smith", "Jo", t0, false, drugs), null, "test")).Error);
        var r4 = await ana.IngestAsync(SimplePdf.SaferSleepOru("SS4", "CASE-3", null, p.Nhi!, p.FamilyName, p.GivenNames, t0.AddDays(-3), false, drugs), null, "test");
        Assert.True(r4.Accepted); Assert.False(r4.Filed);
        Assert.Null(await ana.FileToEpisodeAsync(r4.RecordId!.Value, e.Id, null, "admin"));
        Assert.NotNull((await db.Records.SingleAsync(x => x.Id == r4.RecordId)).NoteId);
        Assert.Equal("This record is already filed.", await ana.FileToEpisodeAsync(r4.RecordId!.Value, e.Id, null, "admin"));
    }

    [Fact]
    public void ADT_for_SaferSleep_has_patient_visit_allergies_and_procedure()
    {
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.FromHours(13));
        var hl7 = DeviceHl7.BuildAdt("A01", new AdtData(id, "ZSC21TN", "TDS0000001", "Ngātai", "Aroha|Mere", new DateOnly(1980, 5, 17), "F", "1 Main Road", "Nelson", "7010", "Theatre 1", "Dr Ben Hall", "Dr Kate Ford",
            "Laparoscopic cholecystectomy", now.AddHours(1), now, null, ["Penicillin – rash"]), "SAFERSLEEP", "TDS", now);
        var m = BrightPAS.Messaging.Hl7.Hl7Message.Parse(hl7);
        Assert.Equal("ADT^A01", m.MessageType);
        Assert.Equal("ZSC21TN", m.First("PID")!.Get(3, 1));
        Assert.Equal("Aroha|Mere", m.First("PID")!.Get(5, 2));
        Assert.Equal(id.ToString("N"), m.First("PV1")!.Get(19));
        Assert.Equal("Theatre 1", m.First("PV1")!.Get(3, 1));
        Assert.Equal("Penicillin – rash", m.First("AL1")!.Get(3, 2));
        Assert.Equal("Laparoscopic cholecystectomy", m.First("PR1")!.Get(3, 2));
        Assert.Equal("SAFERSLEEP", m.Msh.Get(5));
    }

    [Fact]
    public async Task One_patient_per_monitor_and_one_monitor_per_patient_even_when_two_things_happen_at_once()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var dev = sp.GetRequiredService<DeviceService>();
        var db = sp.GetRequiredService<DevicesDbContext>();
        var (e, p) = await AdmittedAsync(h, sp);
        var (other, _) = await AdmittedAsync(h, sp, "ZAA0024", "Tane");
        var theatre = (await dev.SaveMonitorAsync(null, Guid.NewGuid(), "THEATRE1/OR1", true, "admin")).Monitor!;
        var pacu = (await dev.SaveMonitorAsync(null, Guid.NewGuid(), "PACU/BAY1", true, "admin")).Monitor!;

        // Moving bed: connecting the recovery monitor ends the theatre connection in the same step.
        Assert.Null((await dev.ConnectAsync(theatre.Id, e.Id, "rn")).Error);
        h.Clock.Advance(TimeSpan.FromMinutes(40));
        Assert.Null((await dev.ConnectAsync(pacu.Id, e.Id, "rn2")).Error);
        var mine = await db.Associations.AsNoTracking().Where(a => a.EpisodeId == e.Id).OrderBy(a => a.StartedAt).ToListAsync();
        Assert.Equal(2, mine.Count);
        Assert.NotNull(mine[0].EndedAt); Assert.Equal("rn2", mine[0].EndedBy); Assert.Null(mine[1].EndedAt); Assert.Equal(pacu.Id, mine[1].MonitorId);

        // The monitor in use is refused for another patient, and the first patient stays connected.
        Assert.Contains("another patient", (await dev.ConnectAsync(pacu.Id, other.Id, "rn")).Error);

        // Two requests at once slipping past the checks: the database refuses the second open connection (monitor, then patient).
        db.Associations.Add(new Association { MonitorId = pacu.Id, EpisodeId = other.Id, PatientId = other.PatientId, StartedAt = h.Clock.GetUtcNow(), StartedBy = "race" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Associations.Add(new Association { MonitorId = theatre.Id, EpisodeId = e.Id, PatientId = e.PatientId, StartedAt = h.Clock.GetUtcNow(), StartedBy = "race" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Associations.CountAsync(a => a.EndedAt == null));
        Assert.Equal(p.Id, (await db.Associations.AsNoTracking().SingleAsync(a => a.EndedAt == null)).PatientId);
    }

    [Fact]
    public void ADT_to_SaferSleep_never_states_no_known_allergies_and_escapes_delimiters()
    {
        var now = new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(13));
        var d = new AdtData(Guid.NewGuid(), "ZSC21TN", "TDS000123", "O|Brien^Smith", "Mary & Jo", new DateOnly(1970, 2, 3), "F", "1 High St", "Nelson", "7010", "Theatre 1", "Dr A Surgeon", "Dr B Gas", "Excision | lesion", now, now, null, []);
        var none = DeviceHl7.BuildAdt("A01", d, "SAFERSLEEP", "TDS", now);
        Assert.DoesNotContain("AL1|", none);
        var segs = none.Split('\r');
        Assert.Contains("O\\F\\Brien\\S\\Smith^Mary \\T\\ Jo", none);
        Assert.Contains("PR1|1||^Excision \\F\\ lesion|", none);
        Assert.Equal(d.EpisodeId.ToString("N"), segs.Single(x => x.StartsWith("PV1|", StringComparison.Ordinal)).Split('|')[19]);
        var with = DeviceHl7.BuildAdt("A08", d with { Allergies = ["Penicillin – rash", "Latex"] }, "SAFERSLEEP", "TDS", now);
        Assert.Contains("AL1|1|DA|^Penicillin – rash\rAL1|2|DA|^Latex\r", with);
        Assert.Contains("ADT^A08", with);
    }
}
