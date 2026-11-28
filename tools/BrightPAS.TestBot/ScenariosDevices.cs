using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Devices &amp; SaferSleep (0.17.0) with a virtual GE CARESCAPE Gateway and a virtual SaferSleep server. The robot admits a demonstration patient,
/// then: the gateway sends vital signs (HL7 ORU, IEEE 11073 codes) for a bed; a nurse connects the patient to that monitor on screen, validates the
/// offered reading into the observation chart and disconnects; BrightPAS sends the patient to SaferSleep (ADT) and SaferSleep returns the
/// anaesthetic record with its PDF, a correction, a duplicate, a wrong-patient message and a record that has to be filed by hand.
/// </summary>
public sealed partial class Scenarios
{
    private const int MonitorPort = 12580, SaferSleepRecordPort = 12581;
    private const string RecoveryBed = "PACU/BAY1", TheatreBed = "THEATRE1/OR1";
    private sealed record DeviceCase(string BookingId, string EpisodeId, string Nhi, string Family, string Given);
    private DeviceCase? _case;
    private Session? _deviceNurse;

    private async Task DevicesStepsAsync()
    {
        await Step("Virtual GE monitor gateway", "Admit a demonstration patient to recovery for the monitor and SaferSleep tests", DeviceCaseAsync);
        if (_case is null) return;
        await Step("Virtual GE monitor gateway", "A monitor BrightPAS does not know, and values that cannot be real, are refused", MonitorRefusalsAsync);
        await Step("Virtual GE monitor gateway", "Nurse connects the patient to the bedside monitor; live values show; the offered reading is validated into the chart", MonitorChartAsync);
        await Step("Virtual GE monitor gateway", "A monitor admitted to a different NHI cannot be connected to this patient", MonitorWrongPatientAsync);
        await Step("Virtual SaferSleep", "BrightPAS sends the admitted patient and booking to SaferSleep (ADT) by itself and again on request", SaferSleepAdtAsync);
        await Step("Virtual SaferSleep", "Anaesthetic record comes back: drugs, events and PDF filed to the episode; correction replaces it; duplicate and wrong patient handled", SaferSleepRecordAsync);
        await Step("Virtual SaferSleep", "A record that cannot be matched waits under Devices › SaferSleep and is filed to an episode by hand", SaferSleepUnmatchedAsync);
    }

    // ------------------------------------------------------------------ the patient
    private async Task<(Outcome, string)> DeviceCaseAsync()
    {
        // Needs an account that may both write the clinical record and use devices (a nurse) – not the administrator.
        Session? nurse = null;
        foreach (var a in new[] { "nurse", "hreid" })
        {
            var s = await As(a);
            if ((await s.GetAsync("/api/devices/overview")).Status == 200 && (await s.GetAsync("/api/emr/today")).Status == 200) { nurse = s; break; }
        }
        if (nurse is null) return (Outcome.Skip, "No demonstration nurse may use Devices & SaferSleep (module off or no permission).");
        _deviceNurse = nurse;
        var today = DateOnly.FromDateTime(NzNow);
        for (var d = 0; d <= 14; d++)
        {
            var list = await nurse.GetAsync($"/api/emr/today?date={today.AddDays(d):yyyy-MM-dd}");
            if (list.Json is not { ValueKind: JsonValueKind.Array } arr) continue;
            foreach (var b in arr.EnumerateArray())
            {
                if (Str(b, "stage") is not ("NotStarted" or "PreAdmission") || Str(b, "nhi") is not { } nhi || Str(b, "bookingStatus") is "Cancelled") continue;
                var bookingId = Str(b, "bookingId")!;
                var rec = await nurse.GetAsync("/api/emr/bookings/" + bookingId);
                if (rec.Json is not { } rj || !rj.TryGetProperty("episode", out var ep) || ep.ValueKind != JsonValueKind.Object) continue;
                var id = Str(ep, "id")!;
                var a = await nurse.PostAsync($"/api/emr/episodes/{id}/assessments", new { kind = "NursingAdmission", answers = new Dictionary<string, string> { ["id_band"] = "yes", ["allergies_checked"] = "None known", ["fasting_from"] = "06:00", ["escort_name"] = "Test robot escort" }, actions = (string?)null });
                if (a.Status != 200) continue;
                var adm = await nurse.PostAsync($"/api/emr/episodes/{id}/admit", new { sedationOrAnaesthesia = true });
                if (adm.Status != 200) return (Outcome.Fail, "Could not admit the patient: " + adm.Text);
                var st = await nurse.PostAsync($"/api/emr/episodes/{id}/stage", new { stage = "Recovery" });
                if (st.Status != 200) return (Outcome.Fail, "Could not move the patient to recovery: " + st.Text);
                var name = (Str(b, "patientName") ?? "PATIENT, Test").Split(',', 2);
                _case = new DeviceCase(bookingId, id, nhi, name[0].Trim(), name.Length > 1 ? name[1].Trim() : "");
                return (Outcome.Pass, $"{Str(b, "patientName")} (NHI {nhi}) admitted and in recovery – booking of {today.AddDays(d):d MMM}.");
            }
        }
        return (Outcome.Skip, "No demonstration booking waiting to be admitted in the next two weeks.");
    }

    // ------------------------------------------------------------------ virtual GE gateway
    /// <summary>NZ wall-clock time: the monitors and SaferSleep send local NZ time, whatever clock the robot machine runs on.</summary>
    private static DateTime NzNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland")).DateTime;

    private static string Ts(DateTime t) => t.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    /// <summary>One ORU^R01 as the CARESCAPE Gateway sends it (IHE PCD-01 style): PV1-3 the bed, OBX-3 IEEE 11073 (MDC) codes, OBX-18 the equipment.</summary>
    private static string MonitorOru(string bed, string? nhi, params (string Mdc, string Name, string Value, string Unit)[] values)
    {
        var now = NzNow;
        var parts = bed.Split('/');
        var sb = new StringBuilder();
        sb.Append($"MSH|^~\\&|CARESCAPE_GW|TDS|BrightPAS|TDS|{Ts(now)}||ORU^R01^ORU_R01|GE{now:HHmmssfff}{Random.Shared.Next(100, 999)}|P|2.6\r");
        sb.Append(nhi is null ? "PID|1\r" : $"PID|1||{nhi}^^^NZLMOH^NHI\r");
        sb.Append($"PV1|1|I|{parts[0]}^{(parts.Length > 1 ? parts[1] : "")}\r");
        sb.Append($"OBR|1|||182777000^monitoring of patient^SCT|||{Ts(now)}\r");
        var n = 1;
        foreach (var v in values) sb.Append($"OBX|{n++}|NM|{v.Mdc}^{v.Name}^MDC|1.0.0.{n}|{v.Value}|{v.Unit}|||||R|||{Ts(now)}||||ROBOT-GE-B1X5\r");
        return sb.ToString();
    }

    private static (string, string, string, string)[] Vitals(int hr, int spo2, int sys, int dia, int rr) =>
    [
        ("147842", "MDC_ECG_HEART_RATE", hr.ToString(CultureInfo.InvariantCulture), "264864^MDC_DIM_BEAT_PER_MIN^MDC"), ("150456", "MDC_PULS_OXIM_SAT_O2", spo2.ToString(CultureInfo.InvariantCulture), "262688^MDC_DIM_PERCENT^MDC"),
        ("150021", "MDC_PRESS_BLD_NONINV_SYS", sys.ToString(CultureInfo.InvariantCulture), "266016^MDC_DIM_MMHG^MDC"), ("150022", "MDC_PRESS_BLD_NONINV_DIA", dia.ToString(CultureInfo.InvariantCulture), "266016^MDC_DIM_MMHG^MDC"),
        ("151562", "MDC_RESP_RATE", rr.ToString(CultureInfo.InvariantCulture), "264928^MDC_DIM_RESP_PER_MIN^MDC"), ("150344", "MDC_TEMP", "36.6", "268192^MDC_DIM_DEGC^MDC"),
    ];

    private async Task<string> GatewayAsync(string hl7, string what)
    {
        var ack = await Sims.SendMllpAsync("127.0.0.1", MonitorPort, hl7);
        R.Device("Virtual GE monitor gateway", what, hl7.Replace("\r", "\n") + "\n--- BrightPAS answered ---\n" + ack.Replace("\r", "\n"));
        return ack;
    }

    private async Task<(Outcome, string)> MonitorRefusalsAsync()
    {
        string unknown;
        try { unknown = await GatewayAsync(MonitorOru("ROBOT/NOWHERE", null, Vitals(70, 98, 120, 80, 14)), "Vital signs from a bed BrightPAS has no monitor for"); }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException or IOException) { return (Outcome.Fail, $"Could not connect to the monitor interface on port {MonitorPort}: {ex.Message}"); }
        if (!unknown.Contains("MSA|AR", StringComparison.Ordinal)) return (Outcome.Fail, "BrightPAS accepted vital signs from a monitor it does not know – they would belong to no bed. Answer: " + unknown.Replace('\r', ' '));
        // Values no patient can have (probe off, artefact) must not be kept.
        var silly = await GatewayAsync(MonitorOru(TheatreBed, null, ("147842", "MDC_ECG_HEART_RATE", "999", "264864^MDC_DIM_BEAT_PER_MIN^MDC"), ("150456", "MDC_PULS_OXIM_SAT_O2", "3", "262688^MDC_DIM_PERCENT^MDC")), "Impossible values (heart rate 999, SpO2 3%)");
        if (!silly.Contains("MSA|AA", StringComparison.Ordinal)) return (Outcome.Fail, "BrightPAS refused a message from a known monitor: " + silly.Replace('\r', ' '));
        var o = await _deviceNurse!.GetAsync("/api/devices/overview");
        var mon = Find(o.Json?.GetProperty("monitors"), m => Str(m, "code") == TheatreBed);
        if (mon is null) return (Outcome.Fail, $"The demonstration monitor {TheatreBed} is not in the monitor list.");
        var kept = mon.Value.GetProperty("live").EnumerateArray().Any(v => v.GetProperty("value").GetDecimal() is 999 or 3);
        return kept ? (Outcome.Fail, "Impossible values (heart rate 999, SpO2 3%) are shown as live readings – they must be discarded as artefact.")
            : (Outcome.Pass, "Unknown monitor refused (MSA|AR) so the set-up problem is visible; impossible values were discarded.");
    }

    private async Task<(Outcome, string)> MonitorChartAsync()
    {
        var c = _case!; var nurse = _deviceNurse!;
        var ack = await GatewayAsync(MonitorOru(RecoveryBed, null, Vitals(72, 98, 124, 78, 14)), $"Vital signs for bed {RecoveryBed}");
        if (!ack.Contains("MSA|AA", StringComparison.Ordinal)) return (Outcome.Fail, "BrightPAS did not accept the monitor message: " + ack.Replace('\r', ' '));
        var view = await nurse.GetAsync("/api/devices/episodes/" + c.EpisodeId);
        var mon = Find(view.Json?.GetProperty("monitors"), m => Str(m, "code") == RecoveryBed);
        if (mon is null) return (Outcome.Fail, $"Monitor {RecoveryBed} is not offered for this patient.");

        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/emr/{c.BookingId}?tab=devices");
        var pick = page.GetByLabel("Bedside monitor", new() { Exact = true });
        await Assertions.Expect(pick).ToBeVisibleAsync(new() { Timeout = 15000 });
        await pick.SelectOptionAsync(Str(mon.Value, "id")!);
        // The nurse sees the monitor's values before confirming it is the right one.
        await Assertions.Expect(page.GetByText("Heart rate").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Connect monitor" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Connected:")).ToBeVisibleAsync(new() { Timeout = 10000 });

        // The gateway keeps sending; BrightPAS offers a reading to the chart once the patient has been connected a little while.
        JsonElement? pending = null;
        for (var i = 0; i < 16 && pending is null; i++)
        {
            await Sims.SendMllpAsync("127.0.0.1", MonitorPort, MonitorOru(RecoveryBed, null, Vitals(72 + i % 3, 98, 124, 78, 14)));
            await Task.Delay(5000);
            var v = await nurse.GetAsync("/api/devices/episodes/" + c.EpisodeId);
            if (v.Json is { } j && j.GetProperty("pending").GetArrayLength() > 0) pending = j.GetProperty("pending")[0].Clone();
        }
        if (pending is null) return (Outcome.Fail, "The patient is connected and the monitor is sending, but no reading was offered to the chart within 80 seconds.");
        var before = await nurse.GetAsync("/api/emr/bookings/" + c.BookingId);
        var chartedBefore = before.Json!.Value.GetProperty("observations").GetArrayLength();
        if (before.Json.Value.GetProperty("observations").EnumerateArray().Any(x => Str(x, "source") == "Device"))
            return (Outcome.Fail, "A monitor reading reached the observation chart before any nurse validated it.");

        await page.ReloadAsync();
        var validate = page.GetByRole(AriaRole.Button, new() { Name = "Validate and chart" }).First;
        await Assertions.Expect(validate).ToBeVisibleAsync(new() { Timeout = 15000 });
        var live = await nurse.SafeTextAsync();
        await validate.ClickAsync();
        await Assertions.Expect(page.GetByText("Charted – the reading is now in Observations.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        var after = await nurse.GetAsync("/api/emr/bookings/" + c.BookingId);
        var obs = after.Json!.Value.GetProperty("observations").EnumerateArray().ToList();
        var charted = obs.FirstOrDefault(x => Str(x, "source") == "Device");
        if (obs.Count != chartedBefore + 1 || charted.ValueKind != JsonValueKind.Object) return (Outcome.Fail, "The nurse validated the reading but no observation with source 'Device' was added to the chart.");
        var hr = charted.GetProperty("heartRate").GetInt32(); var sys = charted.GetProperty("systolicBp").GetInt32();
        if (hr is < 72 or > 74 || sys != 124 || charted.GetProperty("spO2").GetInt32() != 98) return (Outcome.Fail, $"The charted values differ from what the monitor sent (HR {hr}, BP {sys}, expected HR 72–74, BP 124, SpO2 98).");

        await page.GetByRole(AriaRole.Button, new() { Name = "Disconnect monitor" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Monitor disconnected.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        return (Outcome.Pass, $"Connected on screen, live values shown ({(live.Contains("SpO2", StringComparison.Ordinal) ? "heart rate, SpO2, blood pressure" : "values")}), nothing charted until the nurse validated; then HR {hr}, SpO2 98, BP {sys}/78 charted with source Device and an early warning score; monitor disconnected.");
    }

    private async Task<(Outcome, string)> MonitorWrongPatientAsync()
    {
        var c = _case!; var nurse = _deviceNurse!;
        // The theatre monitor has another patient admitted on it (their NHI comes with the readings).
        var other = c.Nhi == "ZZZ0016" ? "ZZZ0024" : "ZZZ0016";
        var ack = await GatewayAsync(MonitorOru(TheatreBed, other, Vitals(64, 97, 118, 70, 12)), $"Vital signs for bed {TheatreBed} carrying NHI {other}");
        if (!ack.Contains("MSA|AA", StringComparison.Ordinal)) return (Outcome.Fail, "BrightPAS did not accept the monitor message: " + ack.Replace('\r', ' '));
        var view = await nurse.GetAsync("/api/devices/episodes/" + c.EpisodeId);
        var mon = Find(view.Json?.GetProperty("monitors"), m => Str(m, "code") == TheatreBed);
        if (mon is null) return (Outcome.Skip, $"Monitor {TheatreBed} is not available.");
        var r = await nurse.PostAsync($"/api/devices/episodes/{c.EpisodeId}/connect", new { monitorId = Str(mon.Value, "id") });
        if (r.Status == 200)
        {
            if (r.Json is { } j && Str(j, "id") is { } assoc) await nurse.PostAsync($"/api/devices/associations/{assoc}/disconnect", new { });
            return (Outcome.Fail, $"BrightPAS connected {c.Family} (NHI {c.Nhi}) to a monitor that is admitted to NHI {other} – another patient's readings could be charted.");
        }
        return r.Text.Contains(other, StringComparison.Ordinal) ? (Outcome.Pass, $"Refused: “{Report.Trim(ErrorText(r.Text), 160)}”")
            : (Outcome.Warn, "Refused, but the message does not name the NHI on the monitor: " + Report.Trim(r.Text, 200));
    }

    private static string ErrorText(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array ? string.Join(" ", e.EnumerateArray().Select(x => x.GetString())) : json; }
        catch (JsonException) { return json; }
    }

    // ------------------------------------------------------------------ virtual SaferSleep
    private static string Field(string hl7, string segment, int field)
    {
        var line = hl7.Split('\r').FirstOrDefault(l => l.StartsWith(segment + "|", StringComparison.Ordinal));
        var f = line?.Split('|');
        return f is not null && f.Length > field ? f[field] : "";
    }

    private async Task<(Outcome, string)> SaferSleepAdtAsync()
    {
        var c = _case!; var nurse = _deviceNurse!;
        var visit = c.EpisodeId.Replace("-", "", StringComparison.Ordinal);
        bool Mine(string m) => Field(m, "PV1", 19).Equals(visit, StringComparison.OrdinalIgnoreCase);
        var a01 = await Sims.WaitForAsync(sims.SaferSleepIn, m => Mine(m) && Field(m, "MSH", 8).StartsWith("ADT^A01", StringComparison.Ordinal), TimeSpan.FromSeconds(90));
        if (a01 is null)
        {
            var view = await nurse.GetAsync("/api/devices/episodes/" + c.EpisodeId);
            return (Outcome.Fail, $"The virtual SaferSleep did not receive an admission (ADT^A01) for the patient within 90 seconds of admission. BrightPAS shows: {Report.Trim(view.Json?.GetProperty("saferSleep").ToString() ?? view.Text, 300)}");
        }
        var problems = new List<string>();
        if (!Field(a01, "PID", 3).StartsWith(c.Nhi + "^", StringComparison.Ordinal)) problems.Add($"PID-3 does not start with the NHI {c.Nhi}");
        if (!Field(a01, "PID", 5).StartsWith(c.Family, StringComparison.OrdinalIgnoreCase)) problems.Add("PID-5 is not the patient's name");
        if (Field(a01, "PID", 7).Length != 8) problems.Add("PID-7 date of birth missing");
        if (Field(a01, "PR1", 3).Length < 2) problems.Add("PR1-3 planned procedure missing");
        if (a01.Split('\r').Any(l => l.StartsWith("AL1|", StringComparison.Ordinal) && l.Contains("No known", StringComparison.OrdinalIgnoreCase))) problems.Add("an AL1 segment claims 'no known allergies' although BrightPAS only knows that none is recorded");
        if (problems.Count > 0) return (Outcome.Fail, "The admission message is not usable by SaferSleep: " + string.Join("; ", problems) + ".");

        // On request (details changed): the nurse presses Send to SaferSleep on the episode.
        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/emr/{c.BookingId}?tab=devices");
        var send = page.GetByRole(AriaRole.Button, new() { Name = "Send to SaferSleep" });
        await Assertions.Expect(send).ToBeVisibleAsync(new() { Timeout = 15000 });
        await send.ClickAsync();
        await Assertions.Expect(page.GetByText("Patient and booking sent to SaferSleep.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        var a08 = await Sims.WaitForAsync(sims.SaferSleepIn, m => Mine(m) && Field(m, "MSH", 8).StartsWith("ADT^A08", StringComparison.Ordinal), TimeSpan.FromSeconds(60));
        if (a08 is null) return (Outcome.Fail, "The screen said the patient was sent again, but no update (ADT^A08) reached the virtual SaferSleep within 60 seconds.");
        var once = sims.SaferSleepIn.Count(m => Mine(m) && Field(m, "MSH", 8).StartsWith("ADT^A01", StringComparison.Ordinal));
        return once == 1 ? (Outcome.Pass, $"ADT^A01 arrived by itself after admission (NHI, name, date of birth, visit number, procedure {Field(a01, "PR1", 3).TrimStart('^')}); Send to SaferSleep on the episode sent ADT^A08.")
            : (Outcome.Fail, $"The admission (ADT^A01) was sent {once} times – SaferSleep would get duplicate cases.");
    }

    /// <summary>A one-page PDF standing in for SaferSleep's own record printout.</summary>
    private static byte[] RecordPdf(string title)
    {
        var text = $"BT /F1 12 Tf 50 780 Td ({title.Replace("(", "", StringComparison.Ordinal).Replace(")", "", StringComparison.Ordinal)}) Tj ET";
        var objs = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {text.Length} >>\nstream\n{text}\nendstream", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objs.Length; i++) { offsets.Add(sb.Length); sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        var xref = sb.Length;
        sb.Append($"xref\n0 {objs.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objs.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>The anaesthetic record as SaferSleep returns it (BrightPAS interface profile): OBR case number and status, drug / event OBX, vital signs, PDF in OBX ED.</summary>
    private static string RecordOru(string control, string caseNo, string? visit, string nhi, string family, string given, DateTime start, bool correction, string ondansetron)
    {
        static string T(DateTime t) => new DateTimeOffset(t).ToString("yyyyMMddHHmmsszzz", CultureInfo.InvariantCulture).Replace(":", "", StringComparison.Ordinal);
        var end = start.AddMinutes(40);
        var sb = new StringBuilder();
        sb.Append($"MSH|^~\\&|SAFERSLEEP|TDS|BrightPAS|TDS|{T(NzNow)}||ORU^R01|{control}|P|2.4\r");
        sb.Append($"PID|1||{nhi}^^^NZLMOH^NHI||{family}^{given}\r");
        sb.Append($"PV1|1|I|THEATRE 1|||||||||||||||||{visit}\r");
        sb.Append($"OBR|1|{visit}|{caseNo}|ANREC^Anaesthetic record^L|||{T(start)}|{T(end)}|||||||||||||||||{(correction ? "C" : "F")}|||||||ROBOT01&Robot&Anaesthetist\r");
        var n = 1;
        void Obx(string type, string id, string value, string unit = "", DateTime? at = null, string route = "") => sb.Append($"OBX|{n++}|{type}|{id}||{value}|{unit}|||||F|||{(at is { } a ? T(a) : "")}|||{route}\r");
        Obx("ST", "ASA^ASA status^L", "2");
        Obx("ST", "TECH^Technique^L", "General anaesthesia – TIVA");
        Obx("ST", "AIRWAY^Airway^L", "LMA size 4");
        Obx("NM", "DRUG^Fentanyl^SSDRUG", "100", "microgram", start.AddMinutes(1), "IV");
        Obx("NM", "DRUG^Propofol^SSDRUG", "180", "mg", start.AddMinutes(2), "IV");
        Obx("NM", "DRUG^Ondansetron^SSDRUG", ondansetron, "mg", start.AddMinutes(25), "IV");
        Obx("ST", "DRUGINF^Propofol infusion^SSDRUG", "6 mg/kg/h", "", start.AddMinutes(3), "IV");
        Obx("ST", "EVENT^Event^L", "Induction", "", start.AddMinutes(2));
        Obx("ST", "EVENT^Event^L", "End of anaesthesia", "", end);
        for (var m = 0; m <= 40; m += 5)
        {
            Obx("NM", "147842^MDC_ECG_HEART_RATE^MDC", (66 + m % 7).ToString(CultureInfo.InvariantCulture), "/min", start.AddMinutes(m));
            Obx("NM", "150456^MDC_PULS_OXIM_SAT_O2^MDC", "98", "%", start.AddMinutes(m));
        }
        if (correction) Obx("TX", "COMMENT^Comments^L", "Corrected by the test robot: ondansetron dose amended.");
        Obx("ED", "PDF^Anaesthetic record^L", $"^AP^PDF^Base64^{Convert.ToBase64String(RecordPdf($"Virtual SaferSleep record {caseNo} for {family} {nhi}"))}");
        return sb.ToString();
    }

    private async Task<string> SaferSleepSendsAsync(string hl7, string what)
    {
        var ack = await Sims.SendMllpAsync("127.0.0.1", SaferSleepRecordPort, hl7);
        var shown = string.Join("\n", hl7.Split('\r').Select(l => l.Length > 220 ? l[..200] + "… (" + l.Length + " characters)" : l));
        R.Device("Virtual SaferSleep", what, shown + "\n--- BrightPAS answered ---\n" + ack.Replace("\r", "\n"));
        return ack;
    }

    private string? _robotCase;

    private async Task<(Outcome, string)> SaferSleepRecordAsync()
    {
        var c = _case!; var nurse = _deviceNurse!;
        var visit = c.EpisodeId.Replace("-", "", StringComparison.Ordinal);
        var stamp = DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
        var caseNo = _robotCase = "SS-RBT-" + stamp;
        var start = NzNow.AddMinutes(-50);
        string ack;
        try { ack = await SaferSleepSendsAsync(RecordOru("SSR" + stamp + "A", caseNo, visit, c.Nhi, c.Family, c.Given, start, false, "4"), $"Anaesthetic record {caseNo} (final)"); }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException or IOException) { return (Outcome.Fail, $"Could not connect to the SaferSleep record interface on port {SaferSleepRecordPort}: {ex.Message}"); }
        if (!ack.Contains("MSA|AA", StringComparison.Ordinal)) return (Outcome.Fail, "BrightPAS did not accept the anaesthetic record: " + ack.Replace('\r', ' '));

        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/emr/{c.BookingId}?tab=devices");
        await Assertions.Expect(page.GetByText("Case " + caseNo)).ToBeVisibleAsync(new() { Timeout = 15000 });
        var text = await nurse.SafeTextAsync();
        foreach (var must in new[] { "Propofol", "Fentanyl", "Ondansetron", "6 mg/kg/h", "Induction", "Dr Anaesthetist Robot" })
            if (!text.Contains(must, StringComparison.Ordinal)) return (Outcome.Fail, $"The anaesthetic record on the episode does not show “{must}”.");
        var view = await nurse.GetAsync("/api/devices/episodes/" + c.EpisodeId);
        var docId = Str(view.Json!.Value.GetProperty("record"), "documentId");
        if (docId is null) return (Outcome.Fail, "The PDF of the anaesthetic record was not filed to the patient's documents.");
        var pdf = await nurse.GetAsync("/api/patients/documents/" + docId);
        if (pdf.Status != 200 || !pdf.Text.StartsWith("%PDF", StringComparison.Ordinal)) return (Outcome.Fail, $"The filed anaesthetic record does not open as a PDF (answer {pdf.Status}).");
        int Meds(JsonElement rec) => rec.GetProperty("medications").EnumerateArray().Count(m => Str(m, "source") == "SaferSleep");
        var rec1 = await nurse.GetAsync("/api/emr/bookings/" + c.BookingId);
        var meds1 = Meds(rec1.Json!.Value);
        if (meds1 != 4) return (Outcome.Fail, $"Expected the 4 anaesthetic drugs in the eMR medicines list marked SaferSleep, found {meds1}.");
        if (!rec1.Json.Value.GetProperty("notes").EnumerateArray().Any(n => (Str(n, "text") ?? "").Contains(caseNo, StringComparison.Ordinal))) return (Outcome.Fail, "No anaesthetic note was filed in the episode notes.");

        // The same message again (SaferSleep resends when it gets no answer) must not file twice.
        var again = await SaferSleepSendsAsync(RecordOru("SSR" + stamp + "A", caseNo, visit, c.Nhi, c.Family, c.Given, start, false, "4"), "The same record again (resend)");
        var rec2 = await nurse.GetAsync("/api/emr/bookings/" + c.BookingId);
        if (!again.Contains("MSA|AA", StringComparison.Ordinal) || Meds(rec2.Json!.Value) != 4) return (Outcome.Fail, $"A resent record was not handled as a duplicate (answer {again.Replace('\r', ' ')}; SaferSleep medicines now {Meds(rec2.Json!.Value)}).");

        // Correction: replaces the first filing instead of adding to it.
        var corr = await SaferSleepSendsAsync(RecordOru("SSR" + stamp + "B", caseNo, visit, c.Nhi, c.Family, c.Given, start, true, "8"), $"Correction to {caseNo} (ondansetron 8 mg)");
        if (!corr.Contains("MSA|AA", StringComparison.Ordinal)) return (Outcome.Fail, "BrightPAS did not accept the correction: " + corr.Replace('\r', ' '));
        await page.ReloadAsync();
        await Assertions.Expect(page.GetByText("Corrected by the test robot")).ToBeVisibleAsync(new() { Timeout = 15000 });
        var rec3 = await nurse.GetAsync("/api/emr/bookings/" + c.BookingId);
        var ss = rec3.Json!.Value.GetProperty("medications").EnumerateArray().Where(m => Str(m, "source") == "SaferSleep").ToList();
        var onda = ss.Where(m => Str(m, "medicine") == "Ondansetron").Select(m => Str(m, "dose")).ToList();
        if (ss.Count != 4 || onda.Count != 1 || onda[0] is null || !onda[0]!.StartsWith('8')) return (Outcome.Fail, $"After the correction the medicines list should hold the 4 corrected drugs with ondansetron 8 mg once; it holds {ss.Count} with ondansetron {string.Join(" and ", onda)}.");

        // A record whose visit number belongs to this patient but whose NHI is someone else's must never be filed.
        var wrongNhi = c.Nhi == "ZZZ0016" ? "ZZZ0024" : "ZZZ0016";
        var wrong = await SaferSleepSendsAsync(RecordOru("SSR" + stamp + "W", caseNo + "-W", visit, wrongNhi, "Other", "Patient", start, false, "4"), $"A record with this visit number but NHI {wrongNhi}");
        if (!wrong.Contains("MSA|AR", StringComparison.Ordinal)) return (Outcome.Fail, "BrightPAS accepted an anaesthetic record whose NHI does not match the patient of the visit number – it could be filed to the wrong patient.");
        return (Outcome.Pass, "Record matched by visit number; drugs, infusion, events, vital signs, note and PDF on the episode; 4 medicines marked SaferSleep; resend not filed twice; correction replaced the first filing (ondansetron 8 mg once); wrong-patient record refused (MSA|AR).");
    }

    private async Task<(Outcome, string)> SaferSleepUnmatchedAsync()
    {
        var c = _case!;
        var stamp = DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
        var caseNo = "SS-RBT-U" + stamp;
        // No visit number, and an anaesthetic on a day the patient was not here: BrightPAS must not guess.
        var ack = await SaferSleepSendsAsync(RecordOru("SSU" + stamp, caseNo, null, c.Nhi, c.Family, c.Given, NzNow.AddDays(-3), false, "4"), $"Record {caseNo} without a visit number, dated three days ago");
        if (!ack.Contains("MSA|AA", StringComparison.Ordinal)) return (Outcome.Fail, "BrightPAS refused a record it could have kept for filing by hand: " + ack.Replace('\r', ' '));
        var admin = await As("admin");
        var list = await admin.GetAsync("/api/devices/records?unmatched=true");
        if (list.Status != 200) return (Outcome.Skip, "The administrator account cannot open Devices › SaferSleep.");
        // The administrator manages devices but has no clinical record access: the episode's readings and anaesthetic record must stay closed.
        if ((await admin.GetAsync("/api/emr/today")).Status == 403 && (await admin.GetAsync("/api/devices/episodes/" + c.EpisodeId)).Status != 403)
            return (Outcome.Fail, "An account without clinical record access can read the monitor readings and anaesthetic record of an episode through the devices screen.");
        if (Find(list.Json, r => Str(r, "caseNumber") == caseNo) is null) return (Outcome.Fail, $"Record {caseNo} was accepted but is not waiting under Devices › SaferSleep – it would be lost.");
        var page = admin.Page;
        await page.GotoAsync($"{bot.O.Url}/devices?tab=safersleep");
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = caseNo });
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 15000 });
        await row.GetByRole(AriaRole.Button, new() { Name = "File to an episode" }).ClickAsync();
        var choice = page.GetByRole(AriaRole.Listitem).Filter(new() { HasText = c.Nhi }).First;
        await Assertions.Expect(choice).ToBeVisibleAsync(new() { Timeout = 10000 });
        var offered = await page.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "NHI " }).AllInnerTextsAsync();
        if (offered.Any(o => !o.Contains(c.Nhi, StringComparison.Ordinal))) return (Outcome.Fail, "Episodes of other patients are offered for a record that carries this patient's NHI.");
        await choice.GetByRole(AriaRole.Button, new() { Name = "File here" }).ClickAsync();
        await Assertions.Expect(page.GetByText($"Case {caseNo} filed to the episode.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        var left = await admin.GetAsync("/api/devices/records?unmatched=true");
        return Find(left.Json, r => Str(r, "caseNumber") == caseNo) is null ? (Outcome.Pass, $"Kept unfiled (not guessed), shown under Devices › SaferSleep, only this patient's episodes offered, and filed by the administrator ({offered.Count} episode(s) to choose from).")
            : (Outcome.Fail, "The screen said the record was filed, but it is still listed as not matched.");
    }
}
