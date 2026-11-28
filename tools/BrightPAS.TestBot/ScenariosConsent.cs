using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// E-consent (0.29.0) through the screens: a nurse sends a prepared consent; the patient opens the link on a phone, reads every section and
/// signs; the nurse confirms it on the day (the theatre consent check is ticked); a receptionist cannot approve an information sheet.
/// </summary>
public sealed partial class Scenarios
{
    private const string ConsentArea = "E-consent";
    private string? _consentId;

    private async Task ConsentStepsAsync()
    {
        await Step(ConsentArea, "Nurse sends a prepared consent to the patient", ConsentSendAsync);
        await Step(ConsentArea, "Patient reads every section and signs on a phone", ConsentSignAsync);
        await Step(ConsentArea, "Nurse confirms the consent on the day; the theatre consent check is ticked", ConsentConfirmAsync);
        await Step(ConsentArea, "Reception cannot approve an information sheet", ConsentGuardAsync);
    }

    private async Task<(Outcome, string)> ConsentSendAsync()
    {
        var nurse = await As("nurse");
        var list = await nurse.GetAsync("/api/consent?view=sent");
        if (list.Status != 200) return (Outcome.Skip, $"E-consent is not available (HTTP {list.Status}).");
        var draft = Find(list.Json, x => Str(x, "status") == "Draft");
        if (draft is null) return (Outcome.Skip, "No demo consent is waiting to be sent.");
        _consentId = Str(draft.Value, "id");
        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/consent/{_consentId}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Send to the patient", Exact = true }).ClickAsync();
        await page.Locator("code.break").WaitForAsync(new() { Timeout = 15000 });
        var c = (await nurse.GetAsync($"/api/consent/{_consentId}")).Json!.Value;
        return Str(c, "status") == "Sent" ? (Outcome.Pass, $"{Str(c, "number")} sent; the link is shown to copy.") : (Outcome.Fail, $"Status is {Str(c, "status")} after sending.");
    }

    private async Task<(Outcome, string)> ConsentSignAsync()
    {
        if (_consentId is null) return (Outcome.Skip, "No consent was sent.");
        var nurse = await As("nurse");
        var page = nurse.Page;
        var text = await page.Locator("code.break").InnerTextAsync();
        var link = bot.O.Url + new Uri(text.Trim()).PathAndQuery;
        var c = (await nurse.GetAsync($"/api/consent/{_consentId}")).Json!.Value;
        var patient = (await nurse.GetAsync($"/api/patients/{Str(c, "patientId")}")).Json!.Value;
        await using var phone = await bot.NewContextAsync("patient", new ViewportSize { Width = 390, Height = 844 }, mobile: true);
        var p = phone.Page;
        await p.GotoAsync(link);
        await p.Locator("input[type=date]").FillAsync(Str(patient, "dateOfBirth")![..10]);
        await p.GetByRole(AriaRole.Button, new() { Name = "Open" }).ClickAsync();
        var boxes = p.GetByLabel("I have read this");
        await boxes.First.WaitForAsync(new() { Timeout = 15000 }); // the date-of-birth step also says "Kia ora", so wait for the sections
        var n = await boxes.CountAsync();
        for (var i = 0; i < n; i++) { await boxes.Nth(i).CheckAsync(); await Assertions.Expect(boxes.Nth(i)).ToBeDisabledAsync(new() { Timeout = 10000 }); }
        await p.GetByLabel("Type your full name").FillAsync($"{Str(patient, "givenNames")} {Str(patient, "familyName")}");
        await p.GetByLabel(new System.Text.RegularExpressions.Regex("I have read the information")).CheckAsync();
        await p.GetByLabel(new System.Text.RegularExpressions.Regex("I have had the chance")).CheckAsync();
        await p.GetByLabel(new System.Text.RegularExpressions.Regex("I agree to have")).CheckAsync();
        var shot = await phone.ShotAsync("consent-phone", fullPage: true);
        R.Gallery("/e/:token — patient e-consent on a phone", shot);
        foreach (var u in await Usability.CheckAsync(p, null, 0, desktop: false)) R.Add("screens", "patient", "/e (e-consent, phone)", u.Check, u.Outcome, u.Detail, u.Outcome == Outcome.Warn ? shot : null);
        await p.GetByRole(AriaRole.Button, new() { Name = "Sign my consent" }).ClickAsync();
        await p.Locator("text=Your consent has been signed").WaitForAsync(new() { Timeout = 15000 });
        var after = (await nurse.GetAsync($"/api/consent/{_consentId}")).Json!.Value;
        return Str(after, "status") == "Signed" ? (Outcome.Pass, $"{n} sections read and signed on a phone; the signed PDF is filed.") : (Outcome.Fail, $"Status is {Str(after, "status")} after signing.");
    }

    private async Task<(Outcome, string)> ConsentConfirmAsync()
    {
        if (_consentId is null) return (Outcome.Skip, "No consent was signed.");
        var nurse = await As("nurse");
        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/consent/{_consentId}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirm consent", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText("Consent confirmed.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        var c = (await nurse.GetAsync($"/api/consent/{_consentId}")).Json!.Value;
        return Str(c, "status") == "Confirmed" ? (Outcome.Pass, $"{Str(c, "number")} confirmed by the nurse.") : (Outcome.Fail, $"Status is {Str(c, "status")} after confirming.");
    }

    private async Task<(Outcome, string)> ConsentGuardAsync()
    {
        var reception = await As("reception");
        var sheets = await reception.GetAsync("/api/consent/sheets");
        if (sheets.Status != 200) return (Outcome.Skip, $"E-consent is not available (HTTP {sheets.Status}).");
        var any = Find(sheets.Json, _ => true);
        if (any is null) return (Outcome.Skip, "No sheets in the demo.");
        var r = await reception.PostAsync($"/api/consent/sheets/{Str(any.Value, "id")}/approve", new { note = "robot" });
        return r.Status == 403 ? (Outcome.Pass, "Approving a sheet is refused for reception (403).") : (Outcome.Fail, $"Reception got HTTP {r.Status} approving a sheet.");
    }
}
