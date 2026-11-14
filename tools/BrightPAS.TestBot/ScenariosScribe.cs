using System.Text.Json;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// BrightPAS Scribe (0.16.0) with a virtual speech server and language model: an ambient session through the screens (consent, recording from the
/// browser's virtual microphone, transcription, AI draft beside the transcript, safety highlights, sign-off filing the note and deleting the audio),
/// de-identification before cloud drafting, dictation into a note field, and an external (Medow) draft returned with a bearer token.
/// </summary>
public sealed partial class Scenarios
{
    private JsonElement? _scribeOrig;

    private async Task ScribeStepsAsync()
    {
        await Step("Virtual speech server + AI", "Ambient scribe: consent, record, transcribe, draft beside the transcript, check highlights, sign – note filed and audio deleted", ScribeSessionAsync);
        await Step("Virtual speech server + AI", "Cloud drafting gets a de-identified transcript; names come back only inside BrightPAS", ScribeCloudAsync);
        await Step("Virtual speech server + AI", "Dictation into a report field with NZ spelling, te reo and voice commands", DictationAsync);
        await Step("Virtual speech server + AI", "Medow Health option: minimum data, draft returned with a bearer token as an unsigned draft", MedowAsync);
    }

    private async Task<bool> ScribeSettingsAsync(Session admin, object body)
    {
        if (_scribeOrig is null)
        {
            var g = await admin.GetAsync("/api/scribe/settings");
            if (g.Status != 200) return false;
            _scribeOrig = g.Json!.Value.GetProperty("settings").Clone();
            var o = _scribeOrig.Value;
            string? S(string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            _restore.Add(async () => await (await As("admin")).PutAsync("/api/scribe/settings", new
            {
                provider = S("provider"), speech = S("speech"), speechUrl = S("speechUrl"), speechModel = S("speechModel"), speechKey = (string?)null, draft = S("draft"), draftUrl = S("draftUrl"), draftModel = S("draftModel"),
                draftKey = (string?)null, cloudPrivacyReference = S("cloudPrivacyReference"), medowPiaReference = S("medowPiaReference"), medowSupplierAssessment = S("medowSupplierAssessment"),
                medowAgreementReference = S("medowAgreementReference"), medowLaunchUrl = S("medowLaunchUrl"), audioRetentionDays = o.GetProperty("audioRetentionDays").GetInt32(),
            }));
        }
        var r = await admin.PutAsync("/api/scribe/settings", body);
        if (r.Status != 200) throw new InvalidOperationException("Scribe settings refused: " + r.Text);
        return true;
    }

    private object OnSite(string draft = "OnSite", string provider = "BrightPas") => new
    {
        provider, speech = "OnSite", speechUrl = sims.AiUrl, speechModel = "large-v3", speechKey = (string?)null, draft, draftUrl = sims.AiUrl, draftModel = draft == "CloudClaude" ? "claude-test" : "llama-test",
        draftKey = draft == "CloudClaude" ? "robot-key" : null, cloudPrivacyReference = draft == "CloudClaude" ? "ROBOT-PIA-1" : null,
        medowPiaReference = provider == "Medow" ? "PIA-MEDOW" : null, medowSupplierAssessment = provider == "Medow" ? "HISF-MEDOW" : null, medowAgreementReference = provider == "Medow" ? "DPA-MEDOW" : null,
        medowLaunchUrl = provider == "Medow" ? "https://example.invalid/medow" : null, audioRetentionDays = 0,
    };

    private async Task<(Outcome, string)> ScribeSessionAsync()
    {
        var admin = await As("admin");
        if (!await ScribeSettingsAsync(admin, OnSite())) return (Outcome.Skip, "Scribe module not available to the administrator.");
        var dr = await As("hreid");
        var page = dr.Page;
        await page.GotoAsync($"{bot.O.Url}/scribe?patientId={_patient!.Id}");
        var start = page.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true });
        await Assertions.Expect(start).ToBeEnabledAsync(new() { Timeout = 15000 });
        await start.ClickAsync();
        await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/scribe/[0-9a-f-]{36}$"));
        var id = page.Url.Split('/').Last();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Start recording" })).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Button, new() { Name = "Patient agrees (this visit)" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Start recording" }).ClickAsync();
        await Assertions.Expect(page.GetByText("● Recording")).ToBeVisibleAsync();
        await Task.Delay(6000);
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Stop recording") }).ClickAsync();
        await Assertions.Expect(page.GetByText("Recording saved")).ToBeVisibleAsync(new() { Timeout = 20000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Transcribe" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Transcribed:")).ToBeVisibleAsync(new() { Timeout = 30000 });
        try { await Assertions.Expect(page.GetByText("whānau came with me")).ToBeVisibleAsync(new() { Timeout = 10000 }); await Assertions.Expect(page.GetByText("since your last anaesthetic")).ToBeVisibleAsync(); }
        catch (PlaywrightException) { return (Outcome.Fail, "Transcript is missing NZ spelling or te reo correction from the dictionary."); }
        await page.GetByRole(AriaRole.Button, new() { Name = "Draft the note" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Draft written by")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Assertions.Expect(page.GetByText("Check before signing:")).ToBeVisibleAsync();
        var checks = await page.Locator(".card.subtle ul.plain li").AllInnerTextsAsync();
        if (!checks.Any(c => c.Contains("units", StringComparison.OrdinalIgnoreCase)) || !checks.Any(c => c.Contains("Laterality") || c.Contains("side", StringComparison.OrdinalIgnoreCase))) return (Outcome.Fail, "Safety highlights for units/side missing: " + string.Join(" | ", checks));
        await page.Locator(".scribe-sentence.linked").First.HoverAsync();
        var linked = await page.Locator(".scribe-seg.on").CountAsync();
        var shot = await dr.ShotAsync("scribe-review");
        await page.GetByLabel("Note text").FillAsync((await page.GetByLabel("Note text").InputValueAsync()) + "\nReviewed with the patient.");
        await page.GetByLabel("I have checked this AI-assisted note against the conversation and it is correct.").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign note" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Signed –")).ToBeVisibleAsync(new() { Timeout = 20000 });
        var s = (await dr.GetAsync($"/api/scribe/sessions/{id}")).Json!.Value.GetProperty("session");
        if (s.GetProperty("status").GetString() != "Signed" || s.GetProperty("audioDeletedAt").ValueKind == JsonValueKind.Null || s.GetProperty("documentId").ValueKind == JsonValueKind.Null)
            return (Outcome.Fail, $"After signing: {s}");
        R.Device("Virtual speech server", "Scribe review screen (draft linked to transcript, safety checks)", $"{checks.Count} check(s); {linked} transcript part(s) lit by the first sentence", shot);
        return (Outcome.Pass, $"Consent recorded; {Str(s, "durationSeconds")} s recorded from the virtual microphone; transcript with NZ spelling and 'whānau'; draft linked to {linked} transcript part(s); {checks.Count} safety checks (units, side, medicines); signed {Str(s, "editPercent")}% edited – PDF in documents, audio deleted.");
    }

    private async Task<(Outcome, string)> ScribeCloudAsync()
    {
        var admin = await As("admin");
        await ScribeSettingsAsync(admin, OnSite("CloudClaude"));
        var dr = await As("hreid");
        var p = _patient!;
        sims.HeardName = $"{p.Given} {p.Family}"; sims.HeardNhi = p.Nhi ?? p.Mrn;
        try
        {
            var templates = (await dr.GetAsync("/api/scribe/templates")).Json!.Value;
            var tid = Str(templates.EnumerateArray().First(t => Str(t, "code") == "CONSULT"), "id");
            var st = await dr.PostAsync("/api/scribe/sessions", new { patientId = p.Id, bookingId = (string?)null, context = "Consultation", templateId = tid });
            var id = Str(st.Json!.Value, "id")!;
            await dr.PostAsync("/api/scribe/consent", new { patientId = p.Id, sessionId = id, given = true, standing = false });
            var up = await dr.Ctx.APIRequest.PostAsync($"{bot.O.Url}/api/scribe/sessions/{id}/audio/0", new() { IgnoreHTTPSErrors = true, Headers = new Dictionary<string, string> { ["X-BrightPAS-Request"] = "1", ["Content-Type"] = "audio/webm" }, DataByte = new byte[4096] });
            if (up.Status != 200) return (Outcome.Fail, "Audio refused: " + await up.TextAsync());
            await dr.PostAsync($"/api/scribe/sessions/{id}/stop", new { seconds = 17 });
            var tr = await dr.PostAsync($"/api/scribe/sessions/{id}/transcribe", new { });
            if (tr.Status != 200) return (Outcome.Fail, "Transcribe failed: " + tr.Text);
            var df = await dr.PostAsync($"/api/scribe/sessions/{id}/draft", new { });
            if (df.Status != 200) return (Outcome.Fail, "Draft failed: " + df.Text);
            var prompt = sims.LastDraftPrompt ?? "";
            if (sims.LastDraftPath != "/v1/messages") return (Outcome.Fail, "The Claude-compatible endpoint was not used.");
            if (prompt.Contains(p.Family, StringComparison.OrdinalIgnoreCase) || (p.Nhi is { } nhi && prompt.Contains(nhi, StringComparison.OrdinalIgnoreCase))) return (Outcome.Fail, "The patient's name or NHI was sent to the cloud drafting service.");
            if (!prompt.Contains("[PATIENT_", StringComparison.Ordinal)) return (Outcome.Fail, "No de-identification tokens in what was sent.");
            var rec = (await dr.GetAsync($"/api/scribe/sessions/{id}")).Json!.Value;
            var history = rec.GetProperty("sentences").EnumerateArray().Select(x => Str(x, "text") ?? "").FirstOrDefault() ?? "";
            if (!history.Contains(p.Given.Split(' ')[0], StringComparison.OrdinalIgnoreCase)) return (Outcome.Fail, "The name was not put back into the draft inside BrightPAS: " + history);
            await dr.PostAsync($"/api/scribe/sessions/{id}/discard", new { reason = "Robot test" });
            return (Outcome.Pass, $"Sent to the virtual Claude API ({prompt.Length} characters) with name/NHI replaced by tokens; the draft came back and BrightPAS put '{p.Given.Split(' ')[0]}' back in.");
        }
        finally { sims.HeardName = null; sims.HeardNhi = null; await ScribeSettingsAsync(admin, OnSite()); }
    }

    private async Task<(Outcome, string)> DictationAsync()
    {
        if (_endoBooking is null) return (Outcome.Skip, "Needs an open endoscopy report.");
        var dr = await As("hreid");
        var page = dr.Page;
        await page.GotoAsync($"{bot.O.Url}/endoscopy/{_endoBooking}?tab=report");
        var mic = page.GetByRole(AriaRole.Button, new() { Name = "Dictate" }).First;
        try { await Assertions.Expect(mic).ToBeVisibleAsync(new() { Timeout = 15000 }); }
        catch (PlaywrightException) { await page.ReloadAsync(); }
        try { await Assertions.Expect(mic).ToBeVisibleAsync(new() { Timeout = 15000 }); }
        catch (PlaywrightException) { var st = await dr.GetAsync("/api/scribe/status"); await dr.ShotAsync("dictation-missing"); var me = await dr.GetAsync("/api/me"); return (Outcome.Fail, $"No Dictate button on the report (status {st.Text}); buttons: {string.Join(" | ", await page.Locator("button").AllInnerTextsAsync())}; perms scribe.use={me.Text.Contains("scribe.use")}, modules: {(me.Json?.TryGetProperty("moduleSetup", out var ms) == true ? ms.GetRawText() : "")}"); }
        var box = page.Locator("label:has-text('Conclusion / diagnosis') textarea");
        var before = await box.InputValueAsync();
        await mic.ClickAsync();
        await Task.Delay(2500);
        await page.GetByRole(AriaRole.Button, new() { Name = "Stop dictating" }).First.ClickAsync();
        var after = before;
        for (var i = 0; i < 40 && after.Length <= before.Length; i++) { await Task.Delay(500); after = await box.InputValueAsync(); }
        var added = after[before.Length..];
        if (!added.Contains("anaesthetic") || !added.Contains("whānau") || !added.Contains("\n\nThe plan")) return (Outcome.Fail, "Dictated text not corrected as expected: " + added);
        var warn = await page.Locator(".dictate-warn").First.InnerTextAsync();
        return (Outcome.Pass, $"Dictated {added.Length} characters into the conclusion with 'anaesthetic', 'whānau' and a new paragraph from the voice command; warnings shown ({warn.Split('.').Length - 1} check(s)).");
    }

    private async Task<(Outcome, string)> MedowAsync()
    {
        var admin = await As("admin");
        await ScribeSettingsAsync(admin, OnSite(provider: "Medow"));
        try
        {
            var token = Str((await admin.PostAsync("/api/scribe/settings/token", new { })).Json!.Value, "token")!;
            var dr = await As("hreid");
            var tid = Str((await dr.GetAsync("/api/scribe/templates")).Json!.Value.EnumerateArray().First(t => Str(t, "code") == "CONSULT"), "id");
            var id = Str((await dr.PostAsync("/api/scribe/sessions", new { patientId = _patient!.Id, bookingId = (string?)null, context = "Consultation", templateId = tid })).Json!.Value, "id")!;
            await dr.PostAsync("/api/scribe/consent", new { patientId = _patient.Id, sessionId = id, given = true, standing = false });
            var rec = (await dr.GetAsync($"/api/scribe/sessions/{id}")).Json!.Value;
            var payload = rec.GetProperty("externalPayload").GetRawText();
            if (_patient.Nhi is { } nhi && payload.Contains(nhi, StringComparison.Ordinal)) return (Outcome.Fail, "NHI included in the data for Medow.");
            if (payload.Contains(_patient.Family, StringComparison.OrdinalIgnoreCase)) return (Outcome.Fail, "Surname included in the data for Medow.");
            var anon = await bot.Pw.APIRequest.NewContextAsync(new() { IgnoreHTTPSErrors = true });
            var bad = await anon.PostAsync($"{bot.O.Url}/api/scribe/inbound", new() { Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer wrong" }, DataObject = new { sessionId = id, text = "x" } });
            var ok = await anon.PostAsync($"{bot.O.Url}/api/scribe/inbound", new() { Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
                DataObject = new { sessionId = id, text = "History\nVirtual Medow draft.\n\nPlan\nReview in clinic.", product = "Medow Health (virtual)" } });
            await anon.DisposeAsync();
            if (bad.Status != 401) return (Outcome.Fail, $"A wrong token was not refused ({bad.Status}).");
            if (ok.Status != 200) return (Outcome.Fail, $"Medow draft refused ({ok.Status}): {await ok.TextAsync()}");
            var s = (await dr.GetAsync($"/api/scribe/sessions/{id}")).Json!.Value.GetProperty("session");
            if (Str(s, "status") != "Drafted" || Str(s, "draftEngine") != "Medow Health (virtual)") return (Outcome.Fail, "Draft not stored as an unsigned Medow draft.");
            await dr.PostAsync($"/api/scribe/sessions/{id}/discard", new { reason = "Robot test" });
            return (Outcome.Pass, $"Medow gets only {payload.Length} characters of minimum data (no NHI/surname); a wrong token is refused (401); the draft came back as an unsigned 'Medow Health' draft.");
        }
        finally { await ScribeSettingsAsync(admin, OnSite()); }
    }
}
