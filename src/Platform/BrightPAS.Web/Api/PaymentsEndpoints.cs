using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Modules.Payments;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>Online payments and statements (0.30.0, ADR 0043): payment links, online card payments, statement runs, and the patient's own payment link.</summary>
public static class PaymentsEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Err(string? e) => e is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { e } });
    private static IResult Errs(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record LinkRequest(Guid InvoiceId, bool Send);
    public sealed record NoteRequest(string? Note);
    public sealed record RunRequest(DateOnly StatementDate);
    public sealed record DobRequest(DateOnly DateOfBirth);
    public sealed record StartRequest(DateOnly DateOfBirth, decimal Amount);
    public sealed record SimRequest(string? Reference, string? Outcome);
    public sealed record CheckRequest(string? SessionId);
    public sealed record RefundRequest(decimal Amount, string? Reason);

    private static object SettingsView(PaymentSettings s) => new
    {
        provider = s.Provider.ToString(),
        s.PublicUrl,
        s.LinkDays,
        s.MinimumPayment,
        s.WindcaveUrl,
        s.WindcaveUser,
        windcaveKeySet = s.WindcaveKeyProtected is not null,
        s.StatementMinimum,
        s.StatementMessage,
        s.AutoStatementDay,
        s.AutoSendStatements,
        s.OverdueFirstDays,
        s.OverdueSecondDays,
        s.IncludeShortfalls,
        s.PlanReminderDays,
        s.LastAutomationOn,
        s.UpdatedAt,
        s.UpdatedBy,
    };

    public static void MapPaymentsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/payments").RequireModule(PaymentsModule.Id);
        g.MapGet("/settings", async (PaymentLinkService svc) => Results.Ok(SettingsView(await svc.SettingsAsync()))).RequireAuthorization(Permissions.OnlinePayAny);
        g.MapPut("/settings", async (SettingsInput r, ClaimsPrincipal u, PaymentLinkService svc) => Errs(await svc.SaveSettingsAsync(r, Me(u)))).RequireAuthorization(Permissions.OnlinePaySettings);

        g.MapGet("/links", async (string? view, Guid? invoiceId, PaymentLinkService svc) => Results.Ok(await svc.LinksAsync(view, invoiceId))).RequireAuthorization(Permissions.OnlinePayAny);
        g.MapPost("/links", async (LinkRequest r, ClaimsPrincipal u, PaymentLinkService svc) =>
        {
            var (link, sent, error) = await svc.CreateForInvoiceAsync(r.InvoiceId, r.Send, Me(u));
            return link is null ? Err(error) : Results.Ok(new { link, sent });
        }).RequireAuthorization(Permissions.OnlinePayManage);
        g.MapPost("/links/{id:guid}/cancel", async (Guid id, ClaimsPrincipal u, PaymentLinkService svc) => Err(await svc.CancelLinkAsync(id, Me(u)))).RequireAuthorization(Permissions.OnlinePayManage);

        g.MapGet("/online", async (string? view, Guid? invoiceId, PaymentLinkService svc) => Results.Ok(await svc.PaymentsAsync(view, invoiceId))).RequireAuthorization(Permissions.OnlinePayAny);
        g.MapGet("/count", async (PaymentLinkService svc) => Results.Ok(new { attention = await svc.AttentionCountAsync() })).RequireAuthorization(Permissions.OnlinePayAny);
        g.MapPost("/online/{id:guid}/check", async (Guid id, PaymentLinkService svc) =>
        {
            var (status, error) = await svc.QueryAsync(id);
            return status is null ? Err(error) : Results.Ok(new { status, message = error });
        }).RequireAuthorization(Permissions.OnlinePayManage);
        g.MapPost("/online/{id:guid}/resolve", async (Guid id, NoteRequest r, ClaimsPrincipal u, PaymentLinkService svc) => Err(await svc.ResolveAsync(id, r.Note, Me(u))))
            .RequireAuthorization(Permissions.OnlinePayManage);

        g.MapGet("/statements/runs", async (StatementService svc) => Results.Ok(await svc.RunsAsync())).RequireAuthorization(Permissions.OnlinePayAny);
        g.MapPost("/statements/runs", async (RunRequest r, ClaimsPrincipal u, StatementService svc) =>
        {
            var (run, error) = await svc.PrepareAsync(r.StatementDate, Me(u));
            return run is null ? Err(error) : Results.Ok(new { run.Id, run.Count, run.Total });
        }).RequireAuthorization(Permissions.OnlinePayManage);
        g.MapGet("/statements/runs/{id:guid}", async (Guid id, StatementService svc) => Results.Ok(await svc.StatementsAsync(id))).RequireAuthorization(Permissions.OnlinePayAny);
        g.MapDelete("/statements/runs/{id:guid}", async (Guid id, ClaimsPrincipal u, StatementService svc) => Err(await svc.DeleteRunAsync(id, Me(u)))).RequireAuthorization(Permissions.OnlinePayManage);
        g.MapPost("/statements/runs/{id:guid}/send", async (Guid id, ClaimsPrincipal u, StatementService svc) =>
        {
            var (sent, post, error) = await svc.SendAsync(id, Me(u));
            return error is not null ? Err(error) : Results.Ok(new { sent, toPost = post });
        }).RequireAuthorization(Permissions.OnlinePayManage);
        g.MapPost("/statements/runs/{id:guid}/print", async (Guid id, bool? reprint, ClaimsPrincipal u, StatementService svc) =>
        {
            var (pdf, error) = await svc.PrintBatchAsync(id, reprint == true, Me(u));
            return pdf is null ? Err(error) : Results.File(pdf, "application/pdf", "statements-to-post.pdf");
        }).RequireAuthorization(Permissions.OnlinePayManage);
        g.MapGet("/statements/patient/{id:guid}", async (Guid id, StatementService svc) => Results.Ok(await svc.ForPatientAsync(id))).RequireAuthorization(Permissions.OnlinePayAny);
        g.MapGet("/statements/{id:guid}/pdf", async (Guid id, ClaimsPrincipal u, StatementService svc) =>
        {
            var (pdf, number) = await svc.PdfAsync(id, Me(u));
            return pdf is null ? Results.NotFound() : Results.File(pdf, "application/pdf", $"statement-{number}.pdf");
        }).RequireAuthorization(Permissions.OnlinePayAny);

        // ---- the patient's own link (no staff sign-in): secret link + date of birth; rate-limited per IP; locks after 5 wrong dates.
        // 0.32.0 (ADR 0045): refunds, payment plans, the daily accounts round and overdue reminders
        g.MapPost("/online/{id:guid}/refund", async (Guid id, RefundRequest r, ClaimsPrincipal u, RefundService svc) =>
        {
            var (refund, error) = await svc.RefundAsync(id, r.Amount, r.Reason, Me(u));
            return error is null ? Results.Ok(new { refund!.Number, refund.ProviderReference, refund.AppliedTo }) : Results.BadRequest(new { errors = new[] { error } });
        }).RequireAuthorization(Permissions.OnlinePayRefund);
        g.MapGet("/refunds", async (Guid? paymentId, RefundService svc) => Results.Ok(await svc.ListAsync(paymentId))).RequireAuthorization(Permissions.OnlinePayAny);
        g.MapGet("/plans", async (string? view, Guid? patientId, PlanService svc) => Results.Ok(await svc.ListAsync(view, patientId))).RequireAuthorization(Permissions.OnlinePayAny);
        g.MapGet("/plans/{id:guid}", async (Guid id, PlanService svc) => await svc.GetAsync(id) is { } d ? Results.Ok(d) : Results.NotFound()).RequireAuthorization(Permissions.OnlinePayAny);
        g.MapGet("/plans/options/{patientId:guid}", async (Guid patientId, PlanService svc) => Results.Ok(await svc.OptionsAsync(patientId))).RequireAuthorization(Permissions.OnlinePayManage);
        g.MapPost("/plans", async (PlanInput r, ClaimsPrincipal u, PlanService svc) =>
        {
            var (plan, errors) = await svc.CreateAsync(r, Me(u));
            return plan is null ? Results.BadRequest(new { errors }) : Results.Ok(new { plan.Id, plan.Number });
        }).RequireAuthorization(Permissions.OnlinePayManage);
        g.MapPost("/plans/{id:guid}/cancel", async (Guid id, NoteRequest r, ClaimsPrincipal u, PlanService svc) => Err(await svc.CancelAsync(id, r.Note, Me(u))))
            .RequireAuthorization(Permissions.OnlinePayManage);
        g.MapGet("/overdue", async (AutomationService svc) => Results.Ok(await svc.OverdueAsync())).RequireAuthorization(Permissions.OnlinePayAny);
        g.MapPost("/automation/run", async (ClaimsPrincipal u, AutomationService svc) => Results.Ok(await svc.RunAsync(true, Me(u)))).RequireAuthorization(Permissions.OnlinePaySettings);

        var p = app.MapGroup("/api/pay/{token}").RequireModule(PaymentsModule.Id).RequireRateLimiting("patient-links").AllowAnonymous();
        p.MapGet("/", async (string token, PaymentLinkService svc) => await svc.PublicStatusAsync(token) is var st && st != "closed" ? Results.Ok(new { status = st }) : Results.NotFound());
        p.MapPost("/open", async (string token, DobRequest r, PaymentLinkService svc) =>
        {
            var (view, error) = await svc.ViewAsync(token, r.DateOfBirth);
            return view is null ? Err(error) : Results.Ok(view);
        });
        p.MapPost("/start", async (string token, StartRequest r, PaymentLinkService svc) =>
        {
            var (url, error) = await svc.StartAsync(token, r.DateOfBirth, r.Amount);
            return url is null ? Err(error) : Results.Ok(new { redirectUrl = url });
        });
        p.MapPost("/check", async (string token, CheckRequest r, PaymentLinkService svc) =>
        {
            var (status, error) = await svc.CheckAsync(token, r.SessionId ?? "");
            return status is null ? Err(error) : Results.Ok(new { status });
        });
        p.MapPost("/simulator", async (string token, SimRequest r, PaymentLinkService svc) =>
        {
            var (status, error) = await svc.SimulateAsync(token, r.Reference ?? "", r.Outcome ?? "");
            return status is null ? Err(error) : Results.Ok(new { status });
        });
        p.MapPost("/statement", async (string token, DobRequest r, StatementService svc) =>
        {
            var (pdf, number, error) = await svc.PublicPdfAsync(token, r.DateOfBirth);
            return pdf is null ? Err(error) : Results.File(pdf, "application/pdf", $"statement-{number}.pdf");
        });

        // ---- Windcave's server-to-server notification (FPRN). BrightPAS only uses it as a nudge: it asks Windcave for the result itself.
        var n = app.MapGroup("/api/pay-notify").RequireModule(PaymentsModule.Id).RequireRateLimiting("patient-links").AllowAnonymous();
        n.MapGet("/", async (string? sessionId, PaymentLinkService svc) => (await svc.CheckAsync(null, sessionId ?? "")).Status is null ? Results.NotFound() : Results.Ok());
        n.MapPost("/", async (string? sessionId, PaymentLinkService svc) => (await svc.CheckAsync(null, sessionId ?? "")).Status is null ? Results.NotFound() : Results.Ok());
    }
}
