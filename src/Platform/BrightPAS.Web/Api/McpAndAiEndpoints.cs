using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrightPAS.Core.Audit;
using BrightPAS.Identity;
using BrightPAS.Modules.Messaging;
using BrightPAS.Web.Integration;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>
/// ADR 0021: an MCP server so AI tools can read the engine's health (never message content or patient identifiers),
/// built-in failure explanations, and the opt-in AI troubleshooting assistant that only ever receives a de-identified summary.
/// </summary>
public static class McpAndAiEndpoints
{
    public sealed record McpCreate(string Name, int Days);
    private static string Me(ClaimsPrincipal u) => u.Identity?.Name ?? "unknown";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void MapMcpAndAiEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/messaging").RequireModule(MessagingModule.Id);

        // ---------------------------------------------------------------- built-in explanation (nothing leaves BrightPAS)
        g.MapGet("/messages/{id:guid}/explain", async (Guid id, MessagingService svc, MsgDbContext db) =>
        {
            var row = await svc.RowAsync(id);
            if (row is null) return Results.NotFound();
            var i = (await svc.InterfaceAsync(row.InterfaceId))!;
            var events = db.Events.Where(e => e.MessageId == id).OrderBy(e => e.At).ToList();
            return Results.Ok(Explainer.Explain(row, i, events));
        }).RequireAuthorization(Permissions.MessagingView);

        // ---------------------------------------------------------------- AI assistant (opt-in, de-identified)
        g.MapGet("/ai", async (AiAssistService ai) => Results.Ok(await ai.SettingsAsync())).RequireAuthorization(Permissions.IntegrationManage);
        g.MapPut("/ai", async (AiSettingsEdit e, ClaimsPrincipal u, AiAssistService ai) =>
        {
            var errors = await ai.SaveAsync(e, Me(u));
            return errors.Count > 0 ? Results.BadRequest(new { errors }) : Results.Ok();
        }).RequireAuthorization(Permissions.IntegrationManage);
        g.MapGet("/messages/{id:guid}/ai-preview", async (Guid id, AiAssistService ai) =>
            await ai.PayloadAsync(id) is { } p ? Results.Ok(new { payload = p, enabled = (await ai.SettingsAsync()).Enabled }) : Results.NotFound()).RequireAuthorization(Permissions.IntegrationManage);
        g.MapPost("/messages/{id:guid}/ai", async (Guid id, ClaimsPrincipal u, AiAssistService ai, IHttpClientFactory http) =>
        {
            var conn = await ai.ConnectionAsync();
            if (conn is null) return Results.BadRequest(new { errors = new[] { "The AI assistant is switched off. An administrator can switch it on in Messaging › Settings after the privacy check." } });
            var payload = await ai.PayloadAsync(id);
            if (payload is null) return Results.NotFound();
            try
            {
                var body = new JsonObject { ["model"] = conn.Value.Model, ["max_tokens"] = 1200, ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = payload }) };
                using var req = new HttpRequestMessage(HttpMethod.Post, conn.Value.Endpoint) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
                req.Headers.TryAddWithoutValidation("x-api-key", conn.Value.Key);
                req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                using var res = await http.CreateClient("ai").SendAsync(req);
                var text = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                {
                    await ai.LogAsync(id, Me(u), payload, false);
                    return Results.BadRequest(new { errors = new[] { $"The AI service answered HTTP {(int)res.StatusCode}. Check the key and model in Messaging › Settings." } });
                }
                var answer = string.Concat((JsonNode.Parse(text)?["content"] as JsonArray ?? []).Select(c => c?["text"]?.GetValue<string>() ?? ""));
                await ai.LogAsync(id, Me(u), payload, true);
                return Results.Ok(new { answer, sentCharacters = payload.Length });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                await ai.LogAsync(id, Me(u), payload, false);
                return Results.BadRequest(new { errors = new[] { $"Could not reach the AI service: {ex.Message}" } });
            }
        }).RequireAuthorization(Permissions.IntegrationManage);

        // ---------------------------------------------------------------- MCP access tokens
        g.MapGet("/mcp-clients", async (McpService mcp) => Results.Ok((await mcp.ClientsAsync()).Select(c => new { c.Id, c.Name, c.IsActive, c.CreatedAt, c.CreatedBy, c.ExpiresAt, c.LastUsedAt })))
            .RequireAuthorization(Permissions.IntegrationManage);
        g.MapPost("/mcp-clients", async (McpCreate r, ClaimsPrincipal u, McpService mcp) =>
        {
            var (token, error) = await mcp.CreateAsync(r.Name, r.Days, Me(u));
            return token is null ? Results.BadRequest(new { errors = new[] { error } }) : Results.Ok(new { token });
        }).RequireAuthorization(Permissions.IntegrationManage);
        g.MapPost("/mcp-clients/{id:guid}/revoke", async (Guid id, ClaimsPrincipal u, McpService mcp) =>
            await mcp.RevokeAsync(id, Me(u)) is { } e ? Results.BadRequest(new { errors = new[] { e } }) : Results.Ok()).RequireAuthorization(Permissions.IntegrationManage);

        // ---------------------------------------------------------------- MCP server (JSON-RPC 2.0 over HTTP POST, bearer token)
        app.MapPost("/api/mcp", async (HttpContext ctx, McpService mcp, MessagingService svc, FlowService flows, MessagingRuntime runtime, MsgDbContext db, AuditWriter audit) =>
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            var client = await mcp.AuthenticateAsync(auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth[7..].Trim() : null);
            if (client is null)
            {
                ctx.Response.Headers.WWWAuthenticate = "Bearer";
                return Results.Unauthorized();
            }
            if (ctx.Request.ContentLength > 100_000) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            JsonObject? req;
            try { req = await JsonNode.ParseAsync(ctx.Request.Body) as JsonObject; }
            catch (JsonException) { return Rpc(null, error: (-32700, "Parse error")); }
            if (req is null) return Rpc(null, error: (-32600, "Send one JSON-RPC request object."));
            var id = req["id"]?.DeepClone();
            var method = req["method"]?.GetValue<string>() ?? "";
            if (id is null) return Results.Accepted(); // notification (e.g. notifications/initialized)
            switch (method)
            {
                case "initialize":
                    return Rpc(id, new JsonObject
                    {
                        ["protocolVersion"] = req["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "BrightPAS messaging engine", ["version"] = typeof(Program).Assembly.GetName().Version?.ToString() ?? "" },
                        ["instructions"] = "Read-only view of BrightPAS's integration engine for troubleshooting. Message content and patient identifiers are never returned.",
                    });
                case "ping":
                    return Rpc(id, new JsonObject());
                case "tools/list":
                    return Rpc(id, new JsonObject { ["tools"] = Tools() });
                case "tools/call":
                    var name = req["params"]?["name"]?.GetValue<string>() ?? "";
                    var args = req["params"]?["arguments"] as JsonObject ?? [];
                    await audit.WriteAsync($"mcp:{client.Name}", "McpCall", "Messaging", name, reason: Redactor.Text(args.ToJsonString()));
                    try
                    {
                        object? result = name switch
                        {
                            "engine_status" => await StatusAsync(svc, runtime),
                            "list_problem_messages" => await ProblemsAsync(svc, args),
                            "get_message" => await MessageAsync(svc, db, args),
                            "list_flows" => (await flows.FlowsAsync()).Select(f => new { f.Name, f.IsActive, f.Input, f.Outputs, steps = f.Nodes, f.UpdatedAt }),
                            _ => null,
                        };
                        if (result is null) return Rpc(id, error: (-32602, $"Unknown tool or item: {name}"));
                        return Rpc(id, new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(result, Json) }), ["isError"] = false });
                    }
                    catch (Exception ex) when (ex is FormatException or InvalidOperationException or KeyNotFoundException)
                    {
                        return Rpc(id, new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = Redactor.Text(ex.Message) }), ["isError"] = true });
                    }
                default:
                    return Rpc(id, error: (-32601, $"Method not found: {method}"));
            }
        }).AllowAnonymous().RequireRateLimiting("mcp").RequireModule(MessagingModule.Id);
    }


    private static IResult Rpc(JsonNode? id, JsonObject? result = null, (int Code, string Message)? error = null)
    {
        var o = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id };
        if (error is { } e) o["error"] = new JsonObject { ["code"] = e.Code, ["message"] = e.Message };
        else o["result"] = result;
        return Results.Text(o.ToJsonString(), "application/json");
    }

    private static JsonArray Tools()
    {
        static JsonObject Tool(string name, string description, JsonObject? props = null, params string[] required) => new()
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = props ?? new JsonObject(), ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()) },
            ["annotations"] = new JsonObject { ["readOnlyHint"] = true },
        };
        return
        [
            Tool("engine_status", "Every interface with its running state, volumes in the last 24 hours, waiting/failed/given-up counts, and open alerts."),
            Tool("list_problem_messages", "Messages that failed, were rejected or gave up, newest first, with redacted errors (no content, NHI masked).",
                new JsonObject { ["interface_code"] = new JsonObject { ["type"] = "string" }, ["limit"] = new JsonObject { ["type"] = "integer", ["maximum"] = 100 } }),
            Tool("get_message", "One message's status, step history (redacted) and BrightPAS's built-in explanation. Never includes the content.",
                new JsonObject { ["id"] = new JsonObject { ["type"] = "string", ["description"] = "Message id from list_problem_messages" } }, "id"),
            Tool("list_flows", "Message flows: name, on/off, input, outputs and number of steps."),
        ];
    }

    private static async Task<object> StatusAsync(MessagingService svc, MessagingRuntime runtime) => new
    {
        interfaces = (await svc.InterfacesAsync()).Select(i =>
        {
            var s = runtime.States.GetValueOrDefault(i.Id);
            return new { i.Code, i.Name, i.Direction, i.Transport, i.Format, active = i.IsActive, running = s?.Running, problem = Redactor.Text(s?.Problem), last24h = i.Total24h, waiting = i.Queued, failed24h = i.Failed24h, gaveUp = i.DeadLetters, i.LastIn, i.LastOut };
        }),
        openAlerts = (await svc.AlertsAsync(true)).Select(a => new { a.Interface, a.Kind, detail = Redactor.Text(a.Detail), a.RaisedAt }),
    };

    private static async Task<object> ProblemsAsync(MessagingService svc, JsonObject args)
    {
        Guid? iid = null;
        if (args["interface_code"]?.GetValue<string>() is { Length: > 0 } code) iid = (await svc.InterfaceByCodeAsync(code))?.Id ?? throw new KeyNotFoundException($"No interface {code}.");
        var limit = Math.Clamp(args["limit"]?.GetValue<int>() ?? 20, 1, 100);
        return (await svc.SearchAsync(new MessageQuery(iid, "problems", Take: limit)))
            .Select(m => new { m.Id, m.Interface, m.Direction, m.MessageType, m.Status, m.Attempts, error = Redactor.Text(m.LastError), ack = m.AckCode, nhi = Redactor.Mask(m.Nhi), m.CreatedAt });
    }

    private static async Task<object?> MessageAsync(MessagingService svc, MsgDbContext db, JsonObject args)
    {
        if (!Guid.TryParse(args["id"]?.GetValue<string>(), out var id)) throw new FormatException("id must be a message id.");
        var row = await svc.RowAsync(id);
        if (row is null) return null;
        var i = (await svc.InterfaceAsync(row.InterfaceId))!;
        var events = db.Events.Where(e => e.MessageId == id).OrderBy(e => e.At).ToList();
        return new
        {
            row.Id,
            row.Interface,
            row.Direction,
            row.MessageType,
            row.Status,
            row.Attempts,
            error = Redactor.Text(row.LastError),
            ack = row.AckCode,
            ackText = Redactor.Text(row.AckText),
            nhi = Redactor.Mask(row.Nhi),
            row.CreatedAt,
            row.CompletedAt,
            steps = events.Select(e => new { e.At, e.Step, detail = Redactor.Text(e.Detail) }),
            explanation = Explainer.Explain(row, i, events),
        };
    }
}
