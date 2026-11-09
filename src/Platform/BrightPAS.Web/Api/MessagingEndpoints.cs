using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BrightPAS.Identity;
using BrightPAS.Licensing;
using BrightPAS.Modules.Messaging;
using BrightPAS.Web.Integration;
using BrightPAS.Web.Licensing;
using MsgFormat = BrightPAS.Modules.Messaging.MessageFormat;

namespace BrightPAS.Web.Api;

/// <summary>Messaging engine (ADR 0021): dashboard, interfaces, messages, flows, mappings, translation tables and the test harness.</summary>
public static class MessagingEndpoints
{
    public sealed record ReasonRequest(string Reason);
    public sealed record NoteRequest(string? Note);
    public sealed record ContentRequest(string Content);
    public sealed record ParseRequest(MsgFormat Format, string Content);
    public sealed record Base64Request(string ContentBase64);
    public sealed record CsvRequest(string Csv);
    public sealed record PreviewRequest(MsgFormat Source, MsgFormat Target, VersionEdit Version);
    public sealed record NewDraftRequest(Guid FromVersionId);
    public sealed record FlowTestRequest(FlowEdit Flow, string Sample, bool UseDrafts);

    private static string Me(ClaimsPrincipal u) => u.Identity?.Name ?? "unknown";
    private static IResult Errors(IReadOnlyList<string> e) => Results.BadRequest(new { errors = e });
    private static IResult Done(string? error) => error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });

    public static void MapMessagingEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/messaging").RequireModule(MessagingModule.Id);
        const string View = Permissions.MessagingView, Manage = Permissions.IntegrationManage;

        // ---------------------------------------------------------------- dashboard & interfaces
        g.MapGet("/dashboard", async (MessagingService svc, MessagingRuntime runtime) =>
        {
            var ifaces = await svc.InterfacesAsync();
            return Results.Ok(new
            {
                interfaces = ifaces.Select(i => new { i, state = runtime.States.GetValueOrDefault(i.Id) }),
                alerts = await svc.AlertsAsync(true),
                problems = (await svc.SearchAsync(new MessageQuery(Status: "problems", Take: 20))),
            });
        }).RequireAuthorization(View);
        g.MapGet("/interfaces", async (MessagingService svc) => Results.Ok(await svc.InterfacesAsync())).RequireAuthorization(View);
        g.MapPost("/interfaces", async (InterfaceEdit e, ClaimsPrincipal u, MessagingService svc) =>
        {
            var (i, errors) = await svc.SaveInterfaceAsync(e, Me(u));
            return i is null ? Errors(errors) : Results.Ok(new { id = i.Id });
        }).RequireAuthorization(Manage);
        g.MapPost("/interfaces/generate-key", () => Results.Ok(new { key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_') }))
            .RequireAuthorization(Manage);
        g.MapPost("/interfaces/{id:guid}/test", async (Guid id, MessagingService svc, MessagingRuntime runtime, IHttpClientFactory http, HttpContext ctx) =>
        {
            var i = await svc.InterfaceAsync(id);
            if (i is null) return Results.NotFound();
            return Results.Ok(new { result = await TestAsync(i, runtime, http, ctx) });
        }).RequireAuthorization(Manage);
        g.MapPost("/interfaces/{id:guid}/inject", async (Guid id, ContentRequest r, ClaimsPrincipal u, MessagingService svc, MessagingPipeline pipeline, BrightPAS.Core.Audit.AuditWriter audit) =>
        {
            var i = await svc.InterfaceAsync(id);
            if (i is null) return Results.NotFound();
            if (i.Direction != InterfaceDirection.Inbound || i.Transport == Transport.Monitored) return Errors(["Test messages can only be put into an inbound interface."]);
            if (!i.IsActive) return Errors([$"{i.Name} is switched off."]);
            if (string.IsNullOrWhiteSpace(r.Content)) return Errors(["Paste a message first."]);
            var res = await pipeline.ReceiveAsync(i, r.Content, $"test by {Me(u)}");
            await audit.WriteAsync(Me(u), "Inject", "Interface", i.Id.ToString(), reason: $"Test message into {i.Code} → {res.Status}");
            return Results.Ok(new { messageId = res.MessageId, status = res.Status.ToString(), ack = res.Ack, error = res.Error });
        }).RequireAuthorization(Manage);

        // ---------------------------------------------------------------- messages
        g.MapGet("/messages", async (Guid? interfaceId, string? status, Guid? patientId, string? nhi, string? text, DateOnly? from, DateOnly? to, MessagingService svc) =>
            Results.Ok(await svc.SearchAsync(new MessageQuery(interfaceId, status, patientId, nhi, text, from, to)))).RequireAuthorization(View);
        g.MapGet("/messages/{id:guid}", async (Guid id, ClaimsPrincipal u, MessagingService svc) =>
        {
            var d = await svc.GetAsync(id, Me(u));
            if (d is null) return Results.NotFound();
            IReadOnlyList<DocNode> tree = [];
            if (Enum.TryParse<MsgFormat>(d.Format, out var f)) try { tree = MessageDocs.Tree(f, d.Content); } catch (MappingException) { }
            return Results.Ok(new { d.Message, d.Content, d.Format, d.Events, d.Related, tree });
        }).RequireAuthorization(View);
        g.MapPost("/messages/{id:guid}/replay", async (Guid id, ReasonRequest r, ClaimsPrincipal u, MessagingService svc, MessagingPipeline pipeline) =>
        {
            var (copy, error) = await svc.ReplayAsync(id, Me(u), r.Reason);
            if (copy is null) return Done(error);
            if (copy.Direction == InterfaceDirection.Inbound)
            {
                var i = (await svc.InterfaceAsync(copy.InterfaceId))!;
                await pipeline.ProcessAsync(i, copy, svc.ContentOf(copy));
            }
            return Results.Ok(new { id = copy.Id });
        }).RequireAuthorization(Manage);

        // ---------------------------------------------------------------- alerts & settings
        g.MapGet("/alerts", async (bool? all, MessagingService svc) => Results.Ok(await svc.AlertsAsync(all != true))).RequireAuthorization(View);
        g.MapPost("/alerts/{id:guid}/clear", async (Guid id, ClaimsPrincipal u, MessagingService svc) => Done(await svc.ClearAlertAsync(id, Me(u)))).RequireAuthorization(Manage);
        g.MapGet("/settings", async (MessagingService svc) =>
        {
            var s = await svc.SettingsAsync();
            return Results.Ok(new SettingsEdit(s.MaxAttempts, s.AlertEmail, s.RequireSecondApprover));
        }).RequireAuthorization(Manage);
        g.MapPut("/settings", async (SettingsEdit e, ClaimsPrincipal u, MessagingService svc) =>
        {
            var errors = await svc.SaveSettingsAsync(e, Me(u));
            return errors.Count > 0 ? Errors(errors) : Results.Ok();
        }).RequireAuthorization(Manage);

        // ---------------------------------------------------------------- translation tables
        g.MapGet("/tables", async (MappingService svc) => Results.Ok(await svc.TablesAsync())).RequireAuthorization(View);
        g.MapGet("/tables/{id:guid}", async (Guid id, MappingService svc) => await svc.TableAsync(id) is { } t ? Results.Ok(t) : Results.NotFound()).RequireAuthorization(View);
        g.MapPost("/tables", async (TableEdit e, ClaimsPrincipal u, MappingService svc) =>
        {
            var (t, errors) = await svc.SaveTableAsync(e, Me(u));
            return t is null ? Errors(errors) : Results.Ok(new { id = t.Id });
        }).RequireAuthorization(Manage);
        g.MapPost("/tables/{id:guid}/delete", async (Guid id, ClaimsPrincipal u, MappingService svc) => Done(await svc.DeleteTableAsync(id, Me(u)))).RequireAuthorization(Manage);
        g.MapPost("/tables/parse-csv", (CsvRequest r) => r.Csv.Length > 2_000_000 ? Errors(["The file is too large (2 MB)."]) : Results.Ok(MappingService.ParseCsv(r.Csv)))
            .RequireAuthorization(Manage);
        g.MapGet("/tables/{id:guid}/csv", async (Guid id, MappingService svc) =>
            await svc.TableAsync(id) is { } t
                ? Results.File(Encoding.UTF8.GetBytes(MappingService.ToCsv(t.Entries)), "text/csv", $"{string.Concat(t.Table.Name.Where(char.IsLetterOrDigit))}.csv")
                : Results.NotFound()).RequireAuthorization(View);

        // ---------------------------------------------------------------- mappings
        g.MapGet("/mappings", async (MappingService svc) => Results.Ok(await svc.MappingsAsync())).RequireAuthorization(View);
        g.MapPost("/mappings", async (NewMappingEdit e, ClaimsPrincipal u, MappingService svc) =>
        {
            var (m, errors) = await svc.CreateMappingAsync(e, Me(u));
            return m is null ? Errors(errors) : Results.Ok(new { id = m.Id });
        }).RequireAuthorization(Manage);
        g.MapGet("/mappings/{id:guid}", async (Guid id, Guid? version, MappingService svc) => await svc.MappingAsync(id, version) is { } m ? Results.Ok(m) : Results.NotFound())
            .RequireAuthorization(View);
        g.MapPost("/mappings/{id:guid}/new-draft", async (Guid id, NewDraftRequest r, ClaimsPrincipal u, MappingService svc) =>
        {
            var (v, error) = await svc.NewDraftAsync(id, r.FromVersionId, Me(u));
            return v is null ? Done(error) : Results.Ok(new { id = v.Id });
        }).RequireAuthorization(Manage);
        g.MapPost("/versions/{id:guid}/draft", async (Guid id, VersionEdit e, ClaimsPrincipal u, MappingService svc) =>
        {
            var errors = await svc.SaveDraftAsync(id, e, Me(u));
            return errors.Count > 0 ? Errors(errors) : Results.Ok();
        }).RequireAuthorization(Manage);
        g.MapPost("/versions/{id:guid}/submit", async (Guid id, ClaimsPrincipal u, MappingService svc) => Done(await svc.SubmitAsync(id, Me(u)))).RequireAuthorization(Manage);
        g.MapPost("/versions/{id:guid}/approve", async (Guid id, ClaimsPrincipal u, MappingService maps, MessagingService svc) =>
            Done(await maps.ApproveAsync(id, Me(u), (await svc.SettingsAsync()).RequireSecondApprover))).RequireAuthorization(Manage);
        g.MapPost("/versions/{id:guid}/reject", async (Guid id, NoteRequest r, ClaimsPrincipal u, MappingService svc) => Done(await svc.RejectAsync(id, Me(u), r.Note))).RequireAuthorization(Manage);
        g.MapPost("/versions/{id:guid}/rollback", async (Guid id, ReasonRequest r, ClaimsPrincipal u, MappingService svc) => Done(await svc.RollbackAsync(id, Me(u), r.Reason))).RequireAuthorization(Manage);
        g.MapPost("/preview", async (PreviewRequest r, MappingService svc) => Results.Ok(await svc.PreviewAsync(r.Source, r.Target, r.Version))).RequireAuthorization(View);

        // ---------------------------------------------------------------- flows
        g.MapGet("/flows", async (FlowService svc) => Results.Ok(await svc.FlowsAsync())).RequireAuthorization(View);
        g.MapGet("/flows/{id:guid}", async (Guid id, FlowService svc) => await svc.FlowAsync(id) is { } f ? Results.Ok(f) : Results.NotFound()).RequireAuthorization(View);
        g.MapPost("/flows", async (FlowEdit e, ClaimsPrincipal u, FlowService svc) =>
        {
            var (f, problems) = await svc.SaveAsync(e, Me(u));
            return f is null ? Errors(problems) : Results.Ok(new { id = f.Id, warnings = problems });
        }).RequireAuthorization(Manage);
        g.MapPost("/flows/{id:guid}/delete", async (Guid id, ClaimsPrincipal u, FlowService svc) => Done(await svc.DeleteAsync(id, Me(u)))).RequireAuthorization(Manage);
        g.MapPost("/flows/check", async (FlowEdit e, FlowService svc) => Results.Ok(new { errors = await svc.CheckAsync(e.Nodes ?? [], e.Edges ?? []) })).RequireAuthorization(View);
        g.MapPost("/flows/test", async (FlowTestRequest r, FlowService svc) =>
            r.Sample.Length > MessagingService.MaxContentBytes ? Errors(["The sample is too large."]) : Results.Ok(await svc.TestAsync(r.Flow, r.Sample, r.UseDrafts))).RequireAuthorization(View);

        // ---------------------------------------------------------------- tools for the designer
        g.MapPost("/parse", (ParseRequest r) =>
        {
            try { return Results.Ok(new { tree = MessageDocs.Tree(r.Format, r.Content), problems = MessageDocs.Validate(r.Format, r.Content) }); }
            catch (MappingException ex) { return Results.Ok(new { tree = Array.Empty<DocNode>(), problems = new[] { ex.Message } }); }
        }).RequireAuthorization(View);
        g.MapPost("/dicom/convert", (Base64Request r) =>
        {
            try
            {
                var bytes = Convert.FromBase64String(r.ContentBase64);
                if (bytes.Length > 200_000_000) return Errors(["The DICOM file is too large."]);
                return Results.Ok(new { json = DicomConvert.ToJson(bytes) });
            }
            catch (FormatException) { return Errors(["The file could not be read."]); }
            catch (MappingException ex) { return Errors([ex.Message]); }
        }).RequireAuthorization(View);
        g.MapGet("/catalogue", () => Results.Ok(new { hl7 = Hl7Fields.Catalogue(), dicom = DicomDoc.Catalogue() })).RequireAuthorization(View);
        g.MapGet("/samples/{format}", (MsgFormat format) => Results.Ok(new { sample = MappingService.SampleFor(format), template = MappingService.TemplateFor(format) })).RequireAuthorization(View);

        // ---------------------------------------------------------------- inbound REST API (machine to machine)
        app.MapPost("/api/integration/in/{code}", async (string code, HttpContext ctx, MessagingService svc, MessagingPipeline pipeline, LicenceCache licence) =>
        {
            if (licence.Status.AccessTo(MessagingModule.Id) != ModuleAccess.Full) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            var i = await svc.InterfaceByCodeAsync(code);
            if (i is null || i.Transport != Transport.HttpReceive || !i.IsActive) return Results.NotFound();
            var expected = svc.SecretOf(i) ?? "";
            var given = ctx.Request.Headers["X-BrightPAS-Key"].ToString();
            if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected))) return Results.Unauthorized();
            if (ctx.Request.ContentLength > MessagingService.MaxContentBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            if (body.Length == 0) return Results.BadRequest(new { errors = new[] { "The request body is empty." } });
            var r = await pipeline.ReceiveAsync(i, body, $"http:{ctx.Connection.RemoteIpAddress}");
            if (r.Ack is { Length: > 0 } ack) return Results.Text(ack, "application/hl7-v2", Encoding.UTF8, r.Status == BrightPAS.Modules.Messaging.MessageStatus.Processed ? 200 : 422);
            return r.Status == BrightPAS.Modules.Messaging.MessageStatus.Processed
                ? Results.Accepted(value: new { messageId = r.MessageId })
                : Results.UnprocessableEntity(new { messageId = r.MessageId, errors = new[] { r.Error } });
        }).AllowAnonymous().RequireRateLimiting("integration-in").RequireModule(MessagingModule.Id);
    }

    private static async Task<string> TestAsync(Interface i, MessagingRuntime runtime, IHttpClientFactory http, HttpContext ctx)
    {
        switch (i.Transport)
        {
            case Transport.MllpListen:
            case Transport.DicomListen:
            case Transport.FolderWatch:
                var s = runtime.States.GetValueOrDefault(i.Id);
                if (!i.IsActive) return "Switched off – switch it on to start listening.";
                if (s is null) return "Starting… try again in 15 seconds. (The Messaging module must be licensed and the web app running.)";
                return s.Running ? $"Running since {s.Since:dd MMM HH:mm} UTC{(s.Port > 0 ? $" on port {s.Port}" : "")}; {s.Connections} connection(s){(s.LastActivity is { } la ? $", last activity {la:dd MMM HH:mm} UTC" : "")}." : $"Not running: {s.Problem}";
            case Transport.MllpSend:
                try
                {
                    using var client = new TcpClient();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await client.ConnectAsync(i.Host!, i.Port!.Value, cts.Token);
                    return $"Connected to {i.Host}:{i.Port}. (No message was sent.)";
                }
                catch (Exception ex) when (ex is SocketException or OperationCanceledException) { return $"Could not connect to {i.Host}:{i.Port}: {ex.Message}"; }
            case Transport.FolderWrite:
                try
                {
                    if (!Directory.Exists(i.FolderPath)) return $"Folder {i.FolderPath} not found on the server.";
                    var probe = Path.Combine(i.FolderPath!, $".brightpas-test-{Guid.NewGuid():N}.tmp");
                    await File.WriteAllTextAsync(probe, "test");
                    File.Delete(probe);
                    return $"BrightPAS can write to {i.FolderPath}.";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return $"Cannot write to {i.FolderPath}: {ex.Message}"; }
            case Transport.HttpPost:
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Head, i.Url);
                    using var res = await http.CreateClient("messaging").SendAsync(req);
                    return $"{i.Url} answered HTTP {(int)res.StatusCode}. (No message was sent.)";
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return $"Could not reach {i.Url}: {ex.Message}"; }
            case Transport.HttpReceive:
                return $"Other systems POST messages to https://{ctx.Request.Host}/api/integration/in/{i.Code} with the header X-BrightPAS-Key.";
            default:
                return "Recorded automatically by another module.";
        }
    }
}
