using System.Security.Claims;
using System.Text.Json;
using BrightPAS.Identity;
using BrightPAS.Licensing;
using BrightPAS.Modules.Reminders;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>SMS & email reminders: outbox, replies, templates, settings, patient preferences, and SMS vendor callbacks.</summary>
public static class RemindersEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Done(string? error) => error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });

    public sealed record TemplateRequest(MessageKind Kind, Channel Channel, string Language, string? Subject, string Body, bool IsActive);
    public sealed record PreferenceRequest(bool AllowSms, bool AllowEmail, string Language);
    public sealed record OptOutRequest(Channel Channel, string Address, bool OptedOut);
    public sealed record HandledRequest(string? Note);
    public sealed record TestRequest(Channel Channel, string To);
    public sealed record SimulateReplyRequest(string From, string Body);

    /// <summary>True when the reminders module is fully licensed (used by other modules before queueing messages).</summary>
    public static bool Licensed(LicenceCache licence) => licence.Status.AccessTo(RemindersModule.Id) == ModuleAccess.Full;

    public static void MapRemindersEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/reminders").RequireModule(RemindersModule.Id);
        g.MapGet("/status", async (ReminderService svc) => Results.Ok(new { problems = await svc.ProblemsAsync(), counts = await svc.CountsAsync() }))
            .RequireAuthorization(Permissions.RemindersView);
        g.MapGet("/messages", async (string? status, Guid? patientId, ReminderService svc) => Results.Ok(await svc.MessagesAsync(status, patientId)))
            .RequireAuthorization(Permissions.RemindersView);
        g.MapPost("/messages/{id:guid}/retry", async (Guid id, ClaimsPrincipal u, ReminderService svc) => Done(await svc.RetryAsync(id, Me(u))))
            .RequireAuthorization(Permissions.RemindersView);
        g.MapPost("/messages/{id:guid}/cancel", async (Guid id, ClaimsPrincipal u, ReminderService svc) => Done(await svc.CancelAsync(id, Me(u))))
            .RequireAuthorization(Permissions.RemindersView);
        g.MapGet("/replies", async (string? view, ReminderService svc) => Results.Ok(await svc.RepliesAsync(view ?? "new")))
            .RequireAuthorization(Permissions.RemindersView);
        g.MapPost("/replies/{id:guid}/handled", async (Guid id, HandledRequest r, ClaimsPrincipal u, ReminderService svc) => Done(await svc.MarkReplyHandledAsync(id, r.Note, Me(u))))
            .RequireAuthorization(Permissions.RemindersView);
        g.MapGet("/patients/{id:guid}", async (Guid id, ReminderService svc) =>
            await svc.PreferenceAsync(id) is { } p ? Results.Ok(new { preference = p, messages = await svc.MessagesAsync(null, id) }) : Results.NotFound())
            .RequireAuthorization(Permissions.RemindersView);
        g.MapPut("/patients/{id:guid}", async (Guid id, PreferenceRequest r, ClaimsPrincipal u, ReminderService svc) =>
            Done(await svc.SetPreferenceAsync(id, r.AllowSms, r.AllowEmail, r.Language, Me(u)))).RequireAuthorization(Permissions.RemindersView);
        g.MapPost("/opt-outs", async (OptOutRequest r, ClaimsPrincipal u, ReminderService svc) =>
        {
            var address = r.Channel == Channel.Sms ? Addresses.ToE164(r.Address) : Addresses.NormaliseEmail(r.Address);
            if (address is null) return Done("That is not a valid mobile number or email address.");
            await svc.SetOptOutAsync(r.Channel, address, r.OptedOut, Me(u));
            return Results.Ok();
        }).RequireAuthorization(Permissions.RemindersView);

        // Administration: templates, vendors and test messages.
        g.MapGet("/templates", async (ReminderService svc) => Results.Ok(new { templates = await svc.TemplatesAsync(), placeholders = ReminderService.Placeholders }))
            .RequireAuthorization(Permissions.RemindersManage);
        g.MapPut("/templates", async (TemplateRequest r, ClaimsPrincipal u, ReminderService svc) =>
            Done(await svc.SaveTemplateAsync(r.Kind, r.Channel, r.Language, r.Subject, r.Body, r.IsActive, Me(u)))).RequireAuthorization(Permissions.RemindersManage);
        g.MapGet("/settings", async (HttpContext http, ReminderService svc) =>
        {
            var s = await svc.SettingsAsync();
            var key = await svc.WebhookKeyAsync();
            var root = $"{http.Request.Scheme}://{http.Request.Host}/api/hooks/sms/{key}";
            return Results.Ok(new
            {
                smsProvider = s.SmsProvider.ToString(), emailProvider = s.EmailProvider.ToString(), s.SendBookingConfirmation, s.SendDayBeforeReminder, s.SendEmailCopies,
                s.DayBeforeHour, s.ArrivalLeadMinutes, quietStart = s.QuietStart.ToString("HH\\:mm"), quietEnd = s.QuietEnd.ToString("HH\\:mm"), s.ClinicName, s.ClinicPhone,
                s.ModicaBaseUrl, s.ModicaApplication, modicaPasswordSet = s.ModicaPasswordProtected is not null, s.ModicaMask,
                s.GenericUrl, s.GenericAuthHeader, genericAuthValueSet = s.GenericAuthValueProtected is not null, s.GenericBodyTemplate,
                s.SmtpHost, s.SmtpPort, s.SmtpStartTls, s.SmtpUser, smtpPasswordSet = s.SmtpPasswordProtected is not null, s.FromAddress, s.FromName,
                callbacks = new { replies = root + "/inbound", deliveryReceipts = root + "/status" },
                s.UpdatedAt, s.UpdatedBy,
                smsProviders = Enum.GetNames<SmsProvider>(), emailProviders = Enum.GetNames<EmailProvider>()
            });
        }).RequireAuthorization(Permissions.RemindersManage);
        g.MapPut("/settings", async (ReminderService.SettingsUpdate r, ClaimsPrincipal u, ReminderService svc) =>
        {
            var errors = await svc.UpdateSettingsAsync(r, Me(u));
            return errors.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors });
        }).RequireAuthorization(Permissions.RemindersManage);
        g.MapPost("/settings/rotate-callback-key", async (ClaimsPrincipal u, ReminderService svc) => { await svc.RotateWebhookKeyAsync(Me(u)); return Results.Ok(); })
            .RequireAuthorization(Permissions.RemindersManage);
        g.MapPost("/test", async (TestRequest r, ClaimsPrincipal u, ReminderService svc) =>
        {
            var m = await svc.QueueTestAsync(r.Channel, r.To, Me(u));
            if (m.Count == 0) return Done(r.Channel == Channel.Sms ? "Enter a NZ mobile number (e.g. 021 123 4567)." : "Enter a valid email address.");
            await svc.SendDueAsync();
            var row = (await svc.MessagesAsync(null, Guid.Empty)).First(x => x.Id == m[0].Id);
            return Results.Ok(new { row.Status, row.LastError, row.Provider });
        }).RequireAuthorization(Permissions.RemindersManage);
        g.MapPost("/run-now", async (ReminderService svc) => { var (q, s, f) = await svc.RunCycleAsync(); return Results.Ok(new { queued = q, sent = s, failed = f }); })
            .RequireAuthorization(Permissions.RemindersManage);
        // Test mode only: pretend a patient replied, so the reply handling can be tried without a real SMS vendor.
        g.MapPost("/simulate-reply", async (SimulateReplyRequest r, ReminderService svc) =>
        {
            var s = await svc.SettingsAsync();
            if (s.SmsProvider != SmsProvider.TestOutbox) return Done("Simulated replies are only allowed in test mode.");
            var reply = await svc.HandleReplyAsync(r.From, r.Body ?? "", "TestOutbox", Guid.NewGuid().ToString("N"), null);
            return Results.Ok(new { reply.Meaning, reply.ActionTaken });
        }).RequireAuthorization(Permissions.RemindersManage);

        // SMS vendor callbacks (no staff sign-in). The secret key in the URL authenticates the vendor; wrong keys get 404 and are rate-limited.
        // Exempt from the browser CSRF header (see UsePasCsrfProtection). Bodies are capped at 16 KB.
        var hooks = app.MapGroup("/api/hooks/sms/{key}").RequireModule(RemindersModule.Id).RequireRateLimiting("public").AllowAnonymous();
        hooks.MapPost("/inbound", async (string key, HttpRequest req, ReminderService svc) =>
        {
            if (!await svc.WebhookKeyMatchesAsync(key)) return Results.NotFound();
            var parsed = await ReadJsonAsync(req);
            if (parsed is not { } json) return Results.BadRequest();
            // Modica MO: { id, source, destination, content, reply_to, encoding }. Generic vendors: from/to/message/text.
            var from = Str(json, "source") ?? Str(json, "from") ?? Str(json, "msisdn");
            var body = Str(json, "content") ?? Str(json, "message") ?? Str(json, "text") ?? Str(json, "body") ?? "";
            if (Str(json, "encoding") == "base64") { try { body = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(body)); } catch (FormatException) { } }
            if (string.IsNullOrWhiteSpace(from)) return Results.BadRequest();
            var settings = await svc.SettingsAsync();
            await svc.HandleReplyAsync(from, body, settings.SmsProvider.ToString(), Str(json, "id") ?? Str(json, "message_id"), Str(json, "reply_to"));
            return Results.Ok();
        });
        hooks.MapPost("/status", async (string key, HttpRequest req, ReminderService svc) =>
        {
            if (!await svc.WebhookKeyMatchesAsync(key)) return Results.NotFound();
            var json = await ReadJsonAsync(req);
            var id = json is null ? null : Str(json.Value, "message_id") ?? Str(json.Value, "id");
            var status = json is null ? null : Str(json.Value, "status");
            if (id is null || status is null) return Results.BadRequest();
            await svc.HandleDeliveryReceiptAsync(id, status);
            return Results.Ok();
        });
    }

    private static async Task<JsonElement?> ReadJsonAsync(HttpRequest req)
    {
        if (req.ContentLength is > 16_384) return null;
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        int n;
        while ((n = await req.Body.ReadAsync(buffer)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > 16_384) return null;
        }
        try { using var doc = JsonDocument.Parse(ms.ToArray()); return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null; }
        catch (JsonException) { return null; }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null } : null;
}
