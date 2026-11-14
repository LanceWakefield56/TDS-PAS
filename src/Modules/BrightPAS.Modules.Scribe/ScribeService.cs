using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Scribe;

public sealed record SettingsEdit(ScribeProvider Provider, SpeechMode Speech, string? SpeechUrl, string? SpeechModel, string? SpeechKey, DraftMode Draft, string? DraftUrl, string? DraftModel, string? DraftKey,
    string? CloudPrivacyReference, string? MedowPiaReference, string? MedowSupplierAssessment, string? MedowAgreementReference, string? MedowLaunchUrl, int AudioRetentionDays);
public sealed record SettingsView(ScribeProvider Provider, SpeechMode Speech, string? SpeechUrl, string? SpeechModel, bool SpeechKeySet, DraftMode Draft, string? DraftUrl, string? DraftModel, bool DraftKeySet,
    string? CloudPrivacyReference, string? CloudApprovedBy, DateTimeOffset? CloudApprovedAt, string? MedowPiaReference, string? MedowSupplierAssessment, string? MedowAgreementReference, string? MedowLaunchUrl,
    bool InboundTokenSet, string? MedowApprovedBy, DateTimeOffset? MedowApprovedAt, int AudioRetentionDays, bool DictationAvailable);
public sealed record TemplateEdit(string Code, string Name, ScribeContext Context, string Sections);
public sealed record TermEdit(string Term, string? SoundsLike, string Category);
public sealed record NewSession(Guid PatientId, Guid? BookingId, ScribeContext Context, Guid TemplateId);
public sealed record Engine(string Url, string? Model, string? Key);
public sealed record AccuracyRow(string Clinician, string Engine, int Sessions, decimal? MeanEditPercent, decimal? MaxEditPercent);

/// <summary>BrightPAS Scribe state: settings, templates with governance approval, the custom dictionary, AI-recording consent and sessions. Every action is audited.</summary>
public sealed class ScribeService(ScribeDbContext db, PatientsDbContext patients, AudioStore audio, IDocumentCipher cipher, AuditWriter audit, TimeProvider clock)
{
    public static readonly string[] Categories = ["Clinician", "Staff", "Medicine", "Procedure", "Place", "Te reo", "Abbreviation", "Clinic", "Supplier", "NZ health", "Other"];

    // ------------------------------------------------------------------ settings
    private async Task<ScribeSettings> RowAsync(CancellationToken ct) => await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct) ?? db.Settings.Add(new ScribeSettings()).Entity;

    public async Task<SettingsView> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct) ?? new ScribeSettings();
        return new(s.Provider, s.Speech, s.SpeechUrl, s.SpeechModel, s.SpeechKey is not null, s.Draft, s.DraftUrl, s.DraftModel, s.DraftKey is not null, s.CloudPrivacyReference, s.CloudApprovedBy, s.CloudApprovedAt,
            s.MedowPiaReference, s.MedowSupplierAssessment, s.MedowAgreementReference, s.MedowLaunchUrl, s.InboundTokenHash is not null, s.MedowApprovedBy, s.MedowApprovedAt, s.AudioRetentionDays,
            s.Provider == ScribeProvider.BrightPas && s.Speech != SpeechMode.Off && s.SpeechUrl is not null);
    }

    public async Task<IReadOnlyList<string>> SaveSettingsAsync(SettingsEdit e, string user, CancellationToken ct = default)
    {
        var s = await RowAsync(ct);
        var errors = new List<string>();
        static bool Url(string? u) => Uri.TryCreate(u, UriKind.Absolute, out var x) && x.Scheme is "http" or "https";
        if (e.Speech != SpeechMode.Off && !Url(e.SpeechUrl)) errors.Add("Enter the speech engine address (e.g. http://pas-speech:8000).");
        if (e.Draft != DraftMode.TemplateOnly && !Url(e.DraftUrl)) errors.Add("Enter the drafting engine address.");
        if (e.Speech == SpeechMode.Cloud && Uri.TryCreate(e.SpeechUrl, UriKind.Absolute, out var su) && su.Scheme != "https") errors.Add("A cloud speech service must use https.");
        if (e.Draft == DraftMode.CloudClaude && Uri.TryCreate(e.DraftUrl, UriKind.Absolute, out var du) && du.Scheme != "https" && !du.IsLoopback) errors.Add("The cloud drafting service must use https.");
        var cloud = e.Speech == SpeechMode.Cloud || e.Draft == DraftMode.CloudClaude;
        if (cloud && string.IsNullOrWhiteSpace(e.CloudPrivacyReference)) errors.Add("Cloud processing needs the privacy impact assessment addendum / supplier assessment reference.");
        if (e.Draft == DraftMode.CloudClaude && s.DraftKey is null && string.IsNullOrWhiteSpace(e.DraftKey)) errors.Add("Enter the Claude API key.");
        if (e.Speech == SpeechMode.Cloud && s.SpeechKey is null && string.IsNullOrWhiteSpace(e.SpeechKey)) errors.Add("Enter the cloud speech service key.");
        if (e.Provider == ScribeProvider.Medow && (string.IsNullOrWhiteSpace(e.MedowPiaReference) || string.IsNullOrWhiteSpace(e.MedowSupplierAssessment) || string.IsNullOrWhiteSpace(e.MedowAgreementReference)))
            errors.Add("Medow Health can be switched on only after the privacy impact assessment addendum, HISF supplier assessment and data processing agreement are recorded (spec 7.17.2).");
        if (e.MedowLaunchUrl is { Length: > 0 } ml && !Url(ml)) errors.Add("The Medow launch address must be a web address.");
        if (e.AudioRetentionDays is < 0 or > 365) errors.Add("Keep audio for 0 (delete on signing) to 365 days.");
        if (errors.Count > 0) return errors;
        var now = clock.GetUtcNow();
        if (cloud && (s.CloudPrivacyReference != e.CloudPrivacyReference?.Trim() || s.CloudApprovedAt is null)) { s.CloudApprovedBy = user; s.CloudApprovedAt = now; }
        if (!cloud) { s.CloudApprovedBy = null; s.CloudApprovedAt = null; }
        if (e.Provider == ScribeProvider.Medow && s.Provider != ScribeProvider.Medow) { s.MedowApprovedBy = user; s.MedowApprovedAt = now; }
        s.Provider = e.Provider; s.Speech = e.Speech; s.SpeechUrl = Blank(e.SpeechUrl); s.SpeechModel = Blank(e.SpeechModel); s.Draft = e.Draft; s.DraftUrl = Blank(e.DraftUrl); s.DraftModel = Blank(e.DraftModel);
        if (!string.IsNullOrWhiteSpace(e.SpeechKey)) s.SpeechKey = cipher.Protect(Encoding.UTF8.GetBytes(e.SpeechKey.Trim()));
        if (!string.IsNullOrWhiteSpace(e.DraftKey)) s.DraftKey = cipher.Protect(Encoding.UTF8.GetBytes(e.DraftKey.Trim()));
        s.CloudPrivacyReference = Blank(e.CloudPrivacyReference); s.MedowPiaReference = Blank(e.MedowPiaReference); s.MedowSupplierAssessment = Blank(e.MedowSupplierAssessment);
        s.MedowAgreementReference = Blank(e.MedowAgreementReference); s.MedowLaunchUrl = Blank(e.MedowLaunchUrl); s.AudioRetentionDays = e.AudioRetentionDays; s.UpdatedBy = user; s.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "ScribeSettings", "1", reason: $"provider {e.Provider}, speech {e.Speech} {s.SpeechUrl}, drafting {e.Draft} {s.DraftUrl}{(cloud ? $", cloud ref {s.CloudPrivacyReference}" : "")}", ct: ct);
        return [];
    }

    /// <summary>A new inbound token for an external scribe (shown once).</summary>
    public async Task<string> NewInboundTokenAsync(string user, CancellationToken ct = default)
    {
        var s = await RowAsync(ct);
        var token = "bps_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        s.InboundTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "NewToken", "ScribeSettings", "1", reason: "external scribe inbound token", ct: ct);
        return token;
    }

    public async Task<bool> InboundTokenOkAsync(string? token, CancellationToken ct = default)
    {
        var s = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s?.InboundTokenHash is null || string.IsNullOrEmpty(token)) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(s.InboundTokenHash), SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    public async Task<(Engine? Speech, Engine? Draft, ScribeSettings Row)> EnginesAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct) ?? new ScribeSettings();
        string? Key(byte[]? k) => k is null ? null : Encoding.UTF8.GetString(cipher.Unprotect(k));
        return (s.Speech == SpeechMode.Off || s.SpeechUrl is null ? null : new Engine(s.SpeechUrl, s.SpeechModel, Key(s.SpeechKey)),
            s.Draft == DraftMode.TemplateOnly || s.DraftUrl is null ? null : new Engine(s.DraftUrl, s.DraftModel, Key(s.DraftKey)), s);
    }

    // ------------------------------------------------------------------ templates (governance approval)
    public async Task<IReadOnlyList<ScribeTemplate>> TemplatesAsync(CancellationToken ct = default)
    {
        if (!await db.Templates.AnyAsync(ct))
        {
            var now = clock.GetUtcNow();
            foreach (var (code, name, context, sections) in ScribeText.DefaultTemplates)
                db.Templates.Add(new ScribeTemplate { Code = code, Name = name, Context = context, Sections = sections, Status = TemplateStatus.Approved, ApprovedBy = "BrightPAS default (review)", ApprovedAt = now, UpdatedBy = "system", UpdatedAt = now });
            await db.SaveChangesAsync(ct);
        }
        return await db.Templates.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<(ScribeTemplate? Row, IReadOnlyList<string> Errors)> SaveTemplateAsync(Guid? id, TemplateEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var code = (e.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 30 || !code.All(c => char.IsLetterOrDigit(c) || c == '-')) errors.Add("Code is 2–30 letters, digits or dashes.");
        if (string.IsNullOrWhiteSpace(e.Name)) errors.Add("Enter the template name.");
        if (ScribeText.ParseSections(e.Sections ?? "").Count == 0) errors.Add("Add at least one section, one per line: \"Heading: what to put here\".");
        if (await db.Templates.AnyAsync(t => t.Code == code && t.Id != id, ct)) errors.Add("Another template has that code.");
        if (errors.Count > 0) return (null, errors);
        var t = id is null ? null : await db.Templates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && t is null) return (null, ["Template not found."]);
        if (t is null) { t = new ScribeTemplate(); db.Templates.Add(t); }
        else if (t.Sections != e.Sections || t.Name != e.Name.Trim()) { t.Version++; }
        t.Code = code; t.Name = e.Name.Trim(); t.Context = e.Context; t.Sections = e.Sections!.Trim(); t.UpdatedBy = user; t.UpdatedAt = clock.GetUtcNow();
        t.Status = TemplateStatus.Draft; t.ApprovedBy = null; t.ApprovedAt = null; // any change needs approval again
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "ScribeTemplate", t.Id.ToString(), reason: $"{t.Code} v{t.Version} – awaiting approval", ct: ct);
        return (t, []);
    }

    public async Task<string?> ApproveTemplateAsync(Guid id, bool approve, string user, CancellationToken ct = default)
    {
        var t = await db.Templates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return "Template not found.";
        if (approve && string.Equals(t.UpdatedBy, user, StringComparison.OrdinalIgnoreCase)) return "Someone other than the author must approve a clinical template.";
        t.Status = approve ? TemplateStatus.Approved : TemplateStatus.Retired; t.ApprovedBy = approve ? user : null; t.ApprovedAt = approve ? clock.GetUtcNow() : null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, approve ? "Approve" : "Retire", "ScribeTemplate", t.Id.ToString(), reason: $"{t.Code} v{t.Version}", ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ dictionary
    public async Task<IReadOnlyList<DictionaryTerm>> DictionaryAsync(CancellationToken ct = default)
    {
        if (!await db.Dictionary.AnyAsync(ct))
        {
            var now = clock.GetUtcNow();
            foreach (var (term, sounds, cat) in ScribeText.DefaultDictionary) db.Dictionary.Add(new DictionaryTerm { Term = term, SoundsLike = sounds, Category = cat, UpdatedBy = "system", UpdatedAt = now });
            await db.SaveChangesAsync(ct);
        }
        return await db.Dictionary.AsNoTracking().OrderBy(t => t.Category).ThenBy(t => t.Term).ToListAsync(ct);
    }

    public async Task<(DictionaryTerm? Row, string? Error)> SaveTermAsync(Guid? id, TermEdit e, string user, CancellationToken ct = default)
    {
        var term = (e.Term ?? "").Trim();
        if (term.Length is < 2 or > 100) return (null, "Enter the term (2–100 characters).");
        if (!Categories.Contains(e.Category)) return (null, "Choose a category.");
        if (await db.Dictionary.AnyAsync(t => t.Term == term && t.Id != id, ct)) return (null, "That term is already in the dictionary.");
        var t = id is null ? null : await db.Dictionary.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) { t = new DictionaryTerm(); db.Dictionary.Add(t); }
        t.Term = term; t.SoundsLike = Blank(e.SoundsLike)?[..Math.Min(300, e.SoundsLike!.Trim().Length)]; t.Category = e.Category; t.UpdatedBy = user; t.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "ScribeDictionary", t.Id.ToString(), reason: term, ct: ct);
        return (t, null);
    }

    public async Task<string?> DeleteTermAsync(Guid id, string user, CancellationToken ct = default)
    {
        var t = await db.Dictionary.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return "Not found.";
        db.Dictionary.Remove(t);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "ScribeDictionary", id.ToString(), reason: t.Term, ct: ct);
        return null;
    }

    public async Task<DictationResult> DictationAsync(string spoken, CancellationToken ct = default)
    {
        var terms = await DictionaryAsync(ct);
        return ScribeText.Dictation(spoken, terms.Select(t => (t.Term, t.SoundsLike)), terms.Where(t => t.Category == "Medicine").Select(t => t.Term));
    }

    // ------------------------------------------------------------------ consent
    /// <summary>The consent that applies now: a standing consent not withdrawn, else none (a per-session consent is linked to its session).</summary>
    public async Task<ScribeConsent?> StandingConsentAsync(Guid patientId, CancellationToken ct = default) =>
        await db.Consents.AsNoTracking().Where(c => c.PatientId == patientId && c.Standing && c.WithdrawnAt == null).OrderByDescending(c => c.RecordedAt).FirstOrDefaultAsync(ct);

    public async Task<(ScribeConsent? Row, string? Error)> RecordConsentAsync(Guid patientId, Guid? sessionId, bool given, bool standing, string user, CancellationToken ct = default)
    {
        if (!await patients.Patients.AnyAsync(p => p.Id == patientId, ct)) return (null, "Patient not found.");
        var s = await RowAsync(ct);
        if (standing && !given) await WithdrawAsync(patientId, user, ct);
        var c = new ScribeConsent { PatientId = patientId, Given = given, Standing = standing && given, Product = s.Provider, RecordedBy = user, RecordedAt = clock.GetUtcNow() };
        db.Consents.Add(c);
        if (sessionId is { } sid && await db.Sessions.SingleOrDefaultAsync(x => x.Id == sid && x.PatientId == patientId, ct) is { } sess) sess.ConsentId = c.Id;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, given ? "ConsentGiven" : "ConsentDeclined", "ScribeConsent", c.Id.ToString(), patientId.ToString(), reason: $"{(standing ? "standing" : "this session")}, {s.Provider}", ct: ct);
        return (c, null);
    }

    public async Task WithdrawAsync(Guid patientId, string user, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        foreach (var c in await db.Consents.Where(c => c.PatientId == patientId && c.Standing && c.WithdrawnAt == null).ToListAsync(ct)) { c.WithdrawnAt = now; c.WithdrawnBy = user; }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ConsentWithdrawn", "ScribeConsent", patientId.ToString(), patientId.ToString(), ct: ct);
    }

    private async Task<bool> HasConsentAsync(ScribeSession s, CancellationToken ct) =>
        (s.ConsentId is { } cid && await db.Consents.AnyAsync(c => c.Id == cid && c.Given && c.WithdrawnAt == null, ct)) || await StandingConsentAsync(s.PatientId, ct) is { Given: true };

    // ------------------------------------------------------------------ sessions
    public async Task<(ScribeSession? Row, IReadOnlyList<string> Errors)> StartAsync(NewSession n, string user, CancellationToken ct = default)
    {
        var s = await RowAsync(ct);
        if (s.Provider == ScribeProvider.Off) return (null, ["The scribe is switched off (Scribe › Settings)."]);
        if (!await patients.Patients.AnyAsync(p => p.Id == n.PatientId, ct)) return (null, ["Patient not found."]);
        var t = await db.Templates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == n.TemplateId, ct);
        if (t is null) return (null, ["Choose a template."]);
        if (t.Status != TemplateStatus.Approved) return (null, ["That template is not approved for clinical use yet."]);
        var standing = await StandingConsentAsync(n.PatientId, ct);
        var sess = new ScribeSession
        {
            PatientId = n.PatientId,
            BookingId = n.BookingId,
            Context = n.Context,
            TemplateId = t.Id,
            TemplateVersion = t.Version,
            Provider = s.Provider,
            Clinician = user,
            ConsentId = standing is { Given: true } ? standing.Id : null,
            CreatedAt = clock.GetUtcNow()
        };
        db.Sessions.Add(sess);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Start", "ScribeSession", sess.Id.ToString(), n.PatientId.ToString(), reason: $"{s.Provider} {t.Code} {n.Context}", ct: ct);
        return (sess, []);
    }

    private async Task<ScribeSession> OpenAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Sessions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Scribe session not found.");
        if (s.Status is SessionStatus.Signed or SessionStatus.Discarded) throw new InvalidOperationException("This session is closed.");
        return s;
    }

    public async Task<ScribeSession?> FindAsync(Guid id, CancellationToken ct = default) => await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<string?> AppendAudioAsync(Guid id, int seq, string contentType, byte[] data, CancellationToken ct = default)
    {
        var s = await OpenAsync(id, ct);
        if (s.Provider != ScribeProvider.BrightPas) return "Audio is recorded by the external scribe, not BrightPAS.";
        if (!await HasConsentAsync(s, ct)) return "The patient has not consented to AI recording – recording is disabled.";
        if (s.Status != SessionStatus.Recording) return "Recording has finished for this session.";
        if (seq != s.AudioChunks) return seq < s.AudioChunks ? null : $"Audio part {seq} arrived out of order (expected {s.AudioChunks}).";
        if (data.Length > 32 * 1024 * 1024) return "Audio part too large.";
        await audio.AppendAsync(s.Id, seq, data, ct);
        s.AudioChunks++; s.AudioBytes += data.Length; s.AudioType ??= contentType.Split(';')[0].Trim(); s.AudioFolder = s.Id.ToString("N");
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> StopAsync(Guid id, decimal? seconds, string user, CancellationToken ct = default)
    {
        var s = await OpenAsync(id, ct);
        if (s.Status != SessionStatus.Recording) return null;
        if (s.AudioChunks == 0) return "Nothing was recorded.";
        s.Status = SessionStatus.Recorded; s.DurationSeconds = seconds is > 0 and < 36000 ? Math.Round(seconds.Value, 1) : null; s.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "StopRecording", "ScribeSession", s.Id.ToString(), s.PatientId.ToString(), reason: $"{s.DurationSeconds}s, {s.AudioBytes / 1024} KB", ct: ct);
        return null;
    }

    public async Task<(byte[] Audio, string Type)> AudioAsync(Guid id, CancellationToken ct = default)
    {
        var s = await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return (await audio.ReadAllAsync(s.Id, ct), s.AudioType ?? "audio/webm");
    }

    public async Task SaveTranscriptAsync(Guid id, IReadOnlyList<Segment> segments, string engine, string user, CancellationToken ct = default)
    {
        var s = await OpenAsync(id, ct);
        var terms = await DictionaryAsync(ct);
        var dict = terms.Select(t => (t.Term, t.SoundsLike)).ToList();
        await db.Segments.Where(x => x.SessionId == s.Id).ExecuteDeleteAsync(ct);
        await db.Sentences.Where(x => x.SessionId == s.Id).ExecuteDeleteAsync(ct);
        var n = 0;
        foreach (var seg in segments.Where(x => !string.IsNullOrWhiteSpace(x.Text)))
            db.Segments.Add(new TranscriptSegment { SessionId = s.Id, Seq = ++n, Speaker = Clip(seg.Speaker, 30) ?? "Unknown", StartSeconds = seg.Start, EndSeconds = seg.End, Text = Clip(ScribeText.Correct(seg.Text.Trim(), dict), 4000)!, Confidence = seg.Confidence });
        s.Status = SessionStatus.Transcribed; s.SpeechEngine = Clip(engine, 100); s.Draft = null; s.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Transcribe", "ScribeSession", s.Id.ToString(), s.PatientId.ToString(), reason: $"{n} segment(s) by {engine}", ct: ct);
    }

    public async Task<IReadOnlyList<Segment>> SegmentsAsync(Guid id, CancellationToken ct = default) =>
        (await db.Segments.AsNoTracking().Where(x => x.SessionId == id).OrderBy(x => x.Seq).ToListAsync(ct)).Select(x => new Segment(x.Seq, x.Speaker, x.StartSeconds, x.EndSeconds, x.Text, x.Confidence)).ToList();

    public async Task<string?> SetSpeakerAsync(Guid id, int seq, string speaker, CancellationToken ct = default)
    {
        await OpenAsync(id, ct);
        if (speaker is not ("Clinician" or "Patient" or "Support person" or "Interpreter" or "Unknown")) return "Choose clinician, patient, support person, interpreter or unknown.";
        var seg = await db.Segments.SingleOrDefaultAsync(x => x.SessionId == id && x.Seq == seq, ct);
        if (seg is null) return "Segment not found.";
        seg.Speaker = speaker;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task SaveDraftAsync(Guid id, IReadOnlyList<DraftedSentence> sentences, string engine, bool deIdentified, string user, CancellationToken ct = default)
    {
        var s = await OpenAsync(id, ct);
        await db.Sentences.Where(x => x.SessionId == s.Id).ExecuteDeleteAsync(ct);
        var n = 0;
        foreach (var x in sentences) db.Sentences.Add(new DraftSentence { SessionId = s.Id, Seq = ++n, Section = Clip(x.Section, 100) ?? "Notes", Text = Clip(x.Text, 2000) ?? "", Sources = x.Sources.Length == 0 ? null : string.Join(",", x.Sources) });
        s.Draft = ScribeText.Render(sentences); s.DraftEngine = Clip(engine, 100); s.DeIdentified = deIdentified; s.Status = SessionStatus.Drafted; s.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Draft", "ScribeSession", s.Id.ToString(), s.PatientId.ToString(), reason: $"{n} sentence(s) by {engine}{(deIdentified ? ", de-identified" : "")}", ct: ct);
    }

    /// <summary>A draft returned by an external scribe (Medow) or pasted in – still an unsigned draft, marked AI-assisted with the product name.</summary>
    public async Task<string?> ExternalDraftAsync(Guid id, string text, string product, string user, CancellationToken ct = default)
    {
        var s = await OpenAsync(id, ct);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 50_000) return "The draft is empty or too long.";
        if (!await HasConsentAsync(s, ct)) return "The patient has not consented to AI recording for this session.";
        await db.Sentences.Where(x => x.SessionId == s.Id).ExecuteDeleteAsync(ct);
        var n = 0;
        foreach (var para in text.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)) db.Sentences.Add(new DraftSentence { SessionId = s.Id, Seq = ++n, Section = "Draft", Text = Clip(para.Trim(), 2000)! });
        s.Draft = text.Trim(); s.DraftEngine = Clip(product, 100); s.Status = SessionStatus.Drafted; s.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ExternalDraft", "ScribeSession", s.Id.ToString(), s.PatientId.ToString(), reason: product, ct: ct);
        return null;
    }

    public async Task<IReadOnlyList<DraftSentence>> SentencesAsync(Guid id, CancellationToken ct = default) =>
        await db.Sentences.AsNoTracking().Where(x => x.SessionId == id).OrderBy(x => x.Seq).ToListAsync(ct);

    public async Task<IReadOnlyList<Highlight>> HighlightsAsync(Guid id, string text, CancellationToken ct = default)
    {
        var terms = await DictionaryAsync(ct);
        var unsure = await db.Segments.AsNoTracking().Where(x => x.SessionId == id && x.Confidence != null && x.Confidence < 0.6m).Select(x => x.Text).ToListAsync(ct);
        var words = unsure.SelectMany(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(w => w.Length > 4).Take(40);
        return ScribeText.Check(text, terms.Where(t => t.Category == "Medicine").Select(t => t.Term), words);
    }

    /// <summary>The clinician signs the reviewed text. Audio is deleted now unless a retention period is set. Returns the session for the caller to file the note.</summary>
    public async Task<(ScribeSession? Row, string? Error)> SignAsync(Guid id, string text, string user, CancellationToken ct = default)
    {
        var s = await OpenAsync(id, ct);
        if (s.Status != SessionStatus.Drafted) return (null, "Make a draft first (or paste the external scribe's draft).");
        if (string.IsNullOrWhiteSpace(text)) return (null, "The note is empty.");
        if (!string.Equals(s.Clinician, user, StringComparison.OrdinalIgnoreCase)) return (null, "Only the clinician who ran the session can sign it.");
        s.SignedText = text.Replace("\r\n", "\n").Trim(); s.SignedBy = user; s.SignedAt = clock.GetUtcNow(); s.Status = SessionStatus.Signed;
        s.EditPercent = ScribeText.EditPercent(s.Draft ?? "", s.SignedText);
        var keep = (await RowAsync(ct)).AudioRetentionDays;
        if (keep == 0) { audio.Delete(s.Id); if (s.AudioChunks > 0) s.AudioDeletedAt = clock.GetUtcNow(); }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Sign", "ScribeSession", s.Id.ToString(), s.PatientId.ToString(), reason: $"AI-assisted ({s.Provider}, {s.DraftEngine}); edited {s.EditPercent}%", ct: ct);
        return (s, null);
    }

    public async Task SetFiledAsync(Guid id, Guid? noteId, Guid? documentId, CancellationToken ct = default)
    {
        var s = await db.Sessions.SingleAsync(x => x.Id == id, ct);
        s.NoteId = noteId; s.DocumentId = documentId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> DiscardAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var s = await OpenAsync(id, ct);
        if (string.IsNullOrWhiteSpace(reason)) return "Give the reason for discarding.";
        s.Status = SessionStatus.Discarded; s.DiscardReason = Clip(reason, 300); s.UpdatedAt = clock.GetUtcNow();
        audio.Delete(s.Id); if (s.AudioChunks > 0) s.AudioDeletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Discard", "ScribeSession", s.Id.ToString(), s.PatientId.ToString(), reason: s.DiscardReason, ct: ct);
        return null;
    }

    /// <summary>Deletes audio past the retention period (signed/discarded sessions) and of sessions abandoned for 7 days.</summary>
    public async Task<int> RetentionAsync(CancellationToken ct = default)
    {
        var keep = (await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct))?.AudioRetentionDays ?? 0;
        var now = clock.GetUtcNow();
        var due = await db.Sessions.Where(s => s.AudioChunks > 0 && s.AudioDeletedAt == null &&
            (((s.Status == SessionStatus.Signed || s.Status == SessionStatus.Discarded) && (s.SignedAt ?? s.UpdatedAt ?? s.CreatedAt) < now.AddDays(-keep)) || (s.CreatedAt < now.AddDays(-7)))).ToListAsync(ct);
        foreach (var s in due) { audio.Delete(s.Id); s.AudioDeletedAt = now; }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    public async Task<IReadOnlyList<ScribeSession>> SessionsAsync(string? clinician, Guid? patientId, int take = 100, CancellationToken ct = default)
    {
        var q = db.Sessions.AsNoTracking();
        if (clinician is not null) q = q.Where(s => s.Clinician == clinician);
        if (patientId is { } pid) q = q.Where(s => s.PatientId == pid);
        return await q.OrderByDescending(s => s.CreatedAt).Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AccuracyRow>> AccuracyAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var a = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var b = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2);
        var rows = (await db.Sessions.AsNoTracking().Where(s => s.Status == SessionStatus.Signed && s.EditPercent != null).ToListAsync(ct)).Where(s => s.SignedAt >= a && s.SignedAt < b).ToList();
        return rows.GroupBy(s => (s.Clinician, Engine: $"{s.SpeechEngine ?? "–"} / {s.DraftEngine ?? "–"}")).OrderBy(g => g.Key.Clinician)
            .Select(g => new AccuracyRow(g.Key.Clinician, g.Key.Engine, g.Count(), Math.Round(g.Average(x => x.EditPercent!.Value), 1), g.Max(x => x.EditPercent))).ToList();
    }

    public async Task<ScribeText.Identity> IdentityAsync(Guid patientId, CancellationToken ct = default)
    {
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == patientId, ct);
        return new(p.FamilyName, p.GivenNames, p.PreferredName, p.Nhi, p.Mrn, p.DateOfBirth, p.MobilePhone ?? p.HomePhone, p.Email, p.AddressLine1);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    internal static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length <= max ? s.Trim() : s.Trim()[..max]);
}
