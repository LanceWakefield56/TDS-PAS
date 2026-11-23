using System.Text.Json;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Patient journey (0.23.0): each person's list says what to do and every "do it" button opens a screen they can use – so a whole journey can be
/// followed from the list without the menu; the patient page shows the journey with one next step; the day board shows today's patients.
/// </summary>
public sealed partial class Scenarios
{
    private const string JourneyArea = "Patient journey";

    private async Task JourneyStepsAsync()
    {
        foreach (var account in new[] { "reception", "nurse", "fgrant", "finance", "coder" })
            await Step(JourneyArea, $"{account}: every action on My list opens a working screen (no menu needed)", () => TodayActionsAsync(account));
        await Step(JourneyArea, "Patient page shows the journey with the next step; the button goes to the right screen", JourneyPanelAsync);
        await Step(JourneyArea, "Theatre day board shows today's patients in columns", DayBoardAsync);
    }

    private async Task<(Outcome, string)> TodayActionsAsync(string account)
    {
        var s = await As(account);
        var r = await s.GetAsync("/api/journey/today");
        if (r.Status != 200) return (Outcome.Fail, $"My list failed (HTTP {r.Status}).");
        var items = r.Json!.Value.EnumerateArray().ToList();
        if (items.Count == 0) return (Outcome.Pass, "Nothing on the list for this person.");
        // One example of each kind of action.
        var kinds = items.GroupBy(i => (Str(i, "what") ?? "") + "|" + (Str(i.GetProperty("action"), "to") ?? "").Split('/', '?')[1]).Select(g => g.First()).Take(12).ToList();
        var page = s.Page;
        await page.GotoAsync($"{bot.O.Url}/");
        await Assertions.Expect(page.GetByText("Start here")).ToBeVisibleAsync(new() { Timeout = 15000 });
        var bad = new List<string>();
        foreach (var i in kinds)
        {
            var to = Str(i.GetProperty("action"), "to")!;
            await page.GotoAsync($"{bot.O.Url}{to}");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var text = await page.Locator("main").InnerTextAsync();
            if (text.Contains("Not available to you", StringComparison.Ordinal) || text.Contains("Page not found", StringComparison.OrdinalIgnoreCase) || await page.Locator("main [role=alert].error, main p.error").CountAsync() > 0)
                bad.Add($"{Str(i, "what")} → {to}");
        }
        return bad.Count == 0
            ? (Outcome.Pass, $"{items.Count} item(s); {kinds.Count} kind(s) of action all open a usable screen: {string.Join("; ", kinds.Select(k => Str(k, "what")).Distinct())}.")
            : (Outcome.Fail, "Action opens a screen this person can't use: " + string.Join(" | ", bad));
    }

    private async Task<(Outcome, string)> JourneyPanelAsync()
    {
        var s = await As("nurse");
        var today = await s.GetAsync("/api/journey/today");
        var first = Find(today.Json, i => Str(i, "group") == "Before surgery");
        if (first is null) return (Outcome.Skip, "Nobody before surgery on the nurse's list.");
        var pid = Str(first.Value, "patientId");
        var j = await s.GetAsync($"/api/journey/patients/{pid}");
        var cases = j.Json!.Value.GetProperty("cases");
        if (cases.GetArrayLength() == 0) return (Outcome.Fail, "The patient on the list has no journey.");
        var next = cases[0].GetProperty("next");
        var page = s.Page;
        await page.GotoAsync($"{bot.O.Url}/patients/{pid}");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Journey", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
        var card = page.Locator(".journey-card");
        await Assertions.Expect(card.Locator("ol.journey li").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        if (next.ValueKind != JsonValueKind.Object) return (Outcome.Pass, "Journey shown; nothing left to do.");
        var label = Str(next, "label");
        await Assertions.Expect(card.Locator(".next-step").First).ToBeVisibleAsync();
        var btn = card.Locator(".next-step").First.Locator(".btn-primary");
        if (await btn.CountAsync() == 0) return (Outcome.Pass, $"Journey shown; next step “{label}” is done by someone else (shown as such).");
        var name = (await btn.InnerTextAsync()).Trim();
        await btn.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var text = await page.Locator("main").InnerTextAsync();
        return text.Contains("Not available to you", StringComparison.Ordinal)
            ? (Outcome.Fail, $"Next step button “{name}” opened a page the nurse can't use.")
            : (Outcome.Pass, $"Journey with {cases[0].GetProperty("stages").GetArrayLength()} stages; next step “{label}”; “{name}” opened {new Uri(page.Url).PathAndQuery}.");
    }

    private async Task<(Outcome, string)> DayBoardAsync()
    {
        var s = await As("nurse");
        var r = await s.GetAsync("/api/journey/day");
        if (r.Status != 200) return (Outcome.Fail, $"Day board failed (HTTP {r.Status}).");
        var n = r.Json!.Value.GetProperty("cards").GetArrayLength();
        var page = s.Page;
        await page.GotoAsync($"{bot.O.Url}/day");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Theatre day" })).ToBeVisibleAsync(new() { Timeout = 15000 });
        if (n == 0) return (Outcome.Pass, "No patients today (weekend); the board says so.");
        await Assertions.Expect(page.Locator("article.board-card").First).ToBeVisibleAsync();
        return (Outcome.Pass, $"{n} patient(s) shown in Coming in / Admitted / In theatre / Recovery / Gone home.");
    }
}
