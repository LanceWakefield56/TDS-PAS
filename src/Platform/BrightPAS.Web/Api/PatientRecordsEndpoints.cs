using BrightPAS.Core;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Setup;
using BrightPAS.Identity;
using BrightPAS.Modules.Patients;
using BrightPAS.Web.Security;
using BrightPAS.Web.Integration;

namespace BrightPAS.Web.Api;

/// <summary>
/// Base-system patient features (0.10.0): documents &amp; scanning, identity corrections and merges (second-person check),
/// wristbands and labels (Zebra), emergency transfer pack; plus administrator security settings and staff offboarding.
/// </summary>
public static class PatientRecordsEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Done(string? error) => error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });
    private static IResult Errors(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record DeleteRequest(string Reason);
    public sealed record PrintRequest(PrinterKind Kind, int Copies, Guid? PrinterId);
    public sealed record TransferExport(TransferEdit Transfer, ExportOptions? Export);
    public sealed record OffboardRequest(bool DeactivateAccount, bool EndRoster, bool DeactivateClinician, bool RemovePortalLinks, DateOnly? LastDay, string? Reason);

    public static void MapPatientRecordsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/patients");

        // ---------------------------------------------------------------- documents & scanning
        g.MapGet("/{id:guid}/documents", async (Guid id, Guid? bookingId, DocumentService svc) => Results.Ok(await svc.ListAsync(id, bookingId)))
            .RequireAuthorization(Permissions.PatientView);
        g.MapPost("/{id:guid}/documents", async (Guid id, NewPatientDocument d, ClaimsPrincipal u, DocumentService svc, SettingsService settings) =>
        {
            if (d.PatientId != id) return Results.BadRequest(new { errors = new[] { "Patient mismatch." } });
            if (d.Source == DocumentSource.Portal) return Results.BadRequest(new { errors = new[] { "Choose upload or scan." } });
            var (row, errors) = await svc.AddAsync(d, Me(u), (await settings.SecurityAsync()).MaxUploadMegabytes);
            return row is null ? Errors(errors) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.PatientEdit);
        g.MapGet("/documents/{docId:guid}", async (Guid docId, bool? download, ClaimsPrincipal u, HttpContext http, DocumentService svc) =>
        {
            var r = await svc.OpenAsync(docId, Me(u), AuthEndpoints.Workstation(http));
            if (r is not { } doc) return Results.NotFound();
            // Opened in the browser only in a sandbox (no scripts, no links back into BrightPAS).
            http.Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; object-src 'self'";
            http.Response.Headers["Cache-Control"] = "no-store";
            return Results.File(doc.Bytes, doc.Meta.ContentType, download == true ? doc.Meta.FileName : null);
        }).RequireAuthorization(Permissions.PatientView);
        g.MapGet("/{id:guid}/documents/search", async (Guid id, string? q, ClaimsPrincipal u, DocumentService svc) => Results.Ok(await svc.SearchAsync(id, q ?? "", Me(u))))
            .RequireAuthorization(Permissions.PatientView);
        g.MapPost("/documents/{docId:guid}/delete", async (Guid docId, DeleteRequest r, ClaimsPrincipal u, DocumentService svc) => Done(await svc.DeleteAsync(docId, r.Reason, Me(u))))
            .RequireAuthorization(Permissions.PatientEdit);

        // ---------------------------------------------------------------- identity corrections & merges (second person approves)
        g.MapPost("/{id:guid}/identity-corrections", async (Guid id, CorrectionRequest r, ClaimsPrincipal u, IdentityService svc) =>
        {
            if (r.PatientId != id) return Results.BadRequest(new { errors = new[] { "Patient mismatch." } });
            var (row, errors) = await svc.RequestCorrectionAsync(r, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.PatientEdit);
        g.MapGet("/{id:guid}/identity-corrections", async (Guid id, IdentityService svc) => Results.Ok(await svc.CorrectionsAsync(false, id))).RequireAuthorization(Permissions.PatientView);
        g.MapGet("/identity-corrections", async (bool? all, IdentityService svc, PatientsDbContext db) =>
        {
            var rows = await svc.CorrectionsAsync(all != true);
            var ids = rows.Select(r => r.PatientId).Distinct().ToList();
            var mrn = await db.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Mrn);
            return Results.Ok(rows.Select(c => new
            {
                c.Id,
                c.PatientId,
                mrn = mrn.GetValueOrDefault(c.PatientId),
                c.Before,
                c.NewFamilyName,
                c.NewGivenNames,
                c.NewDateOfBirth,
                newSex = c.NewSex?.ToString(),
                c.NewNhi,
                c.ClearNhi,
                c.Reason,
                c.Evidence,
                status = c.Status.ToString(),
                c.RequestedAt,
                c.RequestedBy,
                c.DecidedAt,
                c.DecidedBy,
                c.DecisionNote
            }));
        }).RequireAuthorization(Permissions.PatientMerge);
        g.MapPost("/identity-corrections/{cid:guid}/decide", async (Guid cid, Decision d, ClaimsPrincipal u, IdentityService svc) => Done(await svc.DecideCorrectionAsync(cid, d, Me(u))))
            .RequireAuthorization(Permissions.PatientMerge);
        g.MapPost("/merges", async (MergeRequest r, ClaimsPrincipal u, IdentityService svc) =>
        {
            var (row, errors) = await svc.RequestMergeAsync(r, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.PatientEdit);
        g.MapGet("/merges", async (bool? all, IdentityService svc, PatientsDbContext db) =>
        {
            var rows = await svc.MergesAsync(all != true);
            var ids = rows.Select(r => r.SurvivorId).Distinct().ToList();
            var people = await db.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            return Results.Ok(rows.Select(m => new
            {
                m.Id,
                m.SurvivorId,
                survivor = people.TryGetValue(m.SurvivorId, out var p) ? $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}; DOB {p.DateOfBirth:yyyy-MM-dd}; NHI {p.Nhi ?? "none"}; MRN {p.Mrn}" : "?",
                m.DuplicateId,
                duplicate = m.DuplicateSummary,
                m.Reason,
                status = m.Status.ToString(),
                m.RequestedAt,
                m.RequestedBy,
                m.DecidedBy,
                m.CompletedAt,
                m.Result
            }));
        }).RequireAuthorization(Permissions.PatientMerge);
        g.MapPost("/merges/{mid:guid}/decide", async (Guid mid, Decision d, ClaimsPrincipal u, IdentityService svc) =>
        {
            var (error, result) = await svc.DecideMergeAsync(mid, d, Me(u));
            return error is null ? Results.Ok(new { result }) : Results.BadRequest(new { errors = new[] { error } });
        }).RequireAuthorization(Permissions.PatientMerge);

        // ---------------------------------------------------------------- wristbands & labels
        g.MapPost("/{id:guid}/print", async (Guid id, PrintRequest r, ClaimsPrincipal u, LabelService svc) => Done(await svc.PrintAsync(id, r.Kind, r.Copies, r.PrinterId, Me(u))))
            .RequireAuthorization(Permissions.PatientView);
        g.MapPost("/{id:guid}/print/zpl", async (Guid id, PrintRequest r, ClaimsPrincipal u, LabelService svc, AuditWriter audit) =>
        {
            var (error, zpl) = await svc.BuildAsync(id, r.Kind, r.Copies, r.PrinterId);
            if (error is not null) return Results.BadRequest(new { errors = new[] { error } });
            await audit.WriteAsync(Me(u), "Download", r.Kind == PrinterKind.Wristband ? "Wristband" : "PatientLabel", id.ToString(), id.ToString(), reason: "ZPL file for a USB/local Zebra printer");
            return Results.File(Encoding.UTF8.GetBytes(zpl), "application/octet-stream", $"{r.Kind.ToString().ToLowerInvariant()}.zpl");
        }).RequireAuthorization(Permissions.PatientView);
        g.MapGet("/printers", async (LabelService svc) => Results.Ok((await svc.PrintersAsync(true)).Select(p => new { p.Id, p.Name, kind = p.Kind.ToString(), language = p.Language.ToString(), p.MediaCode, p.IsDefault })))
            .RequireAuthorization(Permissions.PatientView);
        g.MapPost("/{id:guid}/print/dymo", async (Guid id, PrintRequest r, ClaimsPrincipal u, LabelService svc) =>
        {
            var (error, sheet) = await svc.DymoSheetAsync(id, r.Copies, r.PrinterId, Me(u));
            return error is null ? Results.Ok(sheet) : Results.BadRequest(new { errors = new[] { error } });
        }).RequireAuthorization(Permissions.PatientView);

        // ---------------------------------------------------------------- emergency transfer pack
        g.MapGet("/{id:guid}/transfer-pack", async (Guid id, ClaimsPrincipal u, HttpContext http, IServiceProvider sp) =>
            await TransferPackAsync(id, sp, Me(u), AuthEndpoints.Workstation(http)) is { } pack ? Results.Ok(pack) : Results.NotFound())
            .RequireAuthorization(Permissions.PatientView);
        g.MapPost("/{id:guid}/transfers", async (Guid id, TransferEdit t, ClaimsPrincipal u, PatientsDbContext db, AuditWriter audit, TimeProvider clock) =>
        {
            var (row, errors) = await SaveTransferAsync(id, t, Me(u), db, audit, clock);
            return row is null ? Errors(errors) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.PatientEdit);
        g.MapPost("/{id:guid}/transfer-pack/export", async (Guid id, TransferExport r, ClaimsPrincipal u, HttpContext http, IServiceProvider sp, SecureExportService export) =>
        {
            var pack = await TransferPackAsync(id, sp, Me(u), AuthEndpoints.Workstation(http));
            if (pack is null) return Results.NotFound();
            if (r.Export?.WantsPdf == true)
                return await export.ExportPdfAsync(r.Export, "BrightPAS-transfer-pack", pw => BrightPAS.Web.Pdf.PdfReports.TransferPack(pack, r.Transfer, pw), Me(u), "TransferPack", id.ToString());
            var html = TransferHtml(pack, r.Transfer);
            return await export.ExportAsync(r.Export, "BrightPAS-transfer-pack", [new("transfer-pack.html", Encoding.UTF8.GetBytes(html))], Me(u), "TransferPack", id.ToString());
        }).RequireAuthorization(Permissions.PatientView);

        // ---------------------------------------------------------------- administration: printers, security settings, offboarding
        var admin = app.MapGroup("/api/admin");
        admin.MapGet("/printers", async (LabelService svc) => Results.Ok(new { printers = await svc.PrintersAsync(false), dymoMedia = LabelService.DymoMediaList }))
            .RequireAuthorization(Permissions.OrganisationManage);
        admin.MapPost("/printers", async (PrinterEdit e, ClaimsPrincipal u, LabelService svc) => Errors(await svc.SavePrinterAsync(e, Me(u)))).RequireAuthorization(Permissions.OrganisationManage);
        // Document text recognition (OCR) status and retry.
        admin.MapGet("/document-text", async (DocumentService svc, IOcrEngine ocr) => Results.Ok(new { summary = await svc.TextSummaryAsync(), engineProblem = ocr.Problem }))
            .RequireAuthorization(Permissions.UsersManage);
        admin.MapPost("/document-text/retry", async (ClaimsPrincipal u, DocumentService svc, AuditWriter audit, OutOfProcessOcrEngine ocr) =>
        {
            ocr.Resume();
            var n = await svc.RetryFailedTextAsync();
            await audit.WriteAsync(Me(u), "Retry", "DocumentText", "failed", reason: $"{n} document(s) queued again");
            return Results.Ok(new { queued = n });
        }).RequireAuthorization(Permissions.UsersManage);
        admin.MapGet("/security-settings", async (SettingsService s) => Results.Ok(await s.SecurityAsync())).RequireAuthorization(Permissions.UsersManage);
        admin.MapPut("/security-settings", async (SecuritySettings e, ClaimsPrincipal u, SettingsService s) => Errors(await s.UpdateSecurityAsync(e, Me(u))))
            .RequireAuthorization(Permissions.UsersManage);
        // Export policy is readable by any signed-in user so the export dialog knows whether a password is required.
        app.MapGet("/api/export-policy", async (SettingsService s) =>
        {
            var p = await s.SecurityAsync();
            return Results.Ok(new { required = p.ExportProtection == ExportProtection.Required, minLength = p.MinExportPasswordLength, maxUploadMegabytes = p.MaxUploadMegabytes });
        }).RequireAuthorization();
        admin.MapGet("/users/{id:guid}/offboard", async (Guid id, OffboardingService svc) => await svc.PreviewAsync(id) is { } p ? Results.Ok(p) : Results.NotFound())
            .RequireAuthorization(Permissions.UsersManage);
        admin.MapPost("/users/{id:guid}/offboard", async (Guid id, OffboardRequest r, ClaimsPrincipal u, OffboardingService svc) =>
        {
            var (errors, done) = await svc.OffboardAsync(id, r, Me(u));
            return errors.Count > 0 ? Results.BadRequest(new { errors }) : Results.Ok(new { done });
        }).RequireAuthorization(Permissions.UsersManage);
    }

    private static async Task<(TransferRecord?, IReadOnlyList<string>)> SaveTransferAsync(Guid id, TransferEdit t, string user, PatientsDbContext db, AuditWriter audit, TimeProvider clock)
    {
        var errors = new List<string>();
        if (t.PatientId != id || !await db.Patients.AnyAsync(p => p.Id == id && p.MergedIntoId == null)) errors.Add("Patient not found.");
        if (string.IsNullOrWhiteSpace(t.ReceivingFacility)) errors.Add("Enter the receiving hospital (e.g. Nelson Hospital ED).");
        if (string.IsNullOrWhiteSpace(t.Reason)) errors.Add("Enter the reason for transfer.");
        if (errors.Count > 0) return (null, errors);
        static string? C(string? s, int n) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= n ? s.Trim() : s.Trim()[..n];
        var row = new TransferRecord
        {
            PatientId = id,
            BookingId = t.BookingId,
            ReceivingFacility = C(t.ReceivingFacility, 150)!,
            Reason = C(t.Reason, 500)!,
            Situation = C(t.Situation, 1000),
            Background = C(t.Background, 2000),
            Assessment = C(t.Assessment, 2000),
            Recommendation = C(t.Recommendation, 1000),
            Escort = C(t.Escort, 150),
            TransportMode = C(t.TransportMode, 50),
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user,
        };
        db.Transfers.Add(row);
        await db.SaveChangesAsync();
        await audit.WriteAsync(user, "EmergencyTransfer", "Patient", id.ToString(), id.ToString(), reason: $"To {row.ReceivingFacility}: {row.Reason}");
        return (row, errors);
    }

    /// <summary>Everything the receiving hospital needs in one place, from every module that holds it. Audited as a view.</summary>
    public static async Task<Dictionary<string, object?>?> TransferPackAsync(Guid id, IServiceProvider sp, string user, string? workstation)
    {
        var patients = sp.GetRequiredService<PatientsDbContext>();
        var p = await patients.Patients.AsNoTracking().Include(x => x.Alerts).Include(x => x.Ethnicities).SingleOrDefaultAsync(x => x.Id == id);
        if (p is null) return null;
        var pack = new Dictionary<string, object?>();
        var site = await sp.GetRequiredService<BrightPAS.Core.Organisation.OrganisationService>().MainSiteAsync();
        pack["from"] = site is { } s ? new { organisation = s.Org.Name, s.Site.AddressLine1, s.Site.City, s.Site.Phone } : null;
        pack["patient"] = new
        {
            p.Id,
            p.Mrn,
            p.Nhi,
            p.FamilyName,
            p.GivenNames,
            p.PreferredName,
            p.DateOfBirth,
            sex = p.Sex.ToString(),
            p.Gender,
            p.MobilePhone,
            p.HomePhone,
            address = string.Join(", ", new[] { p.AddressLine1, p.AddressLine2, p.Suburb, p.City, p.Postcode }.Where(x => !string.IsNullOrWhiteSpace(x))),
        };
        pack["alerts"] = p.Alerts.Where(a => a.IsActive).OrderByDescending(a => a.Type == AlertType.Allergy).ThenByDescending(a => a.Severity)
            .Select(a => new { type = a.Type.ToString(), severity = a.Severity.ToString(), a.Description }).ToList();
        var letters = sp.GetRequiredService<BrightPAS.Modules.Letters.LettersDbContext>();
        pack["gp"] = p.GpPracticeHpiFacilityId is { } hpi
            ? await letters.Practices.AsNoTracking().Where(x => x.HpiFacilityId == hpi).Select(x => new { x.Name, x.Phone, x.HealthLinkEdi }).FirstOrDefaultAsync() ?? (object)new { Name = hpi, Phone = (string?)null, HealthLinkEdi = (string?)null }
            : null;
        var theatre = sp.GetRequiredService<BrightPAS.Modules.Theatre.TheatreDbContext>();
        var bookings = await theatre.Bookings.AsNoTracking().Where(b => b.PatientId == id).Join(theatre.Sessions, b => b.SessionId, s => s.Id, (b, s) => new { b, s }).ToListAsync();
        var org = sp.GetRequiredService<BrightPAS.Core.Organisation.OrganisationService>();
        var people = await org.ClinicianNamesAsync();
        var rooms = await org.LocationNamesAsync();
        pack["bookings"] = bookings.OrderByDescending(x => x.s.Date).Take(3).Select(x => new
        {
            x.b.Id,
            x.s.Date,
            location = rooms.GetValueOrDefault(x.s.LocationId, ""),
            surgeon = people.GetValueOrDefault(x.s.SurgeonId, ""),
            anaesthetist = x.s.AnaesthetistId is { } an ? people.GetValueOrDefault(an) : null,
            x.b.Procedure,
            laterality = x.b.Laterality.ToString(),
            anaesthetic = x.b.Anaesthetic.ToString(),
            status = x.b.Status.ToString(),
            x.b.TheatreIn,
            x.b.TheatreOut,
            x.b.Notes,
            x.b.LoanItems,
            x.b.ScopeId,
        }).ToList();
        var forms = sp.GetRequiredService<BrightPAS.Modules.Forms.FormsDbContext>();
        var form = (await forms.Requests.AsNoTracking().Where(f => f.PatientId == id && f.SubmittedAt != null).ToListAsync()).OrderByDescending(f => f.SubmittedAt).FirstOrDefault();
        pack["preAdmission"] = form is null ? null : new
        {
            form.SubmittedAt,
            triage = form.TriageCategory?.ToString(),
            form.TriageOutcome,
            form.TriageNotes,
            flags = BrightPAS.Modules.Forms.FormService.FlagsOf(form),
        };
        var labs = sp.GetRequiredService<BrightPAS.Modules.Labs.LabsDbContext>();
        pack["labs"] = (await labs.Reports.AsNoTracking().Where(r => r.PatientId == id && r.SupersededById == null).ToListAsync())
            .OrderByDescending(r => r.ReportedAt ?? r.ReceivedAt).Take(10).Select(r => new { r.TestName, r.Laboratory, r.ReportedAt, r.IsAbnormal, status = r.ResultStatus.ToString(), r.Comment }).ToList();
        pack["documents"] = await sp.GetRequiredService<DocumentService>().ListAsync(id);
        pack["transfers"] = (await patients.Transfers.AsNoTracking().Where(t => t.PatientId == id).ToListAsync()).OrderByDescending(t => t.CreatedAt).Take(5).ToList();
        await sp.GetRequiredService<AuditWriter>().WriteAsync(user, "View", "TransferPack", id.ToString(), id.ToString(), workstation: workstation);
        return pack;
    }

    private static string E(object? o) => WebUtility.HtmlEncode(o?.ToString() ?? "");

    /// <summary>Self-contained printable HTML (no scripts, no external resources) for the exported pack.</summary>
    public static string TransferHtml(Dictionary<string, object?> pack, TransferEdit? t)
    {
        var json = JsonSerializer.SerializeToElement(pack, JsonSerializerOptions.Web);
        string V(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
        var p = json.GetProperty("patient");
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Emergency transfer pack</title><style>body{font:14px/1.4 Segoe UI,Arial,sans-serif;margin:24px;color:#111}h1{font-size:20px}h2{font-size:15px;border-bottom:1px solid #999;margin-top:18px}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ccc;padding:4px 6px;text-align:left;vertical-align:top}.alert{color:#a00;font-weight:bold}</style></head><body>");
        sb.Append($"<h1>Emergency transfer pack – {E(V(json.GetProperty("from"), "organisation"))}</h1>");
        sb.Append($"<p><strong>{E(V(p, "familyName").ToUpperInvariant())}, {E(V(p, "givenNames"))}</strong> · DOB {E(V(p, "dateOfBirth"))} · {E(V(p, "sex"))} · NHI {E(V(p, "nhi"))} · MRN {E(V(p, "mrn"))}<br>{E(V(p, "address"))} · {E(V(p, "mobilePhone"))}</p>");
        if (t is not null)
        {
            sb.Append($"<h2>Transfer</h2><p>To: <strong>{E(t.ReceivingFacility)}</strong> · Transport: {E(t.TransportMode)} · Escort: {E(t.Escort)}<br>Reason: {E(t.Reason)}</p>");
            sb.Append($"<table><tr><th>Situation</th><td>{E(t.Situation)}</td></tr><tr><th>Background</th><td>{E(t.Background)}</td></tr><tr><th>Assessment</th><td>{E(t.Assessment)}</td></tr><tr><th>Recommendation</th><td>{E(t.Recommendation)}</td></tr></table>");
        }
        sb.Append("<h2>Alerts and allergies</h2>");
        var alerts = json.GetProperty("alerts").EnumerateArray().ToList();
        sb.Append(alerts.Count == 0 ? "<p>No alerts recorded (confirm with the patient).</p>" : "<ul>" + string.Concat(alerts.Select(a => $"<li class=\"alert\">{E(V(a, "type"))} ({E(V(a, "severity"))}): {E(V(a, "description"))}</li>")) + "</ul>");
        sb.Append("<h2>Procedure</h2><table><tr><th>Date</th><th>Procedure</th><th>Surgeon</th><th>Anaesthetist</th><th>Anaesthetic</th><th>In/out</th><th>Notes</th></tr>");
        foreach (var b in json.GetProperty("bookings").EnumerateArray())
            sb.Append($"<tr><td>{E(V(b, "date"))}</td><td>{E(V(b, "procedure"))} {E(V(b, "laterality") == "NotApplicable" ? "" : V(b, "laterality"))}</td><td>{E(V(b, "surgeon"))}</td><td>{E(V(b, "anaesthetist"))}</td><td>{E(V(b, "anaesthetic"))}</td><td>{E(V(b, "theatreIn"))}–{E(V(b, "theatreOut"))}</td><td>{E(V(b, "notes"))}</td></tr>");
        sb.Append("</table>");
        var pre = json.GetProperty("preAdmission");
        if (pre.ValueKind == JsonValueKind.Object)
        {
            sb.Append($"<h2>Pre-admission questionnaire</h2><p>Triage: {E(V(pre, "triage"))} {E(V(pre, "triageOutcome"))}</p><ul>");
            foreach (var f in pre.GetProperty("flags").EnumerateArray()) sb.Append($"<li>{E(V(f, "level"))}: {E(V(f, "text"))}</li>");
            sb.Append("</ul>");
        }
        sb.Append("<h2>Recent lab results</h2><table><tr><th>Test</th><th>Reported</th><th>Abnormal</th><th>Comment</th></tr>");
        foreach (var l in json.GetProperty("labs").EnumerateArray())
            sb.Append($"<tr><td>{E(V(l, "testName"))}</td><td>{E(V(l, "reportedAt"))}</td><td>{(V(l, "isAbnormal") == "True" ? "<span class=\"alert\">Yes</span>" : "")}</td><td>{E(V(l, "comment"))}</td></tr>");
        sb.Append("</table>");
        var gp = json.GetProperty("gp");
        sb.Append($"<h2>GP</h2><p>{E(V(gp, "name"))} {E(V(gp, "phone"))}</p>");
        sb.Append($"<p style=\"margin-top:24px;font-size:12px\">Generated by BrightPAS {E(DateTimeOffset.UtcNow.ToNz().ToString("d MMM yyyy HH:mm"))}. Contains health information – handle under the Health Information Privacy Code.</p></body></html>");
        return sb.ToString();
    }
}
