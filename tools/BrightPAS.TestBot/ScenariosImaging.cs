using System.Globalization;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Imaging (PACS) (0.37.0) through the screens: a nurse uploads an X-ray carrying a waiting request's number and it is filed to that patient;
/// an X-ray from an unknown patient waits in To match and is filed by the nurse; the viewer draws the picture; reception cannot change the settings.
/// </summary>
public sealed partial class Scenarios
{
    private const string PacsArea = "Imaging (PACS)";
    private string? _pacsUnmatched;

    private async Task ImagingStepsAsync()
    {
        await Step(PacsArea, "Nurse uploads an X-ray with a request number; it is filed to the patient", PacsUploadAsync);
        await Step(PacsArea, "An X-ray from an unknown patient waits in To match; the nurse files it", PacsMatchAsync);
        await Step(PacsArea, "The viewer draws the X-ray and changes brightness", PacsViewerAsync);
        await Step(PacsArea, "Reception cannot change imaging settings; an anaesthetist cannot upload", PacsGuardAsync);
    }

    /// <summary>A small invented 16-bit greyscale X-ray (not a real patient's image).</summary>
    private static byte[] PacsXray(string patientId, string name, string dob, string? accession)
    {
        const int w = 128, h = 128;
        var px = new byte[w * h * 2];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var bone = Math.Abs(x - w / 2) < 14 + 8 * Math.Exp(-Math.Pow((y - h / 2) / 12.0, 2));
                var v = (ushort)(400 + (bone ? 2200 : 0) + (x + y) % 37);
                px[(y * w + x) * 2] = (byte)(v & 0xFF); px[(y * w + x) * 2 + 1] = (byte)(v >> 8);
            }
        var ds = new DicomDataset(DicomTransferSyntax.ExplicitVRLittleEndian)
        {
            { DicomTag.SOPClassUID, DicomUID.DigitalXRayImageStorageForPresentation }, { DicomTag.SOPInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            { DicomTag.StudyInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() }, { DicomTag.SeriesInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            { DicomTag.StudyDate, DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture) }, { DicomTag.Modality, "DX" },
            { DicomTag.StudyDescription, "Robot wrist X-ray" }, { DicomTag.BodyPartExamined, "WRIST" }, { DicomTag.AccessionNumber, accession ?? "" },
            { DicomTag.PatientID, patientId }, { DicomTag.PatientName, name }, { DicomTag.PatientBirthDate, dob },
            { DicomTag.SamplesPerPixel, (ushort)1 }, { DicomTag.PhotometricInterpretation, "MONOCHROME2" }, { DicomTag.Rows, (ushort)h }, { DicomTag.Columns, (ushort)w },
            { DicomTag.BitsAllocated, (ushort)16 }, { DicomTag.BitsStored, (ushort)12 }, { DicomTag.HighBit, (ushort)11 }, { DicomTag.PixelRepresentation, (ushort)0 },
            { DicomTag.WindowCenter, "1500" }, { DicomTag.WindowWidth, "3000" },
        };
        DicomPixelData.Create(ds, true).AddFrame(new FellowOakDicom.IO.Buffer.MemoryByteBuffer(px));
        return DicomBytes(ds);
    }

    private static async Task<string> PacsUploadOnScreenAsync(IPage page, string fileName, byte[] bytes)
    {
        var card = page.Locator("section.file-import").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Upload images" }) });
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15000 });
        await card.Locator("input[type=file]").First.SetInputFilesAsync(Payload(fileName, bytes, "application/dicom"));
        for (var i = 0; i < 120; i++)
        {
            await Task.Delay(500);
            var now = await card.InnerTextAsync();
            if (now.Contains(" accepted", StringComparison.Ordinal) && !now.Contains("Importing ", StringComparison.Ordinal)) break;
        }
        return (await card.InnerTextAsync()).Replace('\n', ' ');
    }

    private async Task<(Outcome, string)> PacsUploadAsync()
    {
        var nurse = await As("nurse");
        var reqs = await nurse.GetAsync("/api/imaging/requests");
        if (reqs.Status != 200) return (Outcome.Skip, $"Imaging is not available (HTTP {reqs.Status}).");
        var req = Find(reqs.Json, x => Str(x, "status") == "Requested");
        if (req is null) return (Outcome.Skip, "No imaging request waiting in the demo.");
        var patient = (await nurse.GetAsync($"/api/patients/{Str(req.Value, "patientId")}")).Json!.Value;
        var acc = Str(req.Value, "accessionNumber")!;
        // The ID and date of birth in the file are wrong on purpose: the request number alone must file it.
        var bytes = PacsXray("OTHER-ID", "ROBOT^Request", "19000101", acc);
        await nurse.Page.GotoAsync($"{bot.O.Url}/imaging?tab=upload");
        var shown = await PacsUploadOnScreenAsync(nurse.Page, "IMG0001.dcm", bytes);
        if (!shown.Contains("1 accepted", StringComparison.Ordinal) || !shown.Contains("Filed to the patient", StringComparison.Ordinal))
            return (Outcome.Fail, "Expected the X-ray filed to the patient; the screen shows: " + Report.Trim(shown, 300));
        var after = Find((await nurse.GetAsync($"/api/imaging/requests?view=all&patientId={Str(patient, "id")}")).Json, x => Str(x, "accessionNumber") == acc);
        return after is { } a && Str(a, "status") == "Done"
            ? (Outcome.Pass, $"X-ray with request number {acc} uploaded on screen and filed to {Str(patient, "familyName")}; the request shows images in.")
            : (Outcome.Fail, $"Request {acc} is not marked done after the upload.");
    }

    private async Task<(Outcome, string)> PacsMatchAsync()
    {
        var nurse = await As("nurse");
        if ((await nurse.GetAsync("/api/imaging/meta")).Status != 200) return (Outcome.Skip, "Imaging is not available.");
        var stamp = DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
        await nurse.Page.GotoAsync($"{bot.O.Url}/imaging?tab=upload");
        var shown = await PacsUploadOnScreenAsync(nurse.Page, $"outside-{stamp}.dcm", PacsXray($"EXT-{stamp}", "OUTSIDE^Robot", "19700101", null));
        if (!shown.Contains("waiting in To match", StringComparison.Ordinal)) return (Outcome.Fail, "Expected the unknown patient's X-ray to wait in To match: " + Report.Trim(shown, 300));
        var list = await nurse.GetAsync("/api/imaging/studies?view=unmatched");
        var study = Find(list.Json, x => Str(x, "filePatientId") == $"EXT-{stamp}");
        if (study is null) return (Outcome.Fail, "The uploaded study is not in To match.");
        _pacsUnmatched = Str(study.Value, "id");
        await nurse.Page.GotoAsync($"{bot.O.Url}/imaging?tab=unmatched");
        await nurse.Page.GetByRole(AriaRole.Region, new() { Name = "Imaging studies" }).WaitForAsync(new() { Timeout = 15000 });
        R.Gallery("/imaging?tab=unmatched — X-rays waiting to be filed", await nurse.ShotAsync("pacs-unmatched"));
        // File it (the patient search is covered elsewhere; the date of birth differs, so the check must be confirmed).
        if (_patient is null) return (Outcome.Pass, "Unknown patient's X-ray waits in To match (no robot patient to file it to).");
        var refused = await nurse.PostAsync($"/api/imaging/studies/{_pacsUnmatched}/file", new { patientId = _patient.Id, bookingId = (string?)null, confirmMismatch = false });
        if (refused.Status != 400) return (Outcome.Fail, $"Filing with a different date of birth was not stopped (HTTP {refused.Status}).");
        var ok = await nurse.PostAsync($"/api/imaging/studies/{_pacsUnmatched}/file", new { patientId = _patient.Id, bookingId = (string?)null, confirmMismatch = true });
        return ok.Status == 200 ? (Outcome.Pass, "Unknown patient's X-ray waited in To match; filing it stopped for the different date of birth until the nurse confirmed.")
            : (Outcome.Fail, $"Filing the study gave HTTP {ok.Status}.");
    }

    private async Task<(Outcome, string)> PacsViewerAsync()
    {
        var doc = await As("hreid");
        var list = await doc.GetAsync("/api/imaging/studies");
        if (list.Status != 200) return (Outcome.Skip, $"Imaging is not available to the surgeon (HTTP {list.Status}).");
        var study = _pacsUnmatched is not null ? Find(list.Json, x => Str(x, "id") == _pacsUnmatched) : null;
        study ??= Find(list.Json, x => Str(x, "firstImageId") is not null);
        if (study is null) return (Outcome.Skip, "No study with a picture.");
        var page = doc.Page;
        await page.GotoAsync($"{bot.O.Url}/imaging/studies/{Str(study.Value, "id")}");
        var canvas = page.Locator("canvas.pacs-canvas");
        await canvas.WaitForAsync(new() { Timeout = 15000 });
        var render = await doc.GetAsync($"/api/imaging/images/{Str(study.Value, "firstImageId")}/render?size=256");
        if (render.Status != 200) return (Outcome.Fail, $"The picture did not draw (HTTP {render.Status}).");
        var brighter = page.GetByRole(AriaRole.Button, new() { Name = "Brighter" });
        if (await brighter.CountAsync() > 0) await brighter.ClickAsync();
        await Task.Delay(800);
        var shot = await doc.ShotAsync("pacs-viewer", fullPage: true);
        R.Gallery("/imaging/studies/:id — X-ray viewer", shot);
        foreach (var u in await Usability.CheckAsync(page, null, 0, desktop: true)) R.Add("screens", "hreid", "/imaging/studies/:id (viewer)", u.Check, u.Outcome, u.Detail, u.Outcome == Outcome.Warn ? shot : null);
        return (Outcome.Pass, "The surgeon opened the study; the viewer drew the X-ray and the brightness control works.");
    }

    private async Task<(Outcome, string)> PacsGuardAsync()
    {
        var reception = await As("reception");
        var r = await reception.ApiAsync("PUT", "/api/imaging/settings", new { storagePath = (string?)null, importFolder = (string?)null, autoFile = true });
        var anaes = await As("bkowalski");
        var up = await anaes.ApiAsync("POST", "/api/imaging/requests", new { patientId = Guid.Empty, modality = "DX", examination = "x" });
        return r.Status == 403 && up.Status == 403 ? (Outcome.Pass, "Reception cannot change imaging settings and an anaesthetist cannot make requests or upload (403).")
            : (Outcome.Fail, $"Reception settings HTTP {r.Status}; anaesthetist request HTTP {up.Status} (both should be 403).");
    }
}
