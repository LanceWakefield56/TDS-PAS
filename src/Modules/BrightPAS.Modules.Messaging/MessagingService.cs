using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Integration;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Messaging;

public sealed record ClientEdit(string Address, string? AeTitle, string? Description);

public sealed record InterfaceEdit(Guid? Id, string Code, string Name, InterfaceDirection Direction, Transport Transport, MessageFormat Format, InboundHandler Handler,
    string? Host, int? Port, string? FolderPath, string? Url, string? Secret, string? AeTitle, IReadOnlyList<ClientEdit>? AllowedClients, bool IsActive, int AlertAfterFailures,
    int? SilentAlertHours, string? Notes, string? SqlText = null, int? PollSeconds = null, string? WatermarkColumn = null);

public sealed record InterfaceRow(Guid Id, string Code, string Name, string Direction, string Transport, string Format, string Handler, string? Host, int? Port,
    string? FolderPath, string? Url, bool HasSecret, string? AeTitle, IReadOnlyList<ClientEdit> AllowedClients, bool IsActive, int AlertAfterFailures, int? SilentAlertHours,
    string? Notes, string? SystemKey, string? SqlText, int? PollSeconds, string? WatermarkColumn, DateTimeOffset? LastIn, DateTimeOffset? LastOut, int Queued, int Failed24h, int DeadLetters, int Total24h, int OpenAlerts);


public sealed record MessageQuery(Guid? InterfaceId = null, string? Status = null, Guid? PatientId = null, string? Nhi = null, string? Text = null,
    DateOnly? From = null, DateOnly? To = null, int Take = 200);

public sealed record MessageRow(Guid Id, Guid InterfaceId, string Interface, string Direction, string Status, string? MessageType, string? ControlId, string? Reference,
    Guid? PatientId, string? Nhi, int SizeBytes, int Attempts, string? LastError, string? AckCode, string? AckText, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    DateTimeOffset? NextAttemptAt, Guid? ParentId, string CreatedBy);

public sealed record MessageDetail(MessageRow Message, string Content, string Format, IReadOnlyList<MessageEvent> Events, IReadOnlyList<MessageRow> Related);

public sealed record AlertRow(Guid Id, Guid InterfaceId, string Interface, string Kind, string Detail, DateTimeOffset RaisedAt, DateTimeOffset? NotifiedAt);

public sealed record SettingsEdit(int MaxAttempts, string? AlertEmail, bool RequireSecondApprover);

/// <summary>Interfaces, the message store, routing, the outbound queue, replay, monitoring and alerts.</summary>
public sealed partial class MessagingService(MsgDbContext db, PatientsDbContext patients, IDocumentCipher cipher, AuditWriter audit, TimeProvider clock)
{
    public const int MaxContentBytes = 5_000_000;
    private static readonly int[] BackoffMinutes = [1, 2, 5, 10, 30, 60, 120, 240];

    // ------------------------------------------------------------------ settings
    public async Task<MsgSettings> SettingsAsync(CancellationToken ct = default) =>
        await db.Settings.SingleOrDefaultAsync(s => s.Id == 1, ct) ?? (await AddSettings(ct));

    private async Task<MsgSettings> AddSettings(CancellationToken ct)
    {
        var s = db.Settings.Add(new MsgSettings()).Entity;
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<IReadOnlyList<string>> SaveSettingsAsync(SettingsEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (e.MaxAttempts is < 1 or > 20) errors.Add("Attempts before giving up must be between 1 and 20.");
        if (!string.IsNullOrWhiteSpace(e.AlertEmail) && !e.AlertEmail.Contains('@')) errors.Add("Enter a valid alert email address.");
        if (errors.Count > 0) return errors;
        var s = await SettingsAsync(ct);
        s.MaxAttempts = e.MaxAttempts; s.AlertEmail = Blank(e.AlertEmail); s.RequireSecondApprover = e.RequireSecondApprover;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "MessagingSettings", "1", reason: $"attempts {s.MaxAttempts}, second approver {(s.RequireSecondApprover ? "on" : "off")}", ct: ct);
        return [];
    }

    // ------------------------------------------------------------------ interfaces
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$")]
    private static partial Regex CodeRx();
    [GeneratedRegex("^[A-Z0-9_\\- ]{1,16}$")]
    private static partial Regex AeRx();

    public async Task<IReadOnlyList<InterfaceRow>> InterfacesAsync(CancellationToken ct = default)
    {
        var since = clock.GetUtcNow().AddDays(-1);
        var list = await db.Interfaces.AsNoTracking().OrderBy(i => i.Direction).ThenBy(i => i.Name).ToListAsync(ct);
        var clients = (await db.AllowedClients.AsNoTracking().ToListAsync(ct)).ToLookup(c => c.InterfaceId, c => new ClientEdit(c.Address, c.AeTitle, c.Description));
        var stats = await db.Messages.AsNoTracking().GroupBy(m => m.InterfaceId).Select(g => new
        {
            g.Key,
            LastIn = g.Where(m => m.Direction == InterfaceDirection.Inbound).Max(m => (DateTimeOffset?)m.CreatedAt),
            LastOut = g.Where(m => m.Direction == InterfaceDirection.Outbound && (m.Status == MessageStatus.Sent || m.Status == MessageStatus.Recorded)).Max(m => (DateTimeOffset?)m.CompletedAt),
            Queued = g.Count(m => m.Status == MessageStatus.Queued || m.Status == MessageStatus.Sending),
            Failed = g.Count(m => m.CreatedAt >= since && (m.Status == MessageStatus.Failed || m.Status == MessageStatus.Rejected)),
            Dead = g.Count(m => m.Status == MessageStatus.DeadLetter),
            Total = g.Count(m => m.CreatedAt >= since),
        }).ToDictionaryAsync(x => x.Key, ct);
        var alerts = await db.Alerts.AsNoTracking().Where(a => a.ClearedAt == null).GroupBy(a => a.InterfaceId).Select(g => new { g.Key, n = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.n, ct);
        return list.Select(i =>
        {
            var s = stats.GetValueOrDefault(i.Id);
            return new InterfaceRow(i.Id, i.Code, i.Name, i.Direction.ToString(), i.Transport.ToString(), i.Format.ToString(), i.Handler.ToString(), i.Host, i.Port,
                i.FolderPath, i.Url, i.Secret is { Length: > 0 }, i.AeTitle, clients[i.Id].ToList(), i.IsActive, i.AlertAfterFailures, i.SilentAlertHours, i.Notes, i.SystemKey, i.SqlText, i.PollSeconds, i.WatermarkColumn,
                s?.LastIn, s?.LastOut, s?.Queued ?? 0, s?.Failed ?? 0, s?.Dead ?? 0, s?.Total ?? 0, alerts.GetValueOrDefault(i.Id));
        }).ToList();
    }

    public Task<Interface?> InterfaceAsync(Guid id, CancellationToken ct = default) => db.Interfaces.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
    public Task<Interface?> InterfaceByCodeAsync(string code, CancellationToken ct = default) => db.Interfaces.AsNoTracking().SingleOrDefaultAsync(i => i.Code == code, ct);

    public async Task<IReadOnlyList<AllowedClient>> AllowedClientsAsync(Guid interfaceId, CancellationToken ct = default) =>
        await db.AllowedClients.AsNoTracking().Where(c => c.InterfaceId == interfaceId).ToListAsync(ct);

    /// <summary>Active interfaces the background host runs (listeners, folders, senders).</summary>
    public async Task<IReadOnlyList<Interface>> ActiveAsync(params Transport[] transports) =>
        await db.Interfaces.AsNoTracking().Where(i => i.IsActive && transports.Contains(i.Transport)).ToListAsync();

    public Task<InterfaceState?> StateAsync(Guid interfaceId, CancellationToken ct = default) => db.States.AsNoTracking().SingleOrDefaultAsync(s => s.InterfaceId == interfaceId, ct);

    public async Task SaveStateAsync(Guid interfaceId, string? watermark, int rows, string? error, CancellationToken ct = default)
    {
        var s = await db.States.SingleOrDefaultAsync(x => x.InterfaceId == interfaceId, ct) ?? db.States.Add(new InterfaceState { InterfaceId = interfaceId }).Entity;
        s.Watermark = watermark is null ? s.Watermark : Trunc(watermark, 200); s.LastRows = rows; s.LastPolledAt = clock.GetUtcNow(); s.LastError = error is null ? null : Trunc(error, 500);
        await db.SaveChangesAsync(ct);
    }

    public string? SecretOf(Interface i) => i.Secret is { Length: > 0 } s ? Encoding.UTF8.GetString(cipher.Unprotect(s)) : null;

    public async Task<(Interface? Interface, IReadOnlyList<string> Errors)> SaveInterfaceAsync(InterfaceEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var code = (e.Code ?? "").Trim().ToLowerInvariant();
        if (!CodeRx().IsMatch(code)) errors.Add("Code: 2–40 lower-case letters, numbers or dashes (e.g. labs-in).");
        if (string.IsNullOrWhiteSpace(e.Name)) errors.Add("Enter a name.");
        var existing = e.Id is { } id ? await db.Interfaces.SingleOrDefaultAsync(i => i.Id == id, ct) : null;
        if (e.Id is not null && existing is null) return (null, ["Interface not found."]);
        if (existing?.Transport == Transport.Monitored) return (null, ["This interface is recorded automatically by another module and cannot be edited here."]);
        if (e.Transport == Transport.Monitored) errors.Add("Choose how messages arrive or leave.");
        if (await db.Interfaces.AnyAsync(i => i.Code == code && i.Id != (e.Id ?? Guid.Empty), ct)) errors.Add($"Another interface already uses the code {code}.");
        var inbound = e.Transport is Transport.MllpListen or Transport.FolderWatch or Transport.DicomListen or Transport.HttpReceive or Transport.SqlQuery;
        if (inbound != (e.Direction == InterfaceDirection.Inbound)) errors.Add(inbound ? "Listening (MLLP, DICOM, web API) or watching a folder is an inbound interface." : "Sending by MLLP, writing to a folder or posting to a web address is an outbound interface.");
        if (e.Transport is Transport.MllpListen or Transport.MllpSend && e.Format != MessageFormat.Hl7v2) errors.Add("MLLP carries HL7 v2 messages – choose the HL7 v2 format.");
        if (e.Transport == Transport.DicomListen && e.Format != MessageFormat.Dicom) errors.Add("A DICOM listener receives DICOM – choose the DICOM format.");
        if (e.Handler != InboundHandler.None && e.Direction != InterfaceDirection.Inbound) errors.Add("Only inbound interfaces can hand messages to a BrightPAS module.");
        if (e.Handler == InboundHandler.LabResults && e.Format != MessageFormat.Hl7v2) errors.Add("Lab results are received as HL7 v2.");
        var clients = (e.AllowedClients ?? []).Where(c => !string.IsNullOrWhiteSpace(c.Address))
            .Select(c => new ClientEdit(c.Address.Trim(), Blank(c.AeTitle)?.ToUpperInvariant(), Blank(c.Description))).DistinctBy(c => (c.Address, c.AeTitle)).ToList();
        var ae = Blank(e.AeTitle)?.ToUpperInvariant();
        switch (e.Transport)
        {
            case Transport.MllpListen:
            case Transport.DicomListen:
                if (e.Port is not (>= 1024 and <= 65535)) errors.Add("Listening port must be between 1024 and 65535.");
                else if (e.IsActive && await db.Interfaces.AnyAsync(i => i.IsActive && (i.Transport == Transport.MllpListen || i.Transport == Transport.DicomListen) && i.Port == e.Port && i.Id != (e.Id ?? Guid.Empty), ct))
                    errors.Add($"Another active interface already listens on port {e.Port}.");
                foreach (var c in clients)
                {
                    if (!System.Net.IPAddress.TryParse(c.Address, out _)) errors.Add($"\"{c.Address}\" is not an IP address.");
                    if (c.AeTitle is { } cae && !AeRx().IsMatch(cae)) errors.Add($"\"{cae}\" is not a valid AE title (up to 16 letters, numbers, spaces, - or _).");
                }
                if (e.Transport == Transport.DicomListen)
                {
                    if (ae is null || !AeRx().IsMatch(ae)) errors.Add("Enter BrightPAS's AE title for this listener (up to 16 letters, numbers, - or _), e.g. BRIGHTPAS.");
                    if (clients.Count == 0) errors.Add("Register each device that may send (IP address and AE title). Unregistered devices are refused.");
                    if (clients.Any(c => c.AeTitle is null)) errors.Add("Give every DICOM device its AE title as well as its IP address.");
                }
                break;
            case Transport.SqlQuery:
            case Transport.SqlWrite:
                if (e.Format != MessageFormat.Json) errors.Add("Database rows are handled as JSON – choose the JSON format.");
                if (e.Id is null && string.IsNullOrWhiteSpace(e.Secret)) errors.Add("Enter the connection string for the other database (stored encrypted).");
                if (!string.IsNullOrWhiteSpace(e.Secret)) errors.AddRange(SqlSafety.ConnectionProblems(e.Secret));
                errors.AddRange(SqlSafety.StatementProblems(e.SqlText, e.Transport == Transport.SqlQuery, Blank(e.WatermarkColumn)));
                if (e.Transport == Transport.SqlQuery && e.PollSeconds is not (>= 30 and <= 86400)) errors.Add("Run the query every 30 seconds to 24 hours (86400 seconds).");
                break;
            case Transport.HttpReceive:
                if (e.Id is null && string.IsNullOrWhiteSpace(e.Secret)) errors.Add("Set the key other systems must send (use Generate key).");
                if (!string.IsNullOrEmpty(e.Secret) && e.Secret.Length < 24) errors.Add("The key must be at least 24 characters.");
                break;
            case Transport.MllpSend:
                if (string.IsNullOrWhiteSpace(e.Host) || e.Host.Contains(' ') || e.Host.Contains('/')) errors.Add("Enter the receiving system's host name or IP address.");
                if (e.Port is not (>= 1 and <= 65535)) errors.Add("Enter the receiving system's port.");
                break;
            case Transport.FolderWatch:
            case Transport.FolderWrite:
                if (string.IsNullOrWhiteSpace(e.FolderPath) || !Path.IsPathRooted(e.FolderPath)) errors.Add("Enter the full folder path on the server (e.g. D:\\Interfaces\\Labs\\In).");
                break;
            case Transport.HttpPost:
                if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var u) || !(u.Scheme == "https" || (u.Scheme == "http" && u.IsLoopback)))
                    errors.Add("Enter a full https:// address (plain http is only allowed to this server).");
                break;
        }
        if (e.AlertAfterFailures is < 1 or > 100) errors.Add("Alert after failures must be between 1 and 100.");
        if (e.SilentAlertHours is < 1 or > 720) errors.Add("Quiet-time alert must be between 1 and 720 hours.");
        if (errors.Count > 0) return (null, errors);

        var now = clock.GetUtcNow();
        var i2 = existing ?? db.Interfaces.Add(new Interface { CreatedAt = now, CreatedBy = user }).Entity;
        i2.Code = code; i2.Name = e.Name.Trim(); i2.Direction = e.Direction; i2.Transport = e.Transport; i2.Format = e.Format; i2.Handler = e.Handler;
        i2.Host = e.Transport == Transport.MllpSend ? e.Host!.Trim() : null;
        i2.Port = e.Transport is Transport.MllpListen or Transport.MllpSend or Transport.DicomListen ? e.Port : null;
        i2.FolderPath = e.Transport is Transport.FolderWatch or Transport.FolderWrite ? e.FolderPath!.Trim() : null;
        i2.Url = e.Transport == Transport.HttpPost ? e.Url!.Trim() : null;
        i2.AeTitle = e.Transport == Transport.DicomListen ? ae : null;
        var sql = e.Transport is Transport.SqlQuery or Transport.SqlWrite;
        i2.SqlText = sql ? e.SqlText!.Trim() : null;
        i2.PollSeconds = e.Transport == Transport.SqlQuery ? e.PollSeconds : null;
        i2.WatermarkColumn = e.Transport == Transport.SqlQuery ? Blank(e.WatermarkColumn) : null;
        if (e.Transport is not (Transport.HttpPost or Transport.HttpReceive or Transport.SqlQuery or Transport.SqlWrite)) i2.Secret = null;
        else if (e.Secret is not null) i2.Secret = e.Secret.Length == 0 && !sql ? null : e.Secret.Length == 0 ? i2.Secret : cipher.Protect(Encoding.UTF8.GetBytes(e.Secret));
        i2.IsActive = e.IsActive; i2.AlertAfterFailures = e.AlertAfterFailures; i2.SilentAlertHours = e.SilentAlertHours; i2.Notes = Blank(e.Notes);
        i2.UpdatedAt = now; i2.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await db.AllowedClients.Where(c => c.InterfaceId == i2.Id).ExecuteDeleteAsync(ct);
        if (e.Transport is Transport.MllpListen or Transport.DicomListen)
            db.AllowedClients.AddRange(clients.Select(c => new AllowedClient { InterfaceId = i2.Id, Address = c.Address, AeTitle = c.AeTitle, Description = c.Description }));
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, existing is null ? "Create" : "Update", "Interface", i2.Id.ToString(),
            reason: $"{i2.Code}: {i2.Transport} {Describe(i2)}{(i2.IsActive ? "" : " (inactive)")}", ct: ct);
        return (i2, []);
    }

    private static string Describe(Interface i) => i.Transport switch
    {
        Transport.SqlQuery => $"SELECT every {i.PollSeconds}s", Transport.SqlWrite => "database write",
        Transport.MllpListen => $"port {i.Port}", Transport.DicomListen => $"{i.AeTitle} on port {i.Port}", Transport.HttpReceive => $"/api/integration/in/{i.Code}", Transport.MllpSend => $"{i.Host}:{i.Port}",
        Transport.FolderWatch or Transport.FolderWrite => i.FolderPath ?? "", Transport.HttpPost => i.Url ?? "", _ => i.SystemKey ?? "",
    };

    /// <summary>Monitored interface for a module that sends its own messages (created on first use).</summary>
    public async Task<Interface> MonitoredAsync(string systemKey, CancellationToken ct = default)
    {
        var key = systemKey.ToLowerInvariant();
        var i = await db.Interfaces.SingleOrDefaultAsync(x => x.SystemKey == key, ct);
        if (i is not null) return i;
        var now = clock.GetUtcNow();
        var (name, format) = key switch
        {
            "healthlink" => ("HealthLink (letters to GPs)", MessageFormat.Hl7v2),
            "sms" => ("SMS (reminders and links)", MessageFormat.Text),
            "email" => ("Email (reminders and links)", MessageFormat.Text),
            _ => (key, MessageFormat.Text),
        };
        i = db.Interfaces.Add(new Interface
        {
            Code = $"{key}-monitor", Name = name, Direction = InterfaceDirection.Outbound, Transport = Transport.Monitored, Format = format, SystemKey = key,
            CreatedAt = now, CreatedBy = "system", UpdatedAt = now, UpdatedBy = "system", AlertAfterFailures = 5,
        }).Entity;
        await db.SaveChangesAsync(ct);
        return i;
    }

    // ------------------------------------------------------------------ message store
    private Message NewMessage(Interface i, InterfaceDirection dir, string content, string by, Guid? parentId, Guid? flowId)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.Length > MaxContentBytes) throw new InvalidOperationException($"Message is larger than {MaxContentBytes / 1_000_000} MB.");
        var m = new Message
        {
            InterfaceId = i.Id, Direction = dir, Content = cipher.Protect(bytes), Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), SizeBytes = bytes.Length,
            CreatedAt = clock.GetUtcNow(), CreatedBy = by, ParentId = parentId, FlowId = flowId,
        };
        Describe(m, i.Format, content);
        return m;
    }

    private void Event(Guid messageId, string step, string? detail) =>
        db.Events.Add(new MessageEvent { MessageId = messageId, At = clock.GetUtcNow(), Step = step, Detail = detail is null ? null : Trunc(detail, 500) });

    [GeneratedRegex(@"^[A-HJ-NP-Z]{3}(\d{4}|\d{2}[A-HJ-NP-Z]{2})$")]
    private static partial Regex NhiRx();

    /// <summary>Message type, control ID, reference and NHI for searching.</summary>
    private static void Describe(Message m, MessageFormat format, string content)
    {
        try
        {
            if (format == MessageFormat.Hl7v2 && content.StartsWith("MSH", StringComparison.Ordinal))
            {
                var d = Hl7Doc.Parse(content);
                m.MessageType = Trunc(string.Join('^', new[] { d.Get("MSH-9.1"), d.Get("MSH-9.2") }.Where(x => x.Length > 0)), 40);
                m.ControlId = Blank(Trunc(d.Get("MSH-10"), 64));
                m.Reference = Blank(Trunc(FirstNonEmpty(d.Get("OBR-3.1"), d.Get("ORC-3.1"), d.Get("OBR-2.1"), d.Get("ORC-2.1"), d.Get("MSA-2")), 64));
                var pid = d.Get("PID").Split('|');
                if (pid.Length > 3)
                    foreach (var rep in pid[3].Split('~'))
                        if (rep.Split('^')[0] is var id && NhiRx().IsMatch(id)) { m.Nhi = id; break; }
            }
            else if (format is MessageFormat.Fhir or MessageFormat.Json && content.TrimStart().StartsWith('{'))
            {
                var n = JsonNode.Parse(content) as JsonObject;
                m.MessageType = Blank(Trunc(n?["resourceType"]?.GetValue<string>() ?? "", 40));
                m.ControlId = Blank(Trunc(n?["id"]?.ToString() ?? "", 64));
                var nhi = Regex.Match(content, "\"value\"\\s*:\\s*\"([A-HJ-NP-Z]{3}(?:\\d{4}|\\d{2}[A-HJ-NP-Z]{2}))\"");
                if (nhi.Success) m.Nhi = nhi.Groups[1].Value;
            }
            else if (format == MessageFormat.Dicom && content.TrimStart().StartsWith('{'))
            {
                var d = DicomDoc.Parse(content);
                foreach (var id in new[] { d.Get("PatientID"), d.Get("OtherPatientIDsSequence[0].PatientID") })
                    if (NhiRx().IsMatch(id)) { m.Nhi = id; break; }
                var modality = d.Get("Modality");
                m.MessageType = Trunc("DICOM" + (modality.Length > 0 ? " " + modality : ""), 40);
                m.ControlId = Blank(Trunc(d.Get("SOPInstanceUID"), 64));
                m.Reference = Blank(Trunc(d.Get("AccessionNumber"), 64));
            }
        }
        catch (Exception ex) when (ex is MappingException or System.Text.Json.JsonException or InvalidOperationException) { }
    }

    private async Task MatchPatientAsync(Message m, CancellationToken ct)
    {
        if (m.Nhi is null) return;
        m.PatientId = await patients.Patients.AsNoTracking().Where(p => p.Nhi == m.Nhi && p.MergedIntoId == null).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
    }

    public string ContentOf(Message m) => Encoding.UTF8.GetString(cipher.Unprotect(m.Content));

    /// <summary>Stores an inbound message (status Received). The caller then runs the handler and <see cref="CompleteInboundAsync"/>.</summary>
    public async Task<Message> ReceiveAsync(Interface i, string content, string receivedBy, Guid? parentId = null, CancellationToken ct = default)
    {
        var m = NewMessage(i, InterfaceDirection.Inbound, content, receivedBy, parentId, null);
        m.Status = MessageStatus.Received;
        await MatchPatientAsync(m, ct);
        db.Messages.Add(m);
        Event(m.Id, "Received", $"{m.SizeBytes} bytes from {receivedBy}{(parentId is null ? "" : " (replay)")}");
        if (m.Nhi is not null) Event(m.Id, m.PatientId is null ? "Unmatched" : "Matched", m.PatientId is null ? $"NHI {m.Nhi} is not registered at TDS." : $"NHI {m.Nhi} matched to a patient.");
        var problems = MessageDocs.Validate(i.Format, content);
        if (problems.Count > 0) Event(m.Id, "Validation", string.Join(" ", problems));
        await db.SaveChangesAsync(ct);
        return m;
    }

    public async Task CompleteInboundAsync(Guid messageId, MessageStatus status, string? ackCode, string? ackText, string? detail, Guid? patientId = null, CancellationToken ct = default)
    {
        var m = await db.Messages.SingleAsync(x => x.Id == messageId, ct);
        m.Status = status; m.AckCode = ackCode; m.AckText = ackText is null ? null : Trunc(ackText, 300); m.CompletedAt = clock.GetUtcNow();
        if (status is MessageStatus.Failed or MessageStatus.Rejected) m.LastError = detail is null ? null : Trunc(detail, 500);
        if (patientId is not null) m.PatientId = patientId;
        Event(m.Id, status.ToString(), detail ?? (ackCode is null ? null : $"ACK {ackCode} {ackText}"));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Queues a flow's output on an outbound interface, linked to the inbound message.</summary>
    public async Task<Message> QueueOutputAsync(Interface to, string content, Message parent, Guid flowId, string detail, CancellationToken ct = default)
    {
        var o = NewMessage(to, InterfaceDirection.Outbound, content, "flow", parent.Id, flowId);
        o.Status = MessageStatus.Queued; o.NextAttemptAt = clock.GetUtcNow(); o.PatientId = parent.PatientId; o.Nhi ??= parent.Nhi;
        db.Messages.Add(o);
        Event(o.Id, "Queued", detail);
        await db.SaveChangesAsync(ct);
        return o;
    }

    /// <summary>Records an output that could not be produced (mapping failed), so it shows in the problems list and can be replayed after a fix.</summary>
    public async Task FailedOutputAsync(Interface to, string content, Message parent, Guid flowId, string error, CancellationToken ct = default)
    {
        var o = NewMessage(to, InterfaceDirection.Outbound, content, "flow", parent.Id, flowId);
        o.Status = MessageStatus.Failed; o.LastError = Trunc(error, 500); o.PatientId = parent.PatientId; o.CompletedAt = clock.GetUtcNow();
        db.Messages.Add(o);
        Event(o.Id, "Failed", $"Not sent: {error}");
        await db.SaveChangesAsync(ct);
    }

    public async Task AddEventAsync(Guid messageId, string step, string? detail, CancellationToken ct = default)
    {
        Event(messageId, step, detail);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Queues a message for an outbound interface by its code (for modules and workflows).</summary>
    public async Task<(Message? Message, string? Error)> QueueAsync(string interfaceCode, string content, string createdBy, Guid? patientId = null, CancellationToken ct = default)
    {
        var i = await db.Interfaces.AsNoTracking().SingleOrDefaultAsync(x => x.Code == interfaceCode, ct);
        if (i is null || i.Direction != InterfaceDirection.Outbound || i.Transport == Transport.Monitored) return (null, $"No outbound interface with the code {interfaceCode}.");
        var problems = MessageDocs.Validate(i.Format, content);
        if (problems.Count > 0) return (null, string.Join(" ", problems));
        var m = NewMessage(i, InterfaceDirection.Outbound, content, createdBy, null, null);
        m.Status = MessageStatus.Queued; m.NextAttemptAt = clock.GetUtcNow(); m.PatientId ??= patientId;
        db.Messages.Add(m);
        Event(m.Id, "Queued", $"Queued by {createdBy}.");
        await db.SaveChangesAsync(ct);
        return (m, null);
    }

    /// <summary>Outbound messages due now, marked Sending.</summary>
    public async Task<IReadOnlyList<(Message Message, Interface Interface)>> TakeDueAsync(int take = 20, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var due = await db.Messages.Where(m => m.Status == MessageStatus.Queued && m.Direction == InterfaceDirection.Outbound && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.NextAttemptAt).Take(take).ToListAsync(ct);
        var ifaces = await db.Interfaces.AsNoTracking().Where(i => due.Select(d => d.InterfaceId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var list = new List<(Message, Interface)>();
        foreach (var m in due)
        {
            if (!ifaces.TryGetValue(m.InterfaceId, out var i) || !i.IsActive) continue;
            m.Status = MessageStatus.Sending;
            list.Add((m, i));
        }
        await db.SaveChangesAsync(ct);
        return list;
    }

    /// <summary>Sending that was interrupted (server restart) goes back to the queue.</summary>
    public async Task<int> RequeueStuckAsync(CancellationToken ct = default)
    {
        var cutoff = clock.GetUtcNow().AddMinutes(-10);
        return await db.Messages.Where(m => m.Status == MessageStatus.Sending && (m.NextAttemptAt == null || m.NextAttemptAt < cutoff))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MessageStatus.Queued), ct);
    }

    public async Task SentAsync(Guid id, string? ackCode, string? ackText, CancellationToken ct = default)
    {
        var m = await db.Messages.SingleAsync(x => x.Id == id, ct);
        m.Attempts++;
        if (ackCode is "AE" or "AR" or "CE" or "CR")
        {
            await FailedInternalAsync(m, $"The receiver rejected the message: {ackCode} {ackText}".Trim(), ackCode is "AR" or "CR", ct, ackCode, ackText);
            return;
        }
        m.Status = MessageStatus.Sent; m.CompletedAt = clock.GetUtcNow(); m.LastError = null; m.AckCode = ackCode; m.AckText = ackText is null ? null : Trunc(ackText, 300);
        Event(m.Id, "Sent", ackCode is null ? $"Attempt {m.Attempts}." : $"Attempt {m.Attempts}; ACK {ackCode} {ackText}".Trim());
        await db.SaveChangesAsync(ct);
    }

    public async Task FailedAsync(Guid id, string error, bool permanent, CancellationToken ct = default)
    {
        var m = await db.Messages.SingleAsync(x => x.Id == id, ct);
        m.Attempts++;
        await FailedInternalAsync(m, error, permanent, ct);
    }

    private async Task FailedInternalAsync(Message m, string error, bool permanent, CancellationToken ct, string? ackCode = null, string? ackText = null)
    {
        var s = await SettingsAsync(ct);
        m.LastError = Trunc(error, 500); m.AckCode = ackCode; m.AckText = ackText is null ? null : Trunc(ackText, 300);
        if (permanent || m.Attempts >= s.MaxAttempts)
        {
            m.Status = MessageStatus.DeadLetter; m.CompletedAt = clock.GetUtcNow(); m.NextAttemptAt = null;
            Event(m.Id, "DeadLetter", $"Gave up after {m.Attempts} attempt(s): {error}");
        }
        else
        {
            m.Status = MessageStatus.Queued;
            var wait = BackoffMinutes[Math.Min(m.Attempts - 1, BackoffMinutes.Length - 1)];
            m.NextAttemptAt = clock.GetUtcNow().AddMinutes(wait);
            Event(m.Id, "Retry", $"Attempt {m.Attempts} failed: {error}. Trying again in {wait} min.");
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Replay an inbound message (runs the handler and routes again) or resend an outbound one, as a new linked message. Audited.</summary>
    public async Task<(Message? Copy, string? Error)> ReplayAsync(Guid id, string user, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5) return (null, "Give a reason (at least 5 characters), e.g. \"mapping fixed\".");
        var m = await db.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return (null, "Message not found.");
        var i = await db.Interfaces.AsNoTracking().SingleAsync(x => x.Id == m.InterfaceId, ct);
        if (i.Transport == Transport.Monitored) return (null, "Messages recorded from another module are resent from that module (e.g. the letter or the reminder).");
        if (!i.IsActive) return (null, $"{i.Name} is switched off.");
        var content = ContentOf(m);
        Message copy;
        if (m.Direction == InterfaceDirection.Inbound)
        {
            copy = await ReceiveAsync(i, content, $"replay by {user}", m.Id, ct);
        }
        else
        {
            if (m.Status is MessageStatus.Queued or MessageStatus.Sending) return (null, "This message is still waiting to be sent.");
            copy = NewMessage(i, InterfaceDirection.Outbound, content, user, m.Id, m.FlowId);
            copy.Status = MessageStatus.Queued; copy.NextAttemptAt = clock.GetUtcNow(); copy.PatientId = m.PatientId;
            db.Messages.Add(copy);
            Event(copy.Id, "Queued", $"Resent by {user}: {reason.Trim()}");
        }
        Event(m.Id, m.Direction == InterfaceDirection.Inbound ? "Replayed" : "Resent", $"By {user}: {reason.Trim()} (new message {copy.Id:N})");
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, m.Direction == InterfaceDirection.Inbound ? "Replay" : "Resend", "Message", m.Id.ToString(), m.PatientId?.ToString(), reason: $"{i.Code}: {reason.Trim()}", ct: ct);
        return (copy, null);
    }

    public async Task<IReadOnlyList<MessageRow>> SearchAsync(MessageQuery q, CancellationToken ct = default)
    {
        var query = db.Messages.AsNoTracking().AsQueryable();
        if (q.InterfaceId is { } iid) query = query.Where(m => m.InterfaceId == iid);
        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            if (q.Status == "problems") query = query.Where(m => m.Status == MessageStatus.Failed || m.Status == MessageStatus.DeadLetter || m.Status == MessageStatus.Rejected);
            else if (q.Status == "waiting") query = query.Where(m => m.Status == MessageStatus.Queued || m.Status == MessageStatus.Sending);
            else if (Enum.TryParse<MessageStatus>(q.Status, true, out var st)) query = query.Where(m => m.Status == st);
        }
        if (q.PatientId is { } pid) query = query.Where(m => m.PatientId == pid);
        if (!string.IsNullOrWhiteSpace(q.Nhi)) { var nhi = q.Nhi.Trim().ToUpperInvariant(); query = query.Where(m => m.Nhi == nhi); }
        if (!string.IsNullOrWhiteSpace(q.Text)) { var t = q.Text.Trim(); query = query.Where(m => m.ControlId == t || m.Reference == t || m.MessageType == t); }
        if (q.From is { } f) { var from = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(-14); query = query.Where(m => m.CreatedAt >= from); }
        if (q.To is { } t2) { var to = new DateTimeOffset(t2.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).AddHours(14); query = query.Where(m => m.CreatedAt <= to); }
        var rows = await query.OrderByDescending(m => m.CreatedAt).Take(Math.Clamp(q.Take, 1, 1000)).ToListAsync(ct);
        return await RowsAsync(rows, ct);
    }

    private async Task<IReadOnlyList<MessageRow>> RowsAsync(IReadOnlyList<Message> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.InterfaceId).Distinct().ToList();
        var names = await db.Interfaces.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        return rows.Select(m => new MessageRow(m.Id, m.InterfaceId, names.GetValueOrDefault(m.InterfaceId, "?"), m.Direction.ToString(), m.Status.ToString(), m.MessageType, m.ControlId,
            m.Reference, m.PatientId, m.Nhi, m.SizeBytes, m.Attempts, m.LastError, m.AckCode, m.AckText, m.CreatedAt, m.CompletedAt, m.NextAttemptAt, m.ParentId, m.CreatedBy)).ToList();
    }

    public async Task<MessageRow?> RowAsync(Guid id, CancellationToken ct = default)
    {
        var m = await db.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return m is null ? null : (await RowsAsync([m], ct))[0];
    }

    /// <summary>Full message with its content and history. Viewing content is audited.</summary>
    public async Task<MessageDetail?> GetAsync(Guid id, string user, CancellationToken ct = default)
    {
        var m = await db.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return null;
        var i = await db.Interfaces.AsNoTracking().SingleAsync(x => x.Id == m.InterfaceId, ct);
        var events = await db.Events.AsNoTracking().Where(e => e.MessageId == id).OrderBy(e => e.At).ToListAsync(ct);
        var related = await db.Messages.AsNoTracking().Where(x => x.ParentId == id || x.Id == m.ParentId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        var content = ContentOf(m);
        if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))) != m.Sha256) content = "[The stored content does not match its fingerprint – it may have been altered. Tell the Privacy Officer.]";
        await audit.WriteAsync(user, "View", "Message", id.ToString(), m.PatientId?.ToString(), reason: $"{i.Code} {m.MessageType} {m.ControlId}".Trim(), ct: ct);
        return new MessageDetail((await RowsAsync([m], ct))[0], content, i.Format.ToString(), events, await RowsAsync(related, ct));
    }

    /// <summary>Records a copy of traffic from another module (HealthLink, SMS, email).</summary>
    public async Task RecordAsync(TappedMessage t, CancellationToken ct = default)
    {
        var i = await MonitoredAsync(t.System, ct);
        var m = NewMessage(i, t.Inbound ? InterfaceDirection.Inbound : InterfaceDirection.Outbound, t.Content, t.System, null, null);
        m.Status = t.Ok ? (t.Inbound ? MessageStatus.Processed : MessageStatus.Recorded) : MessageStatus.Failed;
        m.MessageType ??= t.MessageType is null ? null : Trunc(t.MessageType, 40);
        m.ControlId ??= t.ControlId is null ? null : Trunc(t.ControlId, 64);
        m.Reference = t.Reference is null ? m.Reference : Trunc(t.Reference, 64);
        m.PatientId = t.PatientId; m.CompletedAt = m.CreatedAt;
        if (!t.Ok) m.LastError = t.Detail is null ? null : Trunc(t.Detail, 500);
        db.Messages.Add(m);
        Event(m.Id, t.Ok ? (t.Inbound ? "Received" : "Sent") : "Failed", t.Detail);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ alerts
    public async Task<IReadOnlyList<AlertRow>> AlertsAsync(bool openOnly = true, CancellationToken ct = default)
    {
        var names = await db.Interfaces.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        return (await db.Alerts.AsNoTracking().Where(a => !openOnly || a.ClearedAt == null).OrderByDescending(a => a.RaisedAt).Take(200).ToListAsync(ct))
            .Select(a => new AlertRow(a.Id, a.InterfaceId, names.GetValueOrDefault(a.InterfaceId, "?"), a.Kind.ToString(), a.Detail, a.RaisedAt, a.NotifiedAt)).ToList();
    }

    /// <summary>Raises and clears alerts. <paramref name="listenerProblems"/> are listeners that could not start. Returns alerts not yet notified.</summary>
    public async Task<IReadOnlyList<AlertRow>> EvaluateAlertsAsync(IReadOnlyDictionary<Guid, string> listenerProblems, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var ifaces = await db.Interfaces.AsNoTracking().Where(i => i.IsActive).ToListAsync(ct);
        var open = await db.Alerts.Where(a => a.ClearedAt == null).ToListAsync(ct);
        foreach (var i in ifaces)
        {
            var recent = await db.Messages.AsNoTracking().Where(m => m.InterfaceId == i.Id && m.Status != MessageStatus.Queued && m.Status != MessageStatus.Sending && m.Status != MessageStatus.Received)
                .OrderByDescending(m => m.CreatedAt).Take(i.AlertAfterFailures).Select(m => new { m.Status, m.LastError, m.CreatedAt }).ToListAsync(ct);
            var failing = recent.Count >= i.AlertAfterFailures && recent.All(m => m.Status is MessageStatus.Failed or MessageStatus.DeadLetter or MessageStatus.Rejected);
            Toggle(open, i, AlertKind.Failures, failing, $"{i.Name}: the last {i.AlertAfterFailures} message(s) failed. Latest: {recent.FirstOrDefault()?.LastError}", now);

            var dead = await db.Messages.AsNoTracking().CountAsync(m => m.InterfaceId == i.Id && m.Status == MessageStatus.DeadLetter, ct);
            Toggle(open, i, AlertKind.DeadLetter, dead > 0, $"{i.Name}: {dead} message(s) gave up after all retries – check and resend.", now);

            if (i.SilentAlertHours is { } h && i.CreatedAt < now.AddHours(-h))
            {
                var last = await db.Messages.AsNoTracking().Where(m => m.InterfaceId == i.Id).MaxAsync(m => (DateTimeOffset?)m.CreatedAt, ct);
                Toggle(open, i, AlertKind.Silent, last is null || last < now.AddHours(-h), $"{i.Name}: nothing for {h} hour(s){(last is null ? "" : $" (last {last:dd MMM HH:mm} UTC)")}.", now);
            }
            Toggle(open, i, AlertKind.Stopped, listenerProblems.TryGetValue(i.Id, out var why), $"{i.Name} is not running: {why}", now);
        }
        foreach (var a in open.Where(a => ifaces.All(i => i.Id != a.InterfaceId))) { a.ClearedAt = now; a.ClearedBy = "system (interface off)"; }
        await db.SaveChangesAsync(ct);
        return (await AlertsAsync(true, ct)).Where(a => a.NotifiedAt is null).ToList();
    }

    private void Toggle(List<Alert> open, Interface i, AlertKind kind, bool on, string detail, DateTimeOffset now)
    {
        var a = open.FirstOrDefault(x => x.InterfaceId == i.Id && x.Kind == kind);
        if (on && a is null) { a = new Alert { InterfaceId = i.Id, Kind = kind, Detail = Trunc(detail, 500), RaisedAt = now }; db.Alerts.Add(a); open.Add(a); }
        else if (!on && a is not null) { a.ClearedAt = now; a.ClearedBy = "system (resolved)"; open.Remove(a); }
    }

    public async Task MarkNotifiedAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.ToList();
        var now = clock.GetUtcNow();
        await db.Alerts.Where(a => list.Contains(a.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.NotifiedAt, now), ct);
    }

    public async Task<string?> ClearAlertAsync(Guid id, string user, CancellationToken ct = default)
    {
        var a = await db.Alerts.SingleOrDefaultAsync(x => x.Id == id && x.ClearedAt == null, ct);
        if (a is null) return "Alert not found or already cleared.";
        a.ClearedAt = clock.GetUtcNow(); a.ClearedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Clear", "MessagingAlert", id.ToString(), reason: a.Detail, ct: ct);
        return null;
    }

    internal static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string FirstNonEmpty(params string[] v) => v.FirstOrDefault(x => x.Length > 0) ?? "";
}

/// <summary>Records other modules' traffic in the message store; never throws (recording must not stop a letter or SMS).</summary>
public sealed class MessageStoreTap(MessagingService svc, Microsoft.Extensions.Logging.ILogger<MessageStoreTap> log) : IMessageTap
{
    public async Task RecordAsync(TappedMessage message, CancellationToken ct = default)
    {
        try { await svc.RecordAsync(message, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(log, ex, "Could not record {System} message in the message store.", message.System); }
    }
}
