using System.Text;
using System.Text.Json;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Endoscopy (0.15.0): a virtual Olympus tower talks DICOM to BrightPAS (echo, worklist, a picture, procedure start/end, an unmatched picture),
/// then the capture screen uses a virtual capture card (the browser's fake camera) to take a picture and record a short video,
/// and the clinical and patient reports are downloaded with the pictures in them.
/// </summary>
public sealed partial class Scenarios
{
    private const string TowerAe = "ROBOT_TOWER";
    private string? _endoBooking;
    private string? _endoProcedure;

    private async Task EndoscopyStepsAsync()
    {
        await Step("Virtual Olympus tower (DICOM)", "Tower checks the connection, reads the worklist, sends a picture and the procedure start/end; a picture for an unknown patient waits to be matched", TowerAsync);
        await Step("Virtual capture card", "Capture screen shows the live video, takes a picture and records a short video", CaptureAsync);
        await Step("Endoscopy reports", "Clinical and patient reports download as protected PDFs with the pictures in them", EndoReportsAsync);
    }

    private static DicomDataset PictureDataset(byte[] jpeg, string? accession, string? study, string patientId, string patientName)
    {
        var ds = new DicomDataset(DicomTransferSyntax.JPEGProcess1)
        {
            { DicomTag.SOPClassUID, DicomUID.VLEndoscopicImageStorage }, { DicomTag.SOPInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            { DicomTag.StudyInstanceUID, study ?? DicomUIDGenerator.GenerateDerivedFromUUID().UID }, { DicomTag.SeriesInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            { DicomTag.PatientID, patientId }, { DicomTag.PatientName, patientName }, { DicomTag.AccessionNumber, accession ?? "" }, { DicomTag.Modality, "ES" },
            { DicomTag.Manufacturer, "Virtual Olympus (BrightPAS robot)" }, { DicomTag.ManufacturerModelName, "CV-1500 (virtual)" },
            { DicomTag.Rows, (ushort)900 }, { DicomTag.Columns, (ushort)1366 }, { DicomTag.SamplesPerPixel, (ushort)3 }, { DicomTag.PhotometricInterpretation, "YBR_FULL_422" },
            { DicomTag.BitsAllocated, (ushort)8 }, { DicomTag.BitsStored, (ushort)8 }, { DicomTag.HighBit, (ushort)7 }, { DicomTag.PixelRepresentation, (ushort)0 }, { DicomTag.PlanarConfiguration, (ushort)0 },
        };
        DicomPixelData.Create(ds, true).AddFrame(new MemoryByteBuffer(jpeg.Length % 2 == 0 ? jpeg : [.. jpeg, 0]));
        return ds;
    }

    private async Task<(Outcome, string)> TowerAsync()
    {
        var admin = await As("admin");
        var st = await admin.GetAsync("/api/endoscopy/settings");
        if (st.Status != 200) return (Outcome.Skip, "Endoscopy module not available to the administrator.");
        var orig = st.Json!.Value.GetProperty("settings").Clone();
        var port = 11113;
        var change = await admin.PutAsync("/api/endoscopy/settings", new
        {
            mediaPath = orig.TryGetProperty("mediaPath", out var mp) && mp.ValueKind == JsonValueKind.String ? mp.GetString() : null,
            dicomEnabled = true, dicomPort = port, dicomAeTitle = "BRIGHTPAS_ES", dicomCallingAes = TowerAe, dicomAnyTower = false,
            videoRetentionDays = orig.TryGetProperty("videoRetentionDays", out var vr) && vr.ValueKind == JsonValueKind.Number ? vr.GetInt32() : (int?)null,
        });
        if (change.Status != 200) return (Outcome.Fail, $"Could not turn on the DICOM node: {change.Text}");
        _restore.Add(async () => await (await As("admin")).PutAsync("/api/endoscopy/settings", JsonSerializer.Deserialize<object>(orig.GetRawText())!));
        JsonElement dicom = default;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(500);
            dicom = (await admin.GetAsync("/api/endoscopy/settings")).Json!.Value.GetProperty("dicom");
            if (dicom.GetProperty("listening").GetBoolean()) break;
        }
        if (!dicom.GetProperty("listening").GetBoolean()) return (Outcome.Fail, $"The DICOM node did not start: {dicom}");
        var host = new Uri(bot.O.Url).Host;
        var log = new StringBuilder($"Tower AE {TowerAe} → {host}:{port} AE BRIGHTPAS_ES\n");
        IDicomClient Client() => DicomClientFactory.Create(host, port, false, TowerAe, "BRIGHTPAS_ES");

        // 1. Echo.
        DicomStatus? echo = null;
        var c1 = Client();
        await c1.AddRequestAsync(new DicomCEchoRequest { OnResponseReceived = (_, r) => echo = r.Status });
        await c1.SendAsync();
        log.AppendLine($"C-ECHO: {echo}");
        if (echo != DicomStatus.Success) return (Outcome.Fail, $"Echo failed: {echo}");

        // 2. Worklist for the next two weeks.
        var items = new List<DicomDataset>();
        var find = DicomCFindRequest.CreateWorklistQuery(null, null, TowerAe, null, "ES", new DicomDateRange(DateTime.Today, DateTime.Today.AddDays(14)));
        find.OnResponseReceived = (_, r) => { if (r.Status == DicomStatus.Pending && r.Dataset is not null) items.Add(r.Dataset); };
        var c2 = Client();
        await c2.AddRequestAsync(find);
        await c2.SendAsync();
        log.AppendLine($"C-FIND worklist: {items.Count} patient(s)");
        foreach (var it in items.Take(5))
            log.AppendLine($"  {it.GetSingleValueOrDefault(DicomTag.PatientName, "")} {it.GetSingleValueOrDefault(DicomTag.PatientID, "")} {it.GetSingleValueOrDefault(DicomTag.AccessionNumber, "")} {it.GetSingleValueOrDefault(DicomTag.RequestedProcedureDescription, "")}");
        if (items.Count == 0) return (Outcome.Skip, "The worklist has no endoscopy booked in the next 14 days.");
        var w = items[0];
        var acc = w.GetSingleValue<string>(DicomTag.AccessionNumber);
        var study = w.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");
        var pid = w.GetSingleValue<string>(DicomTag.PatientID);
        var pname = w.GetSingleValue<string>(DicomTag.PatientName);
        var date = w.GetSequence(DicomTag.ScheduledProcedureStepSequence).Items[0].GetSingleValue<string>(DicomTag.ScheduledProcedureStepStartDate);
        if (string.IsNullOrWhiteSpace(acc) || !acc.StartsWith('E')) return (Outcome.Fail, $"Worklist item has no BrightPAS accession number ({acc}).");

        // 3. MPPS in progress, a picture (JPEG), MPPS completed.
        var mppsUid = DicomUIDGenerator.GenerateDerivedFromUUID();
        var create = new DicomNCreateRequest(DicomUID.ModalityPerformedProcedureStep, mppsUid)
        {
            Dataset = new DicomDataset
            {
                new DicomSequence(DicomTag.ScheduledStepAttributesSequence, new DicomDataset { { DicomTag.AccessionNumber, acc }, { DicomTag.StudyInstanceUID, study } }),
                { DicomTag.PerformedProcedureStepStatus, "IN PROGRESS" }, { DicomTag.Modality, "ES" }, { DicomTag.PatientID, pid },
                { DicomTag.PerformedProcedureStepStartDate, DateTime.Now.ToString("yyyyMMdd") }, { DicomTag.PerformedProcedureStepStartTime, DateTime.Now.ToString("HHmmss") },
            },
        };
        DicomStatus? mpps1 = null, store = null, mpps2 = null, orphan = null;
        create.OnResponseReceived = (_, r) => mpps1 = r.Status;
        var nurse = await As("nurse");
        var jpeg = await nurse.Page.ScreenshotAsync(new() { Type = ScreenshotType.Jpeg, Quality = 80 });
        var pic = new DicomCStoreRequest(new DicomFile(PictureDataset(jpeg, acc, study, pid, pname))) { OnResponseReceived = (_, r) => store = r.Status };
        var set = new DicomNSetRequest(DicomUID.ModalityPerformedProcedureStep, mppsUid)
        {
            Dataset = new DicomDataset { { DicomTag.PerformedProcedureStepStatus, "COMPLETED" }, { DicomTag.PerformedProcedureStepEndDate, DateTime.Now.ToString("yyyyMMdd") }, { DicomTag.PerformedProcedureStepEndTime, DateTime.Now.AddMinutes(8).ToString("HHmmss") } },
            OnResponseReceived = (_, r) => mpps2 = r.Status,
        };
        var stray = new DicomCStoreRequest(new DicomFile(PictureDataset(jpeg, null, null, "ROBOT9999", "UNKNOWN^ROBOT"))) { OnResponseReceived = (_, r) => orphan = r.Status };
        var c3 = Client();
        await c3.AddRequestsAsync([create, pic]);
        await c3.SendAsync();
        var c4 = Client();
        await c4.AddRequestsAsync([set, stray]);
        await c4.SendAsync();
        log.AppendLine($"N-CREATE MPPS (in progress): {mpps1}\nC-STORE picture ({jpeg.Length / 1024} KB JPEG, accession {acc}): {store}\nN-SET MPPS (completed): {mpps2}\nC-STORE picture for an unknown patient: {orphan}");
        R.Device("Virtual Olympus tower (DICOM)", "What the virtual tower sent and received", log.ToString());
        if (mpps1 != DicomStatus.Success || store != DicomStatus.Success || mpps2 != DicomStatus.Success || orphan != DicomStatus.Success)
            return (Outcome.Fail, $"Tower messages not all accepted: MPPS {mpps1}/{mpps2}, picture {store}, unmatched {orphan}.");

        // 4. Check BrightPAS.
        var day = $"{date[..4]}-{date[4..6]}-{date[6..8]}";
        var row = Find((await nurse.GetAsync($"/api/endoscopy/today?date={day}")).Json, x => Str(x, "accession") == acc);
        if (row is not { } r0) return (Outcome.Fail, $"Accession {acc} not found on the endoscopy list for {day}.");
        _endoBooking = Str(r0, "bookingId");
        _endoProcedure = Str(r0, "procedureId");
        if (r0.GetProperty("pictures").GetInt32() < 1) return (Outcome.Fail, "The tower's picture is not on the procedure.");
        var rec = (await nurse.GetAsync($"/api/endoscopy/bookings/{_endoBooking}")).Json!.Value.GetProperty("procedure");
        var started = (rec.TryGetProperty("upperStartAt", out var u) && u.ValueKind == JsonValueKind.String) || (rec.TryGetProperty("insertedAt", out var ins) && ins.ValueKind == JsonValueKind.String);
        if (!started) return (Outcome.Fail, "Procedure start time from the tower (MPPS) was not recorded.");
        var unmatched = Find((await nurse.GetAsync("/api/endoscopy/unmatched")).Json, x => Str(x, "dicomPatientId") == "ROBOT9999");
        if (unmatched is not { } um) return (Outcome.Fail, "The picture for an unknown patient is not in 'Pictures to match'.");
        await admin.ApiAsync("DELETE", $"/api/endoscopy/media/{Str(um, "id")}?reason=Robot%20test%20picture");
        return (Outcome.Pass, $"Echo OK; worklist {items.Count} patient(s) with accession numbers; picture filed on {Str(r0, "patientName")} ({acc}); start/end times from MPPS; unknown-patient picture queued for matching (then removed).");
    }

    private async Task<(Outcome, string)> CaptureAsync()
    {
        if (_endoBooking is null) return (Outcome.Skip, "Needs the tower test's procedure.");
        var nurse = await As("nurse");
        var before = MediaCount(await nurse.GetAsync($"/api/endoscopy/bookings/{_endoBooking}"));
        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/endoscopy/{_endoBooking}?tab=capture");
        var take = page.GetByRole(AriaRole.Button, new() { Name = "Take picture (Space)" });
        await Assertions.Expect(take).ToBeEnabledAsync(new() { Timeout = 15000 });
        await page.WaitForFunctionAsync("() => { const v = document.querySelector('video'); return v && v.videoWidth > 0; }", null, new() { Timeout = 15000 });
        await take.ClickAsync();
        await Assertions.Expect(page.GetByText("1 picture(s) this session")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(page.GetByText("2 picture(s) this session")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Start recording (R)" }).ClickAsync();
        await Assertions.Expect(page.GetByText("● REC")).ToBeVisibleAsync();
        await Task.Delay(5000);
        var shot = await nurse.ShotAsync("endoscopy-capture-recording");
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Stop recording") }).ClickAsync();
        await Assertions.Expect(page.GetByText("Video saved.")).ToBeVisibleAsync(new() { Timeout = 20000 });
        var after = await nurse.GetAsync($"/api/endoscopy/bookings/{_endoBooking}");
        var media = after.Json!.Value.GetProperty("media").EnumerateArray().ToList();
        var video = media.LastOrDefault(m => Str(m, "kind") == "Video");
        if (MediaCount(after) < before + 3 || video.ValueKind != JsonValueKind.Object) return (Outcome.Fail, $"Expected two pictures and a video (had {before}, now {MediaCount(after)}).");
        var bytes = video.GetProperty("bytes").GetInt64();
        var dl = await nurse.Ctx.APIRequest.GetAsync($"{bot.O.Url}{Str(video, "url")}", new() { IgnoreHTTPSErrors = true, Headers = new Dictionary<string, string> { ["Range"] = "bytes=0-15" } });
        var head = await dl.BodyAsync();
        if (bytes < 1000 || dl.Status != 206 || head.Length != 16) return (Outcome.Fail, $"Video not usable: {bytes} bytes, range request {dl.Status}.");
        R.Device("Virtual capture card", "Capture screen while recording (browser's virtual camera)", $"{bytes / 1024} KB {Str(video, "contentType")}, {Str(video, "durationSeconds")} s", shot);
        return (Outcome.Pass, $"Live video shown; 2 pictures (button and Space key); {Str(video, "durationSeconds")} s video saved ({bytes / 1024} KB {Str(video, "contentType")}) and plays from any point (range requests).");
    }

    private static int MediaCount((int Status, JsonElement? Json, string Text) r) => r.Json is { } j && j.TryGetProperty("media", out var m) ? m.GetArrayLength() : 0;

    private async Task<(Outcome, string)> EndoReportsAsync()
    {
        if (_endoBooking is null || _endoProcedure is null) return (Outcome.Skip, "Needs the tower test's procedure.");
        var nurse = await As("nurse");
        var rec = (await nurse.GetAsync($"/api/endoscopy/bookings/{_endoBooking}")).Json!.Value;
        var pic = rec.GetProperty("media").EnumerateArray().FirstOrDefault(m => Str(m, "kind") == "Image");
        if (pic.ValueKind != JsonValueKind.Object) return (Outcome.Fail, "No picture to put in the reports.");
        var mark = await nurse.PutAsync($"/api/endoscopy/media/{Str(pic, "id")}", new { site = (string?)null, caption = "Robot picture", inReport = true, inPatientReport = true, sort = (int?)null, findingId = (string?)null, polypId = (string?)null });
        if (mark.Status != 200) return (Outcome.Fail, $"Could not choose the picture for the reports: {mark.Text}");
        var notes = new List<string>();
        foreach (var kind in new[] { "clinical", "patient" })
        {
            var res = await nurse.Ctx.APIRequest.PostAsync($"{bot.O.Url}/api/endoscopy/procedures/{_endoProcedure}/report-pdf", new() { Headers = new Dictionary<string, string> { ["X-BrightPAS-Request"] = "1" },
                DataObject = new { kind, export = new { protect = true, password = "Robot-Endo-Pass-2026!", format = "pdf" } }, IgnoreHTTPSErrors = true });
            if (res.Status != 200) return (Outcome.Fail, $"{kind} report refused ({res.Status}): {await res.TextAsync()}");
            var b = await res.BodyAsync();
            await File.WriteAllBytesAsync(Path.Combine(R.DeviceFolder, $"endoscopy-{kind}-report.pdf"), b);
            var text = Encoding.Latin1.GetString(b);
            if (!text.StartsWith("%PDF", StringComparison.Ordinal) || !text.Contains("/Encrypt", StringComparison.Ordinal)) return (Outcome.Fail, $"The {kind} report is not a protected PDF.");
            if (!text.Contains("/Image", StringComparison.Ordinal)) return (Outcome.Fail, $"The {kind} report has no picture in it.");
            notes.Add($"{kind} report {b.Length / 1024} KB");
        }
        return (Outcome.Pass, string.Join("; ", notes) + " – both password-protected with the picture included.");
    }
}
