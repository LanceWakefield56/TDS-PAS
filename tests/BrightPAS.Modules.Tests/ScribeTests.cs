using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Modules.Scribe;

namespace BrightPAS.Modules.Tests;

/// <summary>0.16.0 BrightPAS Scribe: voice commands, dictionary/NZ spelling, safety highlights, de-identification, drafts, consent and sign-off rules.</summary>
public class ScribeTests
{
    [Fact]
    public void Voice_commands_and_corrections()
    {
        var (t, cmds) = ScribeText.ApplyVoiceCommands("patient comfortable full stop new paragraph colon looked normal comma no polyps full stop scratch that biopsies taken full stop next field");
        Assert.Equal("Patient comfortable.\n\nBiopsies taken.", t);
        Assert.Equal(["next field"], cmds);
        var dict = new[] { ("whānau", (string?)"far no,whanau"), ("Dr Lindqvist", "doctor lindqvist"), ("Māori", "maori") };
        Assert.Equal("Her whānau and Dr Lindqvist discussed the oesophagus and anaesthetic; Māori patient.",
            ScribeText.Correct("Her far no and doctor lindqvist discussed the esophagus and anesthetic; maori patient.", dict));
    }

    [Fact]
    public void Safety_highlights_doses_lookalikes_and_sides()
    {
        var h = ScribeText.Check("Give 10 units insulin and hydralazine 25 mg to the left knee. Dose 5.0 mg. Site unclear.", ["insulin"]);
        Assert.Contains(h, x => x.Kind == "dose" && x.Message.Contains("units"));
        Assert.Contains(h, x => x.Kind == "lasa" && x.Message.Contains("hydroxyzine"));
        Assert.Contains(h, x => x.Kind == "side");
        Assert.Contains(h, x => x.Message.Contains("Trailing zero"));
        Assert.Contains(h, x => x.Kind == "unsure");
        Assert.Contains(h, x => x.Kind == "medicine");
    }

    [Fact]
    public void De_identification_round_trip_and_draft_parsing()
    {
        var id = new ScribeText.Identity("Smith", "Margaret Joy", "Maggie", "ZAA5280", "TDS0000012", new DateOnly(1950, 3, 4), "021 555 0101", null, "12 Hill Street");
        var (text, map) = ScribeText.DeIdentify("Margaret Smith (Maggie), NHI ZAA5280, born 4 March 1950, lives at 12 Hill Street, ph 021 555 0101. Other ID ZZZ0016.", id);
        Assert.DoesNotContain("Smith", text); Assert.DoesNotContain("ZAA5280", text); Assert.DoesNotContain("Hill Street", text); Assert.DoesNotContain("0101", text); Assert.DoesNotContain("1950", text); Assert.DoesNotContain("ZZZ0016", text);
        Assert.Contains("[PATIENT_SURNAME]", text);
        Assert.Contains("Maggie", ScribeText.ReIdentify("[PATIENT_PREFERRED_NAME] [PATIENT_SURNAME] is well.", map));

        var answer = "Here is the draft:\n```json\n{\"sections\":[{\"heading\":\"History\",\"sentences\":[{\"text\":\"Six months of symptoms.\",\"sources\":[2,99]}]},{\"heading\":\"Plan\",\"sentences\":[{\"text\":\"Book [?] procedure.\",\"sources\":[3]}]}]}\n```";
        var d = ScribeText.ParseDraft(answer, 5);
        Assert.Equal(2, d.Count);
        Assert.Equal([2], d[0].Sources); // 99 is outside the transcript
        Assert.Contains("History\nSix months", ScribeText.Render(d));
        Assert.Throws<FormatException>(() => ScribeText.ParseDraft("Sorry, I cannot help.", 5));
        Assert.Equal(0m, ScribeText.EditPercent("a b c d", "a b c d"));
        Assert.Equal(25m, ScribeText.EditPercent("a b c d", "a b x d"));
        var tpl = ScribeText.TemplateOnly("History: x\nPlan: y", [new Segment(1, "Patient", 0, 1, "Sore", null)]);
        Assert.Equal(("History", "Patient: Sore"), (tpl[0].Section, tpl[0].Text));
        Assert.Equal("Plan", tpl[1].Section);
    }

    [Fact]
    public async Task Consent_templates_settings_sign_and_audio()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ScribeService>();
        var audio = s.ServiceProvider.GetRequiredService<AudioStore>();
        var templates = await svc.TemplatesAsync();
        Assert.True(templates.Count >= 6 && templates.All(t => t.Status == TemplateStatus.Approved));
        var consult = templates.Single(t => t.Code == "CONSULT");

        // Template change needs approval by someone else.
        var (edited, te) = await svc.SaveTemplateAsync(consult.Id, new TemplateEdit("CONSULT", consult.Name, consult.Context, consult.Sections + "\nCoding: suggested codes (not applied)"), "admin");
        Assert.Empty(te);
        Assert.Equal((TemplateStatus.Draft, 2), (edited!.Status, edited.Version));
        Assert.NotEmpty((await svc.StartAsync(new NewSession(p.Id, null, ScribeContext.Consultation, consult.Id), "drsmith")).Errors);
        Assert.Contains("other than the author", await svc.ApproveTemplateAsync(consult.Id, true, "admin"));
        Assert.Null(await svc.ApproveTemplateAsync(consult.Id, true, "governance"));

        // Settings: cloud needs the privacy reference; Medow needs its three references.
        Assert.Contains(await svc.SaveSettingsAsync(new SettingsEdit(ScribeProvider.BrightPas, SpeechMode.OnSite, "http://pas-speech:8000", null, null, DraftMode.CloudClaude, "https://api.anthropic.com", null, "key", null, null, null, null, null, 0), "admin"), e => e.Contains("privacy"));
        Assert.Contains(await svc.SaveSettingsAsync(new SettingsEdit(ScribeProvider.Medow, SpeechMode.Off, null, null, null, DraftMode.TemplateOnly, null, null, null, null, "PIA-1", null, null, null, 0), "admin"), e => e.Contains("Medow"));
        Assert.Empty(await svc.SaveSettingsAsync(new SettingsEdit(ScribeProvider.BrightPas, SpeechMode.OnSite, "http://pas-speech:8000", "large-v3", null, DraftMode.CloudClaude, "https://api.anthropic.com", null, "sk-test", "PIA-SCRIBE-2026", null, null, null, null, 0), "admin"));
        var view = await svc.SettingsAsync();
        Assert.True(view.DictationAvailable && view.DraftKeySet);
        Assert.Equal("admin", view.CloudApprovedBy);
        var (speech, draft, _) = await svc.EnginesAsync();
        Assert.Equal("sk-test", draft!.Key);
        Assert.Equal("http://pas-speech:8000", speech!.Url);

        // No consent: audio refused. With consent: recorded, encrypted, transcribed, drafted, signed – audio deleted.
        var (sess, se) = await svc.StartAsync(new NewSession(p.Id, null, ScribeContext.Consultation, consult.Id), "drsmith");
        Assert.Empty(se);
        Assert.Contains("consented", await svc.AppendAudioAsync(sess!.Id, 0, "audio/webm", [1, 2, 3]));
        Assert.Null((await svc.RecordConsentAsync(p.Id, sess.Id, true, false, "drsmith")).Error);
        Assert.Null(await svc.AppendAudioAsync(sess.Id, 0, "audio/webm;codecs=opus", [1, 2, 3]));
        Assert.Null(await svc.AppendAudioAsync(sess.Id, 1, "audio/webm", [4, 5]));
        Assert.Equal([1, 2, 3, 4, 5], (await svc.AudioAsync(sess.Id)).Audio);
        Assert.Null(await svc.StopAsync(sess.Id, 12.3m, "drsmith"));
        await svc.SaveTranscriptAsync(sess.Id, [new Segment(1, "Patient", 0, 3, "It hurts on the left after my anesthetic", 0.9m), new Segment(2, "Clinician", 3, 6, "We will give 10 units", 0.4m)], "test", "drsmith");
        Assert.Equal("It hurts on the left after my anaesthetic", (await svc.SegmentsAsync(sess.Id))[0].Text); // NZ spelling applied
        Assert.Null(await svc.SetSpeakerAsync(sess.Id, 2, "Clinician"));
        await svc.SaveDraftAsync(sess.Id, [new DraftedSentence("History", "Left-sided pain after anaesthetic.", [1]), new DraftedSentence("Plan", "10 units given.", [2])], "test-llm", true, "drsmith");
        var hl = await svc.HighlightsAsync(sess.Id, "Left-sided pain. 10 units given.");
        Assert.Contains(hl, x => x.Kind == "side"); Assert.Contains(hl, x => x.Kind == "dose");
        Assert.Contains("Only the clinician", (await svc.SignAsync(sess.Id, "x", "someoneelse")).Error);
        var (signed, serr) = await svc.SignAsync(sess.Id, "History\nLeft-sided pain after anaesthetic.\n\nPlan\n10 units of insulin given.", "drsmith");
        Assert.Null(serr);
        Assert.True(signed!.EditPercent > 0);
        Assert.NotNull(signed.AudioDeletedAt);
        Assert.Empty((await svc.AudioAsync(sess.Id)).Audio);
        Assert.Single(await svc.AccuracyAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)));

        // Standing consent carries to the next session; withdrawal stops it. Inbound token.
        Assert.Null((await svc.RecordConsentAsync(p.Id, null, true, true, "nurse")).Error);
        var (s2, _) = await svc.StartAsync(new NewSession(p.Id, null, ScribeContext.TriageCall, templates.Single(t => t.Code == "TRIAGE").Id), "nurse");
        Assert.NotNull(s2!.ConsentId);
        await svc.WithdrawAsync(p.Id, "nurse");
        Assert.Null(await svc.StandingConsentAsync(p.Id));
        var token = await svc.NewInboundTokenAsync("admin");
        Assert.True(await svc.InboundTokenOkAsync(token));
        Assert.False(await svc.InboundTokenOkAsync(token + "x"));
        Assert.NotNull(await svc.ExternalDraftAsync(s2.Id, "Caller reports mild pain.", "Medow Health", "external-scribe")); // withdrawn standing consent no longer applies
        Assert.Null((await svc.RecordConsentAsync(p.Id, s2.Id, true, false, "nurse")).Error);
        Assert.Null(await svc.ExternalDraftAsync(s2.Id, "Caller reports mild pain.\n\nAdvice: paracetamol.", "Medow Health", "external-scribe"));
        Assert.Equal(2, (await svc.SentencesAsync(s2.Id)).Count);
        Assert.Null(await svc.DiscardAsync(s2.Id, "Test", "nurse"));
    }
}
