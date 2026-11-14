using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Messaging;

/// <summary>Rules that keep the SQL connector to safe, parameterised statements.</summary>
public static partial class SqlSafety
{
    [GeneratedRegex(@"\b(DROP|ALTER|TRUNCATE|GRANT|REVOKE|DENY|CREATE|SHUTDOWN|BACKUP|RESTORE|DBCC|OPENROWSET|OPENDATASOURCE|OPENQUERY|XP_\w+|SP_CONFIGURE|BULK|RECONFIGURE)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Dangerous();
    [GeneratedRegex(@"\b(INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE|INTO)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Writes();
    [GeneratedRegex(@"@(\w+)")]
    private static partial Regex Params();
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex Column();

    public static IReadOnlyList<string> Parameters(string sql) => Params().Matches(sql).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static IReadOnlyList<string> StatementProblems(string? sql, bool read, string? watermark)
    {
        var e = new List<string>();
        var s = (sql ?? "").Trim();
        if (s.Length == 0) { e.Add(read ? "Enter the SELECT statement to run." : "Enter the INSERT, UPDATE or EXEC statement to run for each message."); return e; }
        if (s.Length > 4000) e.Add("The statement can be up to 4000 characters.");
        if (s.Contains(';') || s.Contains("--") || s.Contains("/*")) e.Add("Use a single statement with no semicolons or comments.");
        if (Dangerous().IsMatch(s)) e.Add("The statement uses a command that is not allowed here (e.g. DROP, ALTER, GRANT, xp_ procedures).");
        if (read)
        {
            if (!Regex.IsMatch(s, @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase)) e.Add("An inbound database interface runs a SELECT (or WITH … SELECT).");
            if (Writes().IsMatch(s)) e.Add("The SELECT must not change data (no INSERT, UPDATE, DELETE, MERGE, EXEC or SELECT INTO).");
            if (watermark is not null)
            {
                if (!Column().IsMatch(watermark)) e.Add("The watermark column must be a plain column name.");
                if (!s.Contains("@watermark", StringComparison.OrdinalIgnoreCase)) e.Add("Use @watermark in the WHERE clause, e.g. WHERE (@watermark IS NULL OR UpdatedAt > @watermark) ORDER BY UpdatedAt, so each row is read once.");
            }
        }
        else
        {
            if (!Regex.IsMatch(s, @"^(INSERT|UPDATE|MERGE|EXEC|EXECUTE)\b", RegexOptions.IgnoreCase)) e.Add("An outbound database interface runs an INSERT, UPDATE, MERGE or EXEC (stored procedure).");
            if (Regex.IsMatch(s, @"\bDELETE\b", RegexOptions.IgnoreCase)) e.Add("Deleting rows is not allowed from a message.");
            if (Parameters(s).Count == 0) e.Add("Put message values in as @parameters named after the JSON fields (e.g. VALUES (@nhi, @familyName)).");
        }
        if (Regex.IsMatch(s, @"'[^']*'\s*\+|\+\s*'", RegexOptions.IgnoreCase)) e.Add("Do not build SQL by joining text – use @parameters.");
        return e;
    }

    public static IReadOnlyList<string> ConnectionProblems(string cs)
    {
        try
        {
            var b = new SqlConnectionStringBuilder(cs);
            var e = new List<string>();
            if (string.IsNullOrWhiteSpace(b.DataSource)) e.Add("The connection string needs a Server.");
            if (string.IsNullOrWhiteSpace(b.InitialCatalog)) e.Add("The connection string needs a Database (Initial Catalog).");
            if (b.Encrypt == SqlConnectionEncryptOption.Optional) e.Add("Use Encrypt=True (or Strict) so data is encrypted on the network.");
            return e;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException) { return [$"The connection string is not valid: {ex.Message}"]; }
    }
}

/// <summary>Removes patient details before anything is shown to an AI tool: values become shapes (A(4), 9(8)), NHIs and numbers are masked.</summary>
public static partial class Redactor
{
    [GeneratedRegex(@"\b[A-HJ-NP-Z]{3}(\d{4}|\d{2}[A-HJ-NP-Z]{2})\b")]
    private static partial Regex Nhi();
    [GeneratedRegex("\"[^\"]{1,200}\"")]
    private static partial Regex Quoted();
    [GeneratedRegex(@"\d{4,}")]
    private static partial Regex LongNumber();
    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.]+")]
    private static partial Regex Email();

    public static string Text(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = Email().Replace(s, "[email]");
        s = Nhi().Replace(s, "[NHI]");
        s = Quoted().Replace(s, "\"[value]\"");
        return LongNumber().Replace(s, "[number]");
    }

    public static string Mask(string? nhi) => nhi is { Length: 7 } ? $"{nhi[..3]}••{nhi[5..]}" : nhi is null ? "" : "•••";

    /// <summary>The shape of a value without its content: "TEST" → A(4), "19800517" → 9(8), "Aroha Mere" → X(10).</summary>
    public static string Shape(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        var t = v.All(char.IsLetter) ? "A" : v.All(char.IsDigit) ? "9" : "X";
        return $"{t}({v.Length})";
    }

    // Header fields that describe the message, not the patient, are kept.
    [GeneratedRegex(@"^(MSH-(?!7\b)(?!7\.)\d+|MSA-1|EVN-1|OBX(\[\d+\])?-(2|3|6|8|11)|OBR(\[\d+\])?-(4|25)|ORC(\[\d+\])?-(1|5))(\.\d+)*$")]
    private static partial Regex KeepRx();
    private static bool Keep(string path) => KeepRx().IsMatch(path);

    /// <summary>Message structure with every patient value replaced by its shape.</summary>
    public static IReadOnlyList<string> Skeleton(MessageFormat format, string content, int max = 300)
    {
        var lines = new List<string>();
        try
        {
            void Walk(IEnumerable<DocNode> nodes, int depth)
            {
                foreach (var n in nodes)
                {
                    if (lines.Count >= max) return;
                    var val = n.Value is null ? "" : format == MessageFormat.Hl7v2 && Keep(n.Path) ? n.Value : Shape(n.Value);
                    lines.Add($"{new string(' ', depth * 2)}{n.Path}{(val.Length > 0 ? " = " + val : "")}");
                    Walk(n.Children, depth + 1);
                }
            }
            Walk(MessageDocs.Tree(format, content), 0);
        }
        catch (MappingException ex) { lines.Add("(could not read the message: " + Text(ex.Message) + ")"); }
        return lines;
    }
}

public sealed record Finding(string Severity, string Title, string Advice);

/// <summary>Plain-English explanations of common interface failures. Runs entirely inside BrightPAS (nothing is sent anywhere).</summary>
public static class Explainer
{
    public static IReadOnlyList<Finding> Explain(MessageRow m, Interface i, IReadOnlyList<MessageEvent> events)
    {
        var f = new List<Finding>();
        var text = string.Join(" ", new[] { m.LastError ?? "", m.AckText ?? "" }.Concat(events.Select(e => e.Detail ?? ""))).ToLowerInvariant();
        void Add(string sev, string title, string advice) { if (f.All(x => x.Title != title)) f.Add(new(sev, title, advice)); }

        if (m.Status is "Processed" or "Sent" or "Recorded" && m.LastError is null) Add("ok", "No problem found", "The message was handled successfully.");
        if (m.Status == "Filtered" || events.Any(e => e.Step == "Filtered")) Add("info", "Filtered out on purpose", "A filter or decision in the flow stopped this message. That is expected if it is not meant for that output – check the flow's conditions if it should have gone through.");
        if (text.Contains("refused") || text.Contains("actively refused")) Add("error", "The other system refused the connection", $"Nothing is listening at {i.Host}:{i.Port}, or a firewall blocks it. Check the receiving system is running, the host and port are right, and the firewall allows BrightPAS's server. Retries continue automatically.");
        if (text.Contains("timed out") || text.Contains("timeout") || text.Contains("task was canceled")) Add("error", "No answer in time", "The other system did not reply within 30 seconds. It may be busy or offline, or a firewall drops the traffic. Retries continue automatically; if it keeps happening, contact the other system's support.");
        if (text.Contains("no such host") || text.Contains("name or service not known") || text.Contains("nodename nor servname")) Add("error", "Server name not found", "The host name does not resolve. Check the spelling or use the IP address, and check the server's DNS settings.");
        // For a message that came in, the acknowledgement is BrightPAS's own answer to the sender – the advice is different.
        var inbound = m.Direction == "Inbound";
        if (m.AckCode is "AR" or "CR") Add("error", inbound ? "BrightPAS refused this message (AR)" : "The receiver rejected the message (AR)", inbound
            ? "BrightPAS read the message and told the sender it will not accept it – the reason is shown above. Nothing from it was filed. Have it corrected in the sending system and sent again."
            : "The other system read the message but will not accept it – usually a missing or wrong field. Read its reason above, fix the mapping, then resend.");
        if (m.AckCode is "AE" or "CE") Add("error", inbound ? "BrightPAS could not process this message (AE)" : "The receiver had an error (AE)", inbound
            ? "BrightPAS accepted the connection but could not process the message – the reason is shown above. The sender has been told and will usually send it again."
            : "The other system accepted the connection but could not process the message. Read its reason; it may be their problem (contact them) or a field we send.");
        if (text.Contains("http 401") || text.Contains("http 403")) Add("error", "Not authorised by the web service", "The key or token in the interface's Authorization setting is wrong, expired or lacks permission. Ask the partner for a new one and update the interface.");
        if (text.Contains("http 404")) Add("error", "Web address not found", "The URL is wrong or the partner moved the endpoint. Check the address in the interface.");
        if (Regex.IsMatch(text, @"http 5\d\d")) Add("warning", "The partner's web service had an error", "This is on their side. BrightPAS retries automatically; tell the partner if it continues.");
        if (text.Contains("not in the translation table")) Add("error", "A code is missing from a translation table", "Open Translation tables, add the missing code shown in the error with what it should become, then replay or resend the message.");
        if (text.Contains("is not an hl7 path") || text.Contains("is not a json path") || text.Contains("not a dicom")) Add("error", "A mapping refers to a field that does not exist", "A rule uses a field path that is not valid for the message format. Open the mapping, fix the rule (drag the field from the sample tree), send it for approval, then replay.");
        if (text.Contains("no live version")) Add("error", "The mapping is not live yet", "A flow uses a mapping that has never been approved. Open the mapping, send it for approval and have a second administrator approve it.");
        if (text.Contains("msh-10")) Add("error", "The message has no control ID", "HL7 messages need MSH-10 so the receiver can acknowledge them. Add a rule setting MSH-10 (e.g. from the accession number).");
        if (text.Contains("must start with an msh")) Add("error", "Not an HL7 message", "The content does not start with MSH. The sender may be using the wrong format, or the file contains something else.");
        if (text.Contains("not registered at tds") || events.Any(e => e.Step == "Unmatched")) Add("warning", "Patient not found by NHI", "The NHI in the message does not match a registered patient. Register the patient (or correct the NHI) – lab results can be matched by hand in the lab inbox.");
        if (text.Contains("folder") && text.Contains("not found")) Add("error", "Folder missing", "The interface's folder does not exist on the server or BrightPAS's app pool cannot read/write it. Create it and give the BrightPAS app pool access.");
        if (text.Contains("could not listen") || text.Contains("address already in use")) Add("error", "The listening port is in use", "Another program (or another interface) already uses this port. Choose a different port or stop the other program, and allow it through Windows Firewall.");
        if (text.Contains("calledaenotrecognized") || text.Contains("callingaenotrecognized") || text.Contains("unregistered device")) Add("error", "DICOM device not registered", "The device's AE title or IP address is not in the interface's registered devices. Add it exactly as configured on the device.");
        if (text.Contains("login failed") || text.Contains("cannot open database")) Add("error", "The database refused the login", "Check the connection string user and database name, and that the account has only the rights the interface needs.");
        if (m.Status == "DeadLetter") Add("warning", "Retries have stopped", $"BrightPAS gave up after {m.Attempts} attempts. Fix the cause above, then press Resend with a reason.");
        if (f.Count == 0) Add("info", "No known pattern", "Check the step history and the content. The AI assistant (if switched on by an administrator) can look at a de-identified summary.");
        return f;
    }
}

/// <summary>MCP access tokens for AI tools (read-only engine status; never message content).</summary>
public sealed class McpService(MsgDbContext db, AuditWriter audit, TimeProvider clock)
{
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<IReadOnlyList<McpClient>> ClientsAsync(CancellationToken ct = default) => await db.McpClients.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);

    public async Task<(string? Token, string? Error)> CreateAsync(string name, int days, string user, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 2 or > 100) return (null, "Enter a name for the AI tool (2–100 characters).");
        if (days is < 1 or > 365) return (null, "Tokens last 1 to 365 days.");
        var token = "bpmcp_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var c = db.McpClients.Add(new McpClient { Name = name, TokenHash = Hash(token), CreatedAt = clock.GetUtcNow(), CreatedBy = user, ExpiresAt = clock.GetUtcNow().AddDays(days) }).Entity;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "McpClient", c.Id.ToString(), reason: $"{name}, {days} day(s)", ct: ct);
        return (token, null);
    }

    public async Task<string?> RevokeAsync(Guid id, string user, CancellationToken ct = default)
    {
        var c = await db.McpClients.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "Not found.";
        c.IsActive = false;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Revoke", "McpClient", id.ToString(), reason: c.Name, ct: ct);
        return null;
    }

    public async Task<McpClient?> AuthenticateAsync(string? bearer, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bearer) || !bearer.StartsWith("bpmcp_", StringComparison.Ordinal)) return null;
        var hash = Hash(bearer);
        var c = await db.McpClients.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (c is null || !c.IsActive || c.ExpiresAt < clock.GetUtcNow()) return null;
        c.LastUsedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return c;
    }
}

public sealed record AiSettingsEdit(bool Enabled, string? Endpoint, string? Model, string? ApiKey, string? PrivacyReference, bool PrivacyConfirmed);
public sealed record AiSettingsView(bool Enabled, string Endpoint, string Model, bool HasKey, string? PrivacyReference, string? ApprovedBy, DateTimeOffset? ApprovedAt);

/// <summary>Settings and the de-identified payload for the AI troubleshooting assistant (the HTTP call is made by the web host).</summary>
public sealed class AiAssistService(MsgDbContext db, MessagingService messages, MappingService mappings, IDocumentCipher cipher, AuditWriter audit, TimeProvider clock)
{
    public const string DefaultEndpoint = "https://api.anthropic.com/v1/messages";
    public const string DefaultModel = "claude-sonnet-4-5";

    public async Task<AiSettingsView> SettingsAsync(CancellationToken ct = default)
    {
        var s = await messages.SettingsAsync(ct);
        return new(s.AiEnabled, s.AiEndpoint ?? DefaultEndpoint, s.AiModel ?? DefaultModel, s.AiKey is { Length: > 0 }, s.AiPrivacyReference, s.AiApprovedBy, s.AiApprovedAt);
    }

    public async Task<IReadOnlyList<string>> SaveAsync(AiSettingsEdit e, string user, CancellationToken ct = default)
    {
        var s = await messages.SettingsAsync(ct);
        var errors = new List<string>();
        var endpoint = string.IsNullOrWhiteSpace(e.Endpoint) ? DefaultEndpoint : e.Endpoint.Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var u) || u.Scheme != "https") errors.Add("The AI service address must be https://.");
        if (e.Enabled)
        {
            if (!e.PrivacyConfirmed) errors.Add("Confirm the privacy impact assessment covers this use before switching the assistant on.");
            if (string.IsNullOrWhiteSpace(e.PrivacyReference)) errors.Add("Enter the privacy assessment reference (e.g. PIA-2026-07, approved by the Privacy Officer).");
            if (s.AiKey is null && string.IsNullOrWhiteSpace(e.ApiKey)) errors.Add("Enter the AI service API key.");
        }
        if (errors.Count > 0) return errors;
        var wasOn = s.AiEnabled;
        s.AiEndpoint = endpoint; s.AiModel = string.IsNullOrWhiteSpace(e.Model) ? DefaultModel : e.Model.Trim(); s.AiPrivacyReference = e.PrivacyReference?.Trim();
        if (!string.IsNullOrEmpty(e.ApiKey)) s.AiKey = cipher.Protect(Encoding.UTF8.GetBytes(e.ApiKey.Trim()));
        s.AiEnabled = e.Enabled;
        if (e.Enabled && !wasOn) { s.AiApprovedBy = user; s.AiApprovedAt = clock.GetUtcNow(); }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "AiAssistant", "1", reason: $"{(e.Enabled ? "ON" : "off")} ({s.AiModel} at {u!.Host}); privacy ref {s.AiPrivacyReference}", ct: ct);
        return [];
    }

    public async Task<(string Endpoint, string Model, string Key)?> ConnectionAsync(CancellationToken ct = default)
    {
        var s = await messages.SettingsAsync(ct);
        if (!s.AiEnabled || s.AiKey is null) return null;
        return (s.AiEndpoint ?? DefaultEndpoint, s.AiModel ?? DefaultModel, Encoding.UTF8.GetString(cipher.Unprotect(s.AiKey)));
    }

    /// <summary>The exact text the assistant would receive: interface settings, step history and errors (redacted), the message's shape (no values) and the mapping rules.</summary>
    public async Task<string?> PayloadAsync(Guid messageId, CancellationToken ct = default)
    {
        var m = await db.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == messageId, ct);
        if (m is null) return null;
        var i = await db.Interfaces.AsNoTracking().SingleAsync(x => x.Id == m.InterfaceId, ct);
        var events = await db.Events.AsNoTracking().Where(e => e.MessageId == messageId).OrderBy(e => e.At).ToListAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("You are helping a hospital integration administrator troubleshoot a message in BrightPAS's integration engine. All patient values have been removed: values are shown as shapes (A(4) = 4 letters, 9(8) = 8 digits, X(n) = mixed). Explain the likely cause in plain English and give concrete steps; if a mapping change would fix it, say which rule to change and how. Do not ask for patient data.");
        sb.AppendLine();
        sb.AppendLine($"Interface: {i.Name} ({i.Direction}, {i.Transport}, format {i.Format}, handler {i.Handler})");
        sb.AppendLine($"Status: {m.Status}; attempts {m.Attempts}; message type {m.MessageType}; ACK {m.AckCode} {Redactor.Text(m.AckText)}");
        if (m.LastError is not null) sb.AppendLine($"Last error: {Redactor.Text(m.LastError)}");
        sb.AppendLine("Step history:");
        foreach (var e in events) sb.AppendLine($"- {e.At:yyyy-MM-dd HH:mm:ss}Z {e.Step}: {Redactor.Text(e.Detail)}");
        sb.AppendLine();
        sb.AppendLine("Message structure (values replaced by their shape):");
        foreach (var l in Redactor.Skeleton(i.Format, messages.ContentOf(m))) sb.AppendLine(l);
        var flowId = m.FlowId ?? await db.Messages.AsNoTracking().Where(x => x.ParentId == m.Id && x.FlowId != null).Select(x => x.FlowId).FirstOrDefaultAsync(ct);
        if (flowId is { } fid)
        {
            var flow = await db.Flows.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fid, ct);
            var nodes = await db.Nodes.AsNoTracking().Where(n => n.FlowId == fid).ToListAsync(ct);
            var conds = await db.Conditions.AsNoTracking().Where(c => nodes.Select(n => n.Id).Contains(c.NodeId)).ToListAsync(ct);
            sb.AppendLine();
            sb.AppendLine($"Flow \"{flow?.Name}\": steps " + string.Join(", ", nodes.Select(n => $"{n.Kind} '{n.Label}'" + string.Concat(conds.Where(c => c.NodeId == n.Id).Select(c => $" [{c.Source} {c.Operator} value]")))));
            foreach (var mid in nodes.Where(n => n.MappingId != null).Select(n => n.MappingId!.Value).Distinct())
            {
                var live = await mappings.LiveAsync(mid, ct);
                if (live is null) { sb.AppendLine("A mapper's mapping has no live version."); continue; }
                sb.AppendLine($"Mapping \"{live.Mapping.Name}\" v{live.Version.Number} ({live.Mapping.SourceFormat} → {live.Mapping.TargetFormat}), starts from {live.Version.StartFrom}:");
                foreach (var f in live.Filters) sb.AppendLine($"  filter: {f.Source} {f.Operator} [value]");
                foreach (var r in live.Rules)
                    sb.AppendLine($"  rule {r.Sequence}: {r.Target} ← {r.Function}({r.Source}{(r.Function is RuleFunction.Date or RuleFunction.Split or RuleFunction.Join or RuleFunction.Substring ? ", " + r.Argument : "")})" +
                                  (r.TranslationTableId is { } t && live.Tables.TryGetValue(t, out var tab) ? $" via table '{tab.Name}' ({tab.Codes.Count} codes, missing codes: {tab.WhenMissing})" : "") +
                                  (r.ConditionSource is null ? "" : $" only when {r.ConditionSource} {r.ConditionOperator} [value]"));
            }
        }
        return sb.ToString();
    }

    public Task LogAsync(Guid messageId, string user, string payload, bool ok, CancellationToken ct = default) =>
        audit.WriteAsync(user, "AiConsult", "Message", messageId.ToString(), reason: $"{(ok ? "answered" : "failed")}; sent {payload.Length} de-identified characters, SHA-256 {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16]}", ct: ct);
}
