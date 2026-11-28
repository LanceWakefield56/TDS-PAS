using System.Text.Json;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Prescribing and barcode medicine checks (0.31.0): the surgeon signs the demo draft prescription on screen and it goes to NZePS (simulator) and
/// prints; the nurse's barcode check finds the right patient and pack and refuses to stay quiet about someone else's wristband; reception cannot prescribe.
/// </summary>
public sealed partial class Scenarios
{
    private const string RxArea = "Prescribing";

    private async Task PrescribingStepsAsync()
    {
        await Step(RxArea, "Surgeon signs a take-home prescription; it is sent to NZePS and prints with its barcode", RxSignAsync);
        await Step(RxArea, "Nurse scans wristband and medicine pack; someone else's wristband is caught", RxScanAsync);
        await Step(RxArea, "Reception cannot write prescriptions", RxGuardAsync);
    }

    private async Task<(Outcome, string)> RxSignAsync()
    {
        var dr = await As("hreid");
        var list = await dr.GetAsync("/api/prescribing/prescriptions");
        if (list.Status != 200) return (Outcome.Skip, $"Prescribing is not available (HTTP {list.Status}).");
        var draft = Find(list.Json, x => Str(x, "status") == "Draft");
        if (draft is null) return (Outcome.Skip, "No draft prescription in the demo.");
        var id = Str(draft.Value, "id")!; var number = Str(draft.Value, "number");
        var page = dr.Page;
        await page.GotoAsync($"{bot.O.Url}/prescribing?tab=attention");
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = number });
        await row.GetByRole(AriaRole.Button, new() { Name = "Sign", Exact = true }).ClickAsync();
        await page.GetByText("Signed and sent to NZePS.").WaitForAsync(new() { Timeout = 15000 });
        var after = (await dr.GetAsync($"/api/prescribing/prescriptions/{id}")).Json!.Value;
        if (Str(after, "status") != "Sent") return (Outcome.Fail, $"After signing, {number} is {Str(after, "status")}.");
        var pdf = await dr.GetAsync($"/api/prescribing/prescriptions/{id}/pdf");
        return pdf.Status == 200 && pdf.Text.StartsWith("%PDF", StringComparison.Ordinal)
            ? (Outcome.Pass, $"{number} signed on screen, sent (simulator, {Str(after, "sendReference")}) and printed as a PDF.")
            : (Outcome.Fail, $"Printing {number} gave HTTP {pdf.Status}.");
    }

    private async Task<(Outcome, string)> RxScanAsync()
    {
        if (_case is null || _deviceNurse is null) return (Outcome.Skip, "No admitted demonstration patient (the devices steps did not run).");
        var nurse = _deviceNurse;
        var settings = await nurse.GetAsync("/api/prescribing/settings");
        if (settings.Status != 200) return (Outcome.Skip, $"Prescribing is not available (HTTP {settings.Status}).");
        var body = new
        {
            medicine = "Cefazolin 2 g injection (Kefzol pack, demo barcode)",
            dose = "2 g",
            route = "IV",
            givenAt = (string?)null,
            barcode = "]d2010940000000001617301231" + "10ROBOT1",
            controlledDrugId = (string?)null,
            witnessedBy = (string?)null,
            notes = (string?)null,
            overrideReason = (string?)null,
            medicineId = (string?)null,
            weightKg = (decimal?)null,
            patientBarcode = _case.Nhi,
            orderId = (string?)null
        };
        var ok = await nurse.PostAsync($"/api/emr/episodes/{_case.EpisodeId}/medications", body);
        if (ok.Status != 200) return (Outcome.Fail, $"Charting with the right wristband and pack gave HTTP {ok.Status}: {ok.Text}");
        var text = ok.Text;
        if (!text.Contains("right patient", StringComparison.Ordinal) || !text.Contains("Pack scanned", StringComparison.Ordinal))
            return (Outcome.Fail, "The barcode check did not confirm the patient and the pack: " + text);
        var wrong = await nurse.PostAsync($"/api/emr/episodes/{_case.EpisodeId}/medications", body with { patientBarcode = "ZZZ9999" });
        if (!wrong.Text.Contains("belongs to someone else", StringComparison.Ordinal)) return (Outcome.Fail, "A wristband for someone else was not flagged: " + wrong.Text);
        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/emr/{_case.BookingId}?tab=meds");
        await page.GetByLabel("Scan wristband").WaitForAsync(new() { Timeout = 15000 });
        await page.GetByText("Medicine chart").First.WaitForAsync(new() { Timeout = 5000 });
        return (Outcome.Pass, "Right wristband and GS1 pack (batch, expiry) confirmed; someone else's wristband flagged (Advise mode); scan fields and medicine chart shown in the record.");
    }

    private async Task<(Outcome, string)> RxGuardAsync()
    {
        var reception = await As("reception");
        var r = await reception.PostAsync("/api/prescribing/prescriptions", new { patientId = Guid.Empty, episodeId = (Guid?)null, note = (string?)null, items = Array.Empty<object>() });
        return r.Status == 403 ? (Outcome.Pass, "Writing a prescription is refused for reception (403).") : (Outcome.Fail, $"Reception got HTTP {r.Status} writing a prescription.");
    }
}
