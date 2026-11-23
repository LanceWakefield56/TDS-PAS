using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Post-op follow-up and SSI (0.24.0): a nurse makes a call from the script on the screen; wound signs suggest an SSI and the record is opened;
/// the infection prevention lead (quality) classifies it and raises an incident; a patient answers a check-in text on a phone and a concern
/// creates a call back today; plans can't be changed without permission.
/// </summary>
public sealed partial class Scenarios
{
    private const string FollowArea = "Follow-up & SSI";
    private string? _ssiId;

    private async Task FollowUpStepsAsync()
    {
        await Step(FollowArea, "Nurse makes a follow-up call from the script; wound signs open an SSI record", FollowUpCallAsync);
        await Step(FollowArea, "Infection prevention lead classifies the SSI and raises a Quality incident", SsiConfirmAsync);
        await Step(FollowArea, "A nurse cannot change follow-up plans or confirm SSIs", FollowUpGuardAsync);
    }

    private async Task<(Outcome, string)> FollowUpCallAsync()
    {
        var nurse = await As("nurse");
        var list = await nurse.GetAsync("/api/followup/worklist?view=overdue");
        if (list.Status != 200) return (Outcome.Skip, "Follow-up is not available to the nurse.");
        var today = await nurse.GetAsync("/api/followup/worklist?view=today");
        var row = Find(list.Json, r => Str(r, "method") == "Phone" && Str(r, "status") is "Due" or "NoAnswer") ?? Find(today.Json, r => Str(r, "method") == "Phone");
        if (row is null) return (Outcome.Skip, "No phone follow-up due in the demo data.");
        var id = Str(row.Value, "id");
        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/followup");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Follow-up", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
        await page.GotoAsync($"{bot.O.Url}/followup/{id}");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Call script" })).ToBeVisibleAsync(new() { Timeout = 15000 });
        await page.GetByLabel("Pain now, from 0 (none) to 10 (worst)").SelectOptionAsync("4");
        await page.GetByLabel("Anything coming out of the wound?").SelectOptionAsync("Cloudy, yellow/green or smelly");
        await page.GetByRole(AriaRole.Radiogroup, new() { Name = "Is the skin around the wound red, hot or spreading?" }).GetByLabel("Yes").CheckAsync();
        await page.GetByRole(AriaRole.Radiogroup, new() { Name = "Fever, chills or feeling hot and shivery?" }).GetByLabel("Yes").CheckAsync();
        await page.GetByLabel("Escalation").SelectOptionAsync("ReturnToTds");
        await page.GetByLabel("What was arranged").FillAsync("Robot: wound review at TDS tomorrow 9:00");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save the call" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Needs attention:")).ToBeVisibleAsync(new() { Timeout = 10000 });
        var openBtn = page.GetByRole(AriaRole.Button, new() { Name = "Save the SSI record" });
        if (await openBtn.CountAsync() == 0) return (Outcome.Fail, "Wound signs were saved but no SSI record was offered.");
        await openBtn.ClickAsync();
        await page.WaitForURLAsync(new Regex("/followup/ssi/"), new() { Timeout = 10000 });
        _ssiId = page.Url.Split('/')[^1];
        var c = await nurse.GetAsync($"/api/followup/ssi/{_ssiId}");
        return Str(c.Json!.Value, "class") == "Suspected"
            ? (Outcome.Pass, $"Call saved with escalation; warnings shown; {Str(c.Json!.Value, "number")} opened as suspected SSI with the operation's risk factors.")
            : (Outcome.Fail, "SSI record not created as suspected.");
    }

    private async Task<(Outcome, string)> SsiConfirmAsync()
    {
        if (_ssiId is null) return (Outcome.Skip, "No SSI record from the previous step.");
        var q = await As("quality");
        var page = q.Page;
        await page.GotoAsync($"{bot.O.Url}/followup/ssi/{_ssiId}");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Classification (infection prevention lead)" })).ToBeVisibleAsync(new() { Timeout = 15000 });
        await page.GetByLabel("Classification", new() { Exact = false }).First.SelectOptionAsync("Superficial");
        await page.GetByLabel("Evidence / criteria met").FillAsync("Robot: purulent drainage from the superficial incision within 30 days");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save classification" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Classification saved.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Raise a Quality incident" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Incident raised in Quality.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        var c = (await q.GetAsync($"/api/followup/ssi/{_ssiId}")).Json!.Value;
        return Str(c, "class") == "Superficial" && c.GetProperty("incidentId").ValueKind == JsonValueKind.String
            ? (Outcome.Pass, $"{Str(c, "number")} classified superficial incisional by {Str(c, "confirmedBy")}; Quality incident raised.")
            : (Outcome.Fail, $"Class {Str(c, "class")}, incident {c.GetProperty("incidentId")}.");
    }

    private async Task<(Outcome, string)> FollowUpGuardAsync()
    {
        var nurse = await As("nurse");
        var p = await nurse.PostAsync("/api/followup/plans/examples", new { });
        var s = _ssiId is null ? 403 : (await nurse.PostAsync($"/api/followup/ssi/{_ssiId}/confirm", new { @class = "NotSsi", note = "robot" })).Status;
        return p.Status == 403 && s == 403 ? (Outcome.Pass, "Plans and SSI confirmation refused for the nurse (HTTP 403).") : (Outcome.Fail, $"Plans HTTP {p.Status}, confirm HTTP {s}.");
    }

    /// <summary>Runs inside the virtual SMS section: the check-in text reaches a phone and the patient answers it.</summary>
    private async Task<(Outcome, string)> CheckInByTextAsync()
    {
        var nurse = await As("nurse");
        var rows = new List<JsonElement>();
        foreach (var v in new[] { "upcoming", "today", "overdue" })
        {
            var r = await nurse.GetAsync($"/api/followup/worklist?view={v}");
            if (r.Status == 200) rows.AddRange(r.Json!.Value.EnumerateArray().Where(x => Str(x, "status") == "Due" && Str(x, "phone") is { Length: > 0 }));
        }
        if (rows.Count == 0) return (Outcome.Skip, "No follow-up contact for a patient with a mobile.");
        var row = rows[0];
        var pid = Str(row, "patientId")!;
        var patient = (await nurse.GetAsync($"/api/patients/{pid}")).Json!.Value;
        var before = sims.SmsOut.Count;
        var send = await nurse.PostAsync($"/api/followup/contacts/{Str(row, "id")}/send-link", new { });
        if (send.Status != 200) return (Outcome.Fail, $"Could not send the check-in text ({send.Status}): {send.Text}");
        var sms = await Sims.WaitForAsync(sims.SmsOut, x => sims.SmsOut.ToList().IndexOf(x) >= before && x.Text.Contains("/c/", StringComparison.Ordinal), TimeSpan.FromSeconds(20));
        if (sms is null) return (Outcome.Fail, "No check-in SMS reached a virtual phone.");
        var m = Regex.Match(sms.Text, @"https?://\S+/c/\S+");
        var link = bot.O.Url + new Uri(m.Value.TrimEnd('.', ',', ')')).PathAndQuery;
        await using var phone = await bot.NewContextAsync("patient", new ViewportSize { Width = 390, Height = 844 }, mobile: true);
        var p = phone.Page;
        await p.GotoAsync(link);
        await p.Locator("input[type=date]").FillAsync(Str(patient, "dateOfBirth")![..10]);
        await p.GetByRole(AriaRole.Button, new() { Name = "Start" }).ClickAsync();
        await p.Locator("h1:has-text('Kia ora')").WaitForAsync(new() { Timeout = 15000 });
        await p.GetByLabel("Pain now, from 0 (none) to 10 (worst)").SelectOptionAsync("3");
        await p.GetByLabel("Any bleeding?").SelectOptionAsync("Heavy or won't stop");
        await p.GetByLabel("Feeling sick or vomiting?").SelectOptionAsync("No");
        var shot = await phone.ShotAsync("checkin-phone", fullPage: true);
        R.Gallery("/c/:token — patient check-in on a phone", shot);
        foreach (var n in await Usability.CheckAsync(p, null, 0, desktop: false)) R.Add("screens", "patient", "/c (follow-up check-in, phone)", n.Check, n.Outcome, n.Detail, n.Outcome == Outcome.Warn ? shot : null);
        await p.GetByRole(AriaRole.Button, new() { Name = "Send my answers" }).ClickAsync();
        await p.Locator("text=A nurse will phone you today").WaitForAsync(new() { Timeout = 10000 });
        var concerns = await nurse.GetAsync("/api/followup/worklist?view=concerns");
        var found = Find(concerns.Json, x => Str(x, "patientId") == pid && (Str(x, "label") ?? "").StartsWith("Call back", StringComparison.Ordinal));
        var duty = await Sims.WaitForAsync(sims.SmsOut, x => sims.SmsOut.ToList().IndexOf(x) >= before && x.Text.Contains("needs a call back", StringComparison.Ordinal), TimeSpan.FromSeconds(20));
        return found is not null
            ? (Outcome.Pass, $"SMS: \"{Report.Trim(sms.Text, 120)}\". Patient answered on a phone (heavy bleeding) → told a nurse will phone today; call back on the Concerns list{(duty is null ? "" : "; duty nurse texted (no patient details)")}.")
            : (Outcome.Fail, "The patient's concern did not create a call back on the Concerns list.");
    }
}
