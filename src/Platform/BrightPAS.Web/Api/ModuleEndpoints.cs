using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Forms;
using BrightPAS.Modules.Labs;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>API endpoints for the licensed modules: lab results, theatre bookings, pre-admission forms, estimates and invoices.</summary>
public static class ModuleEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Done(string? error) => error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });

    public sealed record AckRequest(string? Comment);
    public sealed record MatchRequest(Guid PatientId, string Reason);
    public sealed record ImportRequest(string Message);
    public sealed record StatusRequest(BookingStatus Status, string? Reason);
    public sealed record ReorderRequest(Guid[] BookingIds);
    public sealed record GenerateRequest(DateOnly From, DateOnly To);
    public sealed record AppointmentUpdate(AppointmentStatus Status, string? Reason, bool IsTentative, string? Notes);
    public sealed record CreateFormRequest(Guid PatientId, Guid? BookingId, bool SendSms = false, bool SendEmail = false);
    public sealed record TriageRequest(TriageCategory Category, string Outcome, string? Notes, bool AddAllergyAlert);
    public sealed record DobRequest(DateOnly DateOfBirth);
    public sealed record DayListPdfRequest(DateOnly Date, BrightPAS.Web.Security.ExportOptions? Export);
    public sealed record AnswersRequest(DateOnly DateOfBirth, Dictionary<string, string> Answers);
    public sealed record DocStatusRequest(DocumentStatus Status, string? Reason);
    public sealed record SettingsRequest(decimal SurgeonPercent, decimal AnaesthetistPercent, decimal GstPercent, int EstimateValidDays, int PaymentTermsDays, string? GstNumber, string? BankAccount);

    public static void MapModuleEndpoints(this IEndpointRouteBuilder app)
    {
        MapLabs(app);
        MapTheatre(app);
        MapForms(app);
        MapBilling(app);
    }

    private static void MapLabs(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/labs").RequireModule(LabsModule.Id);
        g.MapGet("/", async (string? view, string? requestedBy, Guid? patientId, LabService svc) =>
            Results.Ok(await svc.InboxAsync(view ?? "unacknowledged", requestedBy, patientId))).RequireAuthorization(Permissions.LabsView);
        g.MapGet("/unmatched", async (LabService svc) => Results.Ok(await svc.InboxAsync("unmatched", null, null))).RequireAuthorization(Permissions.LabsMatch);
        g.MapGet("/count", async (LabService svc) => Results.Ok(new { unacknowledged = await svc.UnacknowledgedCountAsync() })).RequireAuthorization(Permissions.LabsView);
        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal u, HttpContext http, LabService svc) =>
        {
            var r = await svc.GetAsync(id, Me(u), AuthEndpoints.Workstation(http));
            if (r is null) return Results.NotFound();
            var (row, rep) = r.Value;
            return Results.Ok(new
            {
                row,
                rep.Comment,
                rep.AccessionNumber,
                rep.PlacerOrderNumber,
                rep.AcknowledgementComment,
                rep.PatientDateOfBirth,
                rep.PatientFamilyName,
                rep.PatientGivenNames,
                observations = rep.Observations.Select(o => new { o.Sequence, o.Code, o.Name, o.Value, o.Units, o.ReferenceRange, o.Flag, o.Comment, o.IsAbnormal })
            });
        }).RequireAuthorization(Permissions.LabsView);
        g.MapPost("/{id:guid}/acknowledge", async (Guid id, AckRequest req, ClaimsPrincipal u, LabService svc) =>
            Done(await svc.AcknowledgeAsync(id, Me(u), req.Comment))).RequireAuthorization(Permissions.LabsAcknowledge);
        g.MapPost("/{id:guid}/match", async (Guid id, MatchRequest req, ClaimsPrincipal u, LabService svc) =>
            Done(await svc.MatchToPatientAsync(id, req.PatientId, Me(u), req.Reason))).RequireAuthorization(Permissions.LabsMatch);
        g.MapPost("/import", async (ImportRequest req, ClaimsPrincipal u, LabService svc) =>
        {
            var r = await svc.IngestAsync(req.Message ?? "", "Upload", Me(u));
            return r.Outcome == LabMessageOutcome.Rejected
                ? Results.BadRequest(new { errors = new[] { r.Error }, ack = r.Ack })
                : Results.Ok(new { outcome = r.Outcome.ToString(), reports = r.ReportIds, ack = r.Ack });
        }).RequireAuthorization(Permissions.IntegrationManage);
        g.MapGet("/sample", (string nhi, string family, string given, DateOnly dob, TimeProvider clock) =>
            Results.Ok(new { message = LabService.SampleMessage(nhi, family, given, dob, clock.GetLocalNow()) })).RequireAuthorization(Permissions.IntegrationManage);
    }

    private static void MapTheatre(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/theatre").RequireModule(TheatreModule.Id);
        g.MapGet("/locations", async (TheatreService svc) => Results.Ok(await svc.BookableLocationsAsync())).RequireAuthorization(Permissions.TheatreView);
        g.MapGet("/sessions", async (DateOnly from, DateOnly? to, TheatreService svc) =>
            Results.Ok(await svc.SessionsAsync(from, to ?? from))).RequireAuthorization(Permissions.TheatreView);
        g.MapPost("/sessions", async (NewSession req, ClaimsPrincipal u, TheatreService svc) =>
        {
            var (s, error) = await svc.CreateSessionAsync(req, Me(u));
            return s is null ? Done(error) : Results.Ok(new { s.Id });
        }).RequireAuthorization(Permissions.TheatreManage);
        g.MapPost("/sessions/{id:guid}/cancel", async (Guid id, ClaimsPrincipal u, TheatreService svc) => Done(await svc.CancelSessionAsync(id, Me(u))))
            .RequireAuthorization(Permissions.TheatreManage);
        g.MapPost("/sessions/{id:guid}/order", async (Guid id, ReorderRequest req, ClaimsPrincipal u, TheatreService svc) => Done(await svc.ReorderAsync(id, req.BookingIds, Me(u))))
            .RequireAuthorization(Permissions.TheatreBook);
        // The day's theatre lists as a PDF (protected by the export policy).
        g.MapPost("/lists/pdf", async (DayListPdfRequest r, ClaimsPrincipal u, TheatreService svc, BrightPAS.Web.Security.SecureExportService export) =>
        {
            var sessions = await svc.SessionsAsync(r.Date, r.Date);
            (string, double)[] cols = [("#", 0.8), ("Time", 2.0), ("Patient", 4.5), ("NHI / MRN", 2.8), ("DOB", 2.0), ("Procedure", 6.0), ("Anaesthetic", 2.2), ("Status", 2.2), ("Notes", 3.5)];
            var groups = sessions.Select(s => ($"{s.LocationName} · {s.StartTime:HH\\:mm}–{s.EndTime:HH\\:mm} · {s.Surgeon}{(s.Anaesthetist is null ? "" : " / " + s.Anaesthetist)} · {s.Type}",
                s.Bookings.Where(b => b.Status is not ("Cancelled" or "DidNotAttend")).Select(b => new[] { b.Order.ToString(), $"{b.PlannedStart:HH\\:mm}–{b.PlannedEnd:HH\\:mm}", b.PatientName,
                    $"{b.Nhi ?? "—"} {b.Mrn}", b.DateOfBirth.ToString("dd/MM/yyyy"), b.Procedure + (b.Laterality == "NotApplicable" ? "" : $" ({b.Laterality})"), b.Anaesthetic, b.Status, b.Notes }).AsEnumerable())).ToList();
            return await export.ExportPdfAsync(r.Export, $"BrightPAS-theatre-lists-{r.Date:yyyyMMdd}",
                pw => BrightPAS.Web.Pdf.PdfReports.Table($"Theatre lists {r.Date:dddd d MMMM yyyy}", $"{sessions.Count} list(s)", cols, [], pw, groups: groups), Me(u), "TheatreListPdf");
        }).RequireAuthorization(Permissions.TheatreView);
        g.MapPost("/bookings", async (NewBooking req, ClaimsPrincipal u, HttpContext http, TheatreService svc, IServiceProvider sp, BrightPAS.Core.Audit.AuditWriter audit) =>
        {
            // Credentialing (when licensed): expired credentials or a procedure outside scope are warnings the booker must accept.
            var cred = await PortalEndpoints.BookingCredentialWarningsAsync(sp, req.SessionId, req.Procedure ?? "");
            if (cred.Count > 0 && !req.AcceptWarnings)
            {
                var check = await svc.BookAsync(req, Me(u), validateOnly: true);
                return Results.BadRequest(new { errors = check.Errors, warnings = check.Warnings.Concat(cred) });
            }
            var r = await svc.BookAsync(req, Me(u));
            if (r.Booking is not null && cred.Count > 0)
                await audit.WriteAsync(Me(u), "CredentialWarningAccepted", "TheatreBooking", r.Booking.Id.ToString(), req.PatientId.ToString(), reason: string.Join(" ", cred));
            if (r.Booking is not null) await PortalEndpoints.AutoSendAfterBookingAsync(sp, http, req.PatientId, Me(u));
            return r.Booking is not null ? Results.Ok(new { r.Booking.Id, r.Warnings })
                : Results.BadRequest(new { errors = r.Errors, warnings = r.Warnings });
        }).RequireAuthorization(Permissions.TheatreBook);
        g.MapPost("/bookings/{id:guid}/status", async (Guid id, StatusRequest req, ClaimsPrincipal u, TheatreService svc) =>
            Done(await svc.SetStatusAsync(id, req.Status, req.Reason, Me(u)))).RequireAuthorization(Permissions.TheatreBook);
        g.MapGet("/patients/{patientId:guid}", async (Guid patientId, TheatreService svc) => Results.Ok(await svc.PatientBookingsAsync(patientId)))
            .RequireAuthorization(Permissions.TheatreView);
        g.MapPut("/bookings/{id:guid}/details", async (Guid id, BookingDetails d, ClaimsPrincipal u, TheatreService svc) => Done(await svc.UpdateDetailsAsync(id, d, Me(u))))
            .RequireAuthorization(Permissions.TheatreBook);
        g.MapGet("/checklist-items", () => Results.Ok(TheatreService.ChecklistItems.Select(c => new { key = c.Key, label = c.Label }))).RequireAuthorization(Permissions.TheatreView);
        g.MapGet("/appointments", async (DateOnly? from, DateOnly? to, Guid? patientId, TheatreService svc) =>
            Results.Ok(await svc.AppointmentsAsync(from ?? DateOnly.MinValue, to ?? from ?? DateOnly.MaxValue, patientId))).RequireAuthorization(Permissions.TheatreView);
        g.MapPost("/appointments", async (NewAppointment a, ClaimsPrincipal u, TheatreService svc) =>
        {
            var (row, errors, warnings) = await svc.BookAppointmentAsync(a, Me(u));
            return row is not null ? Results.Ok(new { row.Id, warnings }) : Results.BadRequest(new { errors, warnings });
        }).RequireAuthorization(Permissions.TheatreBook);
        g.MapPost("/appointments/{id:guid}/status", async (Guid id, AppointmentUpdate r, ClaimsPrincipal u, TheatreService svc) =>
            Done(await svc.UpdateAppointmentAsync(id, r.Status, r.Reason, r.IsTentative, r.Notes, Me(u)))).RequireAuthorization(Permissions.TheatreBook);
        // Week/day calendar: theatre lists with their cases, clinic appointments, and each clinician's colour.
        g.MapGet("/calendar", async (DateOnly from, DateOnly to, TheatreService svc, BrightPAS.Core.Organisation.OrganisationService org) =>
        {
            if (to < from || to.DayNumber - from.DayNumber > 42) return Results.BadRequest(new { errors = new[] { "Choose up to 6 weeks." } });
            var colours = (await org.CliniciansAsync(activeOnly: false)).ToDictionary(c => c.Name, c => c.Colour);
            return Results.Ok(new { sessions = await svc.SessionsAsync(from, to), appointments = await svc.AppointmentsAsync(from, to, null), colours });
        }).RequireAuthorization(Permissions.TheatreView);
        g.MapGet("/templates", async (TheatreService svc) => Results.Ok(await svc.TemplatesAsync())).RequireAuthorization(Permissions.TheatreManage);
        g.MapPost("/templates", async (TemplateEdit t, ClaimsPrincipal u, TheatreService svc) =>
        {
            var (row, error) = await svc.SaveTemplateAsync(t, Me(u));
            return row is null ? Done(error) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.TheatreManage);
        g.MapPost("/templates/generate", async (GenerateRequest r, ClaimsPrincipal u, TheatreService svc) => Results.Ok(await svc.GenerateFromTemplatesAsync(r.From, r.To, Me(u))))
            .RequireAuthorization(Permissions.TheatreManage);
    }

    private static void MapForms(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/forms").RequireModule(FormsModule.Id);
        // 0.12.4 (test robot): staff who only triage (e.g. anaesthetists) must be able to list the forms waiting for triage.
        var sendOrTriage = Permissions.AnyOf(Permissions.FormsSend, Permissions.FormsTriage);
        g.MapGet("/", async (string? view, Guid? patientId, FormService svc) => Results.Ok(await svc.ListAsync(view ?? "to-triage", patientId)))
            .RequireAuthorization(sendOrTriage);
        g.MapGet("/definition", () => Results.Ok(new { version = Questionnaire.Version, sections = Questionnaire.Sections })).RequireAuthorization(sendOrTriage);
        g.MapPost("/", async (CreateFormRequest req, ClaimsPrincipal u, HttpContext http, FormService svc, LicenceCache licence, IServiceProvider sp) =>
        {
            var (r, token, error) = await svc.CreateAsync(req.PatientId, req.BookingId, Me(u));
            if (r is null) return Done(error);
            var link = $"{http.Request.Scheme}://{http.Request.Host}/q/{token}";
            object? sent = null;
            if ((req.SendSms || req.SendEmail) && RemindersEndpoints.Licensed(licence))
            {
                var reminders = sp.GetRequiredService<BrightPAS.Modules.Reminders.ReminderService>();
                var msgs = await reminders.QueueFormLinkAsync(req.PatientId, req.BookingId, link, req.SendSms, req.SendEmail, Me(u));
                await reminders.SendDueAsync();
                sent = (await reminders.MessagesAsync(null, req.PatientId)).Where(m => msgs.Any(x => x.Id == m.Id)).Select(m => new { m.Channel, m.Status, m.LastError });
            }
            return Results.Ok(new { r.Id, link, r.ExpiresAt, sent });
        }).RequireAuthorization(Permissions.FormsSend);
        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal u, HttpContext http, FormService svc) =>
        {
            var r = await svc.GetAsync(id, Me(u), AuthEndpoints.Workstation(http));
            if (r is null) return Results.NotFound();
            var (row, req, answers) = r.Value;
            return Results.Ok(new { row, answers, req.TriageOutcome, req.TriageNotes, req.TriagedAt, sections = Questionnaire.Sections });
        }).RequireAuthorization(Permissions.FormsTriage);
        g.MapPost("/{id:guid}/triage", async (Guid id, TriageRequest req, ClaimsPrincipal u, FormService svc) =>
            Done(await svc.TriageAsync(id, req.Category, req.Outcome, req.Notes, req.AddAllergyAlert, Me(u)))).RequireAuthorization(Permissions.FormsTriage);

        // Patient-facing (no staff sign-in): the secret link plus date of birth. Rate-limited per IP; the link locks after 5 wrong attempts.
        var q = app.MapGroup("/api/q/{token}").RequireModule(FormsModule.Id).RequireRateLimiting("patient-links").AllowAnonymous();
        q.MapGet("/", async (string token, FormService svc) => await svc.PublicInfoAsync(token) is { } info ? Results.Ok(info) : Results.NotFound());
        q.MapPost("/open", async (string token, DobRequest req, FormService svc) =>
        {
            var (ok, error, r, name) = await svc.VerifyAsync(token, req.DateOfBirth);
            return ok ? Results.Ok(new { givenName = name, sections = Questionnaire.Sections, answers = FormService.Draft(r!) }) : Results.BadRequest(new { errors = new[] { error } });
        });
        q.MapPost("/save", async (string token, AnswersRequest req, FormService svc) =>
        {
            var (ok, errors) = await svc.SubmitAsync(token, req.DateOfBirth, req.Answers ?? [], final: false);
            return ok ? Results.Ok() : Results.BadRequest(new { errors });
        });
        q.MapPost("/submit", async (string token, AnswersRequest req, FormService svc) =>
        {
            var (ok, errors) = await svc.SubmitAsync(token, req.DateOfBirth, req.Answers ?? [], final: true);
            return ok ? Results.Ok() : Results.BadRequest(new { errors });
        });
    }

    private static object Doc(BillingDocument d)
    {
        var t = BillingService.Calculate(d);
        return new
        {
            d.Id,
            kind = d.Kind.ToString(),
            d.Number,
            d.PatientId,
            d.BookingId,
            d.SourceEstimateId,
            payer = d.Payer.ToString(),
            d.PayerName,
            d.PayerReference,
            d.Surgeon,
            d.Anaesthetist,
            status = d.Status.ToString(),
            d.GstPercent,
            d.CreatedAt,
            d.CreatedBy,
            d.IssuedOn,
            d.DueOn,
            d.Notes,
            d.VoidReason,
            totals = t,
            lines = d.Lines.OrderBy(l => l.Sequence).Select(l => new { l.Sequence, l.Code, l.Description, l.Quantity, l.UnitPrice, l.GstApplicable, l.SurgeonPercent, l.AnaesthetistPercent, net = BillingService.Money(l.Quantity * l.UnitPrice) }),
            payments = d.Payments.OrderBy(p => p.PaidOn).Select(p => new { p.PaidOn, p.Amount, method = p.Method.ToString(), p.Reference, p.RecordedBy })
        };
    }

    private static void MapBilling(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/billing").RequireModule(BillingModule.Id);
        g.MapGet("/settings", async (BillingService svc) => Results.Ok(await svc.SettingsAsync())).RequireAuthorization(Permissions.BillingView);
        g.MapPut("/settings", async (SettingsRequest r, ClaimsPrincipal u, BillingService svc) =>
            Done(await svc.UpdateSettingsAsync(r.SurgeonPercent, r.AnaesthetistPercent, r.GstPercent, r.EstimateValidDays, r.PaymentTermsDays, r.GstNumber, r.BankAccount, Me(u))))
            .RequireAuthorization(Permissions.BillingFees);
        g.MapGet("/fees", async (bool? all, BillingService svc) => Results.Ok(await svc.FeesAsync(all != true))).RequireAuthorization(Permissions.BillingView);
        g.MapPost("/fees", async (FeeItem f, ClaimsPrincipal u, BillingService svc) => Done(await svc.SaveFeeAsync(f, Me(u)))).RequireAuthorization(Permissions.BillingFees);
        g.MapPost("/fees/example", async (BillingService svc) => { await svc.SeedExampleFeesAsync(); return Results.Ok(); }).RequireAuthorization(Permissions.BillingFees);
        g.MapGet("/documents", async (DocumentKind? kind, Guid? patientId, bool? outstanding, BillingService svc, PatientsDbContext patients) =>
        {
            var docs = await svc.ListAsync(kind, patientId, outstanding == true);
            var ids = docs.Select(d => d.PatientId).Distinct().ToList();
            var names = patients.Patients.Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.FamilyName, p.GivenNames, p.Mrn }).ToDictionary(p => p.Id);
            return Results.Ok(docs.Select(d => new
            {
                d.Id,
                kind = d.Kind.ToString(),
                d.Number,
                d.PatientId,
                patientName = names.TryGetValue(d.PatientId, out var n) ? $"{n.FamilyName.ToUpperInvariant()}, {n.GivenNames}" : "",
                mrn = names.TryGetValue(d.PatientId, out var m) ? m.Mrn : "",
                payer = d.Payer.ToString(),
                d.PayerName,
                status = d.Status.ToString(),
                d.CreatedAt,
                d.IssuedOn,
                d.DueOn,
                totals = BillingService.Calculate(d)
            }));
        }).RequireAuthorization(Permissions.BillingView);
        g.MapGet("/documents/{id:guid}", async (Guid id, ClaimsPrincipal u, HttpContext http, BillingService svc) =>
            await svc.GetAsync(id, Me(u), AuthEndpoints.Workstation(http)) is { } d ? Results.Ok(Doc(d)) : Results.NotFound()).RequireAuthorization(Permissions.BillingView);
        g.MapPost("/estimates", async (NewDocument r, ClaimsPrincipal u, BillingService svc) =>
        {
            var (d, errors) = await svc.CreateAsync(DocumentKind.Estimate, r, Me(u));
            return d is null ? Results.BadRequest(new { errors }) : Results.Ok(new { d.Id });
        }).RequireAuthorization(Permissions.BillingManage);
        g.MapPost("/invoices", async (NewDocument r, ClaimsPrincipal u, BillingService svc) =>
        {
            var (d, errors) = await svc.CreateAsync(DocumentKind.Invoice, r, Me(u));
            return d is null ? Results.BadRequest(new { errors }) : Results.Ok(new { d.Id });
        }).RequireAuthorization(Permissions.BillingManage);
        g.MapPost("/estimates/{id:guid}/invoice", async (Guid id, ClaimsPrincipal u, BillingService svc) =>
        {
            var (d, error) = await svc.InvoiceFromEstimateAsync(id, Me(u));
            return d is null ? Done(error) : Results.Ok(new { d.Id });
        }).RequireAuthorization(Permissions.BillingManage);
        g.MapPost("/documents/{id:guid}/status", async (Guid id, DocStatusRequest r, ClaimsPrincipal u, BillingService svc) =>
            Done(await svc.SetStatusAsync(id, r.Status, r.Reason, Me(u)))).RequireAuthorization(Permissions.BillingManage);
        g.MapPost("/invoices/{id:guid}/payments", async (Guid id, NewPayment p, ClaimsPrincipal u, BillingService svc) =>
            Done(await svc.RecordPaymentAsync(id, p, Me(u)))).RequireAuthorization(Permissions.BillingManage);
    }
}
