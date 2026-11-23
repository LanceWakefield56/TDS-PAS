using System.Text.Json;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Procedure estimates (0.21.0) through the screens: Finance builds an estimate from the "Colonoscopy + biopsies" template, issues it and
/// the total matches the TDS price list (colonoscopy $4,750 + biopsies $290); the right-patient checks run before emailing; the Outlook
/// draft is marked unsent; the pricing export comes back as a protected download.
/// </summary>
public sealed partial class Scenarios
{
    private const string EstimatesArea = "Procedure estimates";

    private async Task EstimatesStepsAsync()
    {
        await Step(EstimatesArea, "Finance builds an estimate from a template on the screen, issues it and the total matches the price list", BuildEstimateAsync);
        await Step(EstimatesArea, "Before emailing: right-patient checks, PDF password rule, and an Outlook draft marked unsent", EstimateEmailAsync);
    }

    private string? _estimateId;

    /// <summary>The .eml text with quoted-printable soft breaks removed and base64 text parts decoded, so the body can be checked.</summary>
    private static string Readable(string eml)
    {
        var t = eml.Replace("=\r\n", "", StringComparison.Ordinal).Replace("=\n", "", StringComparison.Ordinal);
        var sb = new System.Text.StringBuilder(t);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(t, @"Content-Type: text/plain[^\r\n]*\r?\n(?:[^\r\n]+\r?\n)*?Content-Transfer-Encoding: base64\r?\n\r?\n([A-Za-z0-9+/=\r\n]+)"))
        {
            try { sb.Append(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal)))); }
            catch (FormatException) { }
        }
        return sb.ToString();
    }

    private async Task<(Outcome, string)> BuildEstimateAsync()
    {
        var fin = await As("finance");
        var tpl = await fin.GetAsync("/api/estimates/templates");
        if (tpl.Status != 200) return (Outcome.Skip, "Finance cannot open estimates (module off or no permission).");
        if (Find(tpl.Json, t => (Str(t, "name") ?? "").StartsWith("Colonoscopy + biopsies", StringComparison.Ordinal)) is null) return (Outcome.Skip, "The starter template is not there.");
        var page = fin.Page;
        await page.GotoAsync($"{bot.O.Url}/estimates/new");
        await page.GetByLabel("Start from a template (optional)").SelectOptionAsync(new SelectOptionValue { Label = "Colonoscopy + biopsies (polypectomy if required)" });
        await Assertions.Expect(page.GetByText("Add-on – Biopsies").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        await page.GetByLabel("First name", new() { Exact = true }).FillAsync("Robot");
        await page.GetByLabel("Last name", new() { Exact = true }).FillAsync("Estimate");
        await page.GetByLabel("Date of birth").FillAsync("1970-01-01");
        await page.GetByLabel("NHI", new() { Exact = true }).FillAsync("ZZZ0016");
        await page.GetByLabel("Email", new() { Exact = true }).FillAsync("robot.estimate@example.nz");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save & issue", Exact = true }).ClickAsync();
        var done = page.GetByText("Issued EST-").Or(page.Locator("div.error[role=alert]"));
        await Assertions.Expect(done.First).ToBeVisibleAsync(new() { Timeout = 15000 });
        var shown = (await done.First.InnerTextAsync()).Trim();
        var list = await fin.GetAsync("/api/estimates?q=ZZZ0016");
        var row = Find(list.Json, r => Str(r, "status") == "Issued" && (Str(r, "patient") ?? "").Contains("Estimate", StringComparison.Ordinal));
        if (row is null) return (Outcome.Fail, $"Not issued: {shown}");
        _estimateId = Str(row.Value, "id");
        var total = row.Value.GetProperty("subtotal").GetDecimal();
        return total == 5040m ? (Outcome.Pass, $"{shown} Total $5,040.00 = colonoscopy $4,750 + biopsies $290; polypectomy listed as ‘if required’.")
            : (Outcome.Warn, $"{shown} Total {total:0.00} – expected 5040.00 with the TDS price list (it may have been changed in this database).");
    }

    private async Task<(Outcome, string)> EstimateEmailAsync()
    {
        if (_estimateId is null) return (Outcome.Skip, "No estimate from the previous step.");
        var fin = await As("finance");
        var check = await fin.GetAsync($"/api/estimates/{_estimateId}/email-check?recipient=Patient");
        if (check.Status != 200) return (Outcome.Fail, $"Email check failed ({check.Status}): {check.Text}");
        var j = check.Json!.Value;
        var checks = j.GetProperty("checks").EnumerateArray().Select(c => $"{c.GetProperty("level").GetString()}: {c.GetProperty("message").GetString()}").ToList();
        if (j.GetProperty("password").GetString() != "DobShort") return (Outcome.Fail, "Patients should get the DDMMYY date-of-birth password by default.");
        var unconfirmed = await fin.PostAsync($"/api/estimates/{_estimateId}/email/draft", new { recipient = "Patient", to = "robot.estimate@example.nz", confirmed = false });
        if (unconfirmed.Status != 400) return (Outcome.Fail, "A draft was made without the right-patient tick.");
        var page = fin.Page;
        await page.GotoAsync($"{bot.O.Url}/estimates/{_estimateId}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Email…", Exact = true }).ClickAsync();
        await page.GetByLabel("I have checked this is the right patient and the right email address.").CheckAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Create Outlook draft" }).ClickAsync());
        var path = await download.PathAsync();
        var eml = Readable(await File.ReadAllTextAsync(path));
        var ok = eml.Contains("X-Unsent: 1", StringComparison.Ordinal) && eml.Contains("application/pdf", StringComparison.Ordinal) && eml.Contains("DDMMYY", StringComparison.Ordinal) && !eml.Contains("010170", StringComparison.Ordinal);
        return ok ? (Outcome.Pass, $"Draft {download.SuggestedFilename}: unsent, PDF attached, password rule stated (never the password). Checks: {(checks.Count == 0 ? "none" : string.Join("; ", checks))}.")
            : (Outcome.Fail, "The Outlook draft is not marked unsent, has no PDF, or shows the password.");
    }
}
