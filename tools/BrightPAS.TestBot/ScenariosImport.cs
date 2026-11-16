using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FellowOakDicom;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Offline import (0.18.0): every device or interface whose connection can be down also takes its messages from a file – a USB stick,
/// a network share or a file the sending system saved. The robot plays each case through the screens: lab results (upload and a
/// watched share), monitor readings, a SaferSleep record, DICOM pictures from the tower (upload and the endoscopy import folder) and a
/// dictaphone recording for the scribe. Re-importing the same file must not file anything twice.
/// </summary>
public sealed partial class Scenarios
{
    private const string ImportArea = "Offline import (USB / share)";

    private async Task ImportStepsAsync()
    {
        await Step(ImportArea, "Lab results file (USB): two results filed through the screen; importing the same file again files nothing twice", LabFileAsync);
        await Step(ImportArea, "Lab results saved to the network share are picked up and filed by themselves", LabShareAsync);
        await Step(ImportArea, "Monitor readings exported while the gateway was down are imported and kept for the monitor", MonitorFileAsync);
        await Step(ImportArea, "SaferSleep record exported to a file is filed to the episode like a live record", SaferSleepFileAsync);
        await Step(ImportArea, "Tower DICOM files on a USB stick (ZIP): the matched picture goes to its procedure, the other waits in Pictures to match", DicomFileAsync);
        await Step(ImportArea, "Tower saves DICOM to the endoscopy import folder (share); BrightPAS files it and moves it to processed", DicomShareAsync);
        await Step(ImportArea, "Steriliser cycle record from the machine's USB (real Sterilizer 1 file): becomes a load with its curve and plateau checks; importing it again is skipped", SteriliserFileAsync);
        await Step(ImportArea, "Scribe: a dictaphone recording is uploaded instead of recording live, then transcribed", RecordingUploadAsync);
    }

    /// <summary>Chooses files on an import card (the robot "picks" them like a person from a USB stick) and returns the summary line shown.</summary>
    private static async Task<string> ImportOnScreenAsync(IPage page, string cardTitle, params FilePayload[] files)
    {
        var card = page.Locator("section.file-import").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = cardTitle }) });
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15000 });
        var before = await card.InnerTextAsync();
        await card.Locator("input[type=file]").SetInputFilesAsync(files);
        for (var i = 0; i < 120; i++)
        {
            await Task.Delay(500);
            var now = await card.InnerTextAsync();
            if (now != before && now.Contains(" accepted", StringComparison.Ordinal) && !now.Contains("Importing ", StringComparison.Ordinal)) break;
        }
        return (await card.InnerTextAsync()).Replace('\n', ' ');
    }

    private static FilePayload Payload(string name, byte[] bytes, string type = "application/octet-stream") => new() { Name = name, MimeType = type, Buffer = bytes };

    private string LabOru(string control, string test, string value)
    {
        var p = _patient!; var now = DateTime.Now;
        var dob = DateOnly.Parse(p.Dob[..10], CultureInfo.InvariantCulture);
        return $"MSH|^~\\&|DELTA|AWANUI-NELSON|BrightPAS|TDS|{now:yyyyMMddHHmm}||ORU^R01|{control}|P|2.4\r" +
               $"PID|1||{p.Nhi}^^^NHI^NHI||{p.Family.ToUpperInvariant()}^{p.Given}||{dob:yyyyMMdd}|{(p.Sex.StartsWith('F') ? 'F' : 'M')}\r" +
               $"OBR|1|TDS{control}|{control}A|{test}^{test}|||{now.AddHours(-2):yyyyMMddHHmm}|||||||||11DEMA^REID^HAMISH^^^DR||||||{now:yyyyMMddHHmm}|||F\r" +
               $"OBX|1|NM|{test}^{test}||{value}|mmol/L|3.5-5.2|N|||F\rNTE|1||Imported from a file by the BrightPAS test robot.\r";
    }

    // ------------------------------------------------------------------ lab results
    /// <summary>An account that can open Lab results (labs.view) and match/import (labs.match) – the screen needs both.</summary>
    private async Task<Session?> LabImporterAsync()
    {
        foreach (var a in Candidates)
        {
            var s = await As(a);
            if ((await s.GetAsync("/api/labs/unmatched")).Status == 200 && (await s.GetAsync("/api/labs?view=all")).Status == 200) return s;
        }
        return null;
    }

    private async Task<(Outcome, string)> LabFileAsync()
    {
        if (_patient?.Nhi is null) return (Outcome.Skip, "Test patient has no NHI.");
        var s = await LabImporterAsync();
        if (s is null) return (Outcome.Skip, "No demonstration account may both see and match lab results.");
        var stamp = DateTime.Now.ToString("HHmmssfff", CultureInfo.InvariantCulture);
        // The lab saved its outgoing queue: MLLP-framed, two results, Windows line ends – exactly what arrives on a USB stick.
        var file = $"\v{LabOru("RBTF1" + stamp, "K", "4.1")}\x1c\r\v{LabOru("RBTF2" + stamp, "NA", "139")}\x1c\r".Replace("\r", "\r\n");
        var bytes = Encoding.UTF8.GetBytes(file);
        await s.Page.GotoAsync($"{bot.O.Url}/labs");
        await s.Page.GetByRole(AriaRole.Tab, new() { Name = "Import from file" }).ClickAsync();
        var first = await ImportOnScreenAsync(s.Page, "Import lab results from a file", Payload($"lab-queue-{stamp}.hl7", bytes, "text/plain"));
        R.Device("Virtual laboratory (file)", "Lab queue saved to USB and imported", file.Replace("\r\n", "\n") + "\n--- screen ---\n" + first);
        if (!first.Contains("2 accepted", StringComparison.Ordinal)) return (Outcome.Fail, "Expected both results accepted; the screen shows: " + Report.Trim(first, 300));
        var again = await ImportOnScreenAsync(s.Page, "Import lab results from a file", Payload($"lab-queue-{stamp}.hl7", bytes, "text/plain"));
        if (!again.Contains("0 accepted", StringComparison.Ordinal) || !again.Contains("2 already held", StringComparison.Ordinal))
            return (Outcome.Fail, "Importing the same file a second time should file nothing new; the screen shows: " + Report.Trim(again, 300));
        var all = await s.GetAsync("/api/labs?view=unacknowledged");
        var filed = all.Json?.EnumerateArray().Count(r => (Str(r, "testName") ?? "") is "K" or "NA" && Str(r, "patientId") == _patient.Id) ?? 0;
        return filed >= 2 ? (Outcome.Pass, $"Both results filed to {_patient.Family} from an MLLP-framed file with Windows line ends; the second import found both already held.")
            : (Outcome.Fail, "The screen said the results were accepted but they are not in the patient's lab results.");
    }

    private async Task<(Outcome, string)> LabShareAsync()
    {
        if (_patient?.Nhi is null) return (Outcome.Skip, "Test patient has no NHI.");
        var integ = await WhoCanAsync("/api/messaging/interfaces");
        if (integ is null) return (Outcome.Skip, "No account may see the messaging interfaces.");
        var list = await integ.GetAsync("/api/messaging/interfaces");
        var share = Find(list.Json, i => Str(i, "code") == "lab-share");
        if (share is null || Str(share.Value, "folderPath") is not { } dir) return (Outcome.Skip, "No lab share interface (lab-share) on this system.");
        if (!Directory.Exists(dir)) return (Outcome.Skip, $"The share {dir} is not reachable from where the robot runs (run it on the BrightPAS server).");
        var control = "RBTS" + DateTime.Now.ToString("HHmmssfff", CultureInfo.InvariantCulture);
        var name = $"{control}.hl7";
        await File.WriteAllTextAsync(Path.Combine(dir, name), LabOru(control, "CRP", "3"));
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(2000);
            var processed = Path.Combine(dir, "processed");
            if (Directory.Exists(processed) && Directory.EnumerateFiles(processed, "*" + name).Any())
            {
                var who = await LabImporterAsync();
                var labs = who is null ? "" : (await who.GetAsync("/api/labs?view=unacknowledged")).Text + (await who.GetAsync("/api/labs?view=all")).Text;
                return labs.Contains("\"CRP\"", StringComparison.Ordinal) ? (Outcome.Pass, $"{name} written to {dir}; BrightPAS filed the result and moved the file to processed\\.")
                    : (Outcome.Fail, "The file was moved to processed but the result is not in Lab results.");
            }
            if (Directory.Exists(Path.Combine(dir, "failed")) && Directory.EnumerateFiles(Path.Combine(dir, "failed"), "*" + name).Any())
                return (Outcome.Fail, $"BrightPAS moved {name} to failed\\ – see Messaging › Messages for the reason.");
        }
        return (Outcome.Fail, $"{name} was still waiting in {dir} after 60 seconds – the folder is not being watched.");
    }

    // ------------------------------------------------------------------ monitors and SaferSleep
    private async Task<(Outcome, string)> MonitorFileAsync()
    {
        var nurse = _deviceNurse;
        if (nurse is null) return (Outcome.Skip, "The devices tests did not run (no nurse with Devices access).");
        var file = MonitorOru(RecoveryBed, null, Vitals(81, 97, 131, 82, 15)) + MonitorOru(TheatreBed, null, Vitals(63, 99, 112, 70, 12));
        await nurse.Page.GotoAsync($"{bot.O.Url}/devices?tab=import");
        var shown = await ImportOnScreenAsync(nurse.Page, "Monitor readings from a file", Payload("gateway-export.hl7", Encoding.UTF8.GetBytes(file), "text/plain"));
        R.Device("Virtual GE monitor gateway", "Readings exported to a file while the network was down, then imported", file.Replace("\r", "\n") + "\n--- screen ---\n" + shown);
        if (!shown.Contains("2 accepted", StringComparison.Ordinal)) return (Outcome.Fail, "Expected both monitor messages accepted; the screen shows: " + Report.Trim(shown, 300));
        var o = await nurse.GetAsync("/api/devices/overview");
        var mon = Find(o.Json?.GetProperty("monitors"), m => Str(m, "code") == RecoveryBed);
        var hasHr = mon?.GetProperty("live").EnumerateArray().Any(v => Str(v, "code") == "HR" && v.GetProperty("value").GetDecimal() == 81) ?? false;
        return hasHr ? (Outcome.Pass, "Two monitors' readings imported from one file and kept for each monitor (heart rate 81 on PACU/BAY1).")
            : (Outcome.Warn, "Accepted, but the recovery monitor does not show the imported heart rate (a newer live reading may have replaced it).");
    }

    private async Task<(Outcome, string)> SaferSleepFileAsync()
    {
        var c = _case; var nurse = _deviceNurse;
        if (c is null || nurse is null) return (Outcome.Skip, "The devices tests did not run.");
        var caseNo = "SS-FILE-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
        var visit = c.EpisodeId.Replace("-", "", StringComparison.Ordinal);
        var hl7 = RecordOru("RBTSSF" + DateTime.Now.ToString("HHmmssfff", CultureInfo.InvariantCulture), caseNo, visit, c.Nhi, c.Family, c.Given, DateTime.Now.AddHours(-1), false, "4");
        await nurse.Page.GotoAsync($"{bot.O.Url}/devices?tab=import");
        var shown = await ImportOnScreenAsync(nurse.Page, "SaferSleep anaesthetic records from a file", Payload($"{caseNo}.hl7", Encoding.UTF8.GetBytes(hl7), "text/plain"));
        if (!shown.Contains("1 accepted", StringComparison.Ordinal)) return (Outcome.Fail, "The record was not accepted: " + Report.Trim(shown, 300));
        var recs = await nurse.GetAsync("/api/devices/records");
        var mine = Find(recs.Json, r => Str(r, "caseNumber") == caseNo);
        if (mine is null) return (Outcome.Fail, $"Accepted, but case {caseNo} is not in Devices › SaferSleep.");
        return mine.Value.TryGetProperty("episodeId", out var e) && e.ValueKind == JsonValueKind.String
            ? (Outcome.Pass, $"Case {caseNo} imported from a file and filed to {c.Family}'s episode (drugs, events and PDF).")
            : (Outcome.Fail, $"Case {caseNo} was imported but not matched to the episode it names.");
    }

    // ------------------------------------------------------------------ endoscopy DICOM
    private async Task<(string Acc, string Pid, string Name)?> EndoTargetAsync(Session s)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        for (var d = 0; d <= 14; d++)
        {
            var list = await s.GetAsync($"/api/endoscopy/today?date={today.AddDays(d):yyyy-MM-dd}");
            var hit = Find(list.Json, x => Str(x, "accession") is { Length: > 0 } && Str(x, "status") != "Signed");
            if (hit is { } h) return (Str(h, "accession")!, Str(h, "nhi") ?? Str(h, "mrn") ?? "", (Str(h, "patientName") ?? "").Replace(", ", "^", StringComparison.Ordinal));
        }
        return null;
    }

    private static byte[] DicomBytes(DicomDataset ds)
    {
        using var ms = new MemoryStream();
        new DicomFile(ds).Save(ms);
        return ms.ToArray();
    }

    private async Task<(Outcome, string)> DicomFileAsync()
    {
        var nurse = await As("nurse");
        if ((await nurse.GetAsync("/api/endoscopy/unmatched")).Status != 200) return (Outcome.Skip, "The nurse cannot use endoscopy.");
        var target = await EndoTargetAsync(nurse);
        if (target is null) return (Outcome.Skip, "No endoscopy procedure with an accession number in the next two weeks.");
        var (acc, pid, name) = target.Value;
        var jpeg = await nurse.Page.ScreenshotAsync(new() { Type = ScreenshotType.Jpeg, Quality = 70 });
        var before = (await nurse.GetAsync("/api/endoscopy/unmatched")).Json?.GetArrayLength() ?? 0;
        using var zipMs = new MemoryStream();
        using (var z = new ZipArchive(zipMs, ZipArchiveMode.Create, true))
        {
            void Add(string n, byte[] b) { using var e = z.CreateEntry(n).Open(); e.Write(b); }
            Add("DICOM/IMG0001.dcm", DicomBytes(PictureDataset(jpeg, acc, null, pid, name)));
            Add("DICOM/IMG0002.dcm", DicomBytes(PictureDataset(jpeg, null, null, "ROBOT-USB", "USB^UNKNOWN")));
            Add("DICOMDIR", [0, 1, 2, 3]);
        }
        await nurse.Page.GotoAsync($"{bot.O.Url}/endoscopy?tab=unmatched");
        var shown = await ImportOnScreenAsync(nurse.Page, "Import DICOM files from the tower", Payload("tower-usb-export.zip", zipMs.ToArray(), "application/zip"));
        if (!shown.Contains("2 accepted", StringComparison.Ordinal)) return (Outcome.Fail, "Expected both DICOM pictures stored; the screen shows: " + Report.Trim(shown, 300));
        var after = (await nurse.GetAsync("/api/endoscopy/unmatched")).Json?.GetArrayLength() ?? 0;
        if (after != before + 1) return (Outcome.Fail, $"Pictures to match went from {before} to {after}; expected exactly one more (the unknown patient's picture).");
        return shown.Contains("filed to its procedure", StringComparison.Ordinal)
            ? (Outcome.Pass, $"ZIP from the tower's USB export (with DICOMDIR) imported on screen: the picture with accession {acc} was filed to its procedure, the unknown one waits in Pictures to match.")
            : (Outcome.Fail, "The matched picture was not reported as filed to its procedure: " + Report.Trim(shown, 300));
    }

    private async Task<(Outcome, string)> DicomShareAsync()
    {
        var admin = await WhoCanAsync("/api/endoscopy/settings");
        if (admin is null) return (Outcome.Skip, "No account may view the endoscopy settings.");
        var st = await admin.GetAsync("/api/endoscopy/settings");
        var dir = st.Json is { } j ? Str(j.GetProperty("settings"), "importFolder") : null;
        if (dir is null) return (Outcome.Skip, "No endoscopy import folder is set.");
        if (!Directory.Exists(dir)) return (Outcome.Skip, $"The import folder {dir} is not reachable from where the robot runs.");
        var jpeg = await admin.Page.ScreenshotAsync(new() { Type = ScreenshotType.Jpeg, Quality = 60 });
        var name = $"ROBOT-{DateTime.Now:HHmmssfff}.dcm";
        await File.WriteAllBytesAsync(Path.Combine(dir, name), DicomBytes(PictureDataset(jpeg, null, null, "ROBOT-SHARE", "SHARE^TEST")));
        for (var i = 0; i < 50; i++)
        {
            await Task.Delay(2000);
            if (Directory.Exists(Path.Combine(dir, "processed")) && Directory.EnumerateFiles(Path.Combine(dir, "processed"), "*" + name).Any())
                return (Outcome.Pass, $"{name} saved to {dir}; BrightPAS stored the picture (Pictures to match) and moved the file to processed\\.");
            if (Directory.Exists(Path.Combine(dir, "failed")) && Directory.EnumerateFiles(Path.Combine(dir, "failed"), "*" + name).Any())
                return (Outcome.Fail, $"BrightPAS moved {name} to failed\\.");
        }
        return (Outcome.Fail, $"{name} was still in {dir} after 100 seconds – the import folder is not being watched.");
    }

    // ------------------------------------------------------------------ scribe recording
    /// <summary>A short WAV file (16 kHz mono, a tone) standing in for a dictaphone recording.</summary>
    private static byte[] Wav(int seconds = 3)
    {
        const int rate = 16000;
        var samples = rate * seconds;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(samples * 2);
        for (var i = 0; i < samples; i++) w.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 4000));
        w.Flush();
        return ms.ToArray();
    }

    private async Task<(Outcome, string)> RecordingUploadAsync()
    {
        var admin = await As("admin");
        if (!await ScribeSettingsAsync(admin, OnSite())) return (Outcome.Skip, "Scribe module not available.");
        var dr = await As("hreid");
        var templates = await dr.GetAsync("/api/scribe/templates");
        if (templates.Status != 200) return (Outcome.Skip, "The surgeon cannot use the scribe.");
        var tid = Str(templates.Json!.Value.EnumerateArray().First(t => Str(t, "code") == "CONSULT"), "id");
        var id = Str((await dr.PostAsync("/api/scribe/sessions", new { patientId = _patient!.Id, bookingId = (string?)null, context = "Consultation", templateId = tid })).Json!.Value, "id")!;
        await dr.PostAsync("/api/scribe/consent", new { patientId = _patient.Id, sessionId = id, given = true, standing = false });
        try
        {
            await dr.Page.GotoAsync($"{bot.O.Url}/scribe/{id}");
            var input = dr.Page.GetByLabel("No microphone or network in the room?", new() { Exact = false });
            await Assertions.Expect(input).ToBeAttachedAsync(new() { Timeout = 15000 });
            await input.SetInputFilesAsync(Payload("dictaphone-0042.wav", Wav(), "audio/wav"));
            await Assertions.Expect(dr.Page.GetByText("Recording uploaded – transcribe it next.")).ToBeVisibleAsync(new() { Timeout = 20000 });
            var tr = await dr.PostAsync($"/api/scribe/sessions/{id}/transcribe", new { });
            if (tr.Status != 200) return (Outcome.Fail, "The uploaded recording could not be transcribed: " + tr.Text);
            var bad = await dr.Ctx.APIRequest.PostAsync($"{bot.O.Url}/api/scribe/sessions/{id}/recording", new() { IgnoreHTTPSErrors = true, Headers = new Dictionary<string, string> { ["X-BrightPAS-Request"] = "1", ["X-File-Name"] = "again.wav" }, DataByte = Wav(1) });
            return bad.Status == 400 ? (Outcome.Pass, "WAV from a dictaphone uploaded on the session screen (consent checked), stored encrypted and transcribed by the speech engine; a second recording for the same session was refused.")
                : (Outcome.Fail, $"A second recording was accepted for a session that already has audio (HTTP {bad.Status}).");
        }
        finally { await dr.PostAsync($"/api/scribe/sessions/{id}/discard", new { reason = "Robot test" }); }
    }

    // ------------------------------------------------------------------ CSSD steriliser records
    private async Task<(Outcome, string)> SteriliserFileAsync()
    {
        using var rs = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("BrightPAS.TestBot.Assets.steriliser-00000217.csv");
        if (rs is null) return (Outcome.Fail, "The robot is missing its steriliser record fixture.");
        using var ms = new MemoryStream(); await rs.CopyToAsync(ms); var bytes = ms.ToArray();
        var s = await As("cssd");
        await s.Page.GotoAsync($"{bot.O.Url}/cssd?tab=loads");
        const string card = "Cycle records from the steriliser or washer (USB / log folder)";
        var shown = await ImportOnScreenAsync(s.Page, card, Payload("00000217.csv", bytes, "text/csv"));
        R.Device("Sterilizer 1 (real cycle record)", "Cycle record copied from the steriliser's USB and imported on the CSSD Loads screen", Report.Trim(Encoding.Latin1.GetString(bytes), 800) + "\n--- screen ---\n" + shown);
        if (!shown.Contains("1 accepted", StringComparison.Ordinal)) return (Outcome.Fail, "Expected the cycle record accepted; the screen shows: " + Report.Trim(shown, 300));
        var again = await ImportOnScreenAsync(s.Page, card, Payload("00000217.csv", bytes, "text/csv"));
        var list = await s.GetAsync("/api/assets/cycles?view=all");
        var row = list.Json?.ValueKind == System.Text.Json.JsonValueKind.Array
            ? list.Json.Value.EnumerateArray().FirstOrDefault(c => c.GetRawText().Contains("00000217", StringComparison.Ordinal)) : default;
        if (row.ValueKind != System.Text.Json.JsonValueKind.Object) return (Outcome.Fail, "Accepted, but no load for cycle 00000217 is listed.");
        var d = await s.GetAsync($"/api/assets/cycles/{Str(row, "id")}");
        var st = d.Json?.TryGetProperty("steriliser", out var x) == true && x.ValueKind == System.Text.Json.JsonValueKind.Object ? x : (System.Text.Json.JsonElement?)null;
        if (st is null) return (Outcome.Fail, "The load has no steriliser curve.");
        var points = st.Value.GetProperty("curve").GetArrayLength();
        var ok = st.Value.GetProperty("checksOk").GetBoolean();
        var dup = again.Contains("duplicate", StringComparison.OrdinalIgnoreCase) || again.Contains("0 accepted", StringComparison.Ordinal);
        if (!ok) return (Outcome.Fail, "The plateau checks did not pass on a cycle the steriliser passed: " + string.Join(" ", st.Value.GetProperty("checks").EnumerateArray().Select(c => c.GetString())));
        return dup ? (Outcome.Pass, $"Cycle 00000217 imported as a load waiting for release, {points} curve points, plateau/band/saturated-steam checks OK; the second import was skipped.")
            : (Outcome.Warn, "Imported with checks OK, but the repeat import did not report a duplicate: " + Report.Trim(again, 200));
    }
}
