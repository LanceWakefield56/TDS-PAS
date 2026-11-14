using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Setup;
using BrightPAS.Identity;
using BrightPAS.Licensing;
using BrightPAS.Modules.Endoscopy;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;
using BrightPAS.Web.Licensing;
using static BrightPAS.Modules.Endoscopy.Terminology;

namespace BrightPAS.Web.Api;

/// <summary>Endoscopy (0.15.0, ADR 0027): procedures, structured findings, pictures and video, reports, quality indicators, tower settings.</summary>
public static class EndoscopyEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Done(string? error) => error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });
    private static IResult Errors(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });
    private static bool Has(ClaimsPrincipal u, string p) => u.HasClaim(Permissions.ClaimType, p);

    public sealed record AmendRequest(string Reason);
    public sealed record VideoStart(string ContentType);
    public sealed record VideoFinish(decimal? DurationSeconds);
    public sealed record AssignRequest(Guid ProcedureId);
    public sealed record AdviceEdit(string Key, string Title, string Body);
    public sealed record ReportRequest(string Kind, BrightPAS.Web.Security.ExportOptions? Export);
    public sealed record SaveRequest(string Kind);
    public sealed record NbspExtract(DateOnly From, DateOnly To, bool OnlyComplete, BrightPAS.Web.Security.ExportOptions? Export);
    public sealed record EndoSettings(string? MediaPath, bool DicomEnabled, int DicomPort, string DicomAeTitle, string? DicomCallingAes, bool DicomAnyTower, int? VideoRetentionDays);

    public const string DicomEnabledKey = "endo.dicom.enabled", DicomPortKey = "endo.dicom.port", DicomAeKey = "endo.dicom.aeTitle",
        DicomCallingKey = "endo.dicom.callingAes", DicomAnyKey = "endo.dicom.anyTower", RetentionKey = "endo.videoRetentionDays";

    private static async Task<IResult> Guard(Func<Task<IResult>> f)
    {
        try { return await f(); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { errors = new[] { ex.Message } }); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { errors = new[] { ex.Message } }); }
    }

    private static async Task<byte[]> Body(HttpRequest req, long max)
    {
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await req.Body.ReadAsync(buf)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > max) throw new InvalidOperationException($"The file is larger than {max / 1024 / 1024} MB.");
        }
        return ms.ToArray();
    }

    public static async Task<EndoSettings> SettingsAsync(SettingsService s, MediaStore store)
    {
        return new EndoSettings(await s.GetAsync(MediaStore.PathKey) is { Length: > 0 } p ? p : null, await s.GetAsync(DicomEnabledKey) == "true",
            int.TryParse(await s.GetAsync(DicomPortKey), out var port) ? port : 11112, await s.GetAsync(DicomAeKey) is { Length: > 0 } ae ? ae : "BRIGHTPAS_ES",
            await s.GetAsync(DicomCallingKey), await s.GetAsync(DicomAnyKey) == "true", int.TryParse(await s.GetAsync(RetentionKey), out var r) ? r : null);
    }

    public static void MapEndoscopyEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/endoscopy").RequireModule(EndoscopyModule.Id);

        g.MapGet("/terminology", () => Results.Ok(new
        {
            upperSites = UpperSites,
            lowerSites = LowerSites,
            upperIndications = UpperIndications,
            lowerIndications = LowerIndications,
            findings = FindingKinds.Select(f => new { f.Code, f.Label, f.Upper, f.Lower, f.GradeLabel, f.Grades }),
            paris = Terminology.Paris,
            nice = Terminology.Nice,
            jnet = Terminology.Jnet,
            techniques = Techniques,
            resections = Resections,
            retrievals = Retrievals,
            completeness = Completeness,
            histologies = Histologies,
            dysplasias = Dysplasias,
            tattooPositions = TattooPositions,
            interventions = InterventionKinds,
            biopsyPurposes = BiopsyPurposes,
            complications = Complications,
        })).RequireAuthorization(Permissions.EndoView);

        // Today's endoscopy list (endoscopy/mixed sessions and any booking that reads as an endoscopy).
        g.MapGet("/today", async (DateOnly? date, TheatreService theatre, EndoDbContext db, TimeProvider clock) =>
        {
            var d = date ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var sessions = (await theatre.SessionsAsync(d, d)).Where(s => !s.IsCancelled).ToList();
            var bookings = sessions.SelectMany(s => s.Bookings.Where(b => b.Status is not ("Cancelled" or "DidNotAttend") && (s.Type != "Surgical" || Guess(b.Procedure) is not null)).Select(b => (s, b))).ToList();
            var ids = bookings.Select(x => x.b.Id).ToList();
            var procs = await db.Procedures.AsNoTracking().Where(p => ids.Contains(p.BookingId)).ToDictionaryAsync(p => p.BookingId);
            var pids = procs.Values.Select(p => p.Id).ToList();
            var counts = await db.Media.AsNoTracking().Where(m => m.ProcedureId != null && pids.Contains(m.ProcedureId.Value) && m.Status != MediaStatus.Deleted)
                .GroupBy(m => new { m.ProcedureId, m.Kind }).Select(x => new { x.Key.ProcedureId, x.Key.Kind, N = x.Count() }).ToListAsync();
            return Results.Ok(bookings.Select(x =>
            {
                var p = procs.GetValueOrDefault(x.b.Id);
                return new
                {
                    bookingId = x.b.Id,
                    x.b.PatientId,
                    x.b.PatientName,
                    x.b.Nhi,
                    x.b.Mrn,
                    x.b.Procedure,
                    x.s.LocationName,
                    start = x.b.PlannedStart,
                    endoscopist = x.s.Surgeon,
                    bookingStatus = x.b.Status,
                    procedureId = p?.Id,
                    kind = (p?.Kind ?? Guess(x.b.Procedure))?.ToString(),
                    status = p?.Status.ToString() ?? "NotStarted",
                    accession = p?.AccessionNumber,
                    pictures = p is null ? 0 : counts.Where(c => c.ProcedureId == p.Id && c.Kind == MediaKind.Image).Sum(c => c.N),
                    videos = p is null ? 0 : counts.Where(c => c.ProcedureId == p.Id && c.Kind == MediaKind.Video).Sum(c => c.N),
                };
            }).OrderBy(x => x.start));
        }).RequireAuthorization(Permissions.EndoView);

        g.MapGet("/bookings/{bookingId:guid}", async (Guid bookingId, ClaimsPrincipal u, IServiceProvider sp) =>
        {
            var theatre = sp.GetRequiredService<TheatreDbContext>();
            var booking = await theatre.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId);
            if (booking is null) return Results.NotFound();
            var session = await theatre.Sessions.AsNoTracking().SingleAsync(s => s.Id == booking.SessionId);
            var svc = sp.GetRequiredService<EndoService>();
            var p = await svc.ForBookingAsync(bookingId);
            if (p is null && Has(u, Permissions.EndoWrite))
            {
                var surgeon = await sp.GetRequiredService<BrightPAS.Core.Data.PasDbContext>().Clinicians.AsNoTracking().Where(c => c.Id == session.SurgeonId).Select(c => c.Name).SingleOrDefaultAsync();
                p = await svc.EnsureAsync(bookingId, booking.PatientId, booking.Procedure, session.SurgeonId, surgeon, session.Date, Me(u));
            }
            await sp.GetRequiredService<BrightPAS.Core.Audit.AuditWriter>().WriteAsync(Me(u), "View", "EndoscopyProcedure", p?.Id.ToString() ?? bookingId.ToString(), booking.PatientId.ToString());
            return Results.Ok(await EndoRecord.BuildAsync(sp, booking, session, p));
        }).RequireAuthorization(Permissions.EndoView);

        g.MapPut("/procedures/{id:guid}/details", (Guid id, DetailsEdit r, ClaimsPrincipal u, IServiceProvider sp) => Guard(async () =>
        {
            var errors = await sp.GetRequiredService<EndoService>().UpdateDetailsAsync(id, r, Me(u));
            if (errors.Count > 0) return Errors(errors);
            var warnings = await EndoRecord.RecordScopeUseAsync(sp, id, Me(u));
            return Results.Ok(new { warnings });
        })).RequireAuthorization(Permissions.EndoWrite);

        g.MapPut("/procedures/{id:guid}/procedure", (Guid id, ProcedureEdit r, ClaimsPrincipal u, EndoService svc) => Guard(async () =>
        {
            var (errors, warnings) = await svc.UpdateProcedureAsync(id, r, Me(u));
            return errors.Count > 0 ? Errors(errors) : Results.Ok(new { warnings });
        })).RequireAuthorization(Permissions.EndoWrite);

        g.MapPut("/procedures/{id:guid}/report", (Guid id, ReportEdit r, ClaimsPrincipal u, EndoService svc) => Guard(async () => Errors(await svc.UpdateReportAsync(id, r, Me(u))))).RequireAuthorization(Permissions.EndoWrite);
        g.MapPost("/procedures/{id:guid}/sign", (Guid id, ClaimsPrincipal u, EndoService svc) => Guard(async () => Errors(await svc.SignAsync(id, Me(u))))).RequireAuthorization(Permissions.EndoSign);
        g.MapPost("/procedures/{id:guid}/amend", (Guid id, AmendRequest r, ClaimsPrincipal u, EndoService svc) => Guard(async () => Done(await svc.AmendAsync(id, r.Reason, Me(u))))).RequireAuthorization(Permissions.EndoSign);

        // Findings, polyps, tattoos and therapy: POST adds, PUT /{itemId} changes, DELETE removes.
        g.MapPost("/procedures/{id:guid}/findings", (Guid id, FindingEdit r, ClaimsPrincipal u, EndoService svc) => Guard(async () => Saved(await svc.SaveFindingAsync(id, null, r, Me(u))))).RequireAuthorization(Permissions.EndoWrite);
        g.MapPut("/procedures/{id:guid}/findings/{itemId:guid}", (Guid id, Guid itemId, FindingEdit r, ClaimsPrincipal u, EndoService svc) => Guard(async () => Saved(await svc.SaveFindingAsync(id, itemId, r, Me(u))))).RequireAuthorization(Permissions.EndoWrite);
        g.MapPost("/procedures/{id:guid}/polyps", (Guid id, PolypEdit r, ClaimsPrincipal u, EndoService svc) => Guard(async () => Saved(await svc.SavePolypAsync(id, null, r, Me(u))))).RequireAuthorization(Permissions.EndoWrite);
        g.MapPut("/procedures/{id:guid}/polyps/{itemId:guid}", (Guid id, Guid itemId, PolypEdit r, ClaimsPrincipal u, EndoService svc) => Guard(async () => Saved(await svc.SavePolypAsync(id, itemId, r, Me(u))))).RequireAuthorization(Permissions.EndoWrite);
        g.MapPost("/procedures/{id:guid}/tattoos", (Guid id, TattooEdit r, ClaimsPrincipal u, EndoService svc) => Guard(async () => Saved(await svc.SaveTattooAsync(id, null, r, Me(u))))).RequireAuthorization(Permissions.EndoWrite);
        g.MapPut("/procedures/{id:guid}/tattoos/{itemId:guid}", (Guid id, Guid itemId, TattooEdit r, ClaimsPrincipal u, EndoService svc) => Guard(async () => Saved(await svc.SaveTattooAsync(id, itemId, r, Me(u))))).RequireAuthorization(Permissions.EndoWrite);
        g.MapPost("/procedures/{id:guid}/therapy", (Guid id, InterventionEdit r, ClaimsPrincipal u, EndoService svc) => Guard(async () => Saved(await svc.SaveInterventionAsync(id, null, r, Me(u))))).RequireAuthorization(Permissions.EndoWrite);
        g.MapPut("/procedures/{id:guid}/therapy/{itemId:guid}", (Guid id, Guid itemId, InterventionEdit r, ClaimsPrincipal u, EndoService svc) => Guard(async () => Saved(await svc.SaveInterventionAsync(id, itemId, r, Me(u))))).RequireAuthorization(Permissions.EndoWrite);
        g.MapDelete("/items/{what}/{itemId:guid}", (string what, Guid itemId, ClaimsPrincipal u, EndoService svc) => Guard(async () => Done(await svc.RemoveAsync(what, itemId, Me(u))))).RequireAuthorization(Permissions.EndoWrite);
        g.MapPut("/polyps/{itemId:guid}/histology", async (Guid itemId, HistologyEdit r, ClaimsPrincipal u, EndoService svc) => Done(await svc.HistologyAsync(itemId, r, Me(u)))).RequireAuthorization(Permissions.EndoWrite);

        // Pictures and video. Pictures/uploads arrive as the raw file body; recordings arrive in numbered chunks.
        g.MapPost("/procedures/{id:guid}/pictures", (Guid id, HttpRequest req, ClaimsPrincipal u, EndoService svc) => Guard(async () =>
        {
            var data = await Body(req, 500L * 1024 * 1024);
            var type = Imaging.Sniff(data);
            var (m, error) = type is "video/mp4" or "video/webm"
                ? await svc.AddVideoFileAsync(id, data, MediaSource.Upload, Me(u))
                : await svc.AddImageAsync(id, data, req.Headers["X-Capture"] == "station" ? MediaSource.CaptureStation : MediaSource.Upload, Me(u));
            return error is not null ? Done(error) : Results.Ok(new { m!.Id });
        })).RequireAuthorization(Permissions.EndoWrite);
        g.MapPost("/procedures/{id:guid}/videos", (Guid id, VideoStart r, ClaimsPrincipal u, EndoService svc) => Guard(async () =>
        {
            var (m, error) = await svc.StartVideoAsync(id, r.ContentType, Me(u));
            return error is not null ? Done(error) : Results.Ok(new { m!.Id });
        })).RequireAuthorization(Permissions.EndoWrite);
        g.MapPost("/videos/{mediaId:guid}/chunks/{seq:int}", async (Guid mediaId, int seq, HttpRequest req, EndoService svc) =>
        {
            var data = await Body(req, 64L * 1024 * 1024);
            using var ms = new MemoryStream(data);
            return Done(await svc.AppendVideoAsync(mediaId, seq, ms));
        }).RequireAuthorization(Permissions.EndoWrite);
        g.MapPost("/videos/{mediaId:guid}/finish", async (Guid mediaId, VideoFinish r, ClaimsPrincipal u, EndoService svc) => Done(await svc.FinishVideoAsync(mediaId, r.DurationSeconds, Me(u)))).RequireAuthorization(Permissions.EndoWrite);

        g.MapGet("/media/{mediaId:guid}", async (Guid mediaId, EndoService svc, MediaStore store) =>
        {
            var m = await svc.MediaRowAsync(mediaId);
            if (m is null || await store.PathIfExistsAsync(m) is not { } path) return Results.NotFound();
            return Results.File(path, m.ContentType, enableRangeProcessing: true);
        }).RequireAuthorization(Permissions.EndoView);
        g.MapPut("/media/{mediaId:guid}", async (Guid mediaId, MediaEdit r, ClaimsPrincipal u, EndoService svc) => Done(await svc.UpdateMediaAsync(mediaId, r, Me(u)))).RequireAuthorization(Permissions.EndoWrite);
        g.MapDelete("/media/{mediaId:guid}", async (Guid mediaId, string reason, ClaimsPrincipal u, EndoService svc, EndoDbContext db) =>
        {
            var m = await svc.MediaRowAsync(mediaId);
            if (m is null) return Results.NotFound();
            var signed = m.ProcedureId is { } pid && await db.Procedures.AnyAsync(p => p.Id == pid && p.Status == ProcedureStatus.Signed);
            if ((signed || m.ProcedureId is null) && !Has(u, Permissions.EndoManage)) return Results.BadRequest(new { errors = new[] { "Only an endoscopy administrator can delete pictures from a signed report or the unmatched list." } });
            return Done(await svc.DeleteMediaAsync(mediaId, reason, Me(u)));
        }).RequireAuthorization(Permissions.EndoWrite);
        g.MapPost("/media/{mediaId:guid}/annotate", (Guid mediaId, HttpRequest req, ClaimsPrincipal u, EndoService svc) => Guard(async () =>
        {
            var (m, error) = await svc.AnnotateAsync(mediaId, await Body(req, 30L * 1024 * 1024), Me(u));
            return error is not null ? Done(error) : Results.Ok(new { m!.Id });
        })).RequireAuthorization(Permissions.EndoWrite);
        g.MapGet("/media/{mediaId:guid}/original", async (Guid mediaId, EndoService svc, MediaStore store) =>
        {
            var m = await svc.MediaRowAsync(mediaId);
            if (m is null || await store.OriginalPathAsync(m) is not { } path) return Results.NotFound();
            return Results.File(path, m.OriginalContentType ?? "application/octet-stream", $"endoscopy-{m.Id:N}{Path.GetExtension(path)}");
        }).RequireAuthorization(Permissions.EndoView);
        g.MapGet("/unmatched", async (EndoService svc) => Results.Ok((await svc.UnmatchedAsync()).Select(m => new { m.Id, m.CapturedAt, m.DicomPatientId, m.DicomPatientName, m.DicomAccession, m.CapturedBy, kind = m.Kind.ToString() })))
            .RequireAuthorization(Permissions.EndoWrite);
        g.MapPost("/media/{mediaId:guid}/assign", (Guid mediaId, AssignRequest r, ClaimsPrincipal u, EndoService svc) => Guard(async () => Done(await svc.AssignAsync(mediaId, r.ProcedureId, Me(u))))).RequireAuthorization(Permissions.EndoWrite);

        // Reports: clinical (endoscopist/GP) and patient (plain language), both with the chosen pictures.
        g.MapPost("/procedures/{id:guid}/report-pdf", async (Guid id, ReportRequest r, ClaimsPrincipal u, IServiceProvider sp, BrightPAS.Web.Security.SecureExportService export) =>
        {
            var data = await EndoRecord.ReportDataAsync(sp, id);
            if (data is null) return Results.NotFound();
            var patient = r.Kind == "patient";
            return await export.ExportPdfAsync((r.Export ?? new(true, null)) with { Format = "pdf" }, $"BrightPAS-{(patient ? "patient-" : "")}endoscopy-{data.Accession}",
                pw => patient ? BrightPAS.Web.Pdf.EndoPdf.PatientReport(data, pw) : BrightPAS.Web.Pdf.EndoPdf.ClinicalReport(data, pw), Me(u), patient ? "EndoscopyPatientReport" : "EndoscopyReport", data.PatientId.ToString());
        }).RequireAuthorization(Permissions.EndoView);
        g.MapPost("/procedures/{id:guid}/save-to-record", async (Guid id, SaveRequest r, ClaimsPrincipal u, IServiceProvider sp) =>
        {
            var data = await EndoRecord.ReportDataAsync(sp, id);
            if (data is null) return Results.NotFound();
            if (!data.Signed) return Results.BadRequest(new { errors = new[] { "Sign the report before saving it to the patient record." } });
            var patient = r.Kind == "patient";
            var pdf = patient ? BrightPAS.Web.Pdf.EndoPdf.PatientReport(data, null) : BrightPAS.Web.Pdf.EndoPdf.ClinicalReport(data, null);
            var title = $"{(patient ? "Patient report" : "Endoscopy report")} – {data.KindLabel} {data.Date:d MMM yyyy}";
            var (doc, errors) = await sp.GetRequiredService<DocumentService>().AddAsync(new NewPatientDocument(data.PatientId, data.BookingId, DocumentCategory.Clinical, title,
                $"{(patient ? "patient-" : "")}endoscopy-{data.Accession}.pdf", Convert.ToBase64String(pdf), DocumentSource.Upload), Me(u), 50);
            return errors.Count > 0 ? Errors(errors) : Results.Ok(new { doc!.Id });
        }).RequireAuthorization(Permissions.EndoSign);

        // Signed clinical report to the patient's GP by HealthLink (a final letter with the PDF attached; Letters module).
        g.MapPost("/procedures/{id:guid}/send-to-gp", async (Guid id, ClaimsPrincipal u, IServiceProvider sp) =>
        {
            if (sp.GetRequiredService<LicenceCache>().Status.AccessTo("letters") != ModuleAccess.Full) return Results.BadRequest(new { errors = new[] { "Sending to the GP needs the Letters module." } });
            var data = await EndoRecord.ReportDataAsync(sp, id);
            if (data is null) return Results.NotFound();
            if (!data.Signed) return Results.BadRequest(new { errors = new[] { "Sign the report before sending it to the GP." } });
            if (data.P.GpLetterId is not null) return Results.BadRequest(new { errors = new[] { "This report has already been sent to the GP (see Letters). Amend and re-sign to send a corrected report." } });
            var p = data.P;
            var body = $"Dear Doctor\n\nRe: {data.PatientName}, NHI {data.Nhi ?? "-"}, born {data.DateOfBirth:d MMM yyyy}\n\n{data.FirstName} had a {data.KindLabel.ToLowerInvariant()} at {data.Organisation} on {data.Date:d MMMM yyyy} with {p.EndoscopistName}. The full report with pictures is attached.\n\n"
                + $"Conclusion: {p.Diagnosis}\n" + (p.Recommendations is null ? "" : $"\nRecommendations: {p.Recommendations}\n") + (p.FollowUp is null ? "" : $"\nFollow-up: {p.FollowUp}\n")
                + (p.SurveillanceMonths is { } sm ? $"\nNext colonoscopy recommended in {(sm % 12 == 0 ? $"{sm / 12} year(s)" : $"{sm} months")}.\n" : "")
                + (data.Polyps.Any(x => x.Retrieved == "Yes" && x.Histology is null) || data.Therapy.Any(x => x.Kind == "Biopsy") ? "\nHistology is awaited and will follow.\n" : "")
                + $"\nKind regards\n{p.EndoscopistName}";
            var letters = sp.GetRequiredService<BrightPAS.Modules.Letters.LetterService>();
            var (letter, errors) = await letters.CreateGpReportAsync(data.PatientId, data.BookingId, $"{data.KindLabel} report – {data.PatientName}", body,
                BrightPAS.Web.Pdf.EndoPdf.ClinicalReport(data, null), $"endoscopy-{data.Accession}.pdf", Me(u), p.EndoscopistName);
            if (errors.Count > 0) return Errors(errors);
            await sp.GetRequiredService<EndoService>().SetGpLetterAsync(id, letter!.Id);
            var (ok, message) = await sp.GetRequiredService<BrightPAS.Modules.Letters.HealthLinkService>().SendAsync(letter.Id, Me(u));
            return Results.Ok(new { letterId = letter.Id, letter.Number, sent = ok, message = ok ? $"Letter {letter.Number} to {letter.RecipientName}: {message}" : $"Letter {letter.Number} created but not sent: {message} You can print or resend it from Letters." });
        }).RequireAuthorization(Permissions.EndoSign);

        // National Bowel Screening Programme: dataset per screening colonoscopy, completeness check, encrypted CSV extract.
        g.MapGet("/nbsp", async (DateOnly? from, DateOnly? to, EndoService svc, TimeProvider clock) =>
        {
            var t = to ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            return Results.Ok(await svc.NbspAsync(from ?? t.AddMonths(-3), t));
        }).RequireAuthorization(Permissions.EndoView);
        g.MapPost("/nbsp/extract", async (NbspExtract r, ClaimsPrincipal u, IServiceProvider sp, BrightPAS.Web.Security.SecureExportService export) =>
        {
            var svc = sp.GetRequiredService<EndoService>();
            var rows = (await svc.NbspAsync(r.From, r.To)).Where(x => !r.OnlyComplete || x.Problems.Count == 0).ToList();
            if (rows.Count == 0) return Results.BadRequest(new { errors = new[] { r.OnlyComplete ? "No complete NBSP procedures in that range (see the problems column)." : "No NBSP procedures in that range." } });
            var pids = rows.Select(x => x.PatientId).Distinct().ToList();
            var pts = await sp.GetRequiredService<PatientsDbContext>().Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            var cids = rows.Select(x => x.EndoscopistId).OfType<Guid>().Distinct().ToList();
            var cpn = await sp.GetRequiredService<BrightPAS.Core.Data.PasDbContext>().Clinicians.AsNoTracking().Where(c => cids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.HpiCpn);
            var site = await sp.GetRequiredService<BrightPAS.Core.Organisation.OrganisationService>().MainSiteAsync();
            static string C(object? v) { var s = v switch { null => "", bool b => b ? "Y" : "N", decimal d => d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), DateOnly d => d.ToString("yyyy-MM-dd"), _ => v.ToString() ?? "" }; return s.IndexOfAny([',', '"', '\n']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s; }
            var sb = new System.Text.StringBuilder("NHI,NBSPParticipantId,FacilityHpi,ProcedureDate,Accession,EndoscopistName,EndoscopistCPN,Indication,Sedation,Extent,CaecalIntubation,TerminalIleum,BBPSTotal,AdequatePrep,WithdrawalMinutes,ComfortGloucester,PolypsSeen,PolypsRemoved,PolypsRetrieved,LargestPolypMm,Adenomas,AdvancedAdenomas,SerratedLesions,CancerSuspected,CancerConfirmed,HistologyAwaited,Tattoos,Complication,SurveillanceMonths,ReportStatus\r\n");
            foreach (var x in rows)
            {
                var pt = pts[x.PatientId];
                sb.Append(string.Join(",", new object?[] { pt.Nhi, x.NbspNumber, site?.Site.HpiFacilityId, x.Date, x.Accession, x.Endoscopist, x.EndoscopistId is { } eid ? cpn.GetValueOrDefault(eid) : null, x.Indications, x.Sedation,
                    x.Extent, x.CaecumReached, x.TerminalIleum, x.Bbps, x.AdequatePrep, x.WithdrawalMinutes, x.Comfort, x.PolypsSeen, x.PolypsRemoved, x.PolypsRetrieved, x.LargestPolypMm, x.Adenomas, x.AdvancedAdenomas,
                    x.Serrated, x.CancerSuspected, x.CancerConfirmed, x.HistologyAwaited, x.Tattoos, x.Complication, x.SurveillanceMonths, x.Status }.Select(C))).Append("\r\n");
            }
            await svc.MarkNbspExportedAsync(rows.Select(x => x.ProcedureId), Me(u));
            return await export.ExportAsync(r.Export, $"BrightPAS-NBSP-{r.From:yyyyMMdd}-{r.To:yyyyMMdd}", [new($"nbsp-endoscopy-{r.From:yyyyMMdd}-{r.To:yyyyMMdd}.csv", BrightPAS.Web.Security.SecureExportService.Utf8Bom(sb.ToString()))], Me(u), "NbspExtract");
        }).RequireAuthorization(Permissions.EndoSign);

        // Quality indicators and settings.
        g.MapGet("/kpis", async (DateOnly? from, DateOnly? to, EndoService svc, TimeProvider clock) =>
        {
            var t = to ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var f = from ?? t.AddMonths(-12);
            return Results.Ok(new { from = f, to = t, targets = await svc.TargetsAsync(), rows = await svc.KpisAsync(f, t) });
        }).RequireAuthorization(Permissions.EndoView);
        g.MapPut("/targets", async (KpiTargets r, ClaimsPrincipal u, EndoService svc) => { await svc.SaveTargetsAsync(r, Me(u)); return Results.Ok(); }).RequireAuthorization(Permissions.EndoManage);
        g.MapGet("/advice", async (EndoService svc) => Results.Ok(await svc.AdviceAsync())).RequireAuthorization(Permissions.EndoView);
        g.MapPut("/advice", async (AdviceEdit r, ClaimsPrincipal u, EndoService svc) => Done(await svc.SaveAdviceAsync(r.Key, r.Title, r.Body, Me(u)))).RequireAuthorization(Permissions.EndoManage);
        g.MapGet("/settings", async (SettingsService s, MediaStore store, BrightPAS.Web.Integration.EndoscopyDicomHost host) =>
        {
            var (ok, detail) = await store.CheckAsync();
            return Results.Ok(new { settings = await SettingsAsync(s, store), defaultMediaPath = store.DefaultRootPath, media = new { ok, detail }, dicom = host.Status });
        }).RequireAuthorization(Permissions.EndoManage);
        g.MapPut("/settings", async (EndoSettings r, ClaimsPrincipal u, SettingsService s, BrightPAS.Web.Integration.EndoscopyDicomHost host) =>
        {
            var errors = new List<string>();
            if (r.DicomPort is < 104 or > 65535) errors.Add("DICOM port is 104–65535 (11112 is usual).");
            if (string.IsNullOrWhiteSpace(r.DicomAeTitle) || r.DicomAeTitle.Length > 16 || r.DicomAeTitle.Any(c => c < 32 || c > 126 || c == '\\')) errors.Add("AE title is 1–16 plain characters.");
            if (r.VideoRetentionDays is < 30) errors.Add("Keep videos for at least 30 days (or leave blank to keep them).");
            if (r.MediaPath is { Length: > 0 } mp && !Path.IsPathFullyQualified(mp)) errors.Add("The media folder must be a full path, e.g. D:\\BrightPAS\\Endoscopy.");
            if (errors.Count > 0) return Errors(errors);
            await s.SetAsync(MediaStore.PathKey, r.MediaPath?.Trim() ?? "", Me(u));
            await s.SetAsync(DicomEnabledKey, r.DicomEnabled ? "true" : "false", Me(u));
            await s.SetAsync(DicomPortKey, r.DicomPort.ToString(System.Globalization.CultureInfo.InvariantCulture), Me(u));
            await s.SetAsync(DicomAeKey, r.DicomAeTitle.Trim().ToUpperInvariant(), Me(u));
            await s.SetAsync(DicomCallingKey, r.DicomCallingAes?.Trim() ?? "", Me(u));
            await s.SetAsync(DicomAnyKey, r.DicomAnyTower ? "true" : "false", Me(u));
            await s.SetAsync(RetentionKey, r.VideoRetentionDays?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", Me(u));
            host.Reload();
            return Results.Ok();
        }).RequireAuthorization(Permissions.EndoManage);
    }

    private static IResult Saved<T>((T? Row, IReadOnlyList<string> Errors) r) where T : class => r.Errors.Count > 0 ? Results.BadRequest(new { errors = r.Errors }) : Results.Ok(r.Row);
}
