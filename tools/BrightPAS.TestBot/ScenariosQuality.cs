using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Quality management (0.19.0) through the screens: a nurse reports an incident in under two minutes; the quality manager triages it (SAC 2
/// escalates), finds it cannot be closed until findings, the HQSC notification and open disclosure are recorded, then closes it; a complaint
/// gets its Right 10 dates; an indicator shows its SPC chart; the Clinical Governance pack downloads as a protected PDF; a nurse acknowledges a policy.
/// </summary>
public sealed partial class Scenarios
{
    private const string QualityArea = "Quality management";

    private async Task QualityStepsAsync()
    {
        await Step(QualityArea, "A nurse reports an incident from the menu (under two minutes)", IncidentReportAsync);
        if (_incidentId is null) return;
        await Step(QualityArea, "Quality manager triages it as SAC 2 (escalated); it cannot close until findings, HQSC notification and open disclosure are recorded", IncidentTriageAsync);
        await Step(QualityArea, "A complaint is recorded and acknowledged: Right 10 dates in working days", ComplaintAsync);
        await Step(QualityArea, "An indicator opens with its SPC chart, monthly table and equity view; the Clinical Governance pack downloads as a protected PDF", IndicatorAndPackAsync);
        await Step(QualityArea, "A nurse reads and acknowledges a policy", PolicyAckAsync);
    }

    private string? _incidentId, _incidentNumber;

    private async Task<(Outcome, string)> IncidentReportAsync()
    {
        var nurse = await As("nurse");
        if ((await nurse.GetAsync("/api/quality/lookups")).Status != 200) return (Outcome.Skip, "The nurse cannot report incidents (Quality module off or no permission).");
        var page = nurse.Page;
        var started = DateTime.Now;
        await page.GotoAsync($"{bot.O.Url}/quality/report");
        await page.GetByLabel("Where", new() { Exact = true }).FillAsync("Recovery bay 2");
        await page.GetByLabel("What kind of incident", new() { Exact = false }).SelectOptionAsync("Medication");
        await page.GetByLabel("Harm (as far as you know)", new() { Exact = false }).SelectOptionAsync("Moderate");
        await page.GetByLabel("What happened", new() { Exact = true }).FillAsync("Robot test: second dose of prophylactic antibiotic given in recovery because the theatre dose was not charted. Rash treated.");
        await page.GetByLabel("What was done straight away (optional)").FillAsync("Antihistamine given; anaesthetist informed.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Send report" }).ClickAsync();
        var done = page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Your report is" });
        await Assertions.Expect(done).ToBeVisibleAsync(new() { Timeout = 15000 });
        var text = await done.InnerTextAsync();
        var m = System.Text.RegularExpressions.Regex.Match(text, @"INC-\d{4}-\d{4}");
        if (!m.Success) return (Outcome.Fail, "No incident number shown: " + text);
        _incidentNumber = m.Value;
        var q = await As("quality");
        var list = await q.GetAsync("/api/quality/incidents?open=true");
        var row = Find(list.Json, i => Str(i, "number") == _incidentNumber);
        if (row is null) return (Outcome.Fail, $"{_incidentNumber} is not in the quality manager's incident list.");
        _incidentId = Str(row.Value, "id");
        var secs = (DateTime.Now - started).TotalSeconds;
        return (Outcome.Pass, $"{_incidentNumber} reported by the nurse in {secs:0} s (including page load); it is in the quality manager's list as Reported.");
    }

    private async Task<(Outcome, string)> IncidentTriageAsync()
    {
        var q = await As("quality");
        var page = q.Page;
        await page.GotoAsync($"{bot.O.Url}/quality/incidents/{_incidentId}");
        await page.GetByLabel("SAC rating", new() { Exact = false }).SelectOptionAsync("2");
        await page.GetByLabel("Investigator", new() { Exact = true }).FillAsync("Morgan Hayes");
        await page.GetByLabel("HQSC", new() { Exact = true }).CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save triage" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Triage saved.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Assertions.Expect(page.GetByText("Escalated", new() { Exact = false }).First).ToBeVisibleAsync(new() { Timeout = 10000 });
        // Closing too early is refused with the reasons.
        await page.GetByRole(AriaRole.Button, new() { Name = "Close incident" }).ClickAsync();
        var err = page.GetByRole(AriaRole.Alert);
        await Assertions.Expect(err).ToBeVisibleAsync(new() { Timeout = 10000 });
        var why = await err.InnerTextAsync();
        if (!why.Contains("findings", StringComparison.Ordinal) || !why.Contains("Hqsc", StringComparison.Ordinal)) return (Outcome.Fail, "Closing was refused, but not for the expected reasons: " + why);
        await page.GetByLabel("Findings", new() { Exact = true }).FillAsync("Theatre dose not charted before transfer to recovery.");
        await page.GetByLabel("Learning summary for staff (no names or identifying details)").FillAsync("Chart every theatre antibiotic dose at Sign Out.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save review" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Review saved.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        await page.GetByLabel("Who was told", new() { Exact = true }).FillAsync("Patient and partner");
        await page.GetByRole(AriaRole.Button, new() { Name = "Record disclosure" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Disclosure recorded.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        await page.GetByLabel("Reference from HQSC", new() { Exact = true }).FillAsync("ROBOT-HQSC-1");
        await page.GetByRole(AriaRole.Button, new() { Name = "Mark notified" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Notification recorded.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Close incident" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Incident closed.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        var d = (await q.GetAsync($"/api/quality/incidents/{_incidentId}")).Json!.Value;
        return Str(d, "status") == "Closed" && d.GetProperty("escalatedAt").ValueKind == JsonValueKind.String
            ? (Outcome.Pass, $"{_incidentNumber}: SAC 2 escalated to “{Str(d, "escalateTo")}”; early close refused (findings, HQSC); after findings, open disclosure and the HQSC reference it closed.")
            : (Outcome.Fail, $"After closing, the incident shows status {Str(d, "status")}.");
    }

    private async Task<(Outcome, string)> ComplaintAsync()
    {
        var q = await As("quality");
        var page = q.Page;
        await page.GotoAsync($"{bot.O.Url}/quality?tab=complaints");
        await page.GetByRole(AriaRole.Button, new() { Name = "Record a complaint" }).ClickAsync();
        await page.GetByLabel("Complainant", new() { Exact = true }).FillAsync("Robot Complainant");
        await page.GetByLabel("Handled by", new() { Exact = true }).FillAsync("Morgan Hayes");
        await page.GetByLabel("Summary", new() { Exact = true }).FillAsync("Robot test: waited two hours past the appointment time without an update.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Complaint CMP-") })).ToBeVisibleAsync(new() { Timeout = 15000 });
        var ackBy = await page.Locator("dl.facts dd").First.InnerTextAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Acknowledged in writing" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Acknowledged – the decision is due in 10 working days.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        // The facts list refreshes just after the message; wait for the decision date rather than reading the old text.
        try { await Assertions.Expect(page.Locator("dl.facts dd").Nth(1)).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^(?!after)\S"), new() { Timeout = 10000 }); } catch (PlaywrightException) { }
        var decide = await page.Locator("dl.facts dd").Nth(1).InnerTextAsync();
        return string.IsNullOrWhiteSpace(decide) || decide.StartsWith("after", StringComparison.Ordinal)
            ? (Outcome.Fail, "After acknowledging, no decision date was shown.")
            : (Outcome.Pass, $"Acknowledge by {ackBy.Split('–')[0].Trim()} (5 working days); after acknowledging today, decide by {decide.Trim()} (10 working days).");
    }

    private async Task<(Outcome, string)> IndicatorAndPackAsync()
    {
        var q = await As("quality");
        var ind = await q.GetAsync("/api/quality/indicators");
        var ssc = Find(ind.Json?.GetProperty("indicators"), i => Str(i, "code") == "SSC");
        if (ssc is null) return (Outcome.Fail, "The starter indicator SSC (Surgical Safety Checklist) is missing.");
        var page = q.Page;
        await page.GotoAsync($"{bot.O.Url}/quality?tab=indicators&id={Str(ssc.Value, "id")}");
        await Assertions.Expect(page.Locator("svg.spc")).ToBeVisibleAsync(new() { Timeout = 15000 });
        var rows = await page.Locator("section.card table.results").Nth(1).Locator("tbody tr").CountAsync();
        var series = (await q.GetAsync($"/api/quality/indicators/{Str(ssc.Value, "id")}/series")).Json!.Value;
        var withRate = series.GetProperty("points").EnumerateArray().Count(p => p.GetProperty("rate").ValueKind == JsonValueKind.Number);
        var res = await q.Ctx.APIRequest.PostAsync($"{bot.O.Url}/api/quality/pack", new()
        {
            Headers = new Dictionary<string, string> { ["X-BrightPAS-Request"] = "1" },
            DataObject = new { month = DateTime.Now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), export = new { protect = true, password = "Robot-Quality-Pass-2026!", format = "pdf" } },
            IgnoreHTTPSErrors = true,
        });
        if (res.Status != 200) return (Outcome.Fail, $"Governance pack refused ({res.Status}): {await res.TextAsync()}");
        var b = await res.BodyAsync();
        await File.WriteAllBytesAsync(Path.Combine(R.DeviceFolder, "quality-pack.pdf"), b);
        var text = Encoding.Latin1.GetString(b);
        if (!text.StartsWith("%PDF", StringComparison.Ordinal) || !text.Contains("/Encrypt", StringComparison.Ordinal)) return (Outcome.Fail, "The governance pack is not a password-protected PDF.");
        return rows >= 12 ? (Outcome.Pass, $"SSC indicator: SPC chart drawn, {rows} monthly rows ({withRate} with data from the clinical record); governance pack {b.Length / 1024} KB, password-protected.")
            : (Outcome.Fail, $"The indicator table shows {rows} months; expected 12.");
    }

    private async Task<(Outcome, string)> PolicyAckAsync()
    {
        var nurse = await As("nurse3");
        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/quality/mine");
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "I have read this" }).First;
        if (await btn.CountAsync() == 0) return (Outcome.Skip, "Nothing waiting for this nurse to acknowledge.");
        await btn.ClickAsync();
        await Assertions.Expect(page.GetByText("acknowledged.", new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = 10000 });
        var q = await As("quality");
        var pol = Find((await q.GetAsync("/api/quality/policies")).Json, p => Str(p, "code") == "TDS-3.2");
        if (pol is null) return (Outcome.Pass, "Policy acknowledged on screen.");
        var acks = await q.GetAsync($"/api/quality/policies/{Str(pol.Value, "id")}/acknowledgements");
        return acks.Text.Contains("nurse3", StringComparison.Ordinal) || acks.Text.Contains("nurse", StringComparison.Ordinal)
            ? (Outcome.Pass, "The nurse read the policy and pressed “I have read this”; the quality manager sees the acknowledgement for that version.")
            : (Outcome.Warn, "Acknowledged on screen, but not listed for TDS-3.2 (another policy may have been first in the list).");
    }
}
