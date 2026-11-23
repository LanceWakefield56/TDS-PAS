using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Triage and suitability (0.22.0) through the screens: a nurse opens the worklist and a risk profile (scores, reasons); an anaesthetist
/// choosing a different outcome from the suggestion is refused without a reason and accepted with one; a nurse cannot approve rule sets.
/// </summary>
public sealed partial class Scenarios
{
    private const string TriageArea = "Triage & suitability";
    private string? _triageId;

    private async Task TriageStepsAsync()
    {
        await Step(TriageArea, "Nurse opens the triage worklist and a patient's risk profile with scores and reasons", TriageViewAsync);
        await Step(TriageArea, "Anaesthetist sign-off: a different outcome needs a reason; with one it is signed off", TriageSignOffAsync);
        await Step(TriageArea, "A nurse cannot approve triage rules", TriageRulesGuardAsync);
    }

    private async Task<(Outcome, string)> TriageViewAsync()
    {
        var nurse = await As("nurse");
        var list = await nurse.GetAsync("/api/triage/worklist?view=open");
        if (list.Status != 200) return (Outcome.Skip, "Triage is not available to the nurse (module off or no permission).");
        var first = Find(list.Json, _ => true);
        if (first is null) return (Outcome.Skip, "No triage waiting for sign-off in the demo data.");
        _triageId = Str(first.Value, "id");
        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/triage");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Triage", Exact = true })).ToBeVisibleAsync(new() { Timeout = 10000 });
        await page.GotoAsync($"{bot.O.Url}/triage/{_triageId}");
        await Assertions.Expect(page.GetByText("STOP-BANG").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Why" })).ToBeVisibleAsync();
        return (Outcome.Pass, $"Worklist and risk profile shown for {Str(first.Value, "patient")} (suggested {Str(first.Value, "suggested")}).");
    }

    private async Task<(Outcome, string)> TriageSignOffAsync()
    {
        if (_triageId is null) return (Outcome.Skip, "No assessment from the previous step.");
        var an = await As("fgrant");
        var a = await an.GetAsync($"/api/triage/assessments/{_triageId}");
        if (a.Status != 200) return (Outcome.Skip, "The anaesthetist cannot open triage.");
        var suggested = Str(a.Json!.Value, "suggested");
        var other = suggested == "NotSuitable" ? "AnaesthetistReview" : "NotSuitable";
        var refused = await an.PostAsync($"/api/triage/assessments/{_triageId}/sign-off", new { outcome = other, confirmedAsa = 2, notes = "Robot check" });
        if (refused.Status != 400) return (Outcome.Fail, $"A different outcome without a reason was accepted (HTTP {refused.Status}).");
        var page = an.Page;
        await page.GotoAsync($"{bot.O.Url}/triage/{_triageId}");
        await page.Locator("section.card", new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Sign off", Exact = true }) }).GetByRole(AriaRole.Combobox).First.SelectOptionAsync(other);
        await page.GetByLabel("Reason for a different outcome").FillAsync("Robot: reviewed at pre-assessment clinic");
        await page.GetByLabel("Notes", new() { Exact = true }).FillAsync("Robot sign-off");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign off", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText("Signed off.").Or(page.Locator("div.error[role=alert]")).First).ToBeVisibleAsync(new() { Timeout = 10000 });
        var after = await an.GetAsync($"/api/triage/assessments/{_triageId}");
        return Str(after.Json!.Value, "final") == other && Str(after.Json!.Value, "status") == "SignedOff"
            ? (Outcome.Pass, $"Refused without a reason (HTTP 400); signed off as {other} with the reason recorded.")
            : (Outcome.Fail, $"Not signed off: status {Str(after.Json!.Value, "status")}, final {Str(after.Json!.Value, "final")}.");
    }

    private async Task<(Outcome, string)> TriageRulesGuardAsync()
    {
        var nurse = await As("nurse");
        var sets = await nurse.GetAsync("/api/triage/rulesets");
        if (sets.Status != 200) return (Outcome.Skip, "The nurse cannot see rule sets.");
        var any = Find(sets.Json, _ => true);
        if (any is null) return (Outcome.Skip, "No rule sets.");
        var r = await nurse.PostAsync($"/api/triage/rulesets/{Str(any.Value, "id")}/approve", new { note = "robot" });
        return r.Status == 403 ? (Outcome.Pass, "Approve refused for the nurse (HTTP 403).") : (Outcome.Fail, $"Nurse approve returned HTTP {r.Status}.");
    }
}
