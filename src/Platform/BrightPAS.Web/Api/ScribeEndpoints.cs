using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Identity;
using BrightPAS.Licensing;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Scribe;
using BrightPAS.Web.Integration;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>BrightPAS Scribe (0.16.0, ADR 0028): dictation, ambient sessions with consent, transcription, drafting, review and sign-off, templates, dictionary, Medow option.</summary>
public static class ScribeEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Done(string? error) => error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });
    private static IResult Errors(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });
    private static IResult Bad(string m) => Results.BadRequest(new { errors = new[] { m } });

    public sealed record ConsentRequest(Guid PatientId, Guid? SessionId, bool Given, bool Standing);
    public sealed record StopRequest(decimal? Seconds);
    public sealed record SpeakerRequest(int Seq, string Speaker);
    public sealed record TextRequest(string Text);
    public sealed record ReasonRequest(string Reason);
    public sealed record ApproveRequest(bool Approve);
    public sealed record InboundDraft(Guid SessionId, string Text, string? Product);

    private static async Task<IResult> Guard(Func<Task<IResult>> f)
    {
        try { return await f(); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { errors = new[] { ex.Message } }); }
        catch (InvalidOperationException ex) { return Bad(ex.Message); }
        catch (FormatException ex) { return Bad(ex.Message); }
        catch (HttpRequestException ex) { return Bad("Could not reach the engine: " + ex.Message); }
        catch (TaskCanceledException) { return Bad("The engine took too long to answer."); }
    }

    private static async Task<byte[]> Body(HttpRequest req, long max)
    {
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms);
        if (ms.Length > max) throw new InvalidOperationException("Too large.");
        return ms.ToArray();
    }

    public static void MapScribeEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/scribe").RequireModule(ScribeModule.Id);

        g.MapGet("/status", async (ScribeService svc) =>
        {
            var s = await svc.SettingsAsync();
            return Results.Ok(new { provider = s.Provider.ToString(), dictation = s.DictationAvailable, ambient = s.Provider != ScribeProvider.Off, drafting = s.Draft.ToString(), medowLaunchUrl = s.Provider == ScribeProvider.Medow ? s.MedowLaunchUrl : null });
        }).RequireAuthorization(Permissions.ScribeUse);

        // Dictation: audio of one utterance → text with voice commands, dictionary/NZ spelling and safety highlights. No patient consent needed (no patient recorded).
        g.MapPost("/dictate", (HttpRequest req, ClaimsPrincipal u, ScribeService svc, ScribeEngines engines, BrightPAS.Core.Audit.AuditWriter audit) => Guard(async () =>
        {
            var (speech, _, _) = await svc.EnginesAsync();
            if (speech is null) return Bad("Dictation needs a speech engine (Scribe › Settings).");
            var audio = await Body(req, 25L * 1024 * 1024);
            if (audio.Length < 100) return Bad("Nothing was heard.");
            var terms = (await svc.DictionaryAsync()).Select(t => t.Term);
            var (segs, engine) = await engines.TranscribeAsync(speech, audio, req.ContentType ?? "audio/webm", terms);
            var result = await svc.DictationAsync(string.Join(" ", segs.Select(s => s.Text)));
            await audit.WriteAsync(Me(u), "Dictate", "Scribe", engine, reason: $"{audio.Length / 1024} KB, {result.Text.Length} characters");
            return Results.Ok(result);
        })).RequireAuthorization(Permissions.ScribeUse);

        // Templates and dictionary.
        g.MapGet("/templates", async (ScribeService svc) => Results.Ok(await svc.TemplatesAsync())).RequireAuthorization(Permissions.ScribeAny);
        g.MapPost("/templates", async (TemplateEdit r, ClaimsPrincipal u, ScribeService svc) => { var (t, e) = await svc.SaveTemplateAsync(null, r, Me(u)); return e.Count > 0 ? Errors(e) : Results.Ok(t); }).RequireAuthorization(Permissions.ScribeManage);
        g.MapPut("/templates/{id:guid}", async (Guid id, TemplateEdit r, ClaimsPrincipal u, ScribeService svc) => { var (t, e) = await svc.SaveTemplateAsync(id, r, Me(u)); return e.Count > 0 ? Errors(e) : Results.Ok(t); }).RequireAuthorization(Permissions.ScribeManage);
        g.MapPost("/templates/{id:guid}/approve", async (Guid id, ApproveRequest r, ClaimsPrincipal u, ScribeService svc) => Done(await svc.ApproveTemplateAsync(id, r.Approve, Me(u)))).RequireAuthorization(Permissions.ScribeApprove);
        g.MapGet("/dictionary", async (ScribeService svc) => Results.Ok(new { terms = await svc.DictionaryAsync(), categories = ScribeService.Categories })).RequireAuthorization(Permissions.ScribeAny);
        g.MapPost("/dictionary", async (TermEdit r, ClaimsPrincipal u, ScribeService svc) => { var (t, e) = await svc.SaveTermAsync(null, r, Me(u)); return e is not null ? Bad(e) : Results.Ok(t); }).RequireAuthorization(Permissions.ScribeManage);
        g.MapPut("/dictionary/{id:guid}", async (Guid id, TermEdit r, ClaimsPrincipal u, ScribeService svc) => { var (t, e) = await svc.SaveTermAsync(id, r, Me(u)); return e is not null ? Bad(e) : Results.Ok(t); }).RequireAuthorization(Permissions.ScribeManage);
        g.MapDelete("/dictionary/{id:guid}", async (Guid id, ClaimsPrincipal u, ScribeService svc) => Done(await svc.DeleteTermAsync(id, Me(u)))).RequireAuthorization(Permissions.ScribeManage);

        // Settings.
        g.MapGet("/settings", async (ScribeService svc, AudioStore audio) => { var (ok, detail) = audio.Check(); return Results.Ok(new { settings = await svc.SettingsAsync(), audio = new { ok, detail } }); }).RequireAuthorization(Permissions.ScribeManage);
        g.MapPut("/settings", async (SettingsEdit r, ClaimsPrincipal u, ScribeService svc) => Errors(await svc.SaveSettingsAsync(r, Me(u)))).RequireAuthorization(Permissions.ScribeManage);
        g.MapPost("/settings/token", async (ClaimsPrincipal u, ScribeService svc) => Results.Ok(new { token = await svc.NewInboundTokenAsync(Me(u)) })).RequireAuthorization(Permissions.ScribeManage);
        g.MapGet("/accuracy", async (DateOnly? from, DateOnly? to, ScribeService svc, TimeProvider clock) =>
        {
            var t = to ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            return Results.Ok(await svc.AccuracyAsync(from ?? t.AddMonths(-3), t));
        }).RequireAuthorization(Permissions.ScribeAny);

        // Consent.
        g.MapGet("/consent/{patientId:guid}", async (Guid patientId, ScribeService svc) => Results.Ok(new { standing = await svc.StandingConsentAsync(patientId) })).RequireAuthorization(Permissions.ScribeUse);
        g.MapPost("/consent", async (ConsentRequest r, ClaimsPrincipal u, ScribeService svc) => { var (c, e) = await svc.RecordConsentAsync(r.PatientId, r.SessionId, r.Given, r.Standing, Me(u)); return e is not null ? Bad(e) : Results.Ok(c); }).RequireAuthorization(Permissions.ScribeUse);
        g.MapPost("/consent/{patientId:guid}/withdraw", async (Guid patientId, ClaimsPrincipal u, ScribeService svc) => { await svc.WithdrawAsync(patientId, Me(u)); return Results.Ok(); }).RequireAuthorization(Permissions.ScribeUse);

        // Sessions.
        g.MapGet("/sessions", async (bool? mine, Guid? patientId, ClaimsPrincipal u, ScribeService svc, PatientsDbContext pts) =>
        {
            var list = await svc.SessionsAsync(mine == false ? null : Me(u), patientId);
            var ids = list.Select(s => s.PatientId).Distinct().ToList();
            var names = await pts.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { name = $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p.Nhi });
            return Results.Ok(list.Select(s => new { s.Id, s.PatientId, patient = names.GetValueOrDefault(s.PatientId), context = s.Context.ToString(), status = s.Status.ToString(), provider = s.Provider.ToString(), s.Clinician, s.CreatedAt, s.SignedAt, s.EditPercent, s.DurationSeconds }));
        }).RequireAuthorization(Permissions.ScribeUse);
        g.MapPost("/sessions", async (NewSession r, ClaimsPrincipal u, ScribeService svc) => { var (s, e) = await svc.StartAsync(r, Me(u)); return e.Count > 0 ? Errors(e) : Results.Ok(new { s!.Id }); }).RequireAuthorization(Permissions.ScribeUse);
        g.MapGet("/sessions/{id:guid}", async (Guid id, ClaimsPrincipal u, IServiceProvider sp) =>
        {
            var svc = sp.GetRequiredService<ScribeService>();
            var s = await svc.FindAsync(id);
            if (s is null) return Results.NotFound();
            var pt = await sp.GetRequiredService<PatientsDbContext>().Patients.AsNoTracking().Include(p => p.Alerts).SingleAsync(p => p.Id == s.PatientId);
            var t = (await svc.TemplatesAsync()).Single(x => x.Id == s.TemplateId);
            var standing = await svc.StandingConsentAsync(s.PatientId);
            var db = sp.GetRequiredService<ScribeDbContext>();
            var consent = s.ConsentId is { } cid ? await db.Consents.AsNoTracking().SingleOrDefaultAsync(c => c.Id == cid) : null;
            var consented = (consent is { Given: true, WithdrawnAt: null }) || standing is { Given: true };
            var settings = await svc.SettingsAsync();
            var draftHighlights = s.Draft is null ? [] : await svc.HighlightsAsync(id, s.Draft);
            string? procedure = null;
            if (s.BookingId is { } bid) procedure = await sp.GetRequiredService<BrightPAS.Modules.Theatre.TheatreDbContext>().Bookings.AsNoTracking().Where(b => b.Id == bid).Select(b => b.Procedure).SingleOrDefaultAsync();
            var age = DateTime.Today.Year - pt.DateOfBirth.Year - (DateTime.Today.DayOfYear < pt.DateOfBirth.DayOfYear ? 1 : 0);
            await sp.GetRequiredService<BrightPAS.Core.Audit.AuditWriter>().WriteAsync(Me(u), "View", "ScribeSession", id.ToString(), s.PatientId.ToString());
            return Results.Ok(new
            {
                session = s,
                template = t,
                consented,
                consent,
                standing,
                settings = new { provider = settings.Provider.ToString(), speech = settings.Speech.ToString(), draft = settings.Draft.ToString(), settings.MedowLaunchUrl },
                patient = new { pt.Id, name = $"{pt.FamilyName.ToUpperInvariant()}, {pt.GivenNames}", pt.Nhi, pt.Mrn, pt.DateOfBirth, sex = pt.Sex.ToString(), alerts = pt.Alerts.Where(a => a.IsActive).Select(a => new { type = a.Type.ToString(), severity = a.Severity.ToString(), a.Description }) },
                procedure,
                segments = await svc.SegmentsAsync(id),
                sentences = await svc.SentencesAsync(id),
                highlights = draftHighlights,
                // What an external scribe receives (minimum necessary, spec 7.17.1): first name, age, sex, procedure, alert types – no NHI, address or contact details.
                externalPayload = s.Provider == ScribeProvider.Medow ? new { reference = s.Id, firstName = pt.PreferredName ?? pt.GivenNames.Split(' ')[0], age, sex = pt.Sex.ToString(), procedure, template = t.Name, alerts = pt.Alerts.Where(a => a.IsActive).Select(a => a.Type.ToString()) } : null,
                mine = string.Equals(s.Clinician, Me(u), StringComparison.OrdinalIgnoreCase),
            });
        }).RequireAuthorization(Permissions.ScribeUse);
        g.MapPost("/sessions/{id:guid}/audio/{seq:int}", (Guid id, int seq, HttpRequest req, ScribeService svc) => Guard(async () =>
            Done(await svc.AppendAudioAsync(id, seq, req.ContentType ?? "audio/webm", await Body(req, 32L * 1024 * 1024))))).RequireAuthorization(Permissions.ScribeUse);
        g.MapPost("/sessions/{id:guid}/stop", (Guid id, StopRequest r, ClaimsPrincipal u, ScribeService svc) => Guard(async () => Done(await svc.StopAsync(id, r.Seconds, Me(u))))).RequireAuthorization(Permissions.ScribeUse);
        g.MapPost("/sessions/{id:guid}/transcribe", (Guid id, ClaimsPrincipal u, ScribeService svc, ScribeEngines engines) => Guard(async () =>
        {
            var s = await svc.FindAsync(id) ?? throw new KeyNotFoundException("Scribe session not found.");
            if (s.Status is not (SessionStatus.Recorded or SessionStatus.Transcribed or SessionStatus.Drafted)) return Bad("Stop the recording first.");
            if (s.AudioDeletedAt is not null) return Bad("The audio has been deleted.");
            var (speech, _, _) = await svc.EnginesAsync();
            if (speech is null) return Bad("No speech engine is set up (Scribe › Settings).");
            var (audio, type) = await svc.AudioAsync(id);
            var (segs, engine) = await engines.TranscribeAsync(speech, audio, type, (await svc.DictionaryAsync()).Select(t => t.Term));
            if (segs.Count == 0) return Bad("The speech engine heard nothing.");
            await svc.SaveTranscriptAsync(id, segs, engine, Me(u));
            return Results.Ok(new { segments = segs.Count, engine });
        })).RequireAuthorization(Permissions.ScribeUse);
        g.MapPut("/sessions/{id:guid}/speaker", (Guid id, SpeakerRequest r, ScribeService svc) => Guard(async () => Done(await svc.SetSpeakerAsync(id, r.Seq, r.Speaker)))).RequireAuthorization(Permissions.ScribeUse);
        g.MapPost("/sessions/{id:guid}/draft", (Guid id, ClaimsPrincipal u, ScribeService svc, ScribeEngines engines) => Guard(async () =>
        {
            var s = await svc.FindAsync(id) ?? throw new KeyNotFoundException("Scribe session not found.");
            var segs = await svc.SegmentsAsync(id);
            if (segs.Count == 0) return Bad("Transcribe the recording first.");
            var t = (await svc.TemplatesAsync()).Single(x => x.Id == s.TemplateId);
            var (_, draft, row) = await svc.EnginesAsync();
            List<DraftedSentence> sentences; string engine; var deId = false;
            if (draft is null) { sentences = ScribeText.TemplateOnly(t.Sections, segs); engine = "Template only (no AI)"; }
            else
            {
                var terms = (await svc.DictionaryAsync()).Select(x => x.Term);
                var map = new Dictionary<string, string>();
                var use = segs;
                if (row.Draft == DraftMode.CloudClaude)
                {
                    var id0 = await svc.IdentityAsync(s.PatientId);
                    use = segs.Select(x => { var (txt, m) = ScribeText.DeIdentify(x.Text, id0); foreach (var kv in m) map[kv.Key] = kv.Value; return x with { Text = txt }; }).ToList();
                    deId = true;
                }
                var (answer, eng) = await engines.DraftAsync(row.Draft, draft, ScribeText.Prompt(t.Name, t.Sections, use, terms));
                sentences = ScribeText.ParseDraft(answer, segs.Count).Select(x => x with { Text = ScribeText.ReIdentify(x.Text, map) }).ToList();
                engine = eng;
            }
            await svc.SaveDraftAsync(id, sentences, engine, deId, Me(u));
            return Results.Ok(new { sentences = sentences.Count, engine, deIdentified = deId });
        })).RequireAuthorization(Permissions.ScribeUse);
        g.MapPost("/sessions/{id:guid}/check", async (Guid id, TextRequest r, ScribeService svc) => Results.Ok(await svc.HighlightsAsync(id, r.Text ?? ""))).RequireAuthorization(Permissions.ScribeUse);
        g.MapPost("/sessions/{id:guid}/paste", (Guid id, TextRequest r, ClaimsPrincipal u, ScribeService svc) => Guard(async () =>
        {
            var s = await svc.FindAsync(id) ?? throw new KeyNotFoundException("Scribe session not found.");
            return Done(await svc.ExternalDraftAsync(id, r.Text, s.Provider == ScribeProvider.Medow ? "Medow Health (pasted)" : "Pasted draft", Me(u)));
        })).RequireAuthorization(Permissions.ScribeUse);
        g.MapPost("/sessions/{id:guid}/discard", (Guid id, ReasonRequest r, ClaimsPrincipal u, ScribeService svc) => Guard(async () => Done(await svc.DiscardAsync(id, r.Reason, Me(u))))).RequireAuthorization(Permissions.ScribeUse);
        g.MapPost("/sessions/{id:guid}/sign", (Guid id, TextRequest r, ClaimsPrincipal u, IServiceProvider sp) => Guard(async () =>
        {
            var svc = sp.GetRequiredService<ScribeService>();
            var (s, error) = await svc.SignAsync(id, r.Text, Me(u));
            if (error is not null) return Bad(error);
            var filed = await FileAsync(sp, s!, Me(u));
            return Results.Ok(new { message = filed });
        })).RequireAuthorization(Permissions.ScribeUse);

        // Inbound drafts from an external scribe (Medow Health API, when confirmed): bearer token, returns to the session as an unsigned draft.
        app.MapPost("/api/scribe/inbound", async (InboundDraft r, HttpRequest req, ScribeService svc, LicenceCache licence) =>
        {
            if (licence.Status.AccessTo(ScribeModule.Id) != ModuleAccess.Full) return Results.NotFound();
            var auth = req.Headers.Authorization.ToString();
            if (!auth.StartsWith("Bearer ", StringComparison.Ordinal) || !await svc.InboundTokenOkAsync(auth[7..].Trim())) return Results.Unauthorized();
            try { return Done(await svc.ExternalDraftAsync(r.SessionId, r.Text, r.Product is { Length: > 0 } p ? (p.Length > 60 ? p[..60] : p) : "Medow Health", "external-scribe")); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException ex) { return Bad(ex.Message); }
        }).AllowAnonymous().RequireRateLimiting("mcp").RequireModule(ScribeModule.Id);
    }

    /// <summary>Files a signed scribe note: always as a PDF in the patient's documents; also as a signed clinical-record note when there is a booking and the eMR module is on.</summary>
    private static async Task<string> FileAsync(IServiceProvider sp, ScribeSession s, string user)
    {
        var svc = sp.GetRequiredService<ScribeService>();
        var t = (await svc.TemplatesAsync()).Single(x => x.Id == s.TemplateId);
        var label = $"[AI-assisted – {(s.Provider == ScribeProvider.Medow ? "Medow Health" : "BrightPAS Scribe")}; {t.Name}; draft by {s.DraftEngine}; {s.EditPercent}% edited by the clinician]";
        Guid? noteId = null; Guid? docId = null;
        var notes = new List<string>();
        if (s.BookingId is { } bid && sp.GetRequiredService<LicenceCache>().Status.AccessTo("emr") == ModuleAccess.Full)
        {
            var emr = sp.GetRequiredService<BrightPAS.Modules.Emr.EmrService>();
            var e = await emr.EpisodeAsync(bid, s.PatientId, user);
            var kind = s.Context switch
            {
                ScribeContext.PreAnaesthetic => BrightPAS.Modules.Emr.NoteKind.Anaesthetic,
                ScribeContext.Discharge => BrightPAS.Modules.Emr.NoteKind.Discharge,
                ScribeContext.Consultation or ScribeContext.PostOpReview => BrightPAS.Modules.Emr.NoteKind.Medical,
                _ => BrightPAS.Modules.Emr.NoteKind.Nursing,
            };
            var (note, err) = await emr.NoteAsync(e.Id, new BrightPAS.Modules.Emr.NoteRequest(kind, label + "\n\n" + s.SignedText, true, null), user);
            if (note is not null) { noteId = note.Id; notes.Add("signed note in the clinical record"); } else if (err is not null) notes.Add("clinical record note not made: " + err);
        }
        var pdf = BrightPAS.Web.Pdf.PdfReports.Letter(new BrightPAS.Web.Pdf.LetterheadData("Tasman Day Surgery", null, null, null),
            new BrightPAS.Web.Pdf.LetterData($"SCR-{s.Id.ToString("N")[..8].ToUpperInvariant()}", "", null, t.Name, label + "\n\n" + s.SignedText, s.SignedAt, false, s.SignedBy), null);
        var (doc, errs) = await sp.GetRequiredService<DocumentService>().AddAsync(new NewPatientDocument(s.PatientId, s.BookingId, DocumentCategory.Clinical, $"{t.Name} (AI-assisted) – {s.SignedAt?.ToLocalTime():d MMM yyyy}",
            $"scribe-{s.Id:N}.pdf", Convert.ToBase64String(pdf), DocumentSource.Upload), user, 50);
        if (doc is not null) { docId = doc.Id; notes.Add("PDF in the patient's documents"); } else notes.Add("document not saved: " + string.Join(" ", errs));
        await svc.SetFiledAsync(s.Id, noteId, docId);
        return "Signed – " + string.Join("; ", notes) + (s.AudioDeletedAt is not null ? "; audio deleted." : ".");
    }
}
