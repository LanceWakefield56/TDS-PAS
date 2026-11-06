using System.Net;
using System.Net.Sockets;
using BrightPAS.Licensing;
using BrightPAS.Messaging.Hl7;
using BrightPAS.Modules.Labs;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>
/// Receives HL7 lab results over MLLP (TCP). Off unless Labs:Mllp:Port is set.
/// Labs:Mllp:AllowedClients limits which IP addresses may connect (comma-separated; strongly recommended).
/// Run it on the internal network/VPN only; MLLP over TLS is on the backlog.
/// </summary>
public sealed class MllpListener(IServiceScopeFactory scopes, IConfiguration config, LicenceCache licence, ILogger<MllpListener> log) : BackgroundService
{
    private const int MaxMessageBytes = LabService.MaxMessageLength;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = config.GetValue("Labs:Mllp:Port", 0);
        if (port <= 0) return;
        var allowed = (config["Labs:Mllp:AllowedClients"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(IPAddress.Parse).ToHashSet();
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        log.LogInformation("HL7 MLLP listener on port {Port} (allowed clients: {Allowed}).", port, allowed.Count == 0 ? "any" : string.Join(", ", allowed));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                var ip = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4();
                if (allowed.Count > 0 && (ip is null || !allowed.Contains(ip)))
                {
                    log.LogWarning("Refused MLLP connection from {Ip}.", ip);
                    client.Dispose();
                    continue;
                }
                _ = Task.Run(() => HandleAsync(client, ip?.ToString() ?? "?", stoppingToken), stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private async Task HandleAsync(TcpClient client, string ip, CancellationToken ct)
    {
        using var _ = client;
        client.ReceiveTimeout = 300_000;
        await using var stream = new BufferedStream(client.GetStream());
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                idle.CancelAfter(TimeSpan.FromMinutes(5));
                var raw = await Mllp.ReadAsync(stream, MaxMessageBytes, idle.Token);
                if (raw is null) return;
                string ack;
                if (licence.Status.AccessTo(LabsModule.Id) != ModuleAccess.Full)
                {
                    ack = TryAck(raw, "AR", "Lab results module not licensed or read-only on this system");
                }
                else
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var r = await scope.ServiceProvider.GetRequiredService<LabService>().IngestAsync(raw, "MLLP", $"mllp:{ip}", ct);
                    ack = r.Ack.Length > 0 ? r.Ack : TryAck(raw, "AR", r.Error ?? "Rejected");
                    log.LogInformation("MLLP message from {Ip}: {Outcome}.", ip, r.Outcome);
                }
                if (ack.Length > 0)
                {
                    await stream.WriteAsync(Mllp.Frame(ack), ct);
                    await stream.FlushAsync(ct);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or Hl7Exception or OperationCanceledException or SocketException)
        {
            log.LogInformation("MLLP connection from {Ip} closed: {Reason}", ip, ex.Message);
        }
    }

    private static string TryAck(string raw, string code, string text)
    {
        try { return Hl7Message.Parse(raw).Ack(code, text, DateTimeOffset.Now); }
        catch (Hl7Exception) { return ""; }
    }
}
