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
    public sealed record CreateFormRequest(Guid PatientId, Guid? BookingId, bool SendSms = false, bool SendEmail = false);
    public sealed record TriageRequest(TriageCategory Category, string Outcome, string? Notes, bool AddAllergyAlert);
    public sealed record DobRequest(DateOnly DateOfBirth);
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
                row, rep.Comment, rep.AccessionNumber, rep.PlacerOrderNumber, rep.AcknowledgementComment, rep.PatientDateOfBirth, rep.PatientFamilyName, rep.PatientGivenNames,
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
        g.MapPost("/bookings", async (NewBooking req, ClaimsPrincipal u, TheatreService svc) =>
        {
            var r = await svc.BookAsync(req, Me(u));
            return r.Booking is not null ? Results.Ok(new { r.Booking.Id, r.Warnings })
                : Results.BadRequest(new { errors = r.Errors, warnings = r.Warnings });
        }).RequireAuthorization(Permissions.TheatreBook);
        g.MapPost("/bookings/{id:guid}/status", async (Guid id, StatusRequest req, ClaimsPrincipal u, TheatreService svc) =>
            Done(await svc.SetStatusAsync(id, req.Status, req.Reason, Me(u)))).RequireAuthorization(Permissions.TheatreBook);
        g.MapGet("/patients/{patientId:guid}", async (Guid patientId, TheatreService svc) => Results.Ok(await svc.PatientBookingsAsync(patientId)))
            .RequireAuthorization(Permissions.TheatreView);
    }

    private static void MapForms(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/forms").RequireModule(FormsModule.Id);
        g.MapGet("/", async (string? view, Guid? patientId, FormService svc) => Results.Ok(await svc.ListAsync(view ?? "to-triage", patientId)))
            .RequireAuthorization(Permissions.FormsSend);
        g.MapGet("/definition", () => Results.Ok(new { version = Questionnaire.Version, sections = Questionnaire.Sections })).RequireAuthorization(Permissions.FormsSend);
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
            d.Id, kind = d.Kind.ToString(), d.Number, d.PatientId, d.BookingId, d.SourceEstimateId, payer = d.Payer.ToString(), d.PayerName, d.PayerReference,
            d.Surgeon, d.Anaesthetist, status = d.Status.ToString(), d.GstPercent, d.CreatedAt, d.CreatedBy, d.IssuedOn, d.DueOn, d.Notes, d.VoidReason, totals = t,
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
                d.Id, kind = d.Kind.ToString(), d.Number, d.PatientId, patientName = names.TryGetValue(d.PatientId, out var n) ? $"{n.FamilyName.ToUpperInvariant()}, {n.GivenNames}" : "",
                mrn = names.TryGetValue(d.PatientId, out var m) ? m.Mrn : "", payer = d.Payer.ToString(), d.PayerName, status = d.Status.ToString(), d.CreatedAt, d.IssuedOn, d.DueOn,
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
