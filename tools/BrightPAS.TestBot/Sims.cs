using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BrightPAS.TestBot;

public sealed record Sms(string To, string Text, string Id, DateTimeOffset At, string From = "TDS");
public sealed record Email(string From, string To, string Subject, string Body, DateTimeOffset At);
public sealed record Label(string Zpl, int Port, DateTimeOffset At);
public sealed record HlFile(string Name, string Message, string Problems, DateTimeOffset At);

/// <summary>
/// The virtual devices and outside systems: an SMS company (Modica-compatible) with virtual phones, an email server with inboxes,
/// Zebra network label printers, a HealthLink messaging client (folder drop with acknowledgements) and a laboratory (MLLP sender).
/// </summary>
public sealed class Sims : IAsyncDisposable
{
    private readonly Options _o;
    private readonly Report _r;
    private readonly CancellationTokenSource _stop = new();
    private WebApplication? _http;
    private TcpListener? _smtp, _zebra;
    private readonly List<Task> _loops = [];
    public X509Certificate2? Certificate { get; private set; }
    public bool CertificateTrusted { get; private set; }
    public ConcurrentQueue<Sms> SmsOut { get; } = new();
    public ConcurrentQueue<Email> Mail { get; } = new();
    public ConcurrentQueue<Label> Labels { get; } = new();
    public ConcurrentQueue<HlFile> HealthLink { get; } = new();
    public string ModicaApp => "tds-testbot";
    public string ModicaPassword { get; } = "Sim-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
    public string ModicaUrl => $"https://localhost:{_o.ModicaPort}/rest/gateway";
    public string HealthLinkOut => Path.Combine(_o.WorkFolder, "HealthLink", "Out");
    public string HealthLinkIn => Path.Combine(_o.WorkFolder, "HealthLink", "In");
    /// <summary>Where the SMS company posts delivery receipts and patient replies (BrightPAS callback URLs).</summary>
    public string? ReceiptUrl { get; set; }
    public string? ReplyUrl { get; set; }
    public Func<string, Task<string?>>? RenderLabel { get; set; }
    private int _smsId = 1000;

    public Sims(Options o, Report r) { _o = o; _r = r; }

    public async Task StartAsync()
    {
        Directory.CreateDirectory(_o.WorkFolder);
        Certificate = LoadOrCreateCertificate(Path.Combine(_o.WorkFolder, "simulator-certificate.pfx"));
        if (_o.TrustCertificate) CertificateTrusted = TrustCertificate(Certificate, true);

        // Virtual SMS company (Modica REST API shape).
        var b = WebApplication.CreateSlimBuilder();
        b.Logging.ClearProviders();
        b.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, _o.ModicaPort, l => l.UseHttps(Certificate)));
        var app = b.Build();
        app.MapPost("/rest/gateway/messages", async (HttpRequest req) =>
        {
            var auth = req.Headers.Authorization.ToString();
            var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ModicaApp}:{ModicaPassword}"));
            if (auth != expected) return Results.Json(new { error = "Unauthorised" }, statusCode: 401);
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var to = doc.RootElement.TryGetProperty("destination", out var d) ? d.GetString() ?? "" : "";
            var text = doc.RootElement.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
            if (!to.StartsWith("+64", StringComparison.Ordinal) || text.Length == 0) return Results.Json(new { error = "destination and content required" }, statusCode: 400);
            var id = "SIM" + Interlocked.Increment(ref _smsId);
            var sms = new Sms(to, text, id, DateTimeOffset.Now, doc.RootElement.TryGetProperty("mask", out var m) ? m.GetString() ?? "TDS" : "TDS");
            SmsOut.Enqueue(sms);
            _r.Device("Virtual phones (SMS)", $"To {to} from {sms.From}", text);
            _ = Task.Run(() => PostReceiptAsync(id));
            return Results.Json(id, statusCode: 201);
        });
        await app.StartAsync(_stop.Token);
        _http = app;

        // Virtual email server.
        _smtp = new TcpListener(IPAddress.Loopback, _o.SmtpPort); _smtp.Start();
        _loops.Add(Task.Run(() => AcceptLoop(_smtp, HandleSmtpAsync)));
        // Virtual Zebra printer.
        _zebra = new TcpListener(IPAddress.Loopback, _o.ZebraPort); _zebra.Start();
        _loops.Add(Task.Run(() => AcceptLoop(_zebra, HandleZebraAsync)));
        // Virtual HealthLink client.
        Directory.CreateDirectory(HealthLinkOut); Directory.CreateDirectory(HealthLinkIn);
        GrantAppPool(Path.Combine(_o.WorkFolder, "HealthLink"));
        _loops.Add(Task.Run(HealthLinkLoop));
    }

    private static X509Certificate2 LoadOrCreateCertificate(string file)
    {
        if (File.Exists(file))
        {
            var existing = X509CertificateLoader.LoadPkcs12FromFile(file, "testbot", X509KeyStorageFlags.Exportable);
            if (existing.NotAfter > DateTime.Now.AddDays(30)) return existing;
        }
        using var key = RSA.Create(2048);
        var req = new CertificateRequest("CN=BrightPAS TestBot simulator (testing only)", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback); san.AddDnsName(Environment.MachineName);
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(2));
        File.WriteAllBytes(file, cert.Export(X509ContentType.Pkcs12, "testbot"));
        return X509CertificateLoader.LoadPkcs12FromFile(file, "testbot", X509KeyStorageFlags.Exportable);
    }

    /// <summary>The server must trust the virtual SMS company's certificate, as it trusts the real Modica's. Removed again when the robot finishes.</summary>
    private bool TrustCertificate(X509Certificate2 cert, bool add)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                using var pub = X509CertificateLoader.LoadCertificate(cert.Export(X509ContentType.Cert));
                if (add) store.Add(pub); else store.Remove(pub);
                return true;
            }
            if (OperatingSystem.IsLinux())
            {
                const string path = "/usr/local/share/ca-certificates/brightpas-testbot.crt";
                if (add) File.WriteAllText(path, cert.ExportCertificatePem());
                else if (File.Exists(path)) File.Delete(path);
                using var p = Process.Start(new ProcessStartInfo("update-ca-certificates") { RedirectStandardOutput = true, RedirectStandardError = true });
                p?.WaitForExit(30000);
                return p?.ExitCode == 0;
            }
        }
        catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
        {
            _r.Add("devices", "robot", "certificate", "Trust the virtual SMS company's certificate", Outcome.Skip, $"Needs an administrator command prompt on the server: {ex.Message}");
        }
        return false;
    }

    private void GrantAppPool(string folder)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("icacls", $"\"{folder}\" /grant \"IIS AppPool\\{_o.AppPool}:(OI)(CI)M\" /T /Q") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            p?.WaitForExit(30000);
            if (p?.ExitCode != 0) _r.Add("devices", "robot", folder, "Let the BrightPAS website write to the virtual HealthLink folders", Outcome.Warn, $"icacls failed: {p?.StandardError.ReadToEnd()}");
        }
        catch (System.ComponentModel.Win32Exception ex) { _r.Add("devices", "robot", folder, "Folder permissions", Outcome.Warn, ex.Message); }
    }

    private async Task AcceptLoop(TcpListener l, Func<TcpClient, Task> handle)
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync(_stop.Token); } catch (Exception) when (_stop.IsCancellationRequested) { return; } catch (SocketException) { continue; }
            _ = Task.Run(async () => { using (c) { try { await handle(c); } catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException) { } } });
        }
    }

    private async Task HandleSmtpAsync(TcpClient c)
    {
        var s = c.GetStream();
        using var rd = new StreamReader(s, Encoding.UTF8);
        await using var wr = new StreamWriter(s, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
        await wr.WriteLineAsync("220 brightpas-testbot ESMTP virtual mail server");
        string from = "", subject = "";
        var to = new List<string>();
        while (await rd.ReadLineAsync() is { } line)
        {
            var cmd = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
            switch (cmd)
            {
                case "EHLO": await wr.WriteLineAsync("250-brightpas-testbot"); await wr.WriteLineAsync("250 8BITMIME"); break;
                case "HELO": await wr.WriteLineAsync("250 brightpas-testbot"); break;
                case "MAIL": from = Addr(line); await wr.WriteLineAsync("250 OK"); break;
                case "RCPT": to.Add(Addr(line)); await wr.WriteLineAsync("250 OK"); break;
                case "DATA":
                    await wr.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    var body = new StringBuilder();
                    while (await rd.ReadLineAsync() is { } l2 && l2 != ".") body.AppendLine(l2.StartsWith("..", StringComparison.Ordinal) ? l2[1..] : l2);
                    var raw = body.ToString();
                    subject = raw.Split('\n').FirstOrDefault(x => x.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))?[8..].Trim() ?? "";
                    if (subject.StartsWith("=?", StringComparison.Ordinal)) subject = DecodeWord(subject);
                    var text = raw.Contains("\r\n\r\n", StringComparison.Ordinal) ? raw[(raw.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..] : raw;
                    if (raw.Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase))
                    {
                        try { text = Encoding.UTF8.GetString(Convert.FromBase64String(string.Concat(text.Split('\n').Select(x => x.Trim())))); } catch (FormatException) { }
                    }
                    else if (raw.Contains("quoted-printable", StringComparison.OrdinalIgnoreCase)) text = DecodeQp(text);
                    foreach (var t in to) { Mail.Enqueue(new Email(from, t, subject, text, DateTimeOffset.Now)); _r.Device("Virtual email inboxes", $"To {t}: {subject}", text); }
                    to.Clear();
                    await wr.WriteLineAsync("250 OK queued");
                    break;
                case "RSET": to.Clear(); await wr.WriteLineAsync("250 OK"); break;
                case "NOOP": await wr.WriteLineAsync("250 OK"); break;
                case "QUIT": await wr.WriteLineAsync("221 Bye"); return;
                default: await wr.WriteLineAsync("502 Not implemented"); break;
            }
        }
        static string Addr(string l) { var a = l.IndexOf('<'); var b = l.IndexOf('>'); return a >= 0 && b > a ? l[(a + 1)..b] : l[(l.IndexOf(':') + 1)..].Trim(); }
    }

    private static string DecodeWord(string s)
    {
        try { return System.Net.Mail.Attachment.CreateAttachmentFromString("", s).Name ?? s; } catch (FormatException) { return s; }
    }

    private static string DecodeQp(string s)
    {
        var bytes = new List<byte>();
        s = s.Replace("=\r\n", "").Replace("=\n", "");
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '=' && i + 2 < s.Length && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2])) { bytes.Add(Convert.ToByte(s.Substring(i + 1, 2), 16)); i += 2; }
            else bytes.AddRange(Encoding.UTF8.GetBytes(s[i].ToString()));
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private async Task HandleZebraAsync(TcpClient c)
    {
        using var ms = new MemoryStream();
        var s = c.GetStream();
        var buf = new byte[8192];
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        while (true)
        {
            idle.CancelAfter(1500);
            int n;
            try { n = await s.ReadAsync(buf, idle.Token); } catch (OperationCanceledException) { break; }
            if (n == 0) break;
            ms.Write(buf, 0, n);
            if (Encoding.ASCII.GetString(ms.ToArray()).TrimEnd().EndsWith("^XZ", StringComparison.Ordinal)) { await Task.Delay(200); if (!s.DataAvailable) break; }
        }
        var zpl = Encoding.UTF8.GetString(ms.ToArray());
        if (zpl.Length == 0) return; // connection test only
        Labels.Enqueue(new Label(zpl, ((IPEndPoint)c.Client.LocalEndPoint!).Port, DateTimeOffset.Now));
        string? img = null;
        if (RenderLabel is not null) img = await RenderLabel(zpl);
        _r.Device("Virtual Zebra printer", $"Label received ({zpl.Length} bytes)", zpl, img);
    }

    private async Task HealthLinkLoop()
    {
        var sent = Path.Combine(HealthLinkOut, "sent");
        Directory.CreateDirectory(sent);
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                foreach (var f in Directory.GetFiles(HealthLinkOut, "*.hl7"))
                {
                    var msg = await File.ReadAllTextAsync(f);
                    var problems = CheckRefI12(msg, out var control, out var to);
                    HealthLink.Enqueue(new HlFile(Path.GetFileName(f), msg, problems, DateTimeOffset.Now));
                    _r.Device("Virtual HealthLink", $"{Path.GetFileName(f)} to {to} {(problems.Length == 0 ? "– valid REF^I12" : "– PROBLEMS: " + problems)}", msg.Replace("\r", "\n"));
                    var code = problems.Length == 0 ? "AA" : "AE";
                    var ack = $"MSH|^~\\&|HealthLink|HL|BrightPAS|TDS|{DateTime.Now:yyyyMMddHHmmss}||ACK^I12|ACK{control}|P|2.4\rMSA|{code}|{control}|{(problems.Length == 0 ? "Delivered to " + to + " (virtual HealthLink)" : problems)}\r";
                    await File.WriteAllTextAsync(Path.Combine(HealthLinkIn, control + ".ack.tmp"), ack);
                    File.Move(Path.Combine(HealthLinkIn, control + ".ack.tmp"), Path.Combine(HealthLinkIn, control + ".ack"), true);
                    File.Move(f, Path.Combine(sent, Path.GetFileName(f)), true);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try { await Task.Delay(1000, _stop.Token); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Checks a letter message against the HL7 v2.4 REF^I12 shape HealthLink expects. Returns "" when valid.</summary>
    public static string CheckRefI12(string msg, out string control, out string to)
    {
        var segs = msg.Replace("\r\n", "\r").Replace('\n', '\r').Split('\r', StringSplitOptions.RemoveEmptyEntries);
        var p = new List<string>();
        control = "?"; to = "?";
        var msh = segs.FirstOrDefault(s => s.StartsWith("MSH|", StringComparison.Ordinal));
        if (msh is null) return "No MSH segment.";
        var f = msh.Split('|');
        if (f.Length < 12) p.Add("MSH has too few fields.");
        else
        {
            control = f[9]; to = f[5];
            if (!f[8].StartsWith("REF^I12", StringComparison.Ordinal)) p.Add($"Message type is {f[8]}, expected REF^I12.");
            if (f[11] != "2.4") p.Add($"HL7 version {f[11]}, expected 2.4.");
            if (string.IsNullOrWhiteSpace(f[5])) p.Add("No receiving EDI account (MSH-6).");
            if (string.IsNullOrWhiteSpace(f[3])) p.Add("No sending EDI account (MSH-4).");
        }
        foreach (var need in new[] { "RF1", "PRD", "PID", "OBR", "OBX" })
            if (!segs.Any(s => s.StartsWith(need + "|", StringComparison.Ordinal))) p.Add($"Missing {need} segment.");
        var pid = segs.FirstOrDefault(s => s.StartsWith("PID|", StringComparison.Ordinal))?.Split('|');
        if (pid is { Length: > 7 })
        {
            if (string.IsNullOrWhiteSpace(pid[5])) p.Add("PID has no patient name.");
            if (pid[7].Length != 8) p.Add("PID date of birth not yyyymmdd.");
        }
        var obx = segs.FirstOrDefault(s => s.StartsWith("OBX|", StringComparison.Ordinal))?.Split('|');
        if (obx is { Length: > 5 } && (obx[2] != "FT" || obx[5].Length < 20)) p.Add("OBX letter text missing or not FT.");
        if (segs.Any(s => s.Length > 0 && !char.IsLetterOrDigit(s[0]))) p.Add("A segment starts with an unexpected character.");
        return string.Join(" ", p);
    }

    private async Task PostReceiptAsync(string id)
    {
        if (ReceiptUrl is null) return;
        await Task.Delay(1500);
        using var h = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });
        try { await h.PostAsync(ReceiptUrl, new StringContent(JsonSerializer.Serialize(new { message_id = id, status = "received" }), Encoding.UTF8, "application/json")); }
        catch (HttpRequestException) { }
    }

    /// <summary>A virtual phone texts back (e.g. "YES" to confirm), through the SMS company's reply callback.</summary>
    public async Task<int> ReplyAsync(string from, string text, string? replyTo)
    {
        if (ReplyUrl is null) return 0;
        using var h = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });
        var r = await h.PostAsync(ReplyUrl, new StringContent(JsonSerializer.Serialize(new { id = "MO" + Interlocked.Increment(ref _smsId), source = from, destination = "TDS", content = text, reply_to = replyTo }), Encoding.UTF8, "application/json"));
        _r.Device("Virtual phones (SMS)", $"Reply from {from}", text + $"  → BrightPAS answered {(int)r.StatusCode}");
        return (int)r.StatusCode;
    }

    /// <summary>A laboratory system sends an HL7 message over MLLP and reads the acknowledgement.</summary>
    public static async Task<string> SendMllpAsync(string host, int port, string hl7)
    {
        using var c = new TcpClient();
        await c.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(10));
        var s = c.GetStream();
        var bytes = new List<byte> { 0x0b }; bytes.AddRange(Encoding.UTF8.GetBytes(hl7)); bytes.Add(0x1c); bytes.Add(0x0d);
        await s.WriteAsync(bytes.ToArray());
        var buf = new byte[65536];
        var ms = new MemoryStream();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            var n = await s.ReadAsync(buf, cts.Token);
            if (n == 0) break;
            ms.Write(buf, 0, n);
            if (ms.ToArray().Contains((byte)0x1c)) break;
        }
        return Encoding.UTF8.GetString(ms.ToArray()).Trim('\x0b', '\x1c', '\r', '\n');
    }

    public static async Task<T?> WaitForAsync<T>(ConcurrentQueue<T> q, Func<T, bool> match, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var hit = q.FirstOrDefault(match);
            if (hit is not null) return hit;
            await Task.Delay(500);
        }
        return default;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _smtp?.Stop(); _zebra?.Stop();
        if (_http is not null) await _http.DisposeAsync();
        if (CertificateTrusted && Certificate is not null) TrustCertificate(Certificate, false);
        Certificate?.Dispose();
        _stop.Dispose();
    }
}
