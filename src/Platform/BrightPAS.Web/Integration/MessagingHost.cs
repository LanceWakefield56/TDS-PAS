using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using FellowOakDicom;
using Microsoft.Data.SqlClient;
using FellowOakDicom.Network;
using BrightPAS.Licensing;
using BrightPAS.Messaging.Hl7;
using BrightPAS.Modules.Labs;
using BrightPAS.Modules.Messaging;
using BrightPAS.Web.Licensing;
using MsgFormat = BrightPAS.Modules.Messaging.MessageFormat;
using MsgStatus = BrightPAS.Modules.Messaging.MessageStatus;

namespace BrightPAS.Web.Integration;

/// <summary>Live state of each listener, shown on the messaging dashboard.</summary>
public sealed class MessagingRuntime
{
    public sealed record ListenerState(Guid InterfaceId, string Kind, int Port, bool Running, string? Problem, DateTimeOffset Since, int Connections, DateTimeOffset? LastActivity);
    private readonly ConcurrentDictionary<Guid, ListenerState> _states = new();
    public IReadOnlyDictionary<Guid, ListenerState> States => _states;
    public void Set(ListenerState s) => _states[s.InterfaceId] = s;
    public void Remove(Guid id) => _states.TryRemove(id, out _);
    public void Touch(Guid id, bool connected = false)
    {
        if (_states.TryGetValue(id, out var s)) _states[id] = s with { LastActivity = DateTimeOffset.UtcNow, Connections = s.Connections + (connected ? 1 : 0) };
    }
    public IReadOnlyDictionary<Guid, string> Problems() => _states.Values.Where(s => !s.Running && s.Problem is not null).ToDictionary(s => s.InterfaceId, s => s.Problem!);
}

/// <summary>Inbound processing: store → hand to a BrightPAS module (e.g. lab results) → acknowledge → run the flows.</summary>
public sealed class MessagingPipeline(MessagingService messages, FlowService flows, IServiceProvider sp, LicenceCache licence, TimeProvider clock, ILogger<MessagingPipeline> log)
{
    public sealed record InboundResult(Guid MessageId, MsgStatus Status, string? Ack, string? Error);

    public async Task<InboundResult> ReceiveAsync(Interface i, string content, string receivedBy, Guid? parentId = null, CancellationToken ct = default)
    {
        var m = await messages.ReceiveAsync(i, content, receivedBy, parentId, ct);
        return await ProcessAsync(i, m, content, ct);
    }

    public async Task<InboundResult> ProcessAsync(Interface i, Message m, string content, CancellationToken ct = default)
    {
        MsgStatus status;
        string? ack = null, error = null, ackCode = null;
        Guid? patientId = null;
        try
        {
            switch (i.Handler)
            {
                case InboundHandler.LabResults:
                    if (licence.Status.AccessTo(LabsModule.Id) != ModuleAccess.Full)
                    {
                        status = MsgStatus.Rejected; error = "Lab results module not licensed or read-only."; ack = TryAck(content, "AR", error); ackCode = "AR";
                        break;
                    }
                    var r = await sp.GetRequiredService<LabService>().IngestAsync(content, i.Transport.ToString(), $"{i.Code}:{m.CreatedBy}", ct);
                    status = r.Outcome == LabMessageOutcome.Rejected ? MsgStatus.Rejected : MsgStatus.Processed;
                    error = r.Error; ack = r.Ack.Length > 0 ? r.Ack : TryAck(content, "AR", r.Error ?? "Rejected");
                    ackCode = r.Outcome == LabMessageOutcome.Rejected ? "AR" : "AA";
                    if (r.Outcome == LabMessageOutcome.Duplicate) error = "Duplicate – already received (acknowledged again).";
                    break;
                case InboundHandler.DeviceObservations:
                case InboundHandler.AnaesthesiaRecord:
                    if (licence.Status.AccessTo(BrightPAS.Modules.Devices.DevicesModule.Id) != ModuleAccess.Full)
                    {
                        status = MsgStatus.Rejected; error = "Devices & SaferSleep module not licensed or read-only."; ack = TryAck(content, "AR", error); ackCode = "AR";
                        break;
                    }
                    if (i.Handler == InboundHandler.DeviceObservations)
                    {
                        // A file (upload or watched share) holds readings from while the gateway was down: file them by the time of each reading.
                        var fromFile = m.CreatedBy.StartsWith("file:", StringComparison.Ordinal) || m.CreatedBy.StartsWith("folder:", StringComparison.Ordinal);
                        var devices = sp.GetRequiredService<BrightPAS.Modules.Devices.DeviceService>();
                        var d = fromFile ? await devices.ImportAsync(content, ct) : await devices.IngestAsync(content, ct);
                        status = d.Accepted ? MsgStatus.Processed : MsgStatus.Rejected; error = d.Error; patientId = d.PatientId;
                    }
                    else
                    {
                        var a = await sp.GetRequiredService<BrightPAS.Modules.Devices.AnaesthesiaService>().IngestAsync(content, m.Id, $"{i.Code}:{m.CreatedBy}", ct);
                        status = a.Accepted ? MsgStatus.Processed : MsgStatus.Rejected; patientId = a.PatientId;
                        error = a.Error ?? (a.Duplicate ? "Duplicate – already received (acknowledged again)." : a.Accepted && !a.Filed ? "Received but not matched to an episode – file it under Devices › SaferSleep." : null);
                    }
                    ackCode = status == MsgStatus.Processed ? "AA" : "AR";
                    ack = TryAck(content, ackCode, status == MsgStatus.Processed ? null : error);
                    break;
                default:
                    var problems = MessageDocs.Validate(i.Format, content);
                    status = problems.Count == 0 ? MsgStatus.Processed : MsgStatus.Rejected;
                    error = problems.Count == 0 ? null : string.Join(" ", problems);
                    if (i.Format == MsgFormat.Hl7v2) { ackCode = status == MsgStatus.Processed ? "AA" : "AR"; ack = TryAck(content, ackCode, error); }
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Inbound message {Id} on {Code} failed.", m.Id, i.Code);
            status = MsgStatus.Failed; error = ex.Message; ackCode = "AE";
            if (i.Format == MsgFormat.Hl7v2) ack = TryAck(content, "AE", "Could not be processed – it has been kept and staff alerted.");
        }
        await messages.CompleteInboundAsync(m.Id, status, ackCode, null, error, patientId, ct);
        if (status == MsgStatus.Processed)
        {
            try { await flows.ProcessAsync(m, content, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Flows for message {Id} failed.", m.Id);
                await messages.AddEventAsync(m.Id, "FlowFailed", ex.Message, ct);
            }
        }
        return new(m.Id, status, ack, error);
    }

    private string? TryAck(string raw, string code, string? text)
    {
        try { return Hl7Message.Parse(raw).Ack(code, text, clock.GetLocalNow()); }
        catch (Hl7Exception) { return null; }
    }
}

/// <summary>Runs the engine: MLLP and DICOM listeners, folder watching, the outbound sender and alerts. Only while the Messaging module is licensed.</summary>
public sealed class MessagingHost(IServiceScopeFactory scopes, MessagingRuntime runtime, LicenceCache licence, IConfiguration config,
    IHttpClientFactory http, ILogger<MessagingHost> log) : BackgroundService
{
    private readonly Dictionary<Guid, (CancellationTokenSource Cts, string Signature)> _mllp = [];
    private readonly Dictionary<Guid, (IDicomServer Server, string Signature)> _dicom = [];
    private readonly Dictionary<Guid, DateTimeOffset> _nextPoll = [];
    private DateTimeOffset _nextReconcile, _nextFolders, _nextAlerts, _nextWorkflow;
    private bool _seeded;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (licence.Status.AccessTo(MessagingModule.Id) != ModuleAccess.Full) StopAll();
                else await CycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Messaging engine cycle failed; it will run again shortly."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        StopAll();
    }

    private async Task CycleAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var scope = scopes.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<MessagingService>();
        if (!_seeded) { await SeedFromConfigAsync(svc, ct); await svc.RequeueStuckAsync(ct); _seeded = true; }
        if (now >= _nextReconcile) { await ReconcileAsync(svc, ct); _nextReconcile = now.AddSeconds(15); }
        if (now >= _nextFolders) { await WatchFoldersAsync(svc, ct); _nextFolders = now.AddSeconds(15); }
        await PollSqlAsync(scope.ServiceProvider, svc, now, ct);
        await SendDueAsync(svc, ct);
        if (now >= _nextAlerts) { await AlertsAsync(scope.ServiceProvider, svc, ct); _nextAlerts = now.AddSeconds(60); }
        // 0.35.0 workflow: held messages whose wait is over carry on; overdue tasks escalate.
        if (now >= _nextWorkflow)
        {
            await scope.ServiceProvider.GetRequiredService<FlowService>().ResumeDueAsync(ct);
            foreach (var t in await scope.ServiceProvider.GetRequiredService<WorkflowService>().EscalateAsync(ct))
                log.LogInformation("Task {Number} escalated from {From} to {To}.", t.Number, t.EscalatedFrom, t.Role);
            _nextWorkflow = now.AddSeconds(30);
        }
    }

    /// <summary>0.12.0 upgrade: the lab MLLP port from appsettings becomes an engine interface (labs-in) so it shows on the dashboard.</summary>
    private async Task SeedFromConfigAsync(MessagingService svc, CancellationToken ct)
    {
        var port = config.GetValue("Labs:Mllp:Port", 0);
        if (port <= 0 || (await svc.ActiveAsync(Transport.MllpListen)).Any(i => i.Port == port) || await svc.InterfaceByCodeAsync("labs-in", ct) is not null) return;
        var clients = (config["Labs:Mllp:AllowedClients"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(a => new ClientEdit(a, null, "From appsettings")).ToList();
        var (_, errors) = await svc.SaveInterfaceAsync(new InterfaceEdit(null, "labs-in", "Lab results (MLLP)", InterfaceDirection.Inbound, Transport.MllpListen, MsgFormat.Hl7v2,
            InboundHandler.LabResults, null, port, null, null, null, null, clients, clients.Count > 0, 3, null,
            clients.Count > 0 ? "Created from Labs:Mllp settings at upgrade." : "Created from Labs:Mllp settings at upgrade – SWITCHED OFF because no allowed laboratory IP address was set. Add it and switch on."), "system", ct);
        if (errors.Count > 0) log.LogWarning("Could not create the labs-in interface from settings: {Errors}", string.Join(" ", errors));
    }

    // ------------------------------------------------------------------ listeners
    private async Task ReconcileAsync(MessagingService svc, CancellationToken ct)
    {
        var mllp = await svc.ActiveAsync(Transport.MllpListen);
        var dicom = await svc.ActiveAsync(Transport.DicomListen);
        foreach (var id in _mllp.Keys.Except(mllp.Select(i => i.Id)).ToList()) StopMllp(id);
        foreach (var id in _dicom.Keys.Except(dicom.Select(i => i.Id)).ToList()) StopDicom(id);
        foreach (var i in mllp)
        {
            var clients = await svc.AllowedClientsAsync(i.Id, ct);
            var sig = $"{i.Port}|{string.Join(',', clients.Select(c => c.Address).Order())}|{i.Handler}|{i.UseTls}|{i.CertificateThumbprint}|{i.PeerThumbprint}";
            if (_mllp.TryGetValue(i.Id, out var cur) && cur.Signature == sig) continue;
            StopMllp(i.Id);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _mllp[i.Id] = (cts, sig);
            _ = Task.Run(() => ListenMllpAsync(i, clients.Select(c => IPAddress.Parse(c.Address)).ToHashSet(), cts.Token), CancellationToken.None);
        }
        foreach (var i in dicom)
        {
            var clients = await svc.AllowedClientsAsync(i.Id, ct);
            var sig = $"{i.Port}|{i.AeTitle}|{string.Join(',', clients.Select(c => c.Address + "/" + c.AeTitle).Order())}|{i.FolderPath}|{i.UseTls}|{i.CertificateThumbprint}";
            if (_dicom.TryGetValue(i.Id, out var cur) && cur.Signature == sig) continue;
            StopDicom(i.Id);
            try
            {
                var ctx = new DicomScpContext(i, clients.Select(c => (IPAddress.Parse(c.Address), c.AeTitle ?? "")).ToList(), scopes, runtime, log);
                FellowOakDicom.Network.Tls.ITlsAcceptor? tls = null;
                if (i.UseTls)
                    tls = new FellowOakDicom.Network.Tls.DefaultTlsAcceptor(TlsSupport.Find(i.CertificateThumbprint) ?? throw new InvalidOperationException("BrightPAS's TLS certificate was not found in the certificate store."))
                    {
                        Protocols = TlsSupport.Protocols,
                        RequireMutualAuthentication = TlsSupport.Normalise(i.PeerThumbprint) is not null,
                        CertificateValidationCallback = (_, cert, _, errors) => TlsSupport.Normalise(i.PeerThumbprint) is not { } pin
                            || (cert is not null && string.Equals(new System.Security.Cryptography.X509Certificates.X509Certificate2(cert).Thumbprint, pin, StringComparison.OrdinalIgnoreCase)),
                    };
                var server = DicomServerFactory.Create<PasDicomScp>(i.Port!.Value, tls, userState: ctx);
                if (server.Exception is { } ex) throw ex;
                _dicom[i.Id] = (server, sig);
                runtime.Set(new(i.Id, "DICOM", i.Port.Value, true, null, DateTimeOffset.UtcNow, 0, null));
                log.LogInformation("DICOM node {Ae} listening on port {Port} for {Code}.", i.AeTitle, i.Port, i.Code);
            }
            catch (Exception ex) when (ex is SocketException or DicomNetworkException or InvalidOperationException)
            {
                runtime.Set(new(i.Id, "DICOM", i.Port ?? 0, false, $"Could not listen on port {i.Port}: {ex.Message}", DateTimeOffset.UtcNow, 0, null));
                log.LogError(ex, "DICOM listener for {Code} could not start.", i.Code);
            }
        }
    }

    private async Task ListenMllpAsync(Interface i, HashSet<IPAddress> allowed, CancellationToken ct)
    {
        TcpListener? listener = null;
        try
        {
            // 0.35.0: TLS listener – our certificate from the Windows store; refuse to start without it rather than fall back to plain text.
            var own = i.UseTls ? TlsSupport.Find(i.CertificateThumbprint) : null;
            if (i.UseTls && own is null)
            {
                runtime.Set(new(i.Id, "MLLP", i.Port ?? 0, false, "TLS is on but BrightPAS's certificate was not found in the certificate store.", DateTimeOffset.UtcNow, 0, null));
                return;
            }
            listener = new TcpListener(IPAddress.Any, i.Port!.Value);
            listener.Start();
            runtime.Set(new(i.Id, "MLLP", i.Port.Value, true, null, DateTimeOffset.UtcNow, 0, null));
            log.LogInformation("MLLP listener for {Code} on port {Port} (allowed: {Allowed}).", i.Code, i.Port, allowed.Count == 0 ? "any" : string.Join(", ", allowed));
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                var ip = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4();
                // Fail closed: a listener that files clinical data and has no allowed list accepts nobody.
                if ((allowed.Count > 0 || i.Handler != InboundHandler.None) && (ip is null || !allowed.Contains(ip)))
                {
                    log.LogWarning("Refused MLLP connection to {Code} from {Ip} (not on the allowed list).", i.Code, ip);
                    client.Dispose();
                    continue;
                }
                runtime.Touch(i.Id, connected: true);
                _ = Task.Run(() => HandleMllpAsync(i, client, ip?.ToString() ?? "?", own, ct), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException ex)
        {
            runtime.Set(new(i.Id, "MLLP", i.Port ?? 0, false, $"Could not listen on port {i.Port}: {ex.Message}", DateTimeOffset.UtcNow, 0, null));
            log.LogError(ex, "MLLP listener for {Code} stopped.", i.Code);
        }
        finally { listener?.Stop(); }
    }

    private async Task HandleMllpAsync(Interface i, TcpClient client, string ip, System.Security.Cryptography.X509Certificates.X509Certificate2? own, CancellationToken ct)
    {
        using var _ = client;
        Stream net = client.GetStream();
        if (own is not null)
        {
            try
            {
                using var hs = CancellationTokenSource.CreateLinkedTokenSource(ct);
                hs.CancelAfter(TimeSpan.FromSeconds(30));
                net = await TlsSupport.AcceptAsync(net, own, i.PeerThumbprint, hs.Token);
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException)
            {
                log.LogWarning("TLS handshake for {Code} from {Ip} failed: {Reason}", i.Code, ip, ex.Message);
                await net.DisposeAsync();
                return;
            }
        }
        await using var stream = new BufferedStream(net);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                idle.CancelAfter(TimeSpan.FromMinutes(5));
                var raw = await Mllp.ReadAsync(stream, MessagingService.MaxContentBytes, idle.Token);
                if (raw is null) return;
                runtime.Touch(i.Id);
                await using var scope = scopes.CreateAsyncScope();
                var r = await scope.ServiceProvider.GetRequiredService<MessagingPipeline>().ReceiveAsync(i, raw, $"mllp:{ip}", null, ct);
                if (r.Ack is { Length: > 0 } ack) { await stream.WriteAsync(Mllp.Frame(ack), ct); await stream.FlushAsync(ct); }
            }
        }
        catch (Exception ex) when (ex is IOException or Hl7Exception or OperationCanceledException or SocketException or InvalidOperationException)
        {
            log.LogInformation("MLLP connection to {Code} from {Ip} closed: {Reason}", i.Code, ip, ex.Message);
        }
    }

    private void StopMllp(Guid id)
    {
        if (_mllp.Remove(id, out var x)) { x.Cts.Cancel(); x.Cts.Dispose(); }
        runtime.Remove(id);
    }

    private void StopDicom(Guid id)
    {
        if (_dicom.Remove(id, out var x)) { x.Server.Stop(); x.Server.Dispose(); }
        runtime.Remove(id);
    }

    private void StopAll()
    {
        foreach (var id in _mllp.Keys.ToList()) StopMllp(id);
        foreach (var id in _dicom.Keys.ToList()) StopDicom(id);
    }

    // ------------------------------------------------------------------ folders
    private static readonly Dictionary<MsgFormat, string[]> Extensions = new()
    {
        [MsgFormat.Hl7v2] = [".hl7", ".txt", ".msg"],
        [MsgFormat.Json] = [".json"],
        [MsgFormat.Fhir] = [".json"],
        [MsgFormat.Xml] = [".xml", ".cda"],
        [MsgFormat.X12] = [".x12", ".edi", ".txt"],
        [MsgFormat.Dicom] = [".dcm", ".dicom", ""],
        [MsgFormat.Text] = [".txt"],
    };

    private async Task WatchFoldersAsync(MessagingService svc, CancellationToken ct)
    {
        foreach (var i in await svc.ActiveAsync(Transport.FolderWatch))
        {
            if (i.FolderPath is not { } dir || !Directory.Exists(dir)) { runtime.Set(new(i.Id, "Folder", 0, false, $"Folder {i.FolderPath} not found or not readable.", DateTimeOffset.UtcNow, 0, null)); continue; }
            runtime.Set(new(i.Id, "Folder", 0, true, null, DateTimeOffset.UtcNow, 0, runtime.States.GetValueOrDefault(i.Id)?.LastActivity));
            var exts = Extensions[i.Format];
            foreach (var file in Directory.EnumerateFiles(dir).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).Take(50))
            {
                string content;
                try
                {
                    var bytes = await File.ReadAllBytesAsync(file, ct);
                    if (bytes.Length > MessagingService.MaxContentBytes * 20) throw new InvalidOperationException("File too large.");
                    content = i.Format == MsgFormat.Dicom ? DicomConvert.ToJson(bytes) : Encoding.UTF8.GetString(bytes);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MappingException or InvalidOperationException)
                {
                    log.LogWarning("Could not read {File} for {Code}: {Reason}", file, i.Code, ex.Message);
                    MoveTo(file, "failed");
                    continue;
                }
                await using var scope = scopes.CreateAsyncScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<MessagingPipeline>();
                // An HL7 file written by a device or exported to a share can hold many messages (0.18.0): each is received on its own.
                var parts = i.Format == MsgFormat.Hl7v2 ? BrightPAS.Messaging.MessageFile.SplitHl7(content) : [content];
                var allOk = parts.Count > 0;
                foreach (var part in parts)
                {
                    var r = await pipeline.ReceiveAsync(i, part, $"folder:{Path.GetFileName(file)}", null, ct);
                    allOk &= r.Status is MsgStatus.Processed;
                }
                MoveTo(file, allOk ? "processed" : "failed");
                runtime.Touch(i.Id);
            }
        }
    }

    private static void MoveTo(string file, string sub)
    {
        var target = Path.Combine(Path.GetDirectoryName(file)!, sub);
        Directory.CreateDirectory(target);
        var dest = Path.Combine(target, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Path.GetFileName(file)}");
        File.Move(file, dest, overwrite: true);
    }

    // ------------------------------------------------------------------ SQL connector
    private async Task PollSqlAsync(IServiceProvider sp, MessagingService svc, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var i in await svc.ActiveAsync(Transport.SqlQuery))
        {
            if (_nextPoll.TryGetValue(i.Id, out var next) && next > now) continue;
            _nextPoll[i.Id] = now.AddSeconds(i.PollSeconds ?? 300);
            var state = await svc.StateAsync(i.Id, ct);
            var watermark = state?.Watermark;
            var rows = 0;
            try
            {
                var pipeline = sp.GetRequiredService<MessagingPipeline>();
                (rows, watermark) = await SqlConnector.ReadAsync(svc.SecretOf(i)!, i.SqlText!, watermark, i.WatermarkColumn, 500,
                    row => pipeline.ReceiveAsync(i, row, $"sql:{i.Code}", null, ct), ct);
                await svc.SaveStateAsync(i.Id, watermark, rows, null, ct);
                runtime.Set(new(i.Id, "SQL", 0, true, null, DateTimeOffset.UtcNow, 0, rows > 0 ? DateTimeOffset.UtcNow : runtime.States.GetValueOrDefault(i.Id)?.LastActivity));
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
            {
                await svc.SaveStateAsync(i.Id, watermark, rows, ex.Message, ct);
                runtime.Set(new(i.Id, "SQL", 0, false, $"Database query failed: {ex.Message}", DateTimeOffset.UtcNow, 0, null));
                log.LogWarning("SQL interface {Code} failed: {Error}", i.Code, ex.Message);
            }
        }
    }

    private static readonly int[] PermanentSqlErrors = [102, 207, 208, 229, 245, 547, 2601, 2627, 8152, 515];

    // ------------------------------------------------------------------ sending
    private async Task SendDueAsync(MessagingService svc, CancellationToken ct)
    {
        foreach (var (m, i) in await svc.TakeDueAsync(20, ct))
        {
            var content = svc.ContentOf(m);
            try
            {
                switch (i.Transport)
                {
                    case Transport.MllpSend:
                        {
                            var (code, text) = await SendMllpAsync(i, content, ct);
                            await svc.SentAsync(m.Id, code, text, ct);
                            break;
                        }
                    case Transport.FolderWrite:
                        {
                            if (i.FolderPath is not { } dir || !Directory.Exists(dir)) { await svc.FailedAsync(m.Id, $"Folder {i.FolderPath} not found.", false, ct); break; }
                            var ext = Extensions[i.Format][0] is { Length: > 0 } e ? e : ".dcm";
                            var name = $"{(m.ControlId ?? m.Id.ToString("N"))}".Replace('/', '_').Replace('\\', '_');
                            var tmp = Path.Combine(dir, name + ".tmp");
                            if (i.Format == MsgFormat.Dicom) await File.WriteAllBytesAsync(tmp, DicomConvert.ToFile(content), ct);
                            else await File.WriteAllTextAsync(tmp, content, new UTF8Encoding(false), ct);
                            // Never overwrite a file the other system has not collected yet (control IDs can repeat, e.g. after a database rebuild): add a suffix.
                            var dest = Path.Combine(dir, name + ext);
                            if (File.Exists(dest)) dest = Path.Combine(dir, $"{name}-{m.Id.ToString("N")[..8]}{ext}");
                            File.Move(tmp, dest, overwrite: false);
                            await svc.SentAsync(m.Id, null, null, ct);
                            break;
                        }
                    case Transport.HttpPost:
                        {
                            var client = http.CreateClient("messaging");
                            using var req = new HttpRequestMessage(HttpMethod.Post, i.Url) { Content = new StringContent(content, Encoding.UTF8, MediaType(i.Format)) };
                            if (svc.SecretOf(i) is { Length: > 0 } auth) req.Headers.TryAddWithoutValidation("Authorization", auth);
                            using var res = await client.SendAsync(req, ct);
                            var code = (int)res.StatusCode;
                            if (res.IsSuccessStatusCode) await svc.SentAsync(m.Id, null, $"HTTP {code}", ct);
                            else await svc.FailedAsync(m.Id, $"HTTP {code} {res.ReasonPhrase}", code is >= 400 and < 500 and not 408 and not 429, ct);
                            break;
                        }
                    case Transport.SqlWrite:
                        {
                            try { await svc.SentAsync(m.Id, null, $"{await SqlConnector.WriteAsync(svc.SecretOf(i)!, i.SqlText!, content, ct)} row(s) written", ct); }
                            catch (SqlException ex) { await svc.FailedAsync(m.Id, $"Database: {ex.Message}", PermanentSqlErrors.Contains(ex.Number), ct); }
                            catch (System.Text.Json.JsonException ex) { await svc.FailedAsync(m.Id, $"Not valid JSON: {ex.Message}", true, ct); }
                            break;
                        }
                    default:
                        await svc.FailedAsync(m.Id, $"{i.Transport} interfaces cannot send.", true, ct);
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or HttpRequestException or TaskCanceledException or MappingException or UnauthorizedAccessException or InvalidOperationException)
            {
                await svc.FailedAsync(m.Id, ex.Message, ex is MappingException, ct);
            }
        }
    }

    public static string MediaType(MsgFormat f) => f switch
    {
        MsgFormat.Hl7v2 => "application/hl7-v2",
        MsgFormat.Fhir => "application/fhir+json",
        MsgFormat.Json => "application/json",
        MsgFormat.Xml => "application/xml",
        MsgFormat.X12 => "application/edi-x12",
        MsgFormat.Dicom => "application/dicom+json",
        _ => "text/plain",
    };

    public static async Task<(string? Code, string? Text)> SendMllpAsync(Interface i, string content, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await client.ConnectAsync(i.Host!, i.Port!.Value, timeout.Token);
        Stream stream = client.GetStream();
        if (i.UseTls) stream = await TlsSupport.ConnectAsync(stream, i.Host!, TlsSupport.Find(i.CertificateThumbprint), i.PeerThumbprint, timeout.Token);
        await using var _ = stream;
        await stream.WriteAsync(Mllp.Frame(content), timeout.Token);
        var ack = await Mllp.ReadAsync(stream, 1_000_000, timeout.Token) ?? throw new IOException("The receiver closed the connection without an acknowledgement.");
        var msa = Hl7Message.Parse(ack).First("MSA");
        return (msa?.Get(1), msa?.Get(3));
    }

    // ------------------------------------------------------------------ alerts
    private async Task AlertsAsync(IServiceProvider sp, MessagingService svc, CancellationToken ct)
    {
        var fresh = await svc.EvaluateAlertsAsync(runtime.Problems(), ct);
        if (fresh.Count == 0) return;
        var settings = await svc.SettingsAsync(ct);
        foreach (var a in fresh) log.LogWarning("Messaging alert: {Detail}", a.Detail);
        if (settings.AlertEmail is { } to && licence.Status.AccessTo(BrightPAS.Modules.Reminders.RemindersModule.Id) != ModuleAccess.None)
        {
            var rs = await sp.GetRequiredService<BrightPAS.Modules.Reminders.ReminderService>().SettingsAsync(ct);
            var gw = sp.GetServices<BrightPAS.Modules.Reminders.IEmailGateway>().FirstOrDefault(g => g.Provider == rs.EmailProvider);
            if (gw is not null && gw.ConfigurationProblem(rs) is null)
            {
                var body = "BrightPAS messaging engine alerts (no patient details are included):\n\n" + string.Join("\n", fresh.Select(a => $"- {a.RaisedAt:dd MMM HH:mm} UTC  {a.Detail}")) +
                           "\n\nOpen BrightPAS › Messaging to see the details.";
                var r = await gw.SendAsync(rs, to, $"BrightPAS interface alert ({fresh.Count})", body, ct);
                if (!r.Ok) log.LogWarning("Could not email the messaging alert: {Error}", r.Error);
            }
        }
        await svc.MarkNotifiedAsync(fresh.Select(a => a.Id), ct);
    }
}

/// <summary>The SQL connector: read rows as JSON (with a watermark) and write a JSON message with parameters only.</summary>
public static class SqlConnector
{
    public static async Task<(int Rows, string? Watermark)> ReadAsync(string connectionString, string sql, string? watermark, string? watermarkColumn, int max,
        Func<string, Task> onRow, CancellationToken ct)
    {
        var rows = 0;
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        if (SqlSafety.Parameters(sql).Contains("watermark", StringComparer.OrdinalIgnoreCase)) cmd.Parameters.AddWithValue("@watermark", (object?)watermark ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (rows < max && await reader.ReadAsync(ct))
        {
            var row = new JsonObject();
            for (var f = 0; f < reader.FieldCount; f++)
            {
                var v = reader.IsDBNull(f) ? null : reader.GetValue(f);
                var name = reader.GetName(f);
                row[name] = v switch
                {
                    null => null,
                    DateTime d => JsonValue.Create(d.ToString("o")),
                    DateTimeOffset o => JsonValue.Create(o.ToString("o")),
                    byte[] => null,
                    bool b => JsonValue.Create(b),
                    int n => JsonValue.Create(n),
                    long n => JsonValue.Create(n),
                    decimal n => JsonValue.Create(n),
                    double n => JsonValue.Create(n),
                    _ => JsonValue.Create(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)),
                };
                if (watermarkColumn is { } wc && string.Equals(name, wc, StringComparison.OrdinalIgnoreCase) && v is not null)
                    watermark = v is DateTime dt ? dt.ToString("yyyy-MM-ddTHH:mm:ss.fffffff") : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            }
            await onRow(row.ToJsonString());
            rows++;
        }
        return (rows, watermark);
    }

    public static async Task<int> WriteAsync(string connectionString, string sql, string content, CancellationToken ct)
    {
        var json = JsonNode.Parse(content) as JsonObject ?? throw new MappingException("A database write needs a JSON object (one row) – add a mapper that produces one.");
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        await using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = 60 };
        foreach (var p in SqlSafety.Parameters(sql))
        {
            var node = json.FirstOrDefault(kv => string.Equals(kv.Key, p, StringComparison.OrdinalIgnoreCase)).Value;
            object value = node switch
            {
                null => DBNull.Value,
                JsonValue v when v.TryGetValue<bool>(out var b) => b,
                JsonValue v when v.TryGetValue<decimal>(out var d) => d,
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                _ => node.ToJsonString(),
            };
            cmd.Parameters.AddWithValue("@" + p, value);
        }
        var n = await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return n;
    }
}

/// <summary>What a DICOM listener needs: its interface, registered devices and a way into the pipeline.</summary>
public sealed record DicomScpContext(Interface Interface, IReadOnlyList<(IPAddress Ip, string AeTitle)> Devices, IServiceScopeFactory Scopes, MessagingRuntime Runtime, ILogger Log);

/// <summary>
/// BrightPAS as a DICOM node: Verification (C-ECHO) and Storage (C-STORE) SCP. Devices must be registered by IP address AND AE title.
/// Headers go to the message store (no pixel data); the original files are kept in the interface's archive folder when one is set.
/// </summary>
public sealed class PasDicomScp(INetworkStream stream, Encoding fallbackEncoding, Microsoft.Extensions.Logging.ILogger log, DicomServiceDependencies dependencies)
    : DicomService(stream, fallbackEncoding, log, dependencies), IDicomServiceProvider, IDicomCStoreProvider, IDicomCEchoProvider
{
    private DicomScpContext Ctx => (DicomScpContext)UserState;

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        var ctx = Ctx;
        var ip = IPAddress.TryParse(association.RemoteHost, out var a) ? a.MapToIPv4() : null;
        if (!string.Equals(association.CalledAE?.Trim(), ctx.Interface.AeTitle, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Log.LogWarning("DICOM {Code}: refused association called {Called} (expected {Ae}).", ctx.Interface.Code, association.CalledAE, ctx.Interface.AeTitle);
            return SendAssociationRejectAsync(DicomRejectResult.Permanent, DicomRejectSource.ServiceUser, DicomRejectReason.CalledAENotRecognized);
        }
        if (!ctx.Devices.Any(d => d.Ip.Equals(ip) && string.Equals(d.AeTitle, association.CallingAE?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ctx.Log.LogWarning("DICOM {Code}: refused unregistered device {Calling} at {Ip}.", ctx.Interface.Code, association.CallingAE, ip);
            return SendAssociationRejectAsync(DicomRejectResult.Permanent, DicomRejectSource.ServiceUser, DicomRejectReason.CallingAENotRecognized);
        }
        foreach (var pc in association.PresentationContexts)
        {
            if (pc.AbstractSyntax == DicomUID.Verification) pc.AcceptTransferSyntaxes(DicomTransferSyntax.ExplicitVRLittleEndian, DicomTransferSyntax.ImplicitVRLittleEndian);
            else if (pc.AbstractSyntax.StorageCategory != DicomStorageCategory.None) pc.AcceptTransferSyntaxes(pc.GetTransferSyntaxes().ToArray());
        }
        ctx.Runtime.Touch(ctx.Interface.Id, connected: true);
        return SendAssociationAcceptAsync(association);
    }

    public Task OnReceiveAssociationReleaseRequestAsync() => SendAssociationReleaseResponseAsync();
    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason) { }
    public void OnConnectionClosed(Exception exception) { }
    public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request) => Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));
    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e) { Ctx.Log.LogWarning(e, "DICOM store failed."); return Task.CompletedTask; }

    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        var ctx = Ctx;
        try
        {
            if (ctx.Interface.FolderPath is { } archive && Directory.Exists(archive))
                await new DicomFile(request.Dataset).SaveAsync(Path.Combine(archive, $"{request.SOPInstanceUID.UID}.dcm"));
            var json = DicomConvert.ToJson(request.Dataset);
            await using var scope = ctx.Scopes.CreateAsyncScope();
            var r = await scope.ServiceProvider.GetRequiredService<MessagingPipeline>().ReceiveAsync(ctx.Interface, json, $"dicom:{Association.CallingAE}@{Association.RemoteHost}");
            ctx.Runtime.Touch(ctx.Interface.Id);
            return new DicomCStoreResponse(request, r.Status is MsgStatus.Processed ? DicomStatus.Success : DicomStatus.ProcessingFailure);
        }
        catch (Exception ex) when (ex is IOException or MappingException or InvalidOperationException or UnauthorizedAccessException)
        {
            ctx.Log.LogError(ex, "DICOM {Code}: could not store {Uid}.", ctx.Interface.Code, request.SOPInstanceUID.UID);
            return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure);
        }
    }
}
